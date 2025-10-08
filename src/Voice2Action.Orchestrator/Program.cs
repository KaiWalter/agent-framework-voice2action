using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Azure.AI.OpenAI;
using ModelContextProtocol.Client;
using Voice2Action.Domain;

// Orchestrator aligned with infrastructure prompt templates.
// Executes full multi-step workflow until coordinator outputs DONE (no early-stop).

var utilityUrl = Environment.GetEnvironmentVariable("MCP_UTILITY_ENDPOINT") ?? "http://localhost:5010";
var officeUrl = Environment.GetEnvironmentVariable("MCP_OFFICE_ENDPOINT") ?? "http://localhost:5020";

Console.WriteLine($"Connecting to MCP servers: utility={utilityUrl} office={officeUrl}");

await using var utilityClient = await McpClient.CreateAsync(new HttpClientTransport(new() { Endpoint = new Uri(utilityUrl) }));
await using var officeClient = await McpClient.CreateAsync(new HttpClientTransport(new() { Endpoint = new Uri(officeUrl) }));

var utilityTools = (await utilityClient.ListToolsAsync()).Cast<AITool>().ToList();
var officeTools = (await officeClient.ListToolsAsync()).Cast<AITool>().ToList();

Console.WriteLine("Utility MCP tools: " + string.Join(", ", utilityTools.Select(t => t.Name)));
Console.WriteLine("Office MCP tools: " + string.Join(", ", officeTools.Select(t => t.Name)));

// Azure OpenAI setup for planning/tool invocation
var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
var deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o";
var key = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
{
    Console.WriteLine("Missing Azure OpenAI credentials. Set AZURE_OPENAI_ENDPOINT and AZURE_OPENAI_API_KEY.");
    return;
}
var azureClient = new Azure.AI.OpenAI.AzureOpenAIClient(new Uri(endpoint!), new Azure.AzureKeyCredential(key!));
var chatClient = azureClient.GetChatClient(deployment).AsIChatClient();

// Find and load prompt templates similar to DefaultAgentSetProvider
string FindPromptsDir()
{
    var start = AppContext.BaseDirectory;
    for (int depth = 0; depth < 6; depth++)
    {
        var parts = new[]{ start }.Concat(Enumerable.Repeat("..", depth)).ToArray();
        var candidate = Path.GetFullPath(Path.Combine(parts));
        var probe = Path.Combine(candidate, "src", "Voice2Action.Infrastructure", "Prompts");
        if (Directory.Exists(probe)) return probe;
    }
    throw new DirectoryNotFoundException("Prompts directory not found.");
}
string promptsDir = FindPromptsDir();
string LoadPrompt(string file)
{
    var path = Path.Combine(promptsDir, file);
    if (!File.Exists(path)) throw new FileNotFoundException(path);
    return File.ReadAllText(path);
}
var utilityPrompt = LoadPrompt("utility.md");
var officePrompt = LoadPrompt("office-automation.md");

ITextAgent CreateWorker(string name, string prompt, IEnumerable<AITool> tools)
{
    var chatAgent = new ChatClientAgent(chatClient, new ChatClientAgentOptions(prompt)
    {
        ChatOptions = new ChatOptions { Tools = tools.ToList() }
    });
    return new ChatClientAgentAdapter(name, tools.Select(t => t.Name).ToArray(), chatAgent);
}
var workers = new List<ITextAgent>
{
    CreateWorker("UtilityAgent", utilityPrompt, utilityTools),
    CreateWorker("OfficeAutomation", officePrompt, officeTools)
};

string InferActionFromTask(string task) =>
    task.Contains("Transcribe", StringComparison.OrdinalIgnoreCase) ? "Transcribe" :
    task.Contains("SetReminder", StringComparison.OrdinalIgnoreCase) ? "SetReminder" :
    task.Contains("SendEmail", StringComparison.OrdinalIgnoreCase) ? "SendEmail" :
    task.Contains("GetCurrentDateTime", StringComparison.OrdinalIgnoreCase) ? "GetCurrentDateTime" :
    "Unknown";

async Task RunOrchestrationAsync(string audioPath, CancellationToken ct = default)
{
    if (!File.Exists(audioPath))
    {
        Console.WriteLine($"File not found: {audioPath}");
        return;
    }
    var absPath = Path.GetFullPath(audioPath);
    var catalog = string.Join("\n", workers.Select(w => $"- {w.Name}: {string.Join(", ", w.Capabilities)}"));
    var coordinatorTemplate = LoadPrompt("coordinator-template.md");
    var coordinatorInstructions = coordinatorTemplate.Replace("{{AGENT_CATALOG}}", catalog) +
        "\nAlways respond ONLY in minified JSON: {\"Action\":\"DELEGATE|DONE\",\"Agent\":\"<agent name when delegating>\",\"Task\":\"<task when delegating>\",\"Summary\":\"<summary when done>\"}." +
        $" When delegating transcription prefer: TranscribeVoiceRecording({absPath}). Do not output DONE until all required terminal actions (e.g. reminders, emails) have been delegated per the rules.";
    var planner = new ChatClientAgent(chatClient, new ChatClientAgentOptions(coordinatorInstructions));
    var plannerAdapter = new ChatClientAgentAdapter("Planner", Array.Empty<string>(), planner);

    var actions = new List<AgentActionRecord>();
    string? transcript = null;
    string summary = string.Empty;
    string plannerInput = $"Process voice recording at path {absPath}";
    int safety = 10;
    while (safety-- > 0)
    {
        var plannerRaw = await plannerAdapter.RunAsync(plannerInput, ct);
        actions.Add(new AgentActionRecord { Agent = plannerAdapter.Name, Action = "Plan", RawResult = plannerRaw });
        CoordinatorMessage? parsed = null;
        try { parsed = JsonSerializer.Deserialize<CoordinatorMessage>(plannerRaw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); } catch { }
        if (parsed?.Action?.Equals("DONE", StringComparison.OrdinalIgnoreCase) == true)
        {
            summary = parsed.Summary ?? string.Empty;
            break;
        }
        if (parsed?.Action?.Equals("DELEGATE", StringComparison.OrdinalIgnoreCase) != true || string.IsNullOrWhiteSpace(parsed.Agent) || string.IsNullOrWhiteSpace(parsed.Task))
        {
            plannerInput = "Malformed JSON. Return required shape.";
            continue;
        }
        var worker = workers.FirstOrDefault(w => w.Name.Equals(parsed.Agent, StringComparison.OrdinalIgnoreCase));
        if (worker is null)
        {
            plannerInput = "Unknown agent. Replan.";
            continue;
        }
        var workerOutput = await worker.RunAsync(parsed.Task, ct);
        if (transcript == null && parsed.Task.Contains("TranscribeVoiceRecording", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(workerOutput))
        {
            transcript = workerOutput.Trim();
        }
        actions.Add(new AgentActionRecord { Agent = worker.Name, Action = InferActionFromTask(parsed.Task), RawResult = workerOutput });
        plannerInput = JsonSerializer.Serialize(new
        {
            Transcript = transcript,
            Actions = actions.Select(a => new { a.Agent, a.Action, a.RawResult }),
            Guidance = "Decide next step. If a reminder or email can be created based on the transcript, delegate to OfficeAutomation with explicit SetReminder or SendEmail task (include task, due date, optional reminder date). Otherwise gather missing info or DONE with summary.",
            RequiredResponse = new { Action = "DELEGATE|DONE", Agent = "<when delegating>", Task = "<instruction when delegating>", Summary = "<when done>" }
        });
    }

    Console.WriteLine("--- Orchestration Complete ---");
    Console.WriteLine("Transcript: " + (transcript ?? "<none>"));
    Console.WriteLine("Summary: " + summary);
    Console.WriteLine("Actions:");
    foreach (var a in actions)
    {
        Console.WriteLine($"  {a.Agent} -> {a.Action}: {a.RawResult?.Substring(0, Math.Min(a.RawResult.Length, 160))}");
    }
}

Console.WriteLine("Enter path to audio file (or blank to exit):");
string? line;
while (!string.IsNullOrEmpty(line = Console.ReadLine()))
{
    await RunOrchestrationAsync(line!);
    Console.WriteLine();
    Console.WriteLine("Enter path to audio file (or blank to exit):");
}

// Local type for planner parsing (domain AgentActionRecord already available)
class CoordinatorMessage
{
    public string? Action { get; set; }
    public string? Agent { get; set; }
    public string? Task { get; set; }
    public string? Summary { get; set; }
}

// Adapter to satisfy ITextAgent
sealed class ChatClientAgentAdapter : ITextAgent
{
    private readonly ChatClientAgent _inner;
    public string Name { get; }
    public string[] Capabilities { get; }
    public ChatClientAgentAdapter(string name, string[] capabilities, ChatClientAgent inner)
    { Name = name; Capabilities = capabilities; _inner = inner; }
    public async Task<string> RunAsync(string input, CancellationToken ct = default)
        => (await _inner.RunAsync(input, cancellationToken: ct)).ToString();
}

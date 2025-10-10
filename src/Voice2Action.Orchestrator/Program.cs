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
        ChatOptions = new ChatOptions { Tools = tools.ToList(), Temperature = 0 }
    });
    return new ChatClientAgentAdapter(name, tools.Select(t => t.Name).ToArray(), chatAgent);
}
var workers = new List<ITextAgent>
{
    CreateWorker("UtilityAgent", utilityPrompt, utilityTools),
    CreateWorker("OfficeAutomation", officePrompt, officeTools)
};

string InferActionFromTask(string task)
{
    // Generic heuristic: take leading alphabetic characters up to first '(' or space as action verb/capability name.
    var trimmed = task.Trim();
    int end = trimmed.IndexOf('(');
    if (end < 0) end = trimmed.IndexOf(' ');
    if (end < 0) end = Math.Min(trimmed.Length, 40);
    var head = new string(trimmed.Take(end).Where(char.IsLetter).ToArray());
    return string.IsNullOrWhiteSpace(head) ? "Unknown" : head;
}

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
    var coordinatorInstructions = coordinatorTemplate.Replace("{{AGENT_CATALOG}}", catalog);
    var planner = new ChatClientAgent(chatClient, new ChatClientAgentOptions(coordinatorInstructions)
    {
        ChatOptions = new ChatOptions { Temperature = 0 }
    });
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
            plannerInput = "{\"Error\":\"Malformed JSON - expected DELEGATE or DONE per contract\"}";
            continue;
        }
        var worker = workers.FirstOrDefault(w => w.Name.Equals(parsed.Agent, StringComparison.OrdinalIgnoreCase));
        if (worker is null)
        {
            plannerInput = "Unknown agent. Replan.";
            continue;
        }
        // Auto-populate fallback notification arguments if planner omitted them.
        if (parsed.Task.StartsWith("SendFallbackNotification", StringComparison.OrdinalIgnoreCase))
        {
            var trimmed = parsed.Task.Trim();
            var hasParen = trimmed.Contains('(');
            var hasArgs = hasParen && !trimmed.EndsWith("()", StringComparison.Ordinal);
            if (!hasArgs)
            {
                var subj = "Note: " + (transcript != null ? string.Join(' ', transcript.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(7)) : "Voice note");
                if (subj.Length > 60) subj = subj[..60];
                var bodySource = transcript ?? $"No transcript captured yet for audio {absPath}";
                // Escape quotes minimally for embedding in tool call style string
                subj = subj.Replace("\"", "'");
                bodySource = bodySource.Replace("\"", "'");
                parsed.Task = $"SendFallbackNotification(subject: \"{subj}\", body: \"{bodySource}\")";
                Console.WriteLine($"[AutoArgs] Injected fallback args -> {parsed.Task}");
            }
        }
        var workerOutput = await worker.RunAsync(parsed.Task, ct);
        if (!string.IsNullOrWhiteSpace(workerOutput))
        {
            try
            {
                using var doc0 = JsonDocument.Parse(workerOutput);
                JsonElement root = doc0.RootElement;
                // If the root is a JSON string that itself contains JSON, unwrap once.
                if (root.ValueKind == JsonValueKind.String)
                {
                    var inner = root.GetString();
                    if (!string.IsNullOrWhiteSpace(inner) && inner.TrimStart().StartsWith("{"))
                    {
                        using var doc1 = JsonDocument.Parse(inner);
                        root = doc1.RootElement.Clone();
                    }
                }
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True && root.TryGetProperty("type", out var typeEl2))
                {
                    var type = typeEl2.GetString();
                    if (type == "Transcription" && transcript == null && root.TryGetProperty("data", out var dataEl) && dataEl.TryGetProperty("text", out var textEl))
                    {
                        transcript = textEl.GetString();
                    }
                }
            }
            catch { /* ignore non-JSON outputs */ }
            if (transcript == null && parsed.Task.Contains("Transcribe", StringComparison.OrdinalIgnoreCase))
            {
                transcript = workerOutput.Trim().Trim('"');
            }
        }
        actions.Add(new AgentActionRecord { Agent = worker.Name, Action = InferActionFromTask(parsed.Task), RawResult = workerOutput });
        plannerInput = JsonSerializer.Serialize(new
        {
            Transcript = transcript,
            LastWorkerOutputJson = TryParse(workerOutput),
            TerminalActionsExecuted = actions.Where(a => a.Agent != plannerAdapter.Name && a.Action != "Plan" && a.Action != "Unknown" && !a.Action.StartsWith("Get", StringComparison.OrdinalIgnoreCase) && !a.Action.StartsWith("Read", StringComparison.OrdinalIgnoreCase) && !a.Action.StartsWith("Fetch", StringComparison.OrdinalIgnoreCase) && !a.Action.StartsWith("Lookup", StringComparison.OrdinalIgnoreCase) && !a.Action.StartsWith("Look", StringComparison.OrdinalIgnoreCase) && !a.Action.StartsWith("Transcribe", StringComparison.OrdinalIgnoreCase)).Select(a => a.Action).Distinct().ToArray()
        });

        // Simple repetition guard: if last 3 planner actions delegate to the same task, nudge planner.
        var recentPlans = actions.TakeLast(6).Where(a => a.Agent == plannerAdapter.Name && a.Action == "Plan").Select(a => a.RawResult).ToList();
        if (recentPlans.Count >= 3 && recentPlans.TakeLast(3).All(r => r?.Contains(parsed.Task, StringComparison.OrdinalIgnoreCase) == true))
        {
            plannerInput = plannerInput + "\nNOTE: You have repeated the same retrieval task multiple times. Either use a different tool to progress toward a terminal action (schedule, send, create) or conclude with DONE and a summary.";
        }
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

// If audio file paths provided as command line args, process them (supports relative paths) then exit.
var argsList = Environment.GetCommandLineArgs().Skip(1).ToList();
if (argsList.Count > 0)
{
    foreach (var p in argsList)
    {
        var resolved = Path.GetFullPath(p);
        Console.WriteLine($"\n[ARG] Processing audio path: {p} -> {resolved}");
        await RunOrchestrationAsync(resolved);
    }
    return;
}

// Fallback interactive loop.
Console.WriteLine("Enter path to audio file (blank to exit):");
string? line;
while (!string.IsNullOrEmpty(line = Console.ReadLine()))
{
    var resolved = Path.GetFullPath(line!);
    await RunOrchestrationAsync(resolved);
    Console.WriteLine();
    Console.WriteLine("Enter path to audio file (blank to exit):");
}

static object? TryParse(string? json)
{
    if (string.IsNullOrWhiteSpace(json)) return null;
    try { return JsonSerializer.Deserialize<object>(json); } catch { return null; }
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

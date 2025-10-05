using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Voice2Action.Application;
using Voice2Action.Domain;

namespace Voice2Action.Infrastructure.AI;

public sealed class DefaultAgentSetProvider : IAgentSetProvider
{
    private readonly AzureOpenAIClient _client;
    private readonly string _deployment;
    private readonly ITranscriptionService _transcriptionService;
    private readonly IDateTimeService _dateTimeService;
    private readonly IReminderService _reminderService;
    private readonly IEmailService _emailService;
    private readonly string _basePromptPath;
    private readonly string _coordinatorName;

    public DefaultAgentSetProvider(
        AzureOpenAIClient client,
        ITranscriptionService transcriptionService,
        IDateTimeService dateTimeService,
        IReminderService reminderService,
        IEmailService emailService,
        string deploymentName,
        string basePromptPath,
        string coordinatorName = "Planner")
    {
        _client = client;
        _deployment = deploymentName;
        _transcriptionService = transcriptionService;
        _dateTimeService = dateTimeService;
        _reminderService = reminderService;
        _emailService = emailService;
        _basePromptPath = basePromptPath;
        _coordinatorName = coordinatorName;
    }

    public Task<AgentSet> CreateAsync(CancellationToken ct = default)
    {
        var chatClient = _client.GetChatClient(_deployment);
        var asChat = chatClient.AsIChatClient();

        string LoadPrompt(string file)
        {
            var path = Path.Combine(_basePromptPath, file);
            if (!File.Exists(path)) throw new FileNotFoundException($"Prompt file not found: {path}");
            return File.ReadAllText(path);
        }

        var workers = new List<ITextAgent>();

        // Utility agent (transcription + time)
        var utilityInstructions = LoadPrompt("utility.md");
        var utilityOptions = new ChatClientAgentOptions(utilityInstructions)
        {
            ChatOptions = new ChatOptions
            {
                Tools =
                [
                    AIFunctionFactory.Create(new Func<string, string>(TranscribeVoiceRecording)),
                    AIFunctionFactory.Create(new Func<string>(GetCurrentDateTime))
                ]
            }
        };
        var utilityCore = new ChatClientAgent(asChat, utilityOptions);
        workers.Add(new ChatClientTextAgent("Utility", new[] { "TranscribeVoiceRecording(audioPath)", "GetCurrentDateTime()" }, utilityCore));

        // Office automation agent (reminders + email)
        var officeInstructions = LoadPrompt("office-automation.md");
        var officeOptions = new ChatClientAgentOptions(officeInstructions)
        {
            ChatOptions = new ChatOptions
            {
                Tools =
                [
                    AIFunctionFactory.Create(new Func<string, DateTime, DateTime?, string>(SetReminder)),
                    AIFunctionFactory.Create(new Func<string, string, string>(SendEmail))
                ]
            }
        };
        var officeCore = new ChatClientAgent(asChat, officeOptions);
        workers.Add(new ChatClientTextAgent("OfficeAutomation", new[] { "SetReminder(task, dueDate, reminderDate?)", "SendEmail(subject, body)" }, officeCore));

        // Coordinator (LLM only)
        var coordinatorTemplate = LoadPrompt("coordinator-template.md");
        var agentCatalog = string.Join("\n", workers.Select(w => $"- {w.Name}: {string.Join(", ", w.Capabilities)}"));
        var coordinatorInstructions = coordinatorTemplate.Replace("{{AGENT_CATALOG}}", agentCatalog) +
            "\nAlways respond ONLY in minified JSON with this schema: {\"Action\":\"DELEGATE|DONE\",\"Agent\":\"<agent name when delegating>\",\"Task\":\"<task when delegating>\",\"Summary\":\"<summary when done>\"}. When delegating transcription prefer: TranscribeVoiceRecording(/absolute/path.mp3).";
        var coordinatorOptions = new ChatClientAgentOptions(coordinatorInstructions);
        var coordinatorCore = new ChatClientAgent(asChat, coordinatorOptions);
        var coordinatorAdapter = new ChatClientTextAgent(_coordinatorName, Array.Empty<string>(), coordinatorCore);

        var set = new AgentSet
        {
            Coordinator = coordinatorAdapter,
            Workers = workers
        };
        return Task.FromResult(set);
    }

    // Tool bindings delegate to domain services (keeping infrastructure orchestration thin)
    private string TranscribeVoiceRecording(string recording)
    {
        if (string.IsNullOrWhiteSpace(recording)) throw new ArgumentException("Recording path empty", nameof(recording));
        if (!File.Exists(recording)) throw new FileNotFoundException("Audio file not found", recording);
        return _transcriptionService.TranscribeAsync(recording).GetAwaiter().GetResult();
    }
    private string GetCurrentDateTime() => _dateTimeService.GetCurrentDateTime();
    private string SetReminder(string task, DateTime dueDate, DateTime? reminderDate) => _reminderService.SetReminder(task, dueDate, reminderDate);
    private string SendEmail(string subject, string body) => _emailService.SendEmail(subject, body);

    private sealed class ChatClientTextAgent : ITextAgent
    {
        private readonly ChatClientAgent _inner;
        public string Name { get; }
        public string[] Capabilities { get; }
        public ChatClientTextAgent(string name, string[] capabilities, ChatClientAgent inner)
        { Name = name; _inner = inner; Capabilities = capabilities; }
        public async Task<string> RunAsync(string input, CancellationToken ct = default) => (await _inner.RunAsync(input, cancellationToken: ct)).ToString();
    }
}

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

        var workers = new List<ITextAgent>
        {
            UtilityAgentFactory.Create(asChat, _basePromptPath, _transcriptionService, _dateTimeService),
            OfficeAutomationAgentFactory.Create(asChat, _basePromptPath, _reminderService, _emailService)
        };

        // Coordinator (LLM only)
        var coordinatorTemplate = LoadPrompt("coordinator-template.md");
        var agentCatalog = string.Join("\n", workers.Select(w => $"- {w.Name}: {string.Join(", ", w.Capabilities)}"));
        var coordinatorInstructions = coordinatorTemplate.Replace("{{AGENT_CATALOG}}", agentCatalog) +
            "\nAlways respond ONLY in minified JSON with this schema: {\"Action\":\"DELEGATE|DONE\",\"Agent\":\"<agent name when delegating>\",\"Task\":\"<task when delegating>\",\"Summary\":\"<summary when done>\"}. When delegating transcription prefer: TranscribeVoiceRecording(/absolute/path.mp3).";
        var coordinatorOptions = new ChatClientAgentOptions(coordinatorInstructions);
        var coordinatorCore = new ChatClientAgent(asChat, coordinatorOptions);
        var coordinatorAdapter = new BasicTextAgent(_coordinatorName, Array.Empty<string>(), coordinatorCore);

        var set = new AgentSet
        {
            Coordinator = coordinatorAdapter,
            Workers = workers
        };
        return Task.FromResult(set);
    }

    // Tool bindings no longer duplicated here – logic moved into factories.
}

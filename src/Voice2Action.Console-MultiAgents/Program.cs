using System.ComponentModel;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;

public static class Program
{
    private static readonly string Endpoint =
        Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
        ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
    private static readonly string DeploymentName =
        Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-5";

    private static readonly string ApiKey =
        Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
        ?? throw new InvalidOperationException("AZURE_OPENAI_API_KEY is not set.");

    private const string coordinatingAgentName = "Planner";
    private const string utilityAgentName = "Utility";
    private const string officeAutomationAgentName = "OfficeAutomation";

    private static string LoadPrompt(string fileName)
    {
        var baseDir = AppContext.BaseDirectory; // points to build output folder
        var path = Path.Combine(baseDir, "Prompts", fileName);
        if (!File.Exists(path)) throw new FileNotFoundException($"Prompt file not found: {path}");
        return File.ReadAllText(path);
    }

    private static readonly Lazy<IHost> HostContainer = new(() =>
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(sp => new AzureOpenAIClient(
            new Uri(Endpoint),
            new AzureKeyCredential(ApiKey)
        ));
        builder.Services.AddSingleton<Voice2Action.Domain.ITranscriptionService>(sp =>
        {
            var audioDeployment =
                Environment.GetEnvironmentVariable("AZURE_OPENAI_AUDIO_DEPLOYMENT_NAME")
                ?? "whisper";
            return new Voice2Action.Infrastructure.AI.OpenAIAudioTranscriptionService(
                sp.GetRequiredService<AzureOpenAIClient>(),
                audioDeployment
            );
        });
        return builder.Build();
    });

    [Description(
        "Transcribe the given audio file (mp3/wav/m4a) using Azure OpenAI Whisper and return raw text."
    )]
    public static string TranscribeVoiceRecording(
        [Description("Path to recording file.")] string recording
    )
    {
        if (string.IsNullOrWhiteSpace(recording))
            throw new ArgumentException("Recording path empty", nameof(recording));
        if (!File.Exists(recording))
            throw new FileNotFoundException("Audio file not found", recording);
        var transcription =
            HostContainer.Value.Services.GetRequiredService<Voice2Action.Domain.ITranscriptionService>();
        return transcription
            .TranscribeAsync(recording, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    [Description("Return the current date/time to support normalization of relative or partial dates. Format: LOCAL=yyyy-MM-ddTHH:mm:ssK;UTC=yyyy-MM-ddTHH:mm:ssZ")] 
    public static string GetCurrentDateTime()
    {
        var nowLocal = DateTime.Now; // local machine time
        var nowUtc = DateTime.UtcNow;
        return $"LOCAL={nowLocal:yyyy-MM-ddTHH:mm:ssK};UTC={nowUtc:yyyy-MM-ddTHH:mm:ssZ}";
    }

    [Description("Set a reminder for the given task at the specified date and optional earlier reminder time.")]
    public static string SetReminder(
        [Description("Task to be reminded of.")] string task,
        [Description("Due date for the task.")] DateTime dueDate,
        [Description("Optional reminder date/time (before due date).")] DateTime? reminderDate
    )
    {
        var baseMsg = $"Reminder set for task '{task}' due at {dueDate}.";
        return reminderDate.HasValue
            ? baseMsg + $" Reminder will trigger at {reminderDate.Value}."
            : baseMsg;
    }

    [Description("Send an email with the given subject and body to the user.")]
    public static string SendEmail(
        [Description("Email subject.")] string subject,
        [Description("Email body.")] string body
    ) => $"Email sent with subject '{subject}' and body '{body}'";

    public static async Task Main(string[] args)
    {
        var chatClient = new AzureOpenAIClient(
            new Uri(Endpoint),
            new AzureKeyCredential(ApiKey)
        ).GetChatClient(DeploymentName);
        // IMPORTANT: Create agents using (instructions, name) order. Previous version reversed them causing misbehavior.
        var coordinatorInstructions = LoadPrompt("coordinator.md");
        var utilityInstructions = LoadPrompt("utility.md");
        var officeAutomationInstructions = LoadPrompt("office-automation.md");

        AIAgent coordinator = chatClient.CreateAIAgent(coordinatorInstructions, coordinatingAgentName);
        AIAgent utility = chatClient.CreateAIAgent(
            utilityInstructions,
            utilityAgentName,
            tools: [
                AIFunctionFactory.Create(new Func<string, string>(TranscribeVoiceRecording)),
                AIFunctionFactory.Create(new Func<string>(GetCurrentDateTime))
            ]
        );
        AIAgent officeAutomation = chatClient.CreateAIAgent(
            officeAutomationInstructions,
            officeAutomationAgentName,
            tools:
            [
                AIFunctionFactory.Create(new Func<string, DateTime, DateTime?, string>(SetReminder)),
                AIFunctionFactory.Create(new Func<string, string, string>(SendEmail)),
            ]
        );

        // Determine audio file path from first command-line argument.
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.WriteLine("Usage: dotnet run -- <audio-file-path>");
            return;
        }
        var audioPath = args[0];
        if (!File.Exists(audioPath))
        {
            Console.WriteLine($"Audio file not found: {audioPath}");
            return;
        }

        // Construct initial user request referencing provided file.
        string userRequest = $"process voice recording in file {audioPath}";
        string? transcription = null;
        int safetyIterations = 8; // prevent infinite loops
        string coordinatorInput = userRequest;
        while (safetyIterations-- > 0)
        {
            var coordResponse = (await coordinator.RunAsync(coordinatorInput)).ToString();
            Console.WriteLine($"[Coordinator] {coordResponse}");

            if (coordResponse.StartsWith("DONE|", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Workflow complete.");
                break;
            }

            if (!coordResponse.StartsWith("DELEGATE:", StringComparison.OrdinalIgnoreCase))
            {
                coordinatorInput = "Your response did not follow required format. Please output DELEGATE:<Agent>|<Task> or DONE|<summary>.";
                continue;
            }

            // Parse: DELEGATE:AgentName|Task
            var payload = coordResponse.Substring("DELEGATE:".Length);
            var split = payload.Split('|', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (split.Length != 2)
            {
                coordinatorInput = "Format parsing error. Reissue correctly.";
                continue;
            }
            var targetAgent = split[0];
            var task = split[1];

            string agentResult;
            switch (targetAgent)
            {
                case "Utility":
                    agentResult = (await utility.RunAsync(task)).ToString();
                    if (transcription == null && !agentResult.StartsWith("CANNOT"))
                        transcription = agentResult; // assume first utility output is transcription
                    break;
                case "OfficeAutomation":
                    agentResult = (await officeAutomation.RunAsync(task)).ToString();
                    break;
                default:
                    agentResult = $"UNKNOWN_AGENT:{targetAgent}";
                    break;
            }
            Console.WriteLine($"[{targetAgent}] {agentResult}");

            coordinatorInput = $"Result from {targetAgent}: {agentResult}. If more steps needed delegate; else DONE|<summary>.";
        }
        if (safetyIterations <= 0)
        {
            Console.WriteLine("Stopped due to safety iteration limit.");
        }
    }
}

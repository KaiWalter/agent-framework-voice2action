using System.ComponentModel;
using System.IO;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;
using System.Linq;

public static class Program
{
    private static readonly string Endpoint =
        Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
        ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
    private static readonly string DeploymentName =
        Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o";

    private static readonly string ApiKey =
        Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
        ?? throw new InvalidOperationException("AZURE_OPENAI_API_KEY is not set.");

    private const string coordinatingAgentName = "Planner"; // used by provider

    private static readonly Lazy<IHost> HostContainer = new(() =>
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(sp => new AzureOpenAIClient(
            new Uri(Endpoint),
            new AzureKeyCredential(ApiKey)
        ));
        builder.Services.AddSingleton<Voice2Action.Domain.ITranscriptionService>(sp =>
        {
            var audioDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_AUDIO_DEPLOYMENT_NAME") ?? "whisper";
            return new Voice2Action.Infrastructure.AI.OpenAIAudioTranscriptionService(
                sp.GetRequiredService<AzureOpenAIClient>(), audioDeployment);
        });
        builder.Services.AddSingleton<Voice2Action.Domain.IDateTimeService, Voice2Action.Infrastructure.AI.DateTimeService>();
        builder.Services.AddSingleton<Voice2Action.Domain.IReminderService, Voice2Action.Infrastructure.AI.ReminderService>();
        builder.Services.AddSingleton<Voice2Action.Domain.IEmailService, Voice2Action.Infrastructure.AI.EmailService>();
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
        [Description("Optional reminder date/time (before due date)." )] DateTime? reminderDate
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
        // Build agent set via provider (shared logic for other frontends)
        var promptFolder = Path.Combine(AppContext.BaseDirectory, "Prompts");
        var azureClient = new AzureOpenAIClient(new Uri(Endpoint), new AzureKeyCredential(ApiKey));
        var spRoot = HostContainer.Value.Services;
        var provider = new Voice2Action.Infrastructure.AI.DefaultAgentSetProvider(
            azureClient,
            spRoot.GetRequiredService<Voice2Action.Domain.ITranscriptionService>(),
            spRoot.GetRequiredService<Voice2Action.Domain.IDateTimeService>(),
            spRoot.GetRequiredService<Voice2Action.Domain.IReminderService>(),
            spRoot.GetRequiredService<Voice2Action.Domain.IEmailService>(),
            DeploymentName,
            promptFolder,
            coordinatingAgentName);
        var agentSet = await provider.CreateAsync();

        // Determine audio file path from first command-line argument.
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.WriteLine("Usage: dotnet run -- <audio-file-path>");
            return;
        }
        var audioPathInput = args[0];
        var audioPath = Path.GetFullPath(audioPathInput);
        if (!File.Exists(audioPath))
        {
            Console.WriteLine($"Audio file not found: {audioPath}");
            return;
        }

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Voice2Action.Infrastructure.AI.OrchestrationServiceCollectionExtensions.AddVoiceCommandOrchestrator(services, agentSet.Coordinator, agentSet.Workers);
        using var sp = services.BuildServiceProvider();
        var orchestrator = sp.GetRequiredService<Voice2Action.Domain.IVoiceCommandOrchestrator>();
        var orchestrationResult = await orchestrator.ExecuteAsync(audioPath);

        foreach (var action in orchestrationResult.Actions)
        {
            Console.WriteLine($"[{action.Agent}] {action.Action} => {action.RawResult}");
        }
        Console.WriteLine("Summary: " + (orchestrationResult.Summary ?? "<none>"));
    }
}

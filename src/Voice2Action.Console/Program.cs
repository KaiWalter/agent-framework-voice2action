using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Azure; // AzureKeyCredential
using Azure.AI.OpenAI;
using Voice2Action.Infrastructure.AI;
using Voice2Action.Domain;
using Voice2Action.Application;

// Console host & bootstrap. All tool implementations now live in Infrastructure agent factories.
// This Program only wires dependencies and runs the orchestrator.

const string DeploymentName = "gpt-4o"; // fallback if env var not set

static IHost BuildHost()
{
    var builder = Host.CreateDefaultBuilder();
    builder.ConfigureAppConfiguration(cfg =>
    {
        cfg.AddEnvironmentVariables();
    });
    builder.ConfigureServices((ctx, services) =>
    {
        var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? string.Empty;
        var key = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ?? string.Empty;
        var deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? DeploymentName; // chat model deployment
        var audioDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_AUDIO_DEPLOYMENT_NAME") ?? "whisper"; // audio (Whisper) deployment
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
        {
            Console.WriteLine("Warning: Azure OpenAI credentials not set; transcription and chat will fail.");
        }

        // Chat (text) interactions still use AzureOpenAIClient; audio transcription now uses raw HttpClient REST call.
        if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(key))
        {
            services.AddSingleton(new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key)));
        }
        else
        {
            services.AddSingleton(new AzureOpenAIClient(new Uri("https://example.invalid"), new AzureKeyCredential("placeholder")));
        }

        // Domain service implementations
        services.AddSingleton<ITranscriptionService>(sp => new OpenAIAudioTranscriptionService(
            sp.GetRequiredService<AzureOpenAIClient>(),
            audioDeployment
        ));
        services.AddSingleton<IDateTimeService, DateTimeService>();
        services.AddSingleton<IReminderService, ReminderService>();
        services.AddSingleton<IEmailService, EmailService>();

        services.AddAgentSetProvider(o =>
        {
            o.PromptDirectory = Path.Combine(AppContext.BaseDirectory, "Prompts");
            o.DeploymentName = deployment;
        });

        // Eagerly build AgentSet so orchestrator can be created
        services.AddSingleton(sp =>
        {
            var provider = sp.GetRequiredService<IAgentSetProvider>();
            return provider.CreateAsync().GetAwaiter().GetResult();
        });
        services.AddSingleton<IVoiceCommandOrchestrator>(sp =>
        {
            var set = sp.GetRequiredService<AgentSet>();
            return new VoiceCommandOrchestrator(set.Coordinator, set.Workers.ToList());
        });
    });
    return builder.Build();
}

var host = BuildHost();
var sp = host.Services;

if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.WriteLine("Usage: dotnet run -- <audio-file-path>");
    return;
}
var audioPath = Path.GetFullPath(args[0]);
if (!File.Exists(audioPath))
{
    Console.WriteLine($"Audio file not found: {audioPath}");
    return;
}

var orchestrator = sp.GetRequiredService<IVoiceCommandOrchestrator>();
try
{
    var result = await orchestrator.ExecuteAsync(audioPath);
    foreach (var action in result.Actions)
    {
        Console.WriteLine($"[{action.Agent}] {action.Action} => {action.RawResult}");
    }
    Console.WriteLine("Summary: " + (result.Summary ?? "<none>"));
}
catch (Exception ex)
{
    Console.Error.WriteLine("Execution failed: " + ex.GetBaseException().Message);
    Console.Error.WriteLine("Set AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY, AZURE_OPENAI_DEPLOYMENT_NAME (chat) and AZURE_OPENAI_AUDIO_DEPLOYMENT_NAME (audio) env vars.");
    Environment.ExitCode = 1;
}

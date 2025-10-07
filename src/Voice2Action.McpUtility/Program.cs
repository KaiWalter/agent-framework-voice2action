using Azure;
using Azure.AI.OpenAI;
using Voice2Action.Domain;
using Voice2Action.Infrastructure.AI;
using Voice2Action.McpUtility;

// Console app that exposes the Utility agent (transcription + current time) as an MCP HTTP server.
// Port override: MCP_HTTP_PORT_UTILITY

const string DefaultAudioDeployment = "whisper";

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(cfg => cfg.AddEnvironmentVariables())
    .ConfigureServices(services =>
    {
        var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? string.Empty;
        var key = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ?? string.Empty;
        var audioDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_AUDIO_DEPLOYMENT_NAME") ?? DefaultAudioDeployment;

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
        {
            Console.WriteLine("Warning: Azure OpenAI credentials not set; transcription will fail.");
        }

        // Provide an AzureOpenAIClient primarily so the existing transcription service (which may rely on it) works.
        if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(key))
        {
            services.AddSingleton(new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key)));
        }
        else
        {
            services.AddSingleton(new AzureOpenAIClient(new Uri("https://example.invalid"), new AzureKeyCredential("placeholder")));
        }

        // Domain services required by Utility tools
        services.AddSingleton<ITranscriptionService>(sp => new OpenAIAudioTranscriptionService(
            sp.GetRequiredService<AzureOpenAIClient>(),
            audioDeployment));
        services.AddSingleton<IDateTimeService, DateTimeService>();
    });

var host = builder.Build();
var sp = host.Services;
var transcription = sp.GetRequiredService<ITranscriptionService>();
var dateTime = sp.GetRequiredService<IDateTimeService>();
Console.WriteLine("Starting Utility MCP HTTP server (transcription + current time). Press Ctrl+C to exit.");
await UtilityMcpServer.RunAsync(transcription, dateTime, CancellationToken.None);

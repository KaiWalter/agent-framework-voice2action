using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Voice2Action.Domain;
using Voice2Action.Infrastructure.AI;

// (Removed top-level constant to avoid implicit compiler-generated Program partial class)

// NOTE: Previously tools were implemented as local functions which resulted in compiler-generated
// method names (e.g. s___Main___g__GetCurrentDateTime_2) being surfaced by the MCP server. By moving
// them into a static host class with real method names we ensure clean tool identifiers.
internal static class UtilityToolHost
{
    internal static ITranscriptionService? Transcription;
    internal static IDateTimeService? DateTimeService;

    [Description("Transcribe the given audio file (mp3/wav/m4a) using Azure OpenAI Whisper and return raw text.")]
    public static string TranscribeVoiceRecording([Description("Path to recording file.")] string recording)
    {
        if (Transcription is null) throw new InvalidOperationException("Transcription service not initialized yet.");
        if (string.IsNullOrWhiteSpace(recording)) throw new ArgumentException("Recording path empty", nameof(recording));
        if (!File.Exists(recording)) throw new FileNotFoundException("Audio file not found", recording);
        return Transcription.TranscribeAsync(recording).GetAwaiter().GetResult();
    }

    [Description("Return the current date/time to support normalization of relative or partial dates. Format: LOCAL=yyyy-MM-ddTHH:mm:ssK;UTC=yyyy-MM-ddTHH:mm:ssZ")]
    public static string GetCurrentDateTime()
    {
        if (DateTimeService is null) throw new InvalidOperationException("Date/time service not initialized yet.");
        return DateTimeService.GetCurrentDateTime();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

    var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? string.Empty;
    var key = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ?? string.Empty;
    var audioDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_AUDIO_DEPLOYMENT_NAME") ?? "whisper"; // default

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
        {
            Console.WriteLine("Warning: Azure OpenAI credentials not set; transcription will fail.");
        }

        // Provide an AzureOpenAIClient primarily so the existing transcription service (which may rely on it) works.
        if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(key))
        {
            builder.Services.AddSingleton(new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key)));
        }
        else
        {
            builder.Services.AddSingleton(new AzureOpenAIClient(new Uri("https://example.invalid"), new AzureKeyCredential("placeholder")));
        }

        // Domain services required by Utility tools
        builder.Services.AddSingleton<ITranscriptionService>(sp => new OpenAIAudioTranscriptionService(
            sp.GetRequiredService<AzureOpenAIClient>(),
            audioDeployment));
        builder.Services.AddSingleton<IDateTimeService, DateTimeService>();

        var tools = new[]
        {
            McpServerTool.Create(AIFunctionFactory.Create(new Func<string,string>(UtilityToolHost.TranscribeVoiceRecording))),
            McpServerTool.Create(AIFunctionFactory.Create(new Func<string>(UtilityToolHost.GetCurrentDateTime)))
        };

        // Optional port override
        var portEnv = Environment.GetEnvironmentVariable("MCP_HTTP_PORT_UTILITY");
        if (int.TryParse(portEnv, out var port) && port > 0)
        {
            builder.WebHost.UseKestrel(o => o.ListenAnyIP(port));
        }

        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools(tools);

        var app = builder.Build();
        var sp = app.Services;
        UtilityToolHost.Transcription = sp.GetRequiredService<ITranscriptionService>();
        UtilityToolHost.DateTimeService = sp.GetRequiredService<IDateTimeService>();

        app.MapGet("/healthz", () => new { status = "ok", tools = new[] { "TranscribeVoiceRecording", "GetCurrentDateTime" } });
        app.MapMcp();
        Console.WriteLine("Utility MCP HTTP server started. Tools: TranscribeVoiceRecording, GetCurrentDateTime");
        app.Run();
    }
}


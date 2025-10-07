using System.ComponentModel;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using Voice2Action.Domain;

namespace Voice2Action.McpUtility;

public static class UtilityMcpServer
{
    public static async Task RunAsync(ITranscriptionService transcription, IDateTimeService dateTimeService, CancellationToken cancellationToken = default)
    {
        if (transcription is null) throw new ArgumentNullException(nameof(transcription));
        if (dateTimeService is null) throw new ArgumentNullException(nameof(dateTimeService));

        [Description("Transcribe the given audio file (mp3/wav/m4a) using Azure OpenAI Whisper and return raw text.")]
        static string TranscribeVoiceRecordingInner(ITranscriptionService transcription, [Description("Path to recording file.")] string recording)
        {
            if (string.IsNullOrWhiteSpace(recording)) throw new ArgumentException("Recording path empty", nameof(recording));
            if (!File.Exists(recording)) throw new FileNotFoundException("Audio file not found", recording);
            return transcription.TranscribeAsync(recording).GetAwaiter().GetResult();
        }

        [Description("Return the current date/time to support normalization of relative or partial dates. Format: LOCAL=yyyy-MM-ddTHH:mm:ssK;UTC=yyyy-MM-ddTHH:mm:ssZ")]
        static string GetCurrentDateTimeInner(IDateTimeService dateTimeService) => dateTimeService.GetCurrentDateTime();

        string TranscribeVoiceRecording(string recording) => TranscribeVoiceRecordingInner(transcription, recording);
        string GetCurrentDateTime() => GetCurrentDateTimeInner(dateTimeService);

        var tools = new[]
        {
            McpServerTool.Create(AIFunctionFactory.Create(new Func<string,string>(TranscribeVoiceRecording))),
            McpServerTool.Create(AIFunctionFactory.Create(new Func<string>(GetCurrentDateTime)))
        };

        var webBuilder = WebApplication.CreateBuilder();
        var portEnv = Environment.GetEnvironmentVariable("MCP_HTTP_PORT_UTILITY");
        if (int.TryParse(portEnv, out var port) && port > 0)
        {
            webBuilder.WebHost.UseKestrel(o => o.ListenAnyIP(port));
        }

        webBuilder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools(tools);

        var app = webBuilder.Build();
        app.MapGet("/healthz", () => new { status = "ok", tools = new[] { "TranscribeVoiceRecording", "GetCurrentDateTime" } });
        app.MapMcp();
        Console.WriteLine("Utility MCP HTTP server started. Tools: TranscribeVoiceRecording, GetCurrentDateTime");
        await app.RunAsync(cancellationToken).ConfigureAwait(false);
    }
}

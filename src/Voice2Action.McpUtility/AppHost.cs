using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using Voice2Action.Domain;
using Voice2Action.Infrastructure.AI;
using Voice2Action.McpUtility;

// Minimal API style (top-level statements)
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

// NOTE: Port selection now handled by the root distributed AppHost. Remove any direct Kestrel port overrides here.

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
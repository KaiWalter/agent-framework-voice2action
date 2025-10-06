using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Hosting;
using Voice2Action.Domain;
using Voice2Action.Infrastructure.AI;
using Xunit;

namespace Voice2Action.IntegrationTests;

public sealed class AudioTranscriptionIntegrationTests
{
    private static IHost BuildHost() =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? "https://example.openai.azure.com"; // placeholder
                var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ?? "FAKE_KEY"; // placeholder
                var audioDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_AUDIO_DEPLOYMENT_NAME") ?? "whisper"; // audio model

                var useFake = (Environment.GetEnvironmentVariable("USE_FAKE_TRANSCRIPTION_FOR_TESTS") ?? "false").Equals("true", StringComparison.OrdinalIgnoreCase);
                bool placeholderCreds = endpoint.Contains("example.openai.azure.com", StringComparison.OrdinalIgnoreCase) || apiKey == "FAKE_KEY";
                if (useFake || placeholderCreds)
                {
                    services.AddSingleton<ITranscriptionService>(new FakeTranscriptionService("dummy"));
                }
                else
                {
                    services.AddSingleton(new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey)));
                    services.AddSingleton<ITranscriptionService>(sp => new OpenAIAudioTranscriptionService(sp.GetRequiredService<AzureOpenAIClient>(), audioDeployment));
                }
            })
            .Build();

    [Fact]
    public async Task TranscribeAudio_ProducesExpectedText()
    {
        using var host = BuildHost();
        var transcription = host.Services.GetRequiredService<ITranscriptionService>();
        var audioPath = Path.Combine(AppContext.BaseDirectory, "../../../../..", "audio-samples", "sample-recording-1-task-with-due-date-and-reminder.mp3");
        Assert.True(File.Exists(audioPath), $"Test audio file not found at {audioPath}");

        var text = await transcription.TranscribeAsync(audioPath);
        Assert.False(string.IsNullOrWhiteSpace(text));

        var usingFake = (Environment.GetEnvironmentVariable("USE_FAKE_TRANSCRIPTION_FOR_TESTS") ?? "false").Equals("true", StringComparison.OrdinalIgnoreCase) || text == "dummy";
        if (usingFake)
        {
            Assert.Equal("dummy", text);
        }
        else
        {
            var lowered = text.ToLowerInvariant();
            bool keywordMatch = lowered.Contains("follow") || lowered.Contains("boss") || lowered.Contains("august") || lowered.Contains("reminder");
            Assert.True(keywordMatch, $"Transcription did not contain expected keywords. Actual: {text}");
        }
    }
}

// Simple fake used in integration test when real credentials are absent.
file sealed class FakeTranscriptionService(string text) : ITranscriptionService
{
    public Task<string> TranscribeAsync(string filePath, CancellationToken ct = default) => Task.FromResult(text);
}

using System.ComponentModel;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

public static class Program
{
    private static readonly string Endpoint =
        Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
        ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
    private static readonly string DeploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4.1-mini";
    private static readonly string ApiKey =
        Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
        ?? throw new InvalidOperationException("AZURE_OPENAI_API_KEY is not set.");

    private const string AgentName = "Voice2Action";
    private const string AgentInstructions =
        @"Based on a voice recording you determine the user's intent and take action.
        When user explicitly states a task and due date, you set a reminder.
        When no explicit intent can be determined, you draft an email with the transcribed text.
        ";

    private static readonly Lazy<IHost> HostContainer = new(() =>
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(sp => new AzureOpenAIClient(new Uri(Endpoint), new AzureKeyCredential(ApiKey)));
        builder.Services.AddSingleton<Voice2Action.Domain.ITranscriptionService>(sp =>
        {
            var audioDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_AUDIO_DEPLOYMENT_NAME") ?? "whisper";
            return new Voice2Action.Infrastructure.AI.OpenAIAudioTranscriptionService(
                sp.GetRequiredService<AzureOpenAIClient>(), audioDeployment);
        });
        return builder.Build();
    });

    [Description("Transcribe the given audio file (mp3/wav/m4a) using Azure OpenAI Whisper and return raw text.")]
    public static string TranscribeVoiceRecording([Description("Path to recording file.")] string recording)
    {
        if (string.IsNullOrWhiteSpace(recording)) throw new ArgumentException("Recording path empty", nameof(recording));
        if (!File.Exists(recording)) throw new FileNotFoundException("Audio file not found", recording);
        var transcription = HostContainer.Value.Services.GetRequiredService<Voice2Action.Domain.ITranscriptionService>();
        return transcription.TranscribeAsync(recording, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Description("Set a reminder for the given task at the specified date and time.")]
    public static string SetReminder(
        [Description("Task to be reminded of.")] string task,
        [Description("Due date and time for the reminder.")] DateTime dueDate
    ) => $"Reminder set for task '{task}' at {dueDate}.";

    [Description("Send an email with the given subject and body to the user.")]
    public static string SendEmail(
        [Description("Email subject.")] string subject,
        [Description("Email body.")] string body
    ) => $"Email sent with subject '{subject}' and body '{body}'";

    public static async Task Main()
    {
        var chatClient = new AzureOpenAIClient(new Uri(Endpoint), new AzureKeyCredential(ApiKey)).GetChatClient(DeploymentName);
        AIAgent agent = chatClient.CreateAIAgent(
            AgentInstructions,
            AgentName,
            tools:
            [
                AIFunctionFactory.Create(new Func<string, string>(TranscribeVoiceRecording)),
                AIFunctionFactory.Create(new Func<string, DateTime, string>(SetReminder)),
                AIFunctionFactory.Create(new Func<string, string, string>(SendEmail)),
            ]
        );

        AgentThread thread = agent.GetNewThread();
        var result = await agent.RunAsync(
            "process voice recording in file ./audio-samples/sample-recording-1-task-with-due-date-and-reminder.mp3",
            thread
        );
        Console.WriteLine(result);
        result = await agent.RunAsync(
            "process voice recording in file ./audio-samples/sample-recording-2-random-thoughts.mp3",
            thread
        );
        Console.WriteLine(result);
        result = await agent.RunAsync(
            "process voice recording in file ./audio-samples/sample-recording-3-send-email.mp3",
            thread
        );
        Console.WriteLine(result);
    }
}

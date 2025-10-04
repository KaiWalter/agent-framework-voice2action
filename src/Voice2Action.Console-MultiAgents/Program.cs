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
    private const string coordinatingAgentInstructions =
        @"You are a team planner and coordinator. You do not need to fulfill the user's request directly. You solely rely on your team members.
        You collect and state the user's request with all available information and state a task that one of the team members can fulfill.
        General rules:
        - when there is a voice recording file mentioned, ask an agent to transcribe it first
        - when the transcription is available, ask the agents whether they can fulfill the request
        ";

    // @"Based on a voice recording you determine the user's intent and take action.
    // Your task is to coordinate between multiple specialized agents to fulfill the user's request.
    // You cannot fulfill the user's request directly. Respond by asking one of the specialized agents to do so and this response will be passed on to the available agents.
    // ";

    private const string utilityAgentName = "Utility";
    private const string utilityAgentInstructions =
        @"You are a specifically designed agent to handle general utility tasks based on the tools available to you.
        When you can help with a request, you do so directly or respond that you cannot help.
        Always respond in a concise manner.
        ";

    private const string officeAutomationAgentName = "OfficeAutomation";
    private const string officeAutomationAgentInstructions =
        @"You are a specifically designed agent to handle office automation tasks (e.g. sending emails, creating reminders) based on the tools available to you.
        When you can help with a request, you do so directly or respond that you cannot help.
        Always respond in a concise manner.
        ";

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
        var chatClient = new AzureOpenAIClient(
            new Uri(Endpoint),
            new AzureKeyCredential(ApiKey)
        ).GetChatClient(DeploymentName);
        AIAgent coordinator = chatClient.CreateAIAgent(
            coordinatingAgentName,
            coordinatingAgentInstructions
        );

        AIAgent utility = chatClient.CreateAIAgent(
            utilityAgentName,
            utilityAgentInstructions,
            tools: [AIFunctionFactory.Create(new Func<string, string>(TranscribeVoiceRecording))]
        );

        AIAgent officeAutomation = chatClient.CreateAIAgent(
            officeAutomationAgentName,
            officeAutomationAgentInstructions,
            tools:
            [
                AIFunctionFactory.Create(new Func<string, DateTime, string>(SetReminder)),
                AIFunctionFactory.Create(new Func<string, string, string>(SendEmail)),
            ]
        );

        // AgentThread thread = agent.GetNewThread();

        var result = await coordinator.RunAsync(
            "process voice recording in file ../../audio-samples/sample-recording-1-task-with-due-date-and-reminder.mp3"
        );
        Console.WriteLine(result);

        // var result = await agent.RunAsync(
        //     "process voice recording in file ./audio-samples/sample-recording-1-task-with-due-date-and-reminder.mp3",
        //     thread
        // );
        // Console.WriteLine(result);
        // result = await agent.RunAsync(
        //     "process voice recording in file ./audio-samples/sample-recording-2-random-thoughts.mp3",
        //     thread
        // );
        // Console.WriteLine(result);
        // result = await agent.RunAsync(
        //     "process voice recording in file ./audio-samples/sample-recording-3-send-email.mp3",
        //     thread
        // );
        // Console.WriteLine(result);
    }
}

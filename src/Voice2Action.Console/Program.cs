using Azure.AI.OpenAI;
using Azure.Core; // Core abstractions
using Azure; // AzureKeyCredential
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Voice2Action.Application;
using Voice2Action.Domain;
using Voice2Action.Infrastructure.AI;
using Voice2Action.Infrastructure.Messaging;

var builder = Host.CreateApplicationBuilder(args);

var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
              ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o";
var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
             ?? throw new InvalidOperationException("AZURE_OPENAI_API_KEY is not set.");

builder.Services.AddSingleton<IChatClient>(_ =>
    new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey))
        .GetChatClient(deployment)
        .AsIChatClient());

// Two distinct agents: one for spam detection, one for drafting.
// Register named instances using factory pattern.

builder.Services.AddSingleton<AIAgent>(sp =>
{
    var client = sp.GetRequiredService<IChatClient>();
    return new ChatClientAgent(client,
        new ChatClientAgentOptions("You are a spam detection assistant that identifies spam emails.")
        {
            ChatOptions = new() { ResponseFormat = ChatResponseFormat.ForJsonSchema<DetectionResult>() }
        });
});

builder.Services.AddSingleton<AIAgent>(sp =>
{
    var client = sp.GetRequiredService<IChatClient>();
    return new ChatClientAgent(client,
        new ChatClientAgentOptions("You are an email assistant that helps users draft responses professionally.")
        {
            ChatOptions = new() { ResponseFormat = ChatResponseFormat.ForJsonSchema<EmailResponse>() }
        });
});

// Register services mapping: by convention first agent resolves to spam detection, second to draft.
// For clarity in real app you'd use named options or wrapper types. Here keep it simple.

builder.Services.AddSingleton<ISpamDetectionService>(sp =>
{
    // Resolve first registered AIAgent
    var agents = sp.GetServices<AIAgent>().ToList();
    return new OpenAIAgentSpamDetectionService(agents[0]);
});

builder.Services.AddSingleton<IEmailDraftService>(sp =>
{
    var agents = sp.GetServices<AIAgent>().ToList();
    return new OpenAIAgentEmailDraftService(agents[1]);
});

builder.Services.AddSingleton<IEmailSender, ConsoleEmailSender>();
builder.Services.AddSingleton<ISpamDispositionService, ConsoleSpamDispositionService>();

builder.Services.AddTransient<ProcessIncomingEmail>();

var host = builder.Build();

var processor = host.Services.GetRequiredService<ProcessIncomingEmail>();
var sampleEmail = "This is NOT a joke! You are one of only 5 lucky winners selected from millions.";
await processor.HandleAsync(sampleEmail);

Console.WriteLine("Processing complete.");

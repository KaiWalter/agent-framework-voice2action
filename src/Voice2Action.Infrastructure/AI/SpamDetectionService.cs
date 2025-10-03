using System.Text.Json;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Voice2Action.Domain;

namespace Voice2Action.Infrastructure.AI;

public sealed class OpenAIAgentSpamDetectionService : ISpamDetectionService
{
    private readonly AIAgent _agent;
    public OpenAIAgentSpamDetectionService(AIAgent agent) => _agent = agent;

    public async Task<DetectionResult> DetectAsync(string emailContent, CancellationToken ct = default)
    {
        // Adapted to new signature: RunAsync(string message, AgentThread? thread, AgentRunOptions? options, CancellationToken ct)
        var response = await _agent.RunAsync(emailContent, thread: null, options: null, cancellationToken: ct);
        var detection = JsonSerializer.Deserialize<DetectionResult>(response.Text) 
                        ?? throw new InvalidOperationException("Invalid detection JSON");
        return detection;
    }
}

using System.Text.Json;
using Microsoft.Agents.AI;
using Voice2Action.Domain;

namespace Voice2Action.Infrastructure.AI;

public sealed class OpenAIAgentEmailDraftService : IEmailDraftService
{
    private readonly AIAgent _agent;
    public OpenAIAgentEmailDraftService(AIAgent agent) => _agent = agent;

    public async Task<EmailResponse> DraftReplyAsync(string emailContent, CancellationToken ct = default)
    {
        var response = await _agent.RunAsync(emailContent, thread: null, options: null, cancellationToken: ct);
        var draft = System.Text.Json.JsonSerializer.Deserialize<EmailResponse>(response.Text)
                    ?? throw new InvalidOperationException("Invalid email response JSON");
        return draft;
    }
}

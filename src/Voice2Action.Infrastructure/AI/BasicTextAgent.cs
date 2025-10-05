using Microsoft.Agents.AI;
using Voice2Action.Domain;

namespace Voice2Action.Infrastructure.AI;

internal sealed class BasicTextAgent : ITextAgent
{
    private readonly ChatClientAgent _inner;
    public string Name { get; }
    public string[] Capabilities { get; }
    public BasicTextAgent(string name, string[] capabilities, ChatClientAgent inner)
    {
        Name = name; Capabilities = capabilities; _inner = inner;
    }
    public async Task<string> RunAsync(string input, CancellationToken ct = default)
        => (await _inner.RunAsync(input, cancellationToken: ct)).ToString();
}

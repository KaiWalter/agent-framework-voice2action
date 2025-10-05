using Voice2Action.Domain;

namespace Voice2Action.Application;

/// <summary>
/// Provides a configured set of agents (coordinator + workers) for orchestration.
/// </summary>
public interface IAgentSetProvider
{
    Task<AgentSet> CreateAsync(CancellationToken ct = default);
}

namespace Voice2Action.Domain;

/// <summary>
/// High level action produced by multi-agent orchestration.
/// </summary>
public sealed class AgentActionRecord
{
    public string Agent { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // e.g. SetReminder, SendEmail, TranscribeVoiceRecording
    public string RawResult { get; set; } = string.Empty; // opaque raw agent output
}

/// <summary>
/// Aggregate result for a processed voice command.
/// </summary>
public sealed class OrchestrationResult
{
    /// <summary>Original audio file path supplied.</summary>
    public string AudioPath { get; set; } = string.Empty;
    /// <summary>Transcribed text (if obtained).</summary>
    public string? Transcription { get; set; }
    /// <summary>All actions invoked across agents in chronological order.</summary>
    public List<AgentActionRecord> Actions { get; set; } = new();
    /// <summary>Final textual summary supplied by the coordinator (if any).</summary>
    public string? Summary { get; set; }
}

/// <summary>
/// Minimal abstraction for an agent that can process text input and return text output.
/// </summary>
public interface ITextAgent
{
    string Name { get; }
    string[] Capabilities { get; }
    Task<string> RunAsync(string input, CancellationToken ct = default);
}

/// <summary>
/// Orchestrates multi-agent processing of an audio file: transcription, planning, execution of downstream actions.
/// </summary>
public interface IVoiceCommandOrchestrator
{
    Task<OrchestrationResult> ExecuteAsync(string audioPath, CancellationToken ct = default);
}

/// <summary>
/// Aggregates coordinator and worker agents for a voice command workflow.
/// </summary>
public sealed class AgentSet
{
    public ITextAgent Coordinator { get; set; } = default!;
    public IReadOnlyList<ITextAgent> Workers { get; set; } = Array.Empty<ITextAgent>();
}

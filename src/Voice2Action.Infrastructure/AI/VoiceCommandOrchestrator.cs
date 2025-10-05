using System.Text.Json;
using Microsoft.Agents.AI;
using Voice2Action.Domain;

namespace Voice2Action.Infrastructure.AI;

// Adapter so existing AIAgent instances can participate via the domain abstraction.
public sealed class AIAgentAdapter : ITextAgent
{
    private readonly AIAgent _inner;
    public string Name { get; }
    public string[] Capabilities { get; }
    public AIAgentAdapter(string name, AIAgent inner, string[] capabilities)
    {
        Name = name;
        _inner = inner;
        Capabilities = capabilities;
    }
    public async Task<string> RunAsync(string input, CancellationToken ct = default)
        => (await _inner.RunAsync(input, cancellationToken: ct)).ToString();
}

public sealed class VoiceCommandOrchestrator : IVoiceCommandOrchestrator
{
    private readonly ITextAgent _coordinator;
    private readonly IReadOnlyList<ITextAgent> _workers;

    private sealed class CoordinatorMessage
    {
        public string? Action { get; set; } // DELEGATE or DONE
        public string? Agent { get; set; }
        public string? Task { get; set; }
        public string? Summary { get; set; }
    }

    public VoiceCommandOrchestrator(ITextAgent coordinator, IEnumerable<ITextAgent> workers)
    {
        _coordinator = coordinator;
        _workers = workers.ToList();
    }

    public async Task<OrchestrationResult> ExecuteAsync(string audioPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(audioPath)) throw new ArgumentException("Audio path empty", nameof(audioPath));
        if (!File.Exists(audioPath)) throw new FileNotFoundException("Audio file not found", audioPath);

        var result = new OrchestrationResult { AudioPath = audioPath };
    string userRequest = $"process voice recording in file {audioPath}";
    string coordinatorInput = userRequest;
        int safetyIterations = 8;

        while (safetyIterations-- > 0 && !ct.IsCancellationRequested)
        {
            var coordRaw = await _coordinator.RunAsync(coordinatorInput, ct);
            // Record raw coordinator output for traceability (as a planning step)
            result.Actions.Add(new AgentActionRecord
            {
                Agent = _coordinator.Name,
                Action = "Plan",
                RawResult = coordRaw
            });
            CoordinatorMessage? parsed = null;
            try
            {
                parsed = JsonSerializer.Deserialize<CoordinatorMessage>(coordRaw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { /* ignore parse error, will handle below */ }

            if (parsed?.Action?.Equals("DONE", StringComparison.OrdinalIgnoreCase) == true)
            {
                result.Summary = parsed.Summary ?? "";
                break;
            }
            if (parsed?.Action?.Equals("DELEGATE", StringComparison.OrdinalIgnoreCase) != true || string.IsNullOrWhiteSpace(parsed.Agent) || string.IsNullOrWhiteSpace(parsed.Task))
            {
                coordinatorInput = "Malformed JSON. Return {\"Action\":\"DELEGATE|DONE\",...}.";
                continue;
            }

            var targetName = parsed.Agent;
            var task = parsed.Task;
            var registration = _workers.FirstOrDefault(a => a.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
            string workerOutput;
            if (registration is null)
            {
                workerOutput = $"UNKNOWN_AGENT:{targetName}";
            }
            else
            {
                workerOutput = await registration.RunAsync(task, ct);
                if (result.Transcription == null && registration.Capabilities.Any(c => c.StartsWith("Transcribe", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!string.IsNullOrWhiteSpace(workerOutput) && !workerOutput.StartsWith("CANNOT", StringComparison.OrdinalIgnoreCase))
                        result.Transcription = workerOutput.Trim();
                }
            }

            result.Actions.Add(new AgentActionRecord
            {
                Agent = targetName,
                Action = InferActionFromTask(task),
                RawResult = workerOutput
            });

            // Build enriched context for coordinator each loop so it always sees the transcript & history.
            coordinatorInput = JsonSerializer.Serialize(new
            {
                Transcript = result.Transcription,
                Actions = result.Actions.Select(a => new { a.Agent, a.Action, a.RawResult }),
                Guidance = "Decide next step. If a reminder or email can be created based on the transcript, delegate to OfficeAutomation with an explicit SetReminder or SendEmail task including extracted task, due date, optional reminder date. Otherwise gather missing info or DONE with summary.",
                RequiredResponse = new { Action = "DELEGATE|DONE", Agent = "<when delegating>", Task = "<instruction when delegating>", Summary = "<when done>" }
            });
        }

        return result;
    }

    private static string InferActionFromTask(string task)
    {
        if (task.Contains("Transcribe", StringComparison.OrdinalIgnoreCase)) return "Transcribe";
        if (task.Contains("SetReminder", StringComparison.OrdinalIgnoreCase)) return "SetReminder";
        if (task.Contains("SendEmail", StringComparison.OrdinalIgnoreCase)) return "SendEmail";
        if (task.Contains("GetCurrentDateTime", StringComparison.OrdinalIgnoreCase)) return "GetCurrentDateTime";
        return "Unknown";
    }
}

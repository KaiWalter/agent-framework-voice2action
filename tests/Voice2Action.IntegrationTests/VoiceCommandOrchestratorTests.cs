using System.Text.Json;
using Voice2Action.Domain;
using Voice2Action.Infrastructure.AI;
using Xunit;

namespace Voice2Action.IntegrationTests;

file sealed class FakeAgent(string name, string[] capabilities, Func<string, string> responder) : ITextAgent
{
    public string Name => name;
    public string[] Capabilities => capabilities;
    public Task<string> RunAsync(string input, CancellationToken ct = default) => Task.FromResult(responder(input));
}

public class VoiceCommandOrchestratorTests
{
    [Fact]
    public async Task Orchestrator_Completes_With_TwoDelegations()
    {
        // Arrange: Coordinator will delegate twice then finish.
        int step = 0;
        var coordinator = new FakeAgent(
            "Coordinator",
            Array.Empty<string>(),
            input =>
            {
                step++;
                return step switch
                {
                    1 => JsonSerializer.Serialize(new { Action = "DELEGATE", Agent = "Worker1", Task = "Transcribe something" }),
                    2 => JsonSerializer.Serialize(new { Action = "DELEGATE", Agent = "Worker2", Task = "SetReminder for task" }),
                    _ => JsonSerializer.Serialize(new { Action = "DONE", Summary = "All tasks completed" })
                };
            });
        var worker1 = new FakeAgent("Worker1", new[] { "TranscribeVoiceRecording" }, _ => "transcribed text");
        var worker2 = new FakeAgent("Worker2", new[] { "SetReminder" }, _ => "Reminder set");

        var orchestrator = new VoiceCommandOrchestrator(coordinator, new ITextAgent[] { worker1, worker2 });

        // Act
        var result = await orchestrator.ExecuteAsync(Path.GetTempFileName());

        // Assert
    // Expect 5 actions now: Plan, Worker1, Plan, Worker2, Plan(done)
    Assert.Equal(5, result.Actions.Count);
    Assert.Equal("Coordinator", result.Actions[0].Agent);
    Assert.Equal("Worker1", result.Actions[1].Agent);
    Assert.Equal("Coordinator", result.Actions[2].Agent);
    Assert.Equal("Worker2", result.Actions[3].Agent);
    Assert.Equal("Coordinator", result.Actions[4].Agent);
        Assert.Equal("All tasks completed", result.Summary);
    }
}

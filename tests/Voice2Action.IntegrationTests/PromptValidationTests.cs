using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Voice2Action.IntegrationTests;

public sealed class PromptValidationTests
{
    private static string LocatePromptsDir()
    {
        var start = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8; depth++)
        {
            var candidate = Path.GetFullPath(Path.Combine(Enumerable.Repeat("..", depth).Prepend(start).ToArray()));
            var probe = Path.Combine(candidate, "src", "Voice2Action.Infrastructure", "Prompts");
            if (Directory.Exists(probe)) return probe;
        }
        throw new DirectoryNotFoundException("Could not locate Prompts directory for validation tests.");
    }

    private readonly string _promptsDir = LocatePromptsDir();

    private string ReadPrompt(string file) => File.ReadAllText(Path.Combine(_promptsDir, file));

    [Fact]
    public void CoordinatorPrompt_ContainsKeySections()
    {
        var text = ReadPrompt("coordinator-template.md");
        Assert.Contains("You are the Orchestrator", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{{AGENT_CATALOG}}", text); // placeholder must remain for runtime injection
        Assert.Contains("INTERMEDIATE (context gathering", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TERMINAL (state / effect producing", text, StringComparison.OrdinalIgnoreCase);
        // Should not leak tool implementation assumptions (quick heuristic: no direct tool output examples)
        Assert.DoesNotContain("Transcribe voice recording:", text, StringComparison.OrdinalIgnoreCase); // legacy phrase must stay absent
    }

    [Fact]
    public void UtilityPrompt_IsToolOnly()
    {
        var text = ReadPrompt("utility.md");
        Assert.Contains("TranscribeVoiceRecording", text);
        Assert.Contains("GetCurrentDateTime", text);
        Assert.Contains("CANNOT:", text);
        // Ensure any appearance of planning words is in a prohibitive context ("do NOT").
        // Ensure no positive planning directives (like "Plan this" or "Delegate next")
        Assert.DoesNotContain(" delegate this", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" plan the", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OfficePrompt_IsToolOnly()
    {
        var text = ReadPrompt("office-automation.md");
        Assert.Contains("SetReminder", text);
    Assert.Contains("SendEmail", text);
    Assert.Contains("SendFallbackNotification", text);
        Assert.Contains("CANNOT:", text);
        Assert.DoesNotContain(" delegate this", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" plan the", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CoordinatorPrompt_DoesNotEmbedConcreteCapabilityNames()
    {
        var text = ReadPrompt("coordinator-template.md");
        // The coordinator template must stay generic and must not bake in concrete capability identifiers.
        foreach (var forbidden in new[] { "SetReminder", "SendEmail", "SendFallbackNotification", "TranscribeVoiceRecording", "GetCurrentDateTime" })
        {
            Assert.DoesNotContain(forbidden, text);
        }
    }
}

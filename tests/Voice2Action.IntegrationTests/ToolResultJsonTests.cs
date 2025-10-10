using System;
using System.Text.Json;
using Voice2Action.McpUtility;
using Voice2Action.McpOffice;
using Voice2Action.Domain;
using Xunit;

namespace Voice2Action.IntegrationTests;

// Basic shape tests for structured JSON tool outputs.
public sealed class ToolResultJsonTests
{
    private sealed class FakeTranscription(string text) : ITranscriptionService
    { public Task<string> TranscribeAsync(string filePath, CancellationToken ct = default) => Task.FromResult(text); }
    private sealed class FakeDateTime : IDateTimeService { public string GetCurrentDateTime() => "LOCAL=2025-10-10T10:00:00+00:00;UTC=2025-10-10T10:00:00Z"; }
    private sealed class FakeReminder : IReminderService { public string SetReminder(string task, DateTime due, DateTime? rem) => $"Reminder set:{task}:{due:yyyy-MM-dd}:{rem:yyyy-MM-dd}"; }
    private sealed class FakeEmail : IEmailService { public string SendEmail(string subject, string body) => $"Email:{subject}:{body.Length}"; }

    [Fact]
    public void Transcription_ReturnsJson()
    {
        UtilityToolHost.Transcription = new FakeTranscription("hello world");
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, "stub");
        var json = UtilityToolHost.TranscribeVoiceRecording(tmp);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("Transcription", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("hello world", doc.RootElement.GetProperty("data").GetProperty("text").GetString());
    }

    [Fact]
    public void Reminder_ReturnsJson()
    {
        OfficeToolHost.ReminderService = new FakeReminder();
        var json = OfficeToolHost.SetReminder("Task", new DateTime(2025,1,2), null);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Reminder", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("Task", doc.RootElement.GetProperty("data").GetProperty("task").GetString());
    }

    [Fact]
    public void Fallback_ErrorWhenBodyMissing()
    {
        OfficeToolHost.EmailService = new FakeEmail();
        var json = OfficeToolHost.SendFallbackNotification("Note", "");
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("FallbackNotification", doc.RootElement.GetProperty("type").GetString());
    }
}

namespace Voice2Action.Domain;

public interface ITranscriptionService
{
    /// <summary>
    /// Transcribes an audio file (mp3/wav/m4a) to plain text.
    /// </summary>
    /// <param name="filePath">Path to local audio file.</param>
    Task<string> TranscribeAsync(string filePath, CancellationToken ct = default);
}

/// <summary>
/// Provides current date/time information (both local and UTC) for normalization tasks.
/// </summary>
public interface IDateTimeService
{
    string GetCurrentDateTime();
}

/// <summary>
/// Schedules or records reminders. (Current implementation is a placeholder returning a formatted string.)
/// </summary>
public interface IReminderService
{
    string SetReminder(string task, DateTime dueDate, DateTime? reminderDate);
}

/// <summary>
/// Sends emails (placeholder returning a formatted string in the sample).
/// </summary>
public interface IEmailService
{
    string SendEmail(string subject, string body);
}

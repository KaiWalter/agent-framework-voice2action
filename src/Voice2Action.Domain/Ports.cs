namespace Voice2Action.Domain;

public interface ISpamDetectionService
{
    Task<DetectionResult> DetectAsync(string emailContent, CancellationToken ct = default);
}

public interface IEmailDraftService
{
    Task<EmailResponse> DraftReplyAsync(string emailContent, CancellationToken ct = default);
}

public interface IEmailSender
{
    Task SendAsync(EmailResponse response, CancellationToken ct = default);
}

public interface ISpamDispositionService
{
    Task HandleSpamAsync(DetectionResult detection, CancellationToken ct = default);
}

public interface ITranscriptionService
{
    /// <summary>
    /// Transcribes an audio file (mp3/wav/m4a) to plain text.
    /// </summary>
    /// <param name="filePath">Path to local audio file.</param>
    Task<string> TranscribeAsync(string filePath, CancellationToken ct = default);
}

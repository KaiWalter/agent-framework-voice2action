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

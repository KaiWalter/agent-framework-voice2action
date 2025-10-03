using Voice2Action.Domain;

namespace Voice2Action.Infrastructure.Messaging;

public sealed class ConsoleEmailSender : IEmailSender
{
    public Task SendAsync(EmailResponse response, CancellationToken ct = default)
    {
        Console.WriteLine($"Email sent: {response.Response}");
        return Task.CompletedTask;
    }
}

public sealed class ConsoleSpamDispositionService : ISpamDispositionService
{
    public Task HandleSpamAsync(DetectionResult detection, CancellationToken ct = default)
    {
        Console.WriteLine($"Email marked as spam: {detection.Reason}");
        return Task.CompletedTask;
    }
}

using Voice2Action.Domain;

namespace Voice2Action.Infrastructure.AI;

public sealed class EmailService : IEmailService
{
    public string SendEmail(string subject, string body) => $"Email sent with subject '{subject}' and body '{body}'";
}

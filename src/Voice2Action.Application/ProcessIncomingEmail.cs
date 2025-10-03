using Voice2Action.Domain;

namespace Voice2Action.Application;

public sealed class ProcessIncomingEmail
{
    private readonly ISpamDetectionService _spam;
    private readonly IEmailDraftService _draft;
    private readonly IEmailSender _sender;
    private readonly ISpamDispositionService _spamDisposition;

    public ProcessIncomingEmail(
        ISpamDetectionService spam,
        IEmailDraftService draft,
        IEmailSender sender,
        ISpamDispositionService spamDisposition)
    {
        _spam = spam;
        _draft = draft;
        _sender = sender;
        _spamDisposition = spamDisposition;
    }

    public async Task HandleAsync(string rawEmail, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawEmail))
            throw new ArgumentException("Email content is empty", nameof(rawEmail));

        var detection = await _spam.DetectAsync(rawEmail, ct);
        if (detection.IsSpam)
        {
            await _spamDisposition.HandleSpamAsync(detection, ct);
            return;
        }

        var reply = await _draft.DraftReplyAsync(rawEmail, ct);
        await _sender.SendAsync(reply, ct);
    }
}

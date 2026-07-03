namespace ThisCafeteria.Infrastructure.Services;

public sealed record EmailAttachmentData(string FileName, string ContentType, byte[] Content);

public sealed record OutboundEmail(
    string To,
    string Subject,
    string PlainTextBody,
    IReadOnlyList<EmailAttachmentData>? Attachments = null);

public interface IEmailSender
{
    Task SendAsync(OutboundEmail email, CancellationToken cancellationToken = default);
}

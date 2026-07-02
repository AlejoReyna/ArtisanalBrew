using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ThisCafeteria.Infrastructure.Configuration;

namespace ThisCafeteria.Infrastructure.Services;

// Backs the AWS-flavored IEmailSender contract with Azure Communication Services Email.
public sealed class SesEmailSender(
    IOptions<AzureOptions> options,
    ILogger<SesEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(OutboundEmail email, CancellationToken cancellationToken = default)
    {
        var communicationOptions = options.Value.Communication;
        if (string.IsNullOrWhiteSpace(communicationOptions.ConnectionString) ||
            string.IsNullOrWhiteSpace(communicationOptions.SenderAddress))
        {
            logger.LogWarning(
                "Email send skipped for {Recipient}; Azure Communication Services is not configured.",
                email.To);
            return;
        }

        var emailClient = new EmailClient(communicationOptions.ConnectionString);

        var content = new EmailContent(email.Subject) { PlainText = email.PlainTextBody };
        var message = new EmailMessage(communicationOptions.SenderAddress, email.To, content);

        if (email.Attachments is not null)
        {
            foreach (var attachment in email.Attachments)
            {
                message.Attachments.Add(new EmailAttachment(
                    attachment.FileName,
                    attachment.ContentType,
                    BinaryData.FromBytes(attachment.Content)));
            }
        }

        await emailClient.SendAsync(WaitUntil.Completed, message, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Email sent via Azure Communication Services to {Recipient}", email.To);
    }
}

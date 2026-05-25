using Microsoft.Extensions.Logging;

namespace ThisCafeteria.Infrastructure.Services;

public sealed class SesEmailSender(ILogger<SesEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("SES email placeholder to {Recipient} with subject {Subject}", to, subject);
        return Task.CompletedTask;
    }
}

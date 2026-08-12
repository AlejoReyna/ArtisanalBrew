using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ThisCafeteria.Infrastructure.Configuration;

namespace ThisCafeteria.Infrastructure.Services;

public sealed class ResendEmailSender(
    HttpClient httpClient,
    IOptions<ResendOptions> options,
    ILogger<ResendEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(OutboundEmail email, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);

        var resend = options.Value;
        if (string.IsNullOrWhiteSpace(resend.ApiKey))
        {
            throw new InvalidOperationException("Resend:ApiKey is not configured.");
        }

        if (string.IsNullOrWhiteSpace(resend.FromAddress))
        {
            throw new InvalidOperationException("Resend:FromAddress is not configured.");
        }

        var from = string.IsNullOrWhiteSpace(resend.FromName)
            ? resend.FromAddress.Trim()
            : $"{resend.FromName.Trim()} <{resend.FromAddress.Trim()}>";
        var attachments = email.Attachments?
            .Select(attachment => new ResendAttachment(
                attachment.FileName,
                Convert.ToBase64String(attachment.Content)))
            .ToArray();

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new ResendEmailRequest(
                from,
                [email.To],
                email.Subject,
                email.PlainTextBody,
                attachments))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", resend.ApiKey.Trim());
        if (!string.IsNullOrWhiteSpace(email.IdempotencyKey))
        {
            request.Headers.Add("Idempotency-Key", email.IdempotencyKey.Trim());
        }

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"Resend rejected the email with HTTP {(int)response.StatusCode}: {responseBody}",
                null,
                response.StatusCode);
        }

        var result = await response.Content.ReadFromJsonAsync<ResendEmailResponse>(cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation(
            "Email accepted by Resend. Recipient={Recipient}, ResendEmailId={ResendEmailId}",
            email.To,
            result?.Id);
    }

    private sealed record ResendEmailRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("attachments")]
        ResendAttachment[]? Attachments);

    private sealed record ResendAttachment(
        [property: JsonPropertyName("filename")] string FileName,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ResendEmailResponse(
        [property: JsonPropertyName("id")] string Id);
}

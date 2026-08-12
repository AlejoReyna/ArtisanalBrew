using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ThisCafeteria.Infrastructure.Configuration;
using ThisCafeteria.Infrastructure.Services;

namespace ThisCafeteria.UnitTests;

public sealed class ResendEmailSenderTests
{
    [Fact]
    public async Task SendAsync_SendsAttachmentAndIdempotencyKey()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"email_123\"}", Encoding.UTF8, "application/json")
            };
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.test/") };
        var sender = new ResendEmailSender(
            client,
            Options.Create(new ResendOptions
            {
                ApiKey = "re_test",
                FromAddress = "receipts@send.example.com",
                FromName = "ArtisanalBrew"
            }),
            NullLogger<ResendEmailSender>.Instance);

        await sender.SendAsync(new OutboundEmail(
            "customer@example.com",
            "Receipt",
            "Thanks",
            [new EmailAttachmentData("receipt.pdf", "application/pdf", [1, 2, 3])],
            "receipt/order-123"));

        captured.Should().NotBeNull();
        captured!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        captured.Headers.Authorization.Parameter.Should().Be("re_test");
        captured.Headers.GetValues("Idempotency-Key").Should().ContainSingle("receipt/order-123");

        using var json = JsonDocument.Parse(body!);
        var root = json.RootElement;
        root.GetProperty("from").GetString().Should().Be("ArtisanalBrew <receipts@send.example.com>");
        root.GetProperty("to")[0].GetString().Should().Be("customer@example.com");
        root.GetProperty("attachments")[0].GetProperty("filename").GetString().Should().Be("receipt.pdf");
        root.GetProperty("attachments")[0].GetProperty("content").GetString().Should().Be("AQID");
    }

    [Fact]
    public async Task SendAsync_WhenResendRejectsRequest_ThrowsWithStatus()
    {
        var handler = new StubHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("{\"message\":\"domain not verified\"}")
        }));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.test/") };
        var sender = new ResendEmailSender(
            client,
            Options.Create(new ResendOptions
            {
                ApiKey = "re_test",
                FromAddress = "receipts@send.example.com"
            }),
            NullLogger<ResendEmailSender>.Instance);

        var action = () => sender.SendAsync(new OutboundEmail("customer@example.com", "Receipt", "Thanks"));

        await action.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*HTTP 422*domain not verified*");
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request);
    }
}

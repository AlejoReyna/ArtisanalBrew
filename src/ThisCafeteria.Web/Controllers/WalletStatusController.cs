using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Nethereum.Util;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Services;
using ThisCafeteria.Web.Models;

namespace ThisCafeteria.Web.Controllers;

[ApiController]
[Route("api/wallet-status")]
public sealed class WalletStatusController(
    IWalletStatusEventRepository repository,
    ISqsMessagePublisher publisher,
    ILogger<WalletStatusController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateAsync(
        [FromBody] WalletStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeAddress(request.WalletAddress, out var walletAddress))
        {
            return BadRequest("A valid walletAddress is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Status))
        {
            return BadRequest("status is required.");
        }

        var payloadJson = request.Payload.HasValue
            ? JsonSerializer.Serialize(request.Payload.Value)
            : null;

        var statusEvent = new WalletStatusEvent
        {
            WalletAddress = walletAddress,
            Status = request.Status.Trim(),
            EventType = string.IsNullOrWhiteSpace(request.EventType) ? null : request.EventType.Trim(),
            PayloadJson = payloadJson,
            CreatedAt = DateTimeOffset.UtcNow
        };

        logger.LogInformation(
            "Wallet status event received. WalletAddress={WalletAddress}, Status={Status}, EventType={EventType}",
            walletAddress,
            statusEvent.Status,
            statusEvent.EventType);

        await repository.AddAsync(statusEvent, cancellationToken);
        logger.LogInformation("Wallet status event stored. Id={WalletStatusEventId}", statusEvent.Id);

        string? awsMessageId = null;
        try
        {
            awsMessageId = await publisher.PublishAsync(
                new WalletStatusMessage(
                    statusEvent.WalletAddress,
                    statusEvent.Status,
                    statusEvent.EventType,
                    request.Payload,
                    statusEvent.CreatedAt),
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(awsMessageId))
            {
                await repository.MarkPublishedToAwsAsync(
                    statusEvent.Id,
                    awsMessageId,
                    DateTimeOffset.UtcNow,
                    cancellationToken);

                logger.LogInformation(
                    "Wallet status event published to SQS. Id={WalletStatusEventId}, AwsMessageId={AwsMessageId}",
                    statusEvent.Id,
                    awsMessageId);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Wallet status event was stored but could not be published to SQS. Id={WalletStatusEventId}",
                statusEvent.Id);
        }

        return Ok(new WalletStatusCreateResponse(
            statusEvent.Id,
            statusEvent.WalletAddress,
            statusEvent.Status,
            statusEvent.EventType,
            statusEvent.CreatedAt,
            !string.IsNullOrWhiteSpace(awsMessageId),
            awsMessageId));
    }

    [HttpGet("{walletAddress}")]
    public async Task<IActionResult> GetLatestAsync(
        string walletAddress,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeAddress(walletAddress, out var normalizedWalletAddress))
        {
            return BadRequest("A valid walletAddress is required.");
        }

        var latest = await repository.GetLatestForWalletAsync(normalizedWalletAddress, cancellationToken);
        return latest is null ? NotFound() : Ok(Map(latest));
    }

    private static WalletStatusResponse Map(WalletStatusEvent statusEvent) => new(
        statusEvent.Id,
        statusEvent.WalletAddress,
        statusEvent.Status,
        statusEvent.EventType,
        ParsePayload(statusEvent.PayloadJson),
        statusEvent.CreatedAt,
        statusEvent.PublishedToAwsAtUtc,
        statusEvent.AwsMessageId);

    private static JsonElement? ParsePayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(payloadJson);
        return document.RootElement.Clone();
    }

    private static bool TryNormalizeAddress(string? address, out string normalizedAddress)
    {
        normalizedAddress = string.Empty;
        if (string.IsNullOrWhiteSpace(address) || !AddressUtil.Current.IsValidEthereumAddressHexFormat(address))
        {
            return false;
        }

        normalizedAddress = AddressUtil.Current.ConvertToChecksumAddress(address);
        return true;
    }
}

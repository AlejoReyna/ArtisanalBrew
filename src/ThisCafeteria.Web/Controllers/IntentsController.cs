using System.Numerics;
using Microsoft.AspNetCore.Mvc;
using ThisCafeteria.Application.Services;

namespace ThisCafeteria.Web.Controllers;

/// <summary>
/// Phase 5's quote-preview surface: lets a caller ask what the standing cross-chain solver would
/// pay out for a hypothetical intent before ever submitting one on-chain. See
/// <see cref="IIntentQuoteService"/> for why this reuses the solver's own policy rather than a
/// separate pricing calculation.
/// </summary>
[ApiController]
[Route("api/intents")]
public sealed class IntentsController(IIntentQuoteService quoteService) : ControllerBase
{
    [HttpGet("quote")]
    public IActionResult GetQuote([FromQuery] string sourceToken, [FromQuery] string destinationToken, [FromQuery] string amountIn)
    {
        if (string.IsNullOrWhiteSpace(sourceToken) || string.IsNullOrWhiteSpace(destinationToken))
        {
            return BadRequest("sourceToken and destinationToken are required.");
        }

        if (!BigInteger.TryParse(amountIn, out var parsedAmountIn) || parsedAmountIn <= BigInteger.Zero)
        {
            return BadRequest("amountIn must be a positive integer, in the source token's base units.");
        }

        var quote = quoteService.GetQuote(new IntentQuoteRequest
        {
            SourceToken = sourceToken,
            DestinationToken = destinationToken,
            AmountIn = parsedAmountIn
        });

        if (!quote.Fillable)
        {
            return Ok(new
            {
                fillable = false,
                reason = quote.DenialReason?.ToString(),
                detail = quote.Detail
            });
        }

        return Ok(new
        {
            fillable = true,
            amountOut = quote.AmountOut.ToString(),
            sourceChainKey = quote.SourceChainKey,
            destinationChainKey = quote.DestinationChainKey
        });
    }
}

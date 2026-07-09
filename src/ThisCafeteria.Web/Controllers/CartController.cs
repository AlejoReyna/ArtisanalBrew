using Microsoft.AspNetCore.Mvc;
using ThisCafeteria.Web.Services.Cart;

namespace ThisCafeteria.Web.Controllers;

[ApiController]
[Route("api/cart")]
[IgnoreAntiforgeryToken]
public sealed class CartController(IShoppingCartService cartService) : ControllerBase
{
    [HttpPost("items")]
    public async Task<IActionResult> AddItem([FromBody] AddCartItemRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Slug))
        {
            return BadRequest("Product slug is required.");
        }

        try
        {
            await cartService.AddAsync(request.Slug, request.Quantity, cancellationToken);
            var lines = await cartService.GetLinesAsync(cancellationToken);
            var itemCount = lines.Sum(line => line.Quantity);
            return Ok(new CartMutationResponse(itemCount, lines));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return BadRequest(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpPut("items/{slug}")]
    public async Task<IActionResult> SetQuantity(
        string slug,
        [FromBody] SetCartItemQuantityRequest request,
        CancellationToken cancellationToken)
    {
        await cartService.SetQuantityAsync(slug, request.Quantity, cancellationToken);
        return await BuildMutationResponseAsync(cancellationToken);
    }

    [HttpDelete("items/{slug}")]
    public async Task<IActionResult> RemoveItem(string slug, CancellationToken cancellationToken)
    {
        await cartService.RemoveAsync(slug, cancellationToken);
        return await BuildMutationResponseAsync(cancellationToken);
    }

    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
    {
        await cartService.ClearAsync(cancellationToken);
        return await BuildMutationResponseAsync(cancellationToken);
    }

    private async Task<IActionResult> BuildMutationResponseAsync(CancellationToken cancellationToken)
    {
        var lines = await cartService.GetLinesAsync(cancellationToken);
        var itemCount = lines.Sum(line => line.Quantity);
        return Ok(new CartMutationResponse(itemCount, lines));
    }

    public sealed record AddCartItemRequest(string Slug, int Quantity = 1);

    public sealed record SetCartItemQuantityRequest(int Quantity);

    public sealed record CartMutationResponse(int ItemCount, IReadOnlyList<MarketplaceCartLine> Lines);
}

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

    public sealed record AddCartItemRequest(string Slug, int Quantity = 1);

    public sealed record CartMutationResponse(int ItemCount, IReadOnlyList<MarketplaceCartLine> Lines);
}

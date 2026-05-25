using Microsoft.JSInterop;

namespace ThisCafeteria.Web.Services.Cart;

public interface ICartMutationClient
{
    Task AddAsync(string slug, int quantity = 1, CancellationToken cancellationToken = default);
}

public sealed class CartMutationClient(
    IJSRuntime jsRuntime,
    IHttpContextAccessor httpContextAccessor,
    IShoppingCartService cartService,
    ILogger<CartMutationClient> logger) : ICartMutationClient
{
    public async Task AddAsync(string slug, int quantity = 1, CancellationToken cancellationToken = default)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is not null && !httpContext.Response.HasStarted)
        {
            logger.LogDebug("Cart add via direct service (response not started). Slug={Slug}", slug);
            await cartService.AddAsync(slug, quantity, cancellationToken);
            return;
        }

        logger.LogInformation(
            "Cart add via browser fetch. Slug={Slug}, ResponseStarted={ResponseStarted}",
            slug,
            httpContext?.Response.HasStarted);

        IJSObjectReference? module = null;
        try
        {
            module = await jsRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                cancellationToken,
                "/js/cartApi.js");

            var result = await module.InvokeAsync<CartMutationResponse>(
                "addItem",
                cancellationToken,
                slug,
                quantity);

            if (result?.Lines is null || result.Lines.Count == 0)
            {
                throw new InvalidOperationException("Cart API returned no lines.");
            }

            cartService.ApplyCircuitSnapshot(result.Lines);
            logger.LogInformation(
                "Cart circuit synced from browser API. ItemCount={ItemCount}, LineCount={LineCount}",
                result.ItemCount,
                result.Lines.Count);
        }
        catch (JSException exception)
        {
            logger.LogError(exception, "Cart browser API call failed for slug {Slug}", slug);
            throw new InvalidOperationException("Cart could not be updated.", exception);
        }
        finally
        {
            if (module is not null)
            {
                await module.DisposeAsync();
            }
        }
    }

    private sealed class CartMutationResponse
    {
        public int ItemCount { get; set; }

        public List<MarketplaceCartLine> Lines { get; set; } = [];
    }
}

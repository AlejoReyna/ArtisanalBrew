using Microsoft.JSInterop;

namespace ThisCafeteria.Web.Services.Cart;

public interface ICartMutationClient
{
    Task AddAsync(string slug, int quantity = 1, CancellationToken cancellationToken = default);

    Task SetQuantityAsync(string slug, int quantity, CancellationToken cancellationToken = default);

    Task RemoveAsync(string slug, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Mutating the cart from a Blazor Server circuit event (e.g. a button click handled after the
/// page's original HTTP response already completed) leaves <see cref="IHttpContextAccessor.HttpContext"/>
/// stale, so <see cref="ShoppingCartService"/> can only update its in-memory circuit copy - the
/// session/cookie never get rewritten, and the next storage refresh resurrects the old cart. When that's
/// the case, these methods fall back to a real browser fetch (a fresh HTTP request/response) so the
/// persisted cart actually changes, then sync the result back into the circuit.
/// </summary>
public sealed class CartMutationClient(
    IJSRuntime jsRuntime,
    IHttpContextAccessor httpContextAccessor,
    IShoppingCartService cartService,
    ILogger<CartMutationClient> logger) : ICartMutationClient
{
    public Task AddAsync(string slug, int quantity = 1, CancellationToken cancellationToken = default) =>
        MutateAsync(
            "add",
            slug,
            () => cartService.AddAsync(slug, quantity, cancellationToken),
            module => module.InvokeAsync<CartMutationResponse>("addItem", cancellationToken, slug, quantity),
            cancellationToken);

    public Task SetQuantityAsync(string slug, int quantity, CancellationToken cancellationToken = default) =>
        MutateAsync(
            "set quantity",
            slug,
            () => cartService.SetQuantityAsync(slug, quantity, cancellationToken),
            module => module.InvokeAsync<CartMutationResponse>("setQuantity", cancellationToken, slug, quantity),
            cancellationToken);

    public Task RemoveAsync(string slug, CancellationToken cancellationToken = default) =>
        MutateAsync(
            "remove",
            slug,
            () => cartService.RemoveAsync(slug, cancellationToken),
            module => module.InvokeAsync<CartMutationResponse>("removeItem", cancellationToken, slug),
            cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        MutateAsync(
            "clear",
            null,
            () => cartService.ClearAsync(cancellationToken),
            module => module.InvokeAsync<CartMutationResponse>("clearCart", cancellationToken),
            cancellationToken);

    private async Task MutateAsync(
        string action,
        string? slug,
        Func<Task> viaService,
        Func<IJSObjectReference, ValueTask<CartMutationResponse>> viaBrowserFetch,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is not null && !httpContext.Response.HasStarted)
        {
            logger.LogDebug("Cart {Action} via direct service (response not started). Slug={Slug}", action, slug);
            await viaService();
            return;
        }

        logger.LogInformation(
            "Cart {Action} via browser fetch. Slug={Slug}, ResponseStarted={ResponseStarted}",
            action,
            slug,
            httpContext?.Response.HasStarted);

        IJSObjectReference? module = null;
        try
        {
            module = await jsRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                cancellationToken,
                "/js/cartApi.js");

            var result = await viaBrowserFetch(module);

            if (result?.Lines is null)
            {
                throw new InvalidOperationException("Cart API returned no lines.");
            }

            cartService.ApplyCircuitSnapshot(result.Lines);
            logger.LogInformation(
                "Cart circuit synced from browser API. Action={Action}, ItemCount={ItemCount}, LineCount={LineCount}",
                action,
                result.ItemCount,
                result.Lines.Count);
        }
        catch (JSException exception)
        {
            logger.LogError(exception, "Cart browser API call failed. Action={Action}, Slug={Slug}", action, slug);
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

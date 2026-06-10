using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Services;

namespace ThisCafeteria.Web.Services.Cart;

public sealed class ShoppingCartService(
    IHttpContextAccessor httpContextAccessor,
    IProductService productService,
    ILogger<ShoppingCartService> logger) : IShoppingCartService
{
    private const string SessionKey = "MarketplaceCart";
    private const string CookieKey = "MarketplaceCart";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly CookieOptions CookieOptions = new()
    {
        HttpOnly = true,
        IsEssential = true,
        SameSite = SameSiteMode.Lax,
        MaxAge = TimeSpan.FromHours(8)
    };

    private List<MarketplaceCartLine>? _circuitLines;

    public event Action? Changed;

    public async Task<IReadOnlyList<MarketplaceCartLine>> GetLinesAsync(CancellationToken cancellationToken = default) =>
        await LoadAsync(cancellationToken);

    public async Task<int> GetItemCountAsync(CancellationToken cancellationToken = default)
    {
        var lines = await LoadAsync(cancellationToken);
        return lines.Sum(line => line.Quantity);
    }

    public async Task<decimal> GetSubtotalAsync(CancellationToken cancellationToken = default)
    {
        var lines = await LoadAsync(cancellationToken);
        return lines.Sum(line => line.LineTotal);
    }

    public async Task AddAsync(string slug, int quantity = 1, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Cart AddAsync start. Slug={Slug}, Quantity={Quantity}, HttpContext={HasHttpContext}",
            slug,
            quantity,
            httpContextAccessor.HttpContext is not null);

        if (quantity < 1)
        {
            logger.LogWarning("Cart AddAsync rejected invalid quantity {Quantity} for slug {Slug}", quantity, slug);
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be at least 1.");
        }

        var product = await productService.GetProductBySlugAsync(slug, cancellationToken);

        if (product is null)
        {
            logger.LogError("Cart AddAsync product miss. Slug={Slug}", slug);
            throw new InvalidOperationException($"Product '{slug}' was not found in the catalog.");
        }

        if (!product.IsActive)
        {
            logger.LogWarning("Cart AddAsync rejected inactive product {Slug}", slug);
            throw new InvalidOperationException($"Product '{slug}' is not available.");
        }

        if (product.StockQuantity < quantity)
        {
            logger.LogWarning(
                "Cart AddAsync rejected insufficient stock. Slug={Slug}, Requested={Quantity}, Stock={Stock}",
                slug,
                quantity,
                product.StockQuantity);
            throw new InvalidOperationException($"Only {product.StockQuantity} unit(s) of '{product.Name}' are available.");
        }

        logger.LogDebug(
            "Cart resolved product. Slug={Slug}, Name={Name}, Price={Price}",
            product.Slug,
            product.Name,
            product.Price);

        var lines = await LoadAsync(cancellationToken);
        var existing = lines.FindIndex(line => line.Slug == slug);
        if (existing >= 0)
        {
            var current = lines[existing];
            lines[existing] = current with { Quantity = current.Quantity + quantity };
            logger.LogInformation(
                "Cart updated existing line. Slug={Slug}, OldQty={OldQty}, Added={Added}, NewQty={NewQty}",
                slug,
                current.Quantity,
                quantity,
                lines[existing].Quantity);
        }
        else
        {
            lines.Add(new MarketplaceCartLine(
                product.Slug,
                product.Name,
                ImageClassFor(product.Category),
                product.Price,
                quantity,
                product.ImageUrl,
                product.Id));
            logger.LogInformation(
                "Cart added new line. Slug={Slug}, Quantity={Quantity}, LineCount={LineCount}",
                slug,
                quantity,
                lines.Count);
        }

        await SaveAsync(lines, cancellationToken);
    }

    public async Task SetQuantityAsync(string slug, int quantity, CancellationToken cancellationToken = default)
    {
        var lines = await LoadAsync(cancellationToken);
        var index = lines.FindIndex(line => line.Slug == slug);
        if (index < 0)
        {
            logger.LogWarning("Cart SetQuantityAsync skipped unknown slug {Slug}", slug);
            return;
        }

        if (quantity <= 0)
        {
            lines.RemoveAt(index);
            logger.LogInformation("Cart removed line via zero quantity. Slug={Slug}", slug);
        }
        else
        {
            var current = lines[index];
            lines[index] = current with { Quantity = quantity };
            logger.LogInformation("Cart set quantity. Slug={Slug}, Quantity={Quantity}", slug, quantity);
        }

        await SaveAsync(lines, cancellationToken);
    }

    public async Task RemoveAsync(string slug, CancellationToken cancellationToken = default)
    {
        var lines = await LoadAsync(cancellationToken);
        var removed = lines.RemoveAll(line => line.Slug == slug);
        logger.LogInformation("Cart RemoveAsync slug={Slug}, Removed={Removed}", slug, removed);
        await SaveAsync(lines, cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Cart cleared");
        await SaveAsync([], cancellationToken);
    }

    public async Task<IReadOnlyCollection<CartItemDto>> ToOrderItemsAsync(CancellationToken cancellationToken = default)
    {
        var lines = await LoadAsync(cancellationToken);
        return lines.Select(line => new CartItemDto(
            line.ProductId == Guid.Empty ? ProductIdFromSlug(line.Slug) : line.ProductId,
            line.Name,
            line.Quantity,
            line.UnitPrice)).ToArray();
    }

    public async Task RefreshFromStorageAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Cart RefreshFromStorageAsync START. CircuitLinesCount={CircuitCount}", _circuitLines?.Count ?? 0);
        var circuitCount = _circuitLines?.Count ?? 0;
        await ReloadSessionFromStoreAsync(cancellationToken);

        var sessionLines = await LoadFromSessionAsync(cancellationToken);
        var cookieLines = LoadFromCookie();
        
        // Prioritize cookie over session since session doesn't persist across circuit reconnects
        var storedLines = cookieLines.Count > 0 ? cookieLines : sessionLines;

        if (storedLines.Count > 0)
        {
            _circuitLines = storedLines;
            logger.LogInformation(
                "Cart refreshed from storage. LineCount={LineCount}, Source={Source}",
                storedLines.Count,
                cookieLines.Count > 0 ? "cookie" : "session");
        }
        else if (_circuitLines is null)
        {
            // Only reset to empty if we have no existing circuit lines
            _circuitLines = [];
            logger.LogInformation("Cart refreshed with empty storage (no existing circuit lines)");
        }
        else
        {
            // Keep existing circuit lines if storage is empty but we already have data
            logger.LogDebug(
                "Cart refresh kept circuit memory (storage empty but circuit has data). LineCount={LineCount}",
                circuitCount);
        }

        logger.LogInformation("Cart RefreshFromStorageAsync END. FinalCircuitLinesCount={FinalCount}", _circuitLines?.Count ?? 0);
        Changed?.Invoke();
    }

    public void ApplyCircuitSnapshot(IReadOnlyList<MarketplaceCartLine> lines)
    {
        _circuitLines = lines.ToList();
        logger.LogInformation("Cart circuit snapshot applied. LineCount={LineCount}", _circuitLines.Count);
        Changed?.Invoke();
    }

    private async Task<List<MarketplaceCartLine>> LoadAsync(CancellationToken cancellationToken)
    {
        if (_circuitLines is not null)
        {
            logger.LogDebug("Cart load from circuit memory. LineCount={LineCount}", _circuitLines.Count);
            return _circuitLines;
        }

        var sessionLines = await LoadFromSessionAsync(cancellationToken);
        if (sessionLines.Count > 0)
        {
            _circuitLines = sessionLines;
            logger.LogInformation("Cart hydrated circuit from session. LineCount={LineCount}", sessionLines.Count);
            return _circuitLines;
        }

        var cookieLines = LoadFromCookie();
        _circuitLines = cookieLines;
        logger.LogInformation("Cart hydrated circuit from cookie. LineCount={LineCount}", cookieLines.Count);
        return _circuitLines;
    }

    private async Task ReloadSessionFromStoreAsync(CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.Session is not { } session)
        {
            return;
        }

        try
        {
            await session.LoadAsync(cancellationToken);
            logger.LogDebug("Cart session reloaded from store. SessionId={SessionId}", session.Id);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Cart session reload from store failed");
        }
    }

    private async Task<List<MarketplaceCartLine>> LoadFromSessionAsync(CancellationToken cancellationToken)
    {
        var session = await GetSessionAsync(cancellationToken);
        if (session is null)
        {
            return [];
        }

        await session.LoadAsync(cancellationToken);

        var json = session.GetString(SessionKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return DeserializeLines(json, "session");
    }

    private List<MarketplaceCartLine> LoadFromCookie()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.Request.Cookies.TryGetValue(CookieKey, out var json) != true ||
            string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return DeserializeLines(json, "cookie");
    }

    private List<MarketplaceCartLine> DeserializeLines(string json, string source)
    {
        try
        {
            var lines = JsonSerializer.Deserialize<List<MarketplaceCartLine>>(json, JsonOptions) ?? [];
            logger.LogDebug("Cart deserialized {LineCount} lines from {Source}", lines.Count, source);
            return lines;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Cart JSON deserialize failed from {Source}. PayloadLength={Length}", source, json.Length);
            return [];
        }
    }

    private async Task SaveAsync(List<MarketplaceCartLine> lines, CancellationToken cancellationToken)
    {
        _circuitLines = lines.ToList();

        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            logger.LogWarning("Cart saved to circuit memory only (no HttpContext). LineCount={LineCount}", lines.Count);
            Changed?.Invoke();
            return;
        }

        if (httpContext.Response.HasStarted)
        {
            logger.LogWarning(
                "Cart saved to circuit memory only (HTTP response already started). LineCount={LineCount}",
                lines.Count);
            Changed?.Invoke();
            return;
        }

        var json = JsonSerializer.Serialize(lines, JsonOptions);
        var sessionSaved = await TrySaveSessionAsync(json, lines.Count, cancellationToken);
        var cookieSaved = TrySaveCookie(httpContext, json, lines.Count);

        logger.LogInformation(
            "Cart persisted. LineCount={LineCount}, SessionSaved={SessionSaved}, CookieSaved={CookieSaved}",
            lines.Count,
            sessionSaved,
            cookieSaved);

        Changed?.Invoke();
    }

    private async Task<bool> TrySaveSessionAsync(string json, int lineCount, CancellationToken cancellationToken)
    {
        var session = await GetSessionAsync(cancellationToken);
        if (session is null)
        {
            return false;
        }

        try
        {
            session.SetString(SessionKey, json);
            await session.CommitAsync(cancellationToken);
            logger.LogDebug(
                "Cart saved to session. LineCount={LineCount}, SessionId={SessionId}",
                lineCount,
                session.Id);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Cart session save skipped");
            return false;
        }
    }

    private bool TrySaveCookie(HttpContext httpContext, string json, int lineCount)
    {
        try
        {
            httpContext.Response.Cookies.Append(CookieKey, json, CookieOptions);
            logger.LogDebug("Cart saved to cookie. LineCount={LineCount}", lineCount);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Cart cookie save skipped");
            return false;
        }
    }

    private async Task<ISession?> GetSessionAsync(CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            logger.LogDebug("Cart GetSessionAsync: HttpContext is null");
            return null;
        }

        if (httpContext.Session is not { } session)
        {
            logger.LogWarning("Cart GetSessionAsync: HttpContext exists but Session feature is missing");
            return null;
        }

        if (!session.IsAvailable)
        {
            try
            {
                await session.LoadAsync(cancellationToken);
                logger.LogDebug("Cart session loaded. SessionId={SessionId}", session.Id);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Cart session LoadAsync failed");
                return null;
            }
        }

        return session;
    }

    private static Guid ProductIdFromSlug(string slug)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"marketplace-cart:{slug}"));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    private static string ImageClassFor(ThisCafeteria.Domain.Enums.ProductCategory category) =>
        category switch
        {
            ThisCafeteria.Domain.Enums.ProductCategory.BrewingEquipment => "image-frame--equipment",
            ThisCafeteria.Domain.Enums.ProductCategory.CeramicsAndGoods => "image-frame--ceramics",
            _ => "image-frame--coldbrew"
        };
}

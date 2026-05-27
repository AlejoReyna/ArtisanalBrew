using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace ThisCafeteria.Web.Services.Blockchain;

public sealed class CoinGeckoEthUsdPriceService(
    HttpClient httpClient,
    IMemoryCache cache,
    ILogger<CoinGeckoEthUsdPriceService> logger) : IEthUsdPriceService
{
    private const string CacheKey = "eth-usd-price";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<decimal?> GetEthUsdPriceAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue<decimal>(CacheKey, out var cachedPrice))
        {
            return cachedPrice;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "simple/price?ids=ethereum&vs_currencies=usd");
        request.Headers.UserAgent.ParseAdd("ThisCafeteria/1.0");

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("ethereum", out var ethereum) ||
                !ethereum.TryGetProperty("usd", out var usd) ||
                !TryGetDecimal(usd, out var price) ||
                price <= 0m)
            {
                logger.LogWarning("CoinGecko ETH/USD response did not include a usable price.");
                return null;
            }

            cache.Set(CacheKey, price, CacheDuration);
            return price;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not fetch ETH/USD price from CoinGecko.");
            return null;
        }
    }

    private static bool TryGetDecimal(JsonElement element, out decimal value)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetDecimal(out value);
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return decimal.TryParse(
                element.GetString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out value);
        }

        value = 0m;
        return false;
    }
}

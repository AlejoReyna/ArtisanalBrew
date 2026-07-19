using Microsoft.AspNetCore.Http;
using ThisCafeteria.Application.Configuration;

namespace ThisCafeteria.Web.Services.Blockchain;

public interface ISelectedChainAccessor
{
    string SelectedChainKey { get; }
    ChainDefinition SelectedChain { get; }
    Task<bool> SelectAsync(string chainKey, CancellationToken cancellationToken = default);
    event Action? Changed;
}

public sealed class SelectedChainAccessor(IChainRegistry registry, IHttpContextAccessor httpContextAccessor) : ISelectedChainAccessor
{
    private const string CookieName = "ThisCafeteria.SelectedChain";
    private string? _selected;
    public event Action? Changed;

    public string SelectedChainKey
    {
        get
        {
            if (_selected is not null) return _selected;
            var requestValue = httpContextAccessor.HttpContext?.Request.Query["chainKey"].FirstOrDefault();
            var cookieValue = httpContextAccessor.HttpContext?.Request.Cookies[CookieName];
            _selected = registry.TryGet(requestValue ?? cookieValue ?? registry.DefaultChainKey, out var definition) && definition.Enabled
                ? definition.Key : registry.DefaultChainKey;
            return _selected;
        }
    }

    public ChainDefinition SelectedChain => registry.GetRequired(SelectedChainKey);

    public Task<bool> SelectAsync(string chainKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!registry.TryGet(chainKey, out var definition) || !definition.Enabled) return Task.FromResult(false);
        _selected = definition.Key;
        httpContextAccessor.HttpContext?.Response.Cookies.Append(CookieName, definition.Key, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Strict,
            Secure = httpContextAccessor.HttpContext.Request.IsHttps,
            MaxAge = TimeSpan.FromDays(30)
        });
        Changed?.Invoke();
        return Task.FromResult(true);
    }
}

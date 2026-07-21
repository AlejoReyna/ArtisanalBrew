using Microsoft.AspNetCore.Http;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Web.Services.Blockchain;

/// <summary>
/// Remembers which discovered <see cref="SmartAccountType"/> the user picked to act as their
/// "current" account, so future components (checkout, agent-permission flows) can read one
/// answer instead of re-deriving it. This is a UI preference only - it never grants anything;
/// every operation still goes through ISmartAccountService's own on-chain checks regardless of
/// what is selected here. Defaults to SimpleAccount so existing users see unchanged behaviour
/// until they explicitly opt into a modular account.
/// </summary>
public interface ISelectedSmartAccountAccessor
{
    SmartAccountType SelectedAccountType { get; }
    void Select(SmartAccountType accountType);
    event Action? Changed;
}

public sealed class SelectedSmartAccountAccessor(IHttpContextAccessor httpContextAccessor) : ISelectedSmartAccountAccessor
{
    private const string CookieName = "ThisCafeteria.SelectedSmartAccount";
    private SmartAccountType? _selected;
    public event Action? Changed;

    public SmartAccountType SelectedAccountType
    {
        get
        {
            if (_selected is not null) return _selected.Value;
            var cookieValue = httpContextAccessor.HttpContext?.Request.Cookies[CookieName];
            _selected = Enum.TryParse<SmartAccountType>(cookieValue, out var parsed) ? parsed : SmartAccountType.SimpleAccount;
            return _selected.Value;
        }
    }

    public void Select(SmartAccountType accountType)
    {
        _selected = accountType;
        httpContextAccessor.HttpContext?.Response.Cookies.Append(CookieName, accountType.ToString(), new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Strict,
            Secure = httpContextAccessor.HttpContext.Request.IsHttps,
            MaxAge = TimeSpan.FromDays(30)
        });
        Changed?.Invoke();
    }
}

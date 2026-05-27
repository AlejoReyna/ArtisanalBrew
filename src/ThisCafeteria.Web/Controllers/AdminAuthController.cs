using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ThisCafeteria.Infrastructure.Identity;

namespace ThisCafeteria.Web.Controllers;

[Route("admin")]
public sealed class AdminAuthController(
    SignInManager<ApplicationUser> signInManager) : Controller
{
    [HttpPost("login-action")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Login([FromForm] AdminLoginForm form)
    {
        if (string.IsNullOrWhiteSpace(form.Email) || string.IsNullOrWhiteSpace(form.Password))
        {
            return LocalRedirect("/admin/login?error=missing");
        }

        var result = await signInManager.PasswordSignInAsync(
            form.Email.Trim(),
            form.Password,
            form.RememberMe,
            lockoutOnFailure: false);

        return result.Succeeded
            ? LocalRedirect("/admin")
            : LocalRedirect("/admin/login?error=invalid");
    }

    [Authorize]
    [HttpPost("logout")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        await HttpContext.SignOutAsync();
        return LocalRedirect("/");
    }

    public sealed class AdminLoginForm
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool RememberMe { get; set; }
    }
}

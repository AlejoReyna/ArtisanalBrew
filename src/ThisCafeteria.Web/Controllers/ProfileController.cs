using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Infrastructure.Identity;

namespace ThisCafeteria.Web.Controllers;

[ApiController]
[Authorize]
[Route("api/profile")]
public sealed class ProfileController(
    IProfileService profileService,
    IOrderService orderService,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<ProfileDashboardDto>> GetMe(CancellationToken cancellationToken)
    {
        var userProfileId = await ResolveUserProfileIdAsync(cancellationToken);
        if (userProfileId is null)
        {
            return Unauthorized();
        }

        return Ok(await profileService.GetProfileDashboardAsync(userProfileId.Value, cancellationToken));
    }

    [HttpPatch("me")]
    public async Task<ActionResult<UserProfileDto>> UpdateMe(
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken)
    {
        var userProfileId = await ResolveUserProfileIdAsync(cancellationToken);
        if (userProfileId is null)
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await profileService.UpdateDisplayNameAsync(userProfileId.Value, request, cancellationToken));
        }
        catch (ValidationException exception)
        {
            return BadRequest(new ValidationProblemDetails(exception.Errors
                .GroupBy(error => error.PropertyName)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.ErrorMessage).ToArray())));
        }
    }

    [HttpGet("me/orders")]
    public async Task<ActionResult<IReadOnlyCollection<OrderDto>>> GetMyOrders(CancellationToken cancellationToken)
    {
        var userProfileId = await ResolveUserProfileIdAsync(cancellationToken);
        if (userProfileId is null)
        {
            return Unauthorized();
        }

        return Ok(await orderService.GetOrdersForUserAsync(userProfileId.Value, cancellationToken));
    }

    private async Task<Guid?> ResolveUserProfileIdAsync(CancellationToken cancellationToken)
    {
        var applicationUserId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(applicationUserId))
        {
            return null;
        }

        return await profileService.EnsureProfileLinkedAsync(applicationUserId, cancellationToken);
    }
}

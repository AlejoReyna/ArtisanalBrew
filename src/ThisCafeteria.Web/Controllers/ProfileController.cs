using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Services;

namespace ThisCafeteria.Web.Controllers;

[ApiController]
[Authorize]
[Route("api/profile")]
public sealed class ProfileController(
    IProfileService profileService,
    IOrderService orderService) : ControllerBase
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
            return BadRequest(ToProblem(exception));
        }
    }

    // PUT, not PATCH: the editor always submits the whole robot, and a partial
    // avatar payload could not express "take the hat off" — see UpdateAvatarRequest.
    [HttpPut("me/avatar")]
    public async Task<ActionResult<UserProfileDto>> UpdateMyAvatar(
        UpdateAvatarRequest request,
        CancellationToken cancellationToken)
    {
        var userProfileId = await ResolveUserProfileIdAsync(cancellationToken);
        if (userProfileId is null)
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await profileService.UpdateAvatarAsync(userProfileId.Value, request, cancellationToken));
        }
        catch (ValidationException exception)
        {
            return BadRequest(ToProblem(exception));
        }
    }

    /// <summary>Returns the account to the robot derived from its wallet.</summary>
    [HttpDelete("me/avatar")]
    public async Task<ActionResult<UserProfileDto>> ResetMyAvatar(CancellationToken cancellationToken)
    {
        var userProfileId = await ResolveUserProfileIdAsync(cancellationToken);
        if (userProfileId is null)
        {
            return Unauthorized();
        }

        return Ok(await profileService.ResetAvatarAsync(userProfileId.Value, cancellationToken));
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

    private static ValidationProblemDetails ToProblem(ValidationException exception) =>
        new(exception.Errors
            .GroupBy(error => error.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).ToArray()));

    private async Task<Guid?> ResolveUserProfileIdAsync(CancellationToken cancellationToken)
    {
        var applicationUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(applicationUserId))
        {
            return null;
        }

        return await profileService.EnsureProfileLinkedAsync(applicationUserId, cancellationToken);
    }
}

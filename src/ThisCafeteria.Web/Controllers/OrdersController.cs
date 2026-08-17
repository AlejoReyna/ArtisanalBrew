using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using FluentValidation;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Services;

namespace ThisCafeteria.Web.Controllers;

[ApiController]
[Route("api/orders")]
[EnableRateLimiting("sensitive")]
public sealed class OrdersController(
    IOrderService orderService,
    IProfileService profileService) : ControllerBase
{
    [Authorize]
    [HttpPost]
    public async Task<ActionResult<OrderDto>> CreateOrder(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var applicationUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(applicationUserId))
        {
            return Unauthorized();
        }

        var userProfileId = await profileService.EnsureProfileLinkedAsync(applicationUserId, cancellationToken);

        try
        {
            var order = await orderService.CreateOrderAsync(request, userProfileId, cancellationToken);
            return CreatedAtAction(nameof(GetMyOrders), new { }, order);
        }
        catch (ValidationException exception)
        {
            return BadRequest(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<IReadOnlyCollection<OrderDto>>> GetMyOrders(CancellationToken cancellationToken)
    {
        var applicationUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(applicationUserId))
        {
            return Unauthorized();
        }

        var userProfileId = await profileService.EnsureProfileLinkedAsync(applicationUserId, cancellationToken);
        var orders = await orderService.GetOrdersForUserAsync(userProfileId, cancellationToken);
        return Ok(orders);
    }

    [Authorize(Policy = "RequireAdmin")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteOrder(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await orderService.DeleteOrderAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}

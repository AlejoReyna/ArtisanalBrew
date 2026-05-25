using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Infrastructure.Identity;

namespace ThisCafeteria.Web.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(
    IOrderService orderService,
    IProfileService profileService,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    [Authorize]
    [HttpPost]
    public async Task<ActionResult<OrderDto>> CreateOrder(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await orderService.CreateOrderAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetMyOrders), new { }, order);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<IReadOnlyCollection<OrderDto>>> GetMyOrders(CancellationToken cancellationToken)
    {
        var applicationUserId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(applicationUserId))
        {
            return Unauthorized();
        }

        var userProfileId = await profileService.EnsureProfileLinkedAsync(applicationUserId, cancellationToken);
        var orders = await orderService.GetOrdersForUserAsync(userProfileId, cancellationToken);
        return Ok(orders);
    }
}

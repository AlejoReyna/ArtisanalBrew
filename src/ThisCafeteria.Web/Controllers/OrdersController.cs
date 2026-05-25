using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Infrastructure.Persistence;

namespace ThisCafeteria.Web.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(IOrderService orderService, AppDbContext dbContext) : ControllerBase
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
        var email = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(email))
        {
            return Unauthorized();
        }

        var userProfileId = await dbContext.UserProfiles
            .Where(profile => profile.Email == email)
            .Select(profile => profile.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (userProfileId == Guid.Empty)
        {
            return Ok(Array.Empty<OrderDto>());
        }

        var orders = await orderService.GetOrdersForUserAsync(userProfileId, cancellationToken);
        return Ok(orders);
    }
}

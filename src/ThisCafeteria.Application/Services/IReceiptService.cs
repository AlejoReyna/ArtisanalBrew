using ThisCafeteria.Application.DTOs;

namespace ThisCafeteria.Application.Services;

public interface IReceiptService
{
    Task SendReceiptAsync(OrderDetails order, CancellationToken cancellationToken = default);
}

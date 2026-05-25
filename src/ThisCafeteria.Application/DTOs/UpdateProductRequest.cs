using ThisCafeteria.Domain.Enums;

namespace ThisCafeteria.Application.DTOs;

public sealed record UpdateProductRequest(
    string Name,
    string Description,
    decimal Price,
    int StockQuantity,
    string? ImageUrl,
    ProductCategory Category,
    bool IsActive);

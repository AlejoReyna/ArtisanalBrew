using ThisCafeteria.Domain.Enums;

namespace ThisCafeteria.Application.DTOs;

public sealed record ProductDto(
    Guid Id,
    string Name,
    string Slug,
    string Description,
    decimal Price,
    int StockQuantity,
    string? ImageUrl,
    ProductCategory Category,
    bool IsActive);

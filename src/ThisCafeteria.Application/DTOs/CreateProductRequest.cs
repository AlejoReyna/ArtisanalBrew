using ThisCafeteria.Domain.Enums;

namespace ThisCafeteria.Application.DTOs;

public sealed record CreateProductRequest(
    string Name,
    string Description,
    decimal Price,
    int StockQuantity,
    string? ImageUrl,
    ProductCategory Category);

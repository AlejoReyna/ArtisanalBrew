using ThisCafeteria.Application.DTOs;

namespace ThisCafeteria.Application.Services;

public interface IProductService
{
    Task<IReadOnlyCollection<ProductDto>> GetProductsAsync(CancellationToken cancellationToken = default);
    Task<ProductDto?> GetProductByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProductDto?> GetProductBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<ProductDto> CreateProductAsync(CreateProductRequest request, CancellationToken cancellationToken = default);
    Task<ProductDto?> UpdateProductAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteProductAsync(Guid id, CancellationToken cancellationToken = default);
}

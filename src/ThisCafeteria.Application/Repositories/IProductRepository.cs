using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Repositories;

public interface IProductRepository
{
    Task<IReadOnlyCollection<Product>> GetProductsAsync(CancellationToken cancellationToken = default);
    Task<Product?> GetProductByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Product?> GetProductBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task AddAsync(Product product, CancellationToken cancellationToken = default);
    Task UpdateAsync(Product product, CancellationToken cancellationToken = default);
    Task DeleteAsync(Product product, CancellationToken cancellationToken = default);
}

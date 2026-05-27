using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Repositories;

public sealed class ProductRepository(AppDbContext dbContext) : IProductRepository
{
    public async Task<IReadOnlyCollection<Product>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Products
            .AsNoTracking()
            .OrderBy(product => product.Name)
            .ToArrayAsync(cancellationToken);
    }

    public Task<Product?> GetProductByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return dbContext.Products.FirstOrDefaultAsync(product => product.Id == id, cancellationToken);
    }

    public Task<Product?> GetProductBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        return dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(product => product.Slug == slug, cancellationToken);
    }

    public async Task AddAsync(Product product, CancellationToken cancellationToken = default)
    {
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Product product, CancellationToken cancellationToken = default)
    {
        dbContext.Products.Update(product);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Product product, CancellationToken cancellationToken = default)
    {
        var cartItems = await dbContext.CartItems
            .Where(item => item.ProductId == product.Id)
            .ToArrayAsync(cancellationToken);

        dbContext.CartItems.RemoveRange(cartItems);
        dbContext.Products.Remove(product);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

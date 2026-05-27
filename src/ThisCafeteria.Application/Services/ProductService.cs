using FluentValidation;
using ThisCafeteria.Application.Common;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Services;

public sealed class ProductService(
    IProductRepository productRepository,
    IValidator<CreateProductRequest> createValidator,
    IValidator<UpdateProductRequest> updateValidator) : IProductService
{
    public async Task<IReadOnlyCollection<ProductDto>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        var products = await productRepository.GetProductsAsync(cancellationToken);
        return products.Select(Map).ToArray();
    }

    public async Task<ProductDto?> GetProductByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await productRepository.GetProductByIdAsync(id, cancellationToken);
        return product is null ? null : Map(product);
    }

    public async Task<ProductDto?> GetProductBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var product = await productRepository.GetProductBySlugAsync(slug, cancellationToken);
        return product is null ? null : Map(product);
    }

    public async Task<ProductDto> CreateProductAsync(CreateProductRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);

        var product = new Product
        {
            Name = request.Name,
            Slug = SlugGenerator.Create(request.Name),
            Description = request.Description,
            Price = request.Price,
            StockQuantity = request.StockQuantity,
            ImageUrl = request.ImageUrl,
            Category = request.Category,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await productRepository.AddAsync(product, cancellationToken);
        return Map(product);
    }

    public async Task<ProductDto?> UpdateProductAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, cancellationToken);

        var product = await productRepository.GetProductByIdAsync(id, cancellationToken);
        if (product is null)
        {
            return null;
        }

        product.Name = request.Name;
        product.Slug = SlugGenerator.Create(request.Name);
        product.Description = request.Description;
        product.Price = request.Price;
        product.StockQuantity = request.StockQuantity;
        product.ImageUrl = request.ImageUrl;
        product.Category = request.Category;
        product.IsActive = request.IsActive;
        product.UpdatedAt = DateTime.UtcNow;

        await productRepository.UpdateAsync(product, cancellationToken);
        return Map(product);
    }

    public async Task<bool> DeleteProductAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await productRepository.GetProductByIdAsync(id, cancellationToken);
        if (product is null)
        {
            return false;
        }

        await productRepository.DeleteAsync(product, cancellationToken);
        return true;
    }

    private static ProductDto Map(Product product) => new(
        product.Id,
        product.Name,
        product.Slug,
        product.Description,
        product.Price,
        product.StockQuantity,
        product.ImageUrl,
        product.Category,
        product.IsActive);
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Services;

namespace ThisCafeteria.Web.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController(IProductService productService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<ProductDto>>> GetProducts(CancellationToken cancellationToken)
    {
        var products = await productService.GetProductsAsync(cancellationToken);
        return Ok(products);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductDto>> GetProduct(Guid id, CancellationToken cancellationToken)
    {
        var product = await productService.GetProductByIdAsync(id, cancellationToken);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpGet("slug/{slug}")]
    public async Task<ActionResult<ProductDto>> GetProductBySlug(string slug, CancellationToken cancellationToken)
    {
        var product = await productService.GetProductBySlugAsync(slug, cancellationToken);
        return product is null ? NotFound() : Ok(product);
    }

    [Authorize(Policy = "RequireAdmin")]
    [HttpPost]
    public async Task<ActionResult<ProductDto>> CreateProduct(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await productService.CreateProductAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, product);
    }

    [Authorize(Policy = "RequireAdmin")]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProductDto>> UpdateProduct(Guid id, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await productService.UpdateProductAsync(id, request, cancellationToken);
        return product is null ? NotFound() : Ok(product);
    }

    [Authorize(Policy = "RequireAdmin")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteProduct(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await productService.DeleteProductAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}

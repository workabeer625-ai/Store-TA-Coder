using System.ComponentModel.DataAnnotations;
using Marina.Domain.Catalog;
using Marina.Persistence.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marina.Api.Controllers;

[ApiController]
[Authorize(Policy = "StaffMfa")]
[Route("api/v1/admin/reference")]
public sealed class AdminReferenceController(MarinaDbContext db) : ControllerBase
{
    public sealed record NameTranslation([Required, MaxLength(10)] string Culture, [Required, MaxLength(160)] string Name);
    public sealed record BrandRequest([Required, MaxLength(160)] string Slug, bool IsActive, [Required, MinLength(1)] List<NameTranslation> Translations);
    public sealed record CategoryRequest([Required, MaxLength(160)] string Slug, Guid? ParentId, int SortOrder, bool IsActive, [Required, MinLength(1)] List<NameTranslation> Translations);
    public sealed record ColorRequest([Required, MaxLength(40)] string Code, [Required, RegularExpression("^#[0-9A-Fa-f]{6}$")] string Hex, [Required, MinLength(1)] List<NameTranslation> Translations);
    public sealed record SizeRequest([Required, MaxLength(40)] string Code, int SortOrder, [Required, MinLength(1)] List<NameTranslation> Translations);

    [HttpPut("categories/{id:guid}"), Authorize(Policy = "Products.Edit")]
    public async Task<IActionResult>UpdateCategory(Guid id, CategoryRequest request)
    {
        ValidateTranslations(request.Translations);
        if (request.ParentId == id) return BadRequest(new { message = "A category cannot be its own parent." });
        if (request.ParentId.HasValue && !await db.Categories.AnyAsync(x => x.Id == request.ParentId && !x.IsDeleted)) return BadRequest(new { message = "Parent category not found." });
        var item = await db.Categories.Include(x => x.Translations).SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted) ?? throw new KeyNotFoundException("Category not found.");
        item.Slug = Slug(request.Slug); item.ParentId = request.ParentId; item.SortOrder = request.SortOrder; item.IsActive = request.IsActive;
        db.CategoryTranslations.RemoveRange(item.Translations);
        item.Translations = request.Translations.Select(x => new CategoryTranslation { Culture = Culture(x.Culture), Name = x.Name.Trim() }).ToList();
        await db.SaveChangesAsync(); return NoContent();
    }

    [HttpGet("brands"), Authorize(Policy = "Products.View")]
    public async Task<ActionResult>Brands() => Ok(await db.Brands.AsNoTracking().Where(x => !x.IsDeleted).OrderBy(x => x.Slug).Select(x => new { x.Id, x.Slug, x.IsActive, x.Translations }).ToListAsync());

    [HttpPost("brands"), Authorize(Policy = "Products.Create")]
    public async Task<ActionResult>CreateBrand(BrandRequest request)
    {
        ValidateTranslations(request.Translations);
        var item = new Brand { Slug = Slug(request.Slug), IsActive = request.IsActive, Translations = request.Translations.Select(x => new BrandTranslation { Culture = Culture(x.Culture), Name = x.Name.Trim() }).ToList() };
        db.Brands.Add(item); await db.SaveChangesAsync(); return Created("", new { item.Id });
    }

    [HttpPut("brands/{id:guid}"), Authorize(Policy = "Products.Edit")]
    public async Task<IActionResult>UpdateBrand(Guid id, BrandRequest request)
    {
        ValidateTranslations(request.Translations);
        var item = await db.Brands.Include(x => x.Translations).SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted) ?? throw new KeyNotFoundException("Brand not found.");
        item.Slug = Slug(request.Slug); item.IsActive = request.IsActive;
        db.BrandTranslations.RemoveRange(item.Translations);
        item.Translations = request.Translations.Select(x => new BrandTranslation { Culture = Culture(x.Culture), Name = x.Name.Trim() }).ToList();
        await db.SaveChangesAsync(); return NoContent();
    }

    [HttpGet("colors"), Authorize(Policy = "Products.View")]
    public async Task<ActionResult>Colors() => Ok(await db.Colors.AsNoTracking().OrderBy(x => x.Code).Select(x => new { x.Id, x.Code, x.Hex, x.Translations }).ToListAsync());

    [HttpPost("colors"), Authorize(Policy = "Products.Create")]
    public async Task<ActionResult>CreateColor(ColorRequest request)
    {
        ValidateTranslations(request.Translations);
        var item = new ColorOption { Code = request.Code.Trim().ToUpperInvariant(), Hex = request.Hex.ToUpperInvariant(), Translations = request.Translations.Select(x => new ColorTranslation { Culture = Culture(x.Culture), Name = x.Name.Trim() }).ToList() };
        db.Colors.Add(item); await db.SaveChangesAsync(); return Created("", new { item.Id });
    }

    [HttpPut("colors/{id:guid}"), Authorize(Policy = "Products.Edit")]
    public async Task<IActionResult>UpdateColor(Guid id, ColorRequest request)
    {
        ValidateTranslations(request.Translations);
        var item = await db.Colors.Include(x => x.Translations).SingleOrDefaultAsync(x => x.Id == id) ?? throw new KeyNotFoundException("Color not found.");
        item.Code = request.Code.Trim().ToUpperInvariant(); item.Hex = request.Hex.ToUpperInvariant();
        db.ColorTranslations.RemoveRange(item.Translations);
        item.Translations = request.Translations.Select(x => new ColorTranslation { Culture = Culture(x.Culture), Name = x.Name.Trim() }).ToList();
        await db.SaveChangesAsync(); return NoContent();
    }

    [HttpGet("sizes"), Authorize(Policy = "Products.View")]
    public async Task<ActionResult>Sizes() => Ok(await db.Sizes.AsNoTracking().OrderBy(x => x.SortOrder).Select(x => new { x.Id, x.Code, x.SortOrder, x.Translations }).ToListAsync());

    [HttpPost("sizes"), Authorize(Policy = "Products.Create")]
    public async Task<ActionResult>CreateSize(SizeRequest request)
    {
        ValidateTranslations(request.Translations);
        var item = new SizeOption { Code = request.Code.Trim().ToUpperInvariant(), SortOrder = request.SortOrder, Translations = request.Translations.Select(x => new SizeTranslation { Culture = Culture(x.Culture), Name = x.Name.Trim() }).ToList() };
        db.Sizes.Add(item); await db.SaveChangesAsync(); return Created("", new { item.Id });
    }

    [HttpPut("sizes/{id:guid}"), Authorize(Policy = "Products.Edit")]
    public async Task<IActionResult>UpdateSize(Guid id, SizeRequest request)
    {
        ValidateTranslations(request.Translations);
        var item = await db.Sizes.Include(x => x.Translations).SingleOrDefaultAsync(x => x.Id == id) ?? throw new KeyNotFoundException("Size not found.");
        item.Code = request.Code.Trim().ToUpperInvariant(); item.SortOrder = request.SortOrder;
        db.SizeTranslations.RemoveRange(item.Translations);
        item.Translations = request.Translations.Select(x => new SizeTranslation { Culture = Culture(x.Culture), Name = x.Name.Trim() }).ToList();
        await db.SaveChangesAsync(); return NoContent();
    }

    private static void ValidateTranslations(IReadOnlyCollection<NameTranslation> translations)
    {
        if (translations.Count == 0 || translations.Select(x => Culture(x.Culture)).Distinct().Count() != translations.Count)
            throw new ArgumentException("Translations must contain unique supported cultures.");
    }
    private static string Culture(string value) => value.Equals("ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";
    private static string Slug(string value) => value.Trim().ToLowerInvariant().Replace(' ', '-');
}

[ApiController]
[Authorize(Policy = "StaffMfa")]
[Route("api/v1/admin/catalog/products/{productId:guid}/variants")]
public sealed class AdminVariantsController(MarinaDbContext db) : ControllerBase
{
    public sealed record VariantRequest([Required, MaxLength(80)] string Sku, [MaxLength(80)] string? Barcode, Guid? ColorId, Guid? SizeId, decimal Price, decimal? CompareAtPrice, int InitialStock, bool IsActive);

    [HttpPost, Authorize(Policy = "Products.Create")]
    public async Task<ActionResult>Create(Guid productId, VariantRequest request)
    {
        Validate(request, creating: true);
        if (!await db.Products.AnyAsync(x => x.Id == productId && !x.IsDeleted)) return NotFound();
        var item = new ProductVariant { ProductId = productId, Sku = request.Sku.Trim().ToUpperInvariant(), Barcode = request.Barcode?.Trim(), ColorId = request.ColorId, SizeId = request.SizeId, Price = request.Price, CompareAtPrice = request.CompareAtPrice, IsActive = request.IsActive, Inventory = new Inventory { Quantity = request.InitialStock } };
        db.ProductVariants.Add(item); await db.SaveChangesAsync(); return Created("", new { item.Id });
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "Products.Edit")]
    public async Task<IActionResult>Update(Guid productId, Guid id, VariantRequest request)
    {
        Validate(request, creating: false);
        var item = await db.ProductVariants.SingleOrDefaultAsync(x => x.Id == id && x.ProductId == productId && !x.IsDeleted) ?? throw new KeyNotFoundException("Variant not found.");
        item.Sku = request.Sku.Trim().ToUpperInvariant(); item.Barcode = request.Barcode?.Trim(); item.ColorId = request.ColorId; item.SizeId = request.SizeId; item.Price = request.Price; item.CompareAtPrice = request.CompareAtPrice; item.IsActive = request.IsActive;
        await db.SaveChangesAsync(); return NoContent();
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = "Products.Delete")]
    public async Task<IActionResult>Deactivate(Guid productId, Guid id)
    {
        var item = await db.ProductVariants.SingleOrDefaultAsync(x => x.Id == id && x.ProductId == productId) ?? throw new KeyNotFoundException("Variant not found.");
        item.IsActive = false; item.IsDeleted = true; await db.SaveChangesAsync(); return NoContent();
    }

    private static void Validate(VariantRequest request, bool creating)
    {
        if (request.Price < 0 || request.CompareAtPrice < 0 || creating && request.InitialStock < 0)
            throw new ArgumentException("Invalid variant values.");
    }
}

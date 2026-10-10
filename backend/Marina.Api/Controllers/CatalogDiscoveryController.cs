using Marina.Persistence.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marina.Api.Controllers;

[ApiController]
[Route("api/v1/catalog")]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("public")]
public sealed class CatalogDiscoveryController(MarinaDbContext db) : ControllerBase
{
    [HttpGet("brands")]
    public async Task<ActionResult>Brands([FromQuery] string culture = "en")
    {
        culture = Culture(culture);
        return Ok(await db.Brands.AsNoTracking().Where(x => x.IsActive && !x.IsDeleted).OrderBy(x => x.Slug)
            .Select(x => new { x.Id, x.Slug, Name = x.Translations.Where(t => t.Culture == culture).Select(t => t.Name).FirstOrDefault() }).ToListAsync());
    }

    [HttpGet("options")]
    public async Task<ActionResult>Options([FromQuery] string culture = "en")
    {
        culture = Culture(culture);
        var colors = await db.Colors.AsNoTracking().OrderBy(x => x.Code).Select(x => new { x.Id, x.Code, x.Hex, Name = x.Translations.Where(t => t.Culture == culture).Select(t => t.Name).FirstOrDefault() }).ToListAsync();
        var sizes = await db.Sizes.AsNoTracking().OrderBy(x => x.SortOrder).Select(x => new { x.Id, x.Code, Name = x.Translations.Where(t => t.Culture == culture).Select(t => t.Name).FirstOrDefault() }).ToListAsync();
        return Ok(new { colors, sizes });
    }

    [HttpGet("products/{id:guid}/related")]
    public async Task<ActionResult>Related(Guid id, [FromQuery] string culture = "en")
    {
        culture = Culture(culture);
        var categoryId = await db.Products.Where(x => x.Id == id && x.IsActive && !x.IsDeleted).Select(x => (Guid?)x.CategoryId).SingleOrDefaultAsync();
        if (!categoryId.HasValue) return NotFound();
        return Ok(await db.Products.AsNoTracking().Where(x => x.Id != id && x.CategoryId == categoryId && x.IsActive && !x.IsDeleted)
            .OrderByDescending(x => x.IsFeatured).ThenByDescending(x => x.CreatedUtc).Take(8)
            .Select(x => new { x.Id, x.Slug, Name = x.Translations.Where(t => t.Culture == culture).Select(t => t.Name).FirstOrDefault(), VariantId = x.Variants.Where(v => v.IsActive && !v.IsDeleted).OrderBy(v => v.Price).Select(v => v.Id).FirstOrDefault(), Price = x.Variants.Where(v => v.IsActive && !v.IsDeleted).Min(v => v.Price), CompareAtPrice = x.Variants.Where(v => v.IsActive && !v.IsDeleted).Min(v => v.CompareAtPrice), Image = x.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault() }).ToListAsync());
    }

    private static string Culture(string value) => value.Equals("ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";
}

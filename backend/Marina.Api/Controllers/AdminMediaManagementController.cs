using System.ComponentModel.DataAnnotations;
using Marina.Application.Abstractions;
using Marina.Persistence.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marina.Api.Controllers;

[ApiController]
[Authorize(Policy = "StaffMfa")]
[Authorize(Policy = "Products.Edit")]
[Route("api/v1/admin/media/products/{productId:guid}/images")]
public sealed class AdminMediaManagementController(MarinaDbContext db, IEnumerable<IMediaStorage> storageProviders) : ControllerBase
{
    public sealed record ImageOrder(Guid Id, int SortOrder, [MaxLength(300)] string? AltText);

    [HttpGet]
    public async Task<ActionResult>List(Guid productId) => Ok(await db.ProductImages.AsNoTracking()
        .Where(x => x.ProductId == productId).OrderBy(x => x.SortOrder)
        .Select(x => new { x.Id, x.Url, x.AltText, x.SortOrder, x.VariantId }).ToListAsync());

    [HttpPut("order")]
    public async Task<IActionResult>Reorder(Guid productId, List<ImageOrder> items)
    {
        if (items.Count == 0 || items.Any(x => x.SortOrder < 0) || items.Select(x => x.Id).Distinct().Count() != items.Count || items.Select(x => x.SortOrder).Distinct().Count() != items.Count)
            return BadRequest(new { message = "Image IDs and sort positions must be unique." });
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var images = await db.ProductImages.Where(x => x.ProductId == productId).ToListAsync();
        if (images.Count != items.Count || images.Any(x => !items.Any(i => i.Id == x.Id)))
            return BadRequest(new { message = "Supply all product images exactly once." });
        // Vacate the nonnegative positions first, avoiding unique-index swap conflicts.
        for (var index = 0; index < images.Count; index++) images[index].SortOrder = -index - 1;
        await db.SaveChangesAsync();
        foreach (var image in images)
        {
            var input = items.Single(x => x.Id == image.Id);
            image.SortOrder = input.SortOrder;
            if (input.AltText is not null) image.AltText = input.AltText.Trim();
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return NoContent();
    }

    [HttpDelete("{imageId:guid}")]
    public async Task<IActionResult>Delete(Guid productId, Guid imageId, CancellationToken ct)
    {
        var image = await db.ProductImages.SingleOrDefaultAsync(x => x.Id == imageId && x.ProductId == productId, ct);
        if (image is null) return NotFound();
        var storage = storageProviders.SingleOrDefault();
        if (storage is null) return StatusCode(503, new ProblemDetails { Status = 503, Title = "Media storage is not configured." });
        await storage.DeleteAsync(image.StorageKey, ct);
        db.ProductImages.Remove(image);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

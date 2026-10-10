using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Marina.Application.Commerce;
using Marina.Domain.Commerce;
using Marina.Domain.Security;
using Marina.Persistence.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marina.Api.Controllers;

[ApiController]
[Route("api/v1/catalog/products/{productId:guid}/reviews")]
public sealed class ReviewsController(PostPurchaseService service, MarinaDbContext db) : ControllerBase
{
    public sealed record ReviewRequest(
        [Range(1, 5)] int Rating,
        [MaxLength(120)] string Title,
        [Required, MinLength(10), MaxLength(2000)] string Body);

    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult>List(Guid productId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var query = db.ProductReviews.AsNoTracking()
            .Where(x => x.ProductId == productId && x.Status == ReviewStatus.Approved)
            .OrderByDescending(x => x.CreatedUtc);
        var total = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.Rating,
                x.Title,
                x.Body,
                x.CreatedUtc,
                VerifiedPurchase = true,
                DisplayName = db.Users.Where(u => u.Id == x.UserId).Select(u => u.DisplayName).FirstOrDefault()
            })
            .ToListAsync();
        var summary = await db.ProductReviews.AsNoTracking()
            .Where(x => x.ProductId == productId && x.Status == ReviewStatus.Approved)
            .GroupBy(_ => 1)
            .Select(x => new { Average = x.Average(r => r.Rating), Count = x.Count() })
            .SingleOrDefaultAsync();
        return Ok(new { items, page, pageSize, total, average = summary?.Average ?? 0, count = summary?.Count ?? 0 });
    }

    [Authorize]
    [HttpPut("mine")]
    public async Task<ActionResult>Submit(Guid productId, ReviewRequest request, CancellationToken ct)
    {
        var review = await service.SubmitReviewAsync(
            new SubmitReviewCommand(UserId(), productId, request.Rating, request.Title, request.Body), ct);
        return Ok(new { review.Id, review.Rating, review.Title, review.Body, review.Status, review.UpdatedUtc });
    }

    private Guid UserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

[ApiController]
[Authorize]
[Route("api/v1/returns")]
public sealed class ReturnsController(PostPurchaseService service, MarinaDbContext db) : ControllerBase
{
    public sealed record ReturnItemRequest(Guid OrderItemId, [Range(1, 20)] int Quantity);
    public sealed record CreateReturnRequest(
        [Required, MinLength(3), MaxLength(120)] string Reason,
        [MaxLength(1000)] string? Note,
        [Required, MinLength(1)] List<ReturnItemRequest> Items);

    [HttpPost("orders/{orderId:guid}")]
    public async Task<ActionResult>Create(Guid orderId, CreateReturnRequest request, CancellationToken ct)
    {
        var result = await service.CreateReturnAsync(new CreateReturnCommand(
            UserId(), orderId, request.Reason, request.Note,
            request.Items.Select(x => new ReturnLine(x.OrderItemId, x.Quantity)).ToList()), ct);
        return CreatedAtAction(nameof(Details), new { id = result.Id }, new
        {
            result.Id,
            result.PublicNumber,
            result.Status,
            result.CreatedUtc
        });
    }

    [HttpGet]
    public async Task<ActionResult>List([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var query = db.ReturnRequests.AsNoTracking().Where(x => x.UserId == UserId()).OrderByDescending(x => x.CreatedUtc);
        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.OrderId, x.PublicNumber, x.Status, x.Reason, x.CreatedUtc, ItemCount = x.Items.Sum(i => i.Quantity) })
            .ToListAsync();
        return Ok(new { items, page, pageSize, total });
    }

    [HttpGet("{id:guid}", Name = nameof(Details))]
    public async Task<ActionResult>Details(Guid id)
    {
        var item = await db.ReturnRequests.AsNoTracking()
            .Where(x => x.Id == id && x.UserId == UserId())
            .Select(x => new
            {
                x.Id,
                x.OrderId,
                x.PublicNumber,
                x.Status,
                x.Reason,
                x.CustomerNote,
                x.AdminNote,
                x.CreatedUtc,
                Items = x.Items.Select(i => new { i.OrderItemId, i.OrderItem.ProductName, i.OrderItem.Sku, i.Quantity }),
                Timeline = x.StatusHistory.OrderBy(h => h.CreatedUtc).Select(h => new { h.Status, h.Note, h.CreatedUtc })
            })
            .SingleOrDefaultAsync();
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult>Cancel(Guid id, CancellationToken ct)
    {
        if (!await db.ReturnRequests.AnyAsync(x => x.Id == id && x.UserId == UserId(), ct)) return NotFound();
        await service.UpdateReturnStatusAsync(id, UserId(), ReturnStatus.Cancelled, "Cancelled by customer", ct);
        return NoContent();
    }

    private Guid UserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

[ApiController]
[Authorize(Policy = "StaffMfa")]
[Route("api/v1/admin/returns")]
public sealed class AdminReturnsController(PostPurchaseService service, MarinaDbContext db) : ControllerBase
{
    public sealed record UpdateStatusRequest(ReturnStatus Status, [MaxLength(1000)] string? Note);

    [HttpGet]
    [Authorize(Policy = "Orders.View")]
    public async Task<ActionResult>List([FromQuery] ReturnStatus? status = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 30)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.ReturnRequests.AsNoTracking();
        if (status.HasValue) query = query.Where(x => x.Status == status);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(x => x.CreatedUtc).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.OrderId, x.UserId, x.PublicNumber, x.Status, x.Reason, x.CreatedUtc, ItemCount = x.Items.Sum(i => i.Quantity) })
            .ToListAsync();
        return Ok(new { items, page, pageSize, total });
    }

    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = "Orders.UpdateStatus")]
    public async Task<IActionResult>Update(Guid id, UpdateStatusRequest request, CancellationToken ct)
    {
        var old = await db.ReturnRequests.AsNoTracking().Where(x => x.Id == id).Select(x => (ReturnStatus?)x.Status).SingleOrDefaultAsync(ct);
        if (old is null) return NotFound();
        var result = await service.UpdateReturnStatusAsync(id, UserId(), request.Status, request.Note, ct);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = UserId(),
            Action = "Returns.UpdateStatus",
            EntityName = nameof(ReturnRequest),
            EntityId = id.ToString(),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            CorrelationId = HttpContext.TraceIdentifier,
            OldValuesJson = System.Text.Json.JsonSerializer.Serialize(new { Status = old }),
            NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new { result.Status, request.Note })
        });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "Orders.View")]
    public async Task<ActionResult>Details(Guid id)
    {
        var item = await db.ReturnRequests.AsNoTracking().Where(x => x.Id == id).Select(x => new
        {
            x.Id, x.OrderId, x.UserId, x.PublicNumber, x.Status, x.Reason, x.CustomerNote, x.AdminNote,
            x.InventoryRestoredUtc, x.ResolvedUtc, x.CreatedUtc,
            Items = x.Items.Select(i => new { i.OrderItemId, i.OrderItem.ProductName, i.OrderItem.Sku, i.Quantity, PurchasedQuantity = i.OrderItem.Quantity }),
            Timeline = x.StatusHistory.OrderBy(h => h.CreatedUtc).Select(h => new { h.Status, h.ActorId, h.Note, h.CreatedUtc })
        }).SingleOrDefaultAsync();
        return item is null ? NotFound() : Ok(item);
    }

    private Guid UserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

[ApiController]
[Authorize(Policy = "StaffMfa")]
[Route("api/v1/admin/reviews")]
public sealed class AdminReviewsController(MarinaDbContext db) : ControllerBase
{
    public sealed record ModerateReviewRequest(ReviewStatus Status, [MaxLength(500)] string? Note);

    [HttpGet]
    [Authorize(Policy = "Products.Edit")]
    public async Task<ActionResult>List([FromQuery] ReviewStatus? status = ReviewStatus.Pending, [FromQuery] int page = 1, [FromQuery] int pageSize = 30)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.ProductReviews.AsNoTracking();
        if (status.HasValue) query = query.Where(x => x.Status == status);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(x => x.CreatedUtc).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.ProductId, x.UserId, x.Rating, x.Title, x.Body, x.Status, x.CreatedUtc })
            .ToListAsync();
        return Ok(new { items, page, pageSize, total });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "Products.Edit")]
    public async Task<IActionResult>Moderate(Guid id, ModerateReviewRequest request, CancellationToken ct)
    {
        if (request.Status == ReviewStatus.Pending) return BadRequest(new { message = "Choose Approved or Rejected." });
        var review = await db.ProductReviews.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (review is null) return NotFound();
        var old = review.Status;
        review.Status = request.Status;
        review.ModerationNote = request.Note?.Trim();
        db.AuditLogs.Add(new AuditLog
        {
            UserId = UserId(),
            Action = "Reviews.Moderate",
            EntityName = nameof(ProductReview),
            EntityId = review.Id.ToString(),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            CorrelationId = HttpContext.TraceIdentifier,
            OldValuesJson = System.Text.Json.JsonSerializer.Serialize(new { Status = old }),
            NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new { review.Status, request.Note })
        });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Guid UserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

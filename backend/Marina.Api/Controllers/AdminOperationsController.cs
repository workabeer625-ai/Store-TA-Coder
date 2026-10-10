using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Marina.Domain.Commerce;
using Marina.Domain.Security;
using Marina.Persistence.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marina.Api.Controllers;

[ApiController]
[Authorize(Policy = "StaffMfa")]
[Route("api/v1/admin")]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("admin")]
public sealed class AdminOperationsController(MarinaDbContext db) : ControllerBase
{
    public sealed record SettingRequest([Required, MaxLength(120)] string Key, [Required, MaxLength(2000)] string Value, bool IsPublic);
    public sealed record NotificationRequest(Guid UserId, NotificationChannel Channel, [Required, MaxLength(160)] string Title, [Required, MaxLength(2000)] string Body, [MaxLength(300)] string? DeepLink);

    [HttpGet("orders/{id:guid}")]
    [Authorize(Policy = "Orders.View")]
    public async Task<ActionResult>Order(Guid id)
    {
        var order = await db.Orders.AsNoTracking().Where(x => x.Id == id).Select(x => new
        {
            x.Id, x.PublicNumber, x.UserId, x.Status, x.Subtotal, x.Discount, x.Shipping, x.Tax, x.Total,
            x.RecipientSnapshot, x.AddressSnapshot, x.CreatedUtc,
            Items = x.Items.Select(i => new { i.Id, i.VariantId, i.ProductName, i.Sku, i.Color, i.Size, i.UnitPrice, i.Discount, i.Quantity }),
            Timeline = x.StatusHistory.OrderBy(h => h.CreatedUtc).Select(h => new { h.Status, h.Reason, h.ActorId, h.CreatedUtc }),
            Payments = db.Payments.Where(p => p.OrderId == x.Id).Select(p => new { p.Id, p.Provider, p.Status, p.Amount, p.Currency })
        }).SingleOrDefaultAsync();
        return order is null ? NotFound() : Ok(order);
    }

    [HttpPost("orders/{id:guid}/cancel")]
    [Authorize(Policy = "Orders.Cancel")]
    public async Task<IActionResult>CancelOrder(Guid id, CancelOrderRequest request)
    {
        var reason = request.Reason;
        var order = await db.Orders.Include(x => x.Items).ThenInclude(x => x.Variant).ThenInclude(x => x.Inventory)
            .SingleOrDefaultAsync(x => x.Id == id) ?? throw new KeyNotFoundException("Order not found.");
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Delivered or OrderStatus.Returned or OrderStatus.Refunded)
            return Conflict(new { message = "The order can no longer be cancelled." });
        foreach (var item in order.Items)
        {
            var old = item.Variant.Inventory.Quantity;
            item.Variant.Inventory.Quantity += item.Quantity;
            db.StockMovements.Add(new StockMovement
            {
                VariantId = item.VariantId,
                Type = StockMovementType.Cancellation,
                OldQuantity = old,
                Delta = item.Quantity,
                NewQuantity = item.Variant.Inventory.Quantity,
                Reason = reason?.Trim() ?? "Administrative cancellation",
                ReferenceId = order.Id,
                ActorId = UserId()
            });
        }
        order.Status = OrderStatus.Cancelled;
        order.StatusHistory.Add(new OrderStatusHistory { Status = OrderStatus.Cancelled, ActorId = UserId(), Reason = reason?.Trim() });
        Audit("Orders.Cancel", nameof(Order), order.Id, new { reason });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("inventory/{variantId:guid}/movements")]
    [Authorize(Policy = "Inventory.View")]
    public async Task<ActionResult>StockMovements(Guid variantId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.StockMovements.AsNoTracking().Where(x => x.VariantId == variantId).OrderByDescending(x => x.CreatedUtc);
        var total = await query.CountAsync();
        return Ok(new { items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(), page, pageSize, total });
    }

    [HttpGet("customers")]
    [Authorize(Policy = "Customers.View")]
    public async Task<ActionResult>Customers([FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 30)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Email!.Contains(term) || x.DisplayName.Contains(term));
        }
        var customerRole = await db.Roles.Where(x => x.Name == "Customer").Select(x => x.Id).SingleAsync();
        query = query.Where(x => db.UserRoles.Any(r => r.UserId == x.Id && r.RoleId == customerRole));
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(x => x.CreatedUtc).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.Email, x.DisplayName, x.PhoneNumber, x.IsActive, x.EmailConfirmed, x.CreatedUtc, Orders = db.Orders.Count(o => o.UserId == x.Id), Spend = db.Orders.Where(o => o.UserId == x.Id && o.Status != OrderStatus.Cancelled).Sum(o => (decimal?)o.Total) ?? 0 })
            .ToListAsync();
        return Ok(new { items, page, pageSize, total });
    }

    [HttpGet("customers/{id:guid}")]
    [Authorize(Policy = "Customers.View")]
    public async Task<ActionResult>Customer(Guid id)
    {
        var customer = await db.Users.AsNoTracking().Where(x => x.Id == id).Select(x => new
        {
            x.Id, x.Email, x.DisplayName, x.PhoneNumber, x.IsActive, x.EmailConfirmed, x.CreatedUtc, x.LastLoginUtc,
            Addresses = db.Addresses.Where(a => a.UserId == x.Id && !a.IsDeleted).Select(a => new { a.Id, a.Label, a.City, a.Line1, a.IsDefault }),
            Orders = db.Orders.Where(o => o.UserId == x.Id).OrderByDescending(o => o.CreatedUtc).Take(20).Select(o => new { o.Id, o.PublicNumber, o.Status, o.Total, o.CreatedUtc })
        }).SingleOrDefaultAsync();
        return customer is null ? NotFound() : Ok(customer);
    }

    [HttpGet("reports")]
    [Authorize(Policy = "Reports.View")]
    public async Task<ActionResult>Reports([FromQuery] int days = 30)
    {
        days = Math.Clamp(days, 7, 365);
        var since = DateTime.UtcNow.Date.AddDays(-(days - 1));
        var completed = db.Orders.AsNoTracking().Where(x => x.CreatedUtc >= since && x.Status != OrderStatus.Cancelled);
        var revenue = await completed.GroupBy(x => x.CreatedUtc.Date).Select(x => new { Date = x.Key, Revenue = x.Sum(o => o.Total), Orders = x.Count() }).OrderBy(x => x.Date).ToListAsync();
        var bestSellers = await db.OrderItems.AsNoTracking()
            .Where(i => db.Orders.Any(o => o.Id == i.OrderId && o.CreatedUtc >= since && o.Status != OrderStatus.Cancelled))
            .GroupBy(i => new { i.VariantId, i.ProductName, i.Sku })
            .Select(x => new { x.Key.VariantId, x.Key.ProductName, x.Key.Sku, Quantity = x.Sum(i => i.Quantity), Revenue = x.Sum(i => (i.UnitPrice - i.Discount) * i.Quantity) })
            .OrderByDescending(x => x.Quantity).Take(10).ToListAsync();
        var statuses = await db.Orders.AsNoTracking().Where(x => x.CreatedUtc >= since).GroupBy(x => x.Status).Select(x => new { Status = x.Key, Count = x.Count() }).ToListAsync();
        return Ok(new { since, days, revenue, bestSellers, statuses });
    }

    [HttpPut("coupons/{id:guid}")]
    [Authorize(Policy = "Coupons.Manage")]
    public async Task<IActionResult>UpdateCoupon(Guid id, AdminController.CouponRequest request)
    {
        if (request.Value <= 0 || request.ValidToUtc <= request.ValidFromUtc || request.Type == DiscountType.Percentage && request.Value > 100)
            throw new ArgumentException("Invalid coupon rules.");
        var coupon = await db.Coupons.SingleOrDefaultAsync(x => x.Id == id) ?? throw new KeyNotFoundException("Coupon not found.");
        coupon.Code = request.Code.Trim().ToUpperInvariant(); coupon.Type = request.Type; coupon.Value = request.Value;
        coupon.MinimumPurchase = request.MinimumPurchase; coupon.MaximumDiscount = request.MaximumDiscount;
        coupon.ValidFromUtc = request.ValidFromUtc.ToUniversalTime(); coupon.ValidToUtc = request.ValidToUtc.ToUniversalTime();
        coupon.MaxUses = request.MaxUses; coupon.MaxUsesPerCustomer = request.MaxUsesPerCustomer; coupon.IsActive = request.IsActive;
        Audit("Coupons.Edit", nameof(Coupon), coupon.Id, request);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("coupons/{id:guid}/usage")]
    [Authorize(Policy = "Coupons.Manage")]
    public async Task<ActionResult>CouponUsage(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.CouponUsages.AsNoTracking().Where(x => x.CouponId == id).OrderByDescending(x => x.CreatedUtc);
        var total = await query.CountAsync();
        return Ok(new { items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(), page, pageSize, total });
    }

    [HttpGet("settings")]
    [Authorize(Policy = "Settings.Manage")]
    public async Task<ActionResult>Settings() => Ok(await db.AppSettings.AsNoTracking().OrderBy(x => x.Key).ToListAsync());

    [HttpPut("settings")]
    [Authorize(Policy = "Settings.Manage")]
    public async Task<IActionResult>SaveSetting(SettingRequest request)
    {
        var key = request.Key.Trim();
        var setting = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == key);
        if (setting is null) { setting = new AppSetting { Key = key }; db.AppSettings.Add(setting); }
        setting.Value = request.Value.Trim(); setting.IsPublic = request.IsPublic;
        Audit("Settings.Save", nameof(AppSetting), setting.Id, new { setting.Key, setting.IsPublic });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("notifications")]
    [Authorize(Policy = "Notifications.Manage")]
    public async Task<ActionResult>Notifications([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Notifications.AsNoTracking().OrderByDescending(x => x.CreatedUtc);
        var total = await query.CountAsync();
        return Ok(new { items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(), page, pageSize, total });
    }

    [HttpPost("notifications")]
    [Authorize(Policy = "Notifications.Manage")]
    public async Task<ActionResult>CreateNotification(NotificationRequest request)
    {
        if (!await db.Users.AnyAsync(x => x.Id == request.UserId)) return NotFound(new { message = "User not found." });
        var notification = new Notification { UserId = request.UserId, Channel = request.Channel, Title = request.Title.Trim(), Body = request.Body.Trim(), DeepLink = request.DeepLink?.Trim() };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();
        return Created("", new { notification.Id });
    }

    private void Audit(string action, string entityName, Guid id, object value) => db.AuditLogs.Add(new AuditLog
    {
        UserId = UserId(), Action = action, EntityName = entityName, EntityId = id.ToString(),
        IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        CorrelationId = HttpContext.TraceIdentifier,
        NewValuesJson = System.Text.Json.JsonSerializer.Serialize(value)
    });

    private Guid UserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

[ApiController]
[Route("api/v1/app")]
public sealed class AppConfigurationController(MarinaDbContext db) : ControllerBase
{
    [HttpGet("configuration")]
    public async Task<ActionResult>Configuration() => Ok(await db.AppSettings.AsNoTracking().Where(x => x.IsPublic).ToDictionaryAsync(x => x.Key, x => x.Value));
}

using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marina.Domain.Commerce;
using Marina.Domain.Security;
using Marina.Persistence.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marina.Api.Controllers;

[ApiController, Authorize(Policy = "StaffMfa"), Authorize(Policy = "Orders.UpdateStatus")]
public sealed class CashPaymentsController(MarinaDbContext db) : ControllerBase
{
    public sealed record CashReceiptRequest([Required, MinLength(3), MaxLength(200)] string Reference);

    [HttpPost("api/v1/admin/orders/{orderId:guid}/cash-collected")]
    public async Task<IActionResult> Collect(Guid orderId, CashReceiptRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null) return NotFound();
        var payment = await db.Payments.SingleOrDefaultAsync(x => x.OrderId == orderId && x.Provider == "CashOnDelivery", ct);
        if (payment is null) return Conflict(new { message = "This order has no COD payment." });
        if (payment.Status == PaymentStatus.Captured) return NoContent();
        if (payment.Status != PaymentStatus.Pending || order.Status != OrderStatus.Delivered)
            return Conflict(new { message = "Cash collection can only be recorded for a delivered, unpaid COD order." });
        payment.Status = PaymentStatus.Captured;
        Record(payment, "CashCollected", orderId, payment.Amount, request.Reference);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    [HttpPost("api/v1/admin/returns/{returnId:guid}/cash-refunded")]
    public async Task<IActionResult> Refund(Guid returnId, CashReceiptRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var returned = await db.ReturnRequests.Include(x => x.Order).ThenInclude(x => x.Items)
            .Include(x => x.Items).ThenInclude(x => x.OrderItem).SingleOrDefaultAsync(x => x.Id == returnId, ct);
        if (returned is null) return NotFound();
        if (returned.Status == ReturnStatus.Refunded) return NoContent();
        if (returned.Status != ReturnStatus.RefundPending || returned.InventoryRestoredUtc is null)
            return Conflict(new { message = "Receive the returned goods and request a refund first." });
        var payment = await db.Payments.SingleOrDefaultAsync(x => x.OrderId == returned.OrderId && x.Provider == "CashOnDelivery", ct);
        if (payment?.Status != PaymentStatus.Captured)
            return Conflict(new { message = "A recorded cash collection is required before recording its cash refund." });
        var order = returned.Order;
        var fullReturn = order.Items.All(item => returned.Items.Any(x => x.OrderItemId == item.Id && x.Quantity == item.Quantity));
        var merchandise = returned.Items.Sum(x => x.Quantity * x.OrderItem.UnitPrice);
        var net = order.Subtotal == 0 ? 0 : merchandise * (order.Subtotal - order.Discount) / order.Subtotal;
        var taxBase = order.Subtotal - order.Discount + order.Shipping;
        var amount = fullReturn ? order.Total : Math.Round(net + (taxBase == 0 ? 0 : net * order.Tax / taxBase), 2);
        returned.Status = ReturnStatus.Refunded;
        returned.ResolvedUtc = DateTime.UtcNow;
        returned.StatusHistory.Add(new ReturnStatusHistory { Status = ReturnStatus.Refunded, ActorId = Actor(), Note = $"Cash refund recorded; receipt: {request.Reference.Trim()}" });
        if (fullReturn)
        {
            payment.Status = PaymentStatus.Refunded;
            order.Status = OrderStatus.Refunded;
            order.StatusHistory.Add(new OrderStatusHistory { Status = OrderStatus.Refunded, ActorId = Actor(), Reason = "COD refund recorded" });
        }
        Record(payment, "CashRefunded", returnId, amount, request.Reference);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new { amount, currency = payment.Currency });
    }

    private Guid Actor() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private void Record(Payment payment, string operation, Guid referenceId, decimal amount, string receipt)
    {
        db.PaymentTransactions.Add(new PaymentTransaction { PaymentId = payment.Id, ProviderReference = referenceId.ToString(), EventType = operation,
            PayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{operation}:{referenceId}"))) });
        db.AuditLogs.Add(new AuditLog { UserId = Actor(), Action = operation, EntityName = nameof(Payment), EntityId = payment.Id.ToString(),
            CorrelationId = HttpContext.TraceIdentifier, IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            NewValuesJson = JsonSerializer.Serialize(new { amount, payment.Currency, receipt = receipt.Trim(), referenceId }) });
    }
}

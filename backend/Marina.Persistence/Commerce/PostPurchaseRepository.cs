using System.Data;
using Marina.Application.Commerce;
using Marina.Domain.Commerce;
using Marina.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace Marina.Persistence.Commerce;

public sealed class PostPurchaseRepository(MarinaDbContext db) : IPostPurchaseRepository
{
    public Task<ProductReview?> FindReviewAsync(Guid userId, Guid productId, CancellationToken ct) =>
        db.ProductReviews.SingleOrDefaultAsync(x => x.UserId == userId && x.ProductId == productId, ct);

    public Task<OrderItem?> FindDeliveredPurchaseAsync(Guid userId, Guid productId, CancellationToken ct) =>
        db.OrderItems
            .Where(x => x.Variant.ProductId == productId &&
                        db.Orders.Any(o => o.Id == x.OrderId && o.UserId == userId && o.Status == OrderStatus.Delivered))
            .OrderByDescending(x => x.CreatedUtc)
            .FirstOrDefaultAsync(ct);

    public Task<Order?> GetOwnedOrderForReturnAsync(Guid userId, Guid orderId, CancellationToken ct) =>
        db.Orders
            .Include(x => x.Items)
            .Include(x => x.StatusHistory)
            .SingleOrDefaultAsync(x => x.Id == orderId && x.UserId == userId, ct);

    public Task<ReturnRequest?> FindReturnByOrderAsync(Guid userId, Guid orderId, CancellationToken ct) =>
        db.ReturnRequests.SingleOrDefaultAsync(x => x.UserId == userId && x.OrderId == orderId, ct);

    public Task<ReturnRequest?> GetReturnForUpdateAsync(Guid returnId, CancellationToken ct) =>
        db.ReturnRequests
            .Include(x => x.Order).ThenInclude(x => x.Items)
            .Include(x => x.Order).ThenInclude(x => x.StatusHistory)
            .Include(x => x.Items).ThenInclude(x => x.OrderItem).ThenInclude(x => x.Variant).ThenInclude(x => x.Inventory)
            .Include(x => x.StatusHistory)
            .SingleOrDefaultAsync(x => x.Id == returnId, ct);

    public void Add<T>(T entity) where T : class => db.Add(entity);

    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public async Task<T> InSerializableTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var result = await operation(ct);
        await transaction.CommitAsync(ct);
        return result;
    }
}

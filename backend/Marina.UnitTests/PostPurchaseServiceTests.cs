using Marina.Application.Commerce;
using Marina.Domain.Catalog;
using Marina.Domain.Commerce;
using Xunit;

namespace Marina.UnitTests;

public sealed class PostPurchaseServiceTests
{
    [Fact]
    public async Task Delivered_customer_can_submit_verified_review()
    {
        var repo = new FakePostPurchaseRepository { Purchase = new OrderItem { Id = Guid.NewGuid() } };
        var service = new PostPurchaseService(repo);

        var review = await service.SubmitReviewAsync(
            new SubmitReviewCommand(Guid.NewGuid(), Guid.NewGuid(), 5, "Lovely", "Excellent quality and fit."), default);

        Assert.Equal(ReviewStatus.Pending, review.Status);
        Assert.Equal(repo.Purchase.Id, review.OrderItemId);
        Assert.Single(repo.Added.OfType<ProductReview>());
    }

    [Fact]
    public async Task Customer_without_delivered_purchase_cannot_review()
    {
        var service = new PostPurchaseService(new FakePostPurchaseRepository());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitReviewAsync(
            new SubmitReviewCommand(Guid.NewGuid(), Guid.NewGuid(), 4, "Good", "A sufficiently detailed review."), default));
    }

    [Fact]
    public async Task Editing_review_resets_moderation_and_does_not_duplicate()
    {
        var existing = new ProductReview { Status = ReviewStatus.Approved, ModerationNote = "approved" };
        var repo = new FakePostPurchaseRepository
        {
            Purchase = new OrderItem { Id = Guid.NewGuid() },
            Review = existing
        };
        var review = await new PostPurchaseService(repo).SubmitReviewAsync(
            new SubmitReviewCommand(Guid.NewGuid(), Guid.NewGuid(), 3, "Updated", "Updated review content."), default);
        Assert.Same(existing, review);
        Assert.Equal(ReviewStatus.Pending, review.Status);
        Assert.Null(review.ModerationNote);
        Assert.Empty(repo.Added.OfType<ProductReview>());
    }

    [Fact]
    public async Task Review_rejects_rating_outside_supported_range()
    {
        var service = new PostPurchaseService(new FakePostPurchaseRepository());
        await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitReviewAsync(
            new SubmitReviewCommand(Guid.NewGuid(), Guid.NewGuid(), 6, "Bad", "Long enough review body."), default));
    }

    [Fact]
    public async Task Return_rejects_quantity_above_purchased_quantity()
    {
        var orderItem = new OrderItem { Id = Guid.NewGuid(), Quantity = 1 };
        var repo = new FakePostPurchaseRepository
        {
            OwnedOrder = DeliveredOrder(orderItem)
        };
        var service = new PostPurchaseService(repo);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateReturnAsync(
            new CreateReturnCommand(repo.OwnedOrder.UserId, repo.OwnedOrder.Id, "Wrong size", null,
                [new ReturnLine(orderItem.Id, 2)]), default));
    }

    [Fact]
    public async Task Return_rejects_request_outside_thirty_day_window()
    {
        var orderItem = new OrderItem { Id = Guid.NewGuid(), Quantity = 1 };
        var order = DeliveredOrder(orderItem);
        order.StatusHistory.Single().CreatedUtc = DateTime.UtcNow.AddDays(-31);
        var repo = new FakePostPurchaseRepository { OwnedOrder = order };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PostPurchaseService(repo).CreateReturnAsync(
            new CreateReturnCommand(order.UserId, order.Id, "Too late", null, [new ReturnLine(orderItem.Id, 1)]), default));
    }

    [Fact]
    public async Task Return_rejects_duplicate_request_for_same_order()
    {
        var orderItem = new OrderItem { Id = Guid.NewGuid(), Quantity = 1 };
        var order = DeliveredOrder(orderItem);
        var repo = new FakePostPurchaseRepository
        {
            OwnedOrder = order,
            ReturnByOrder = new ReturnRequest { OrderId = order.Id, UserId = order.UserId }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PostPurchaseService(repo).CreateReturnAsync(
            new CreateReturnCommand(order.UserId, order.Id, "Duplicate", null, [new ReturnLine(orderItem.Id, 1)]), default));
    }

    [Fact]
    public async Task Return_rejects_non_delivered_order()
    {
        var orderItem = new OrderItem { Id = Guid.NewGuid(), Quantity = 1 };
        var order = DeliveredOrder(orderItem);
        order.Status = OrderStatus.Shipped;
        var repo = new FakePostPurchaseRepository { OwnedOrder = order };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PostPurchaseService(repo).CreateReturnAsync(
            new CreateReturnCommand(order.UserId, order.Id, "Not delivered", null, [new ReturnLine(orderItem.Id, 1)]), default));
    }

    [Fact]
    public async Task Return_rejects_duplicate_order_item_lines()
    {
        var orderItem = new OrderItem { Id = Guid.NewGuid(), Quantity = 2 };
        var order = DeliveredOrder(orderItem);
        var repo = new FakePostPurchaseRepository { OwnedOrder = order };
        await Assert.ThrowsAsync<ArgumentException>(() => new PostPurchaseService(repo).CreateReturnAsync(
            new CreateReturnCommand(order.UserId, order.Id, "Duplicate lines", null,
                [new ReturnLine(orderItem.Id, 1), new ReturnLine(orderItem.Id, 1)]), default));
    }

    [Fact]
    public async Task Receiving_partial_return_restores_stock_once_without_marking_order_returned()
    {
        var inventory = new Inventory { Quantity = 3 };
        var variant = new ProductVariant { Inventory = inventory };
        var orderItem = new OrderItem { Id = Guid.NewGuid(), VariantId = variant.Id, Variant = variant, Quantity = 2 };
        var order = DeliveredOrder(orderItem);
        var request = new ReturnRequest
        {
            OrderId = order.Id,
            Order = order,
            UserId = order.UserId,
            Status = ReturnStatus.Approved,
            PublicNumber = "RET-TEST"
        };
        request.Items.Add(new ReturnRequestItem { OrderItemId = orderItem.Id, OrderItem = orderItem, Quantity = 1 });
        var repo = new FakePostPurchaseRepository { ReturnForUpdate = request };
        var service = new PostPurchaseService(repo);

        await service.UpdateReturnStatusAsync(request.Id, Guid.NewGuid(), ReturnStatus.Received, null, default);

        Assert.Equal(4, inventory.Quantity);
        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.NotNull(request.InventoryRestoredUtc);
        Assert.Single(repo.Added.OfType<StockMovement>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateReturnStatusAsync(
            request.Id, Guid.NewGuid(), ReturnStatus.Received, null, default));
        Assert.Equal(4, inventory.Quantity);
    }

    [Fact]
    public async Task Staff_cannot_mark_refund_complete_without_provider_confirmation()
    {
        var service = new PostPurchaseService(new FakePostPurchaseRepository());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateReturnStatusAsync(
            Guid.NewGuid(), Guid.NewGuid(), ReturnStatus.Refunded, null, default));
    }

    private static Order DeliveredOrder(OrderItem item)
    {
        var order = new Order { UserId = Guid.NewGuid(), Status = OrderStatus.Delivered };
        order.Items.Add(item);
        order.StatusHistory.Add(new OrderStatusHistory { Status = OrderStatus.Delivered, CreatedUtc = DateTime.UtcNow.AddDays(-2) });
        return order;
    }

    private sealed class FakePostPurchaseRepository : IPostPurchaseRepository
    {
        public ProductReview? Review { get; set; }
        public OrderItem? Purchase { get; set; }
        public Order? OwnedOrder { get; set; }
        public ReturnRequest? ReturnByOrder { get; set; }
        public ReturnRequest? ReturnForUpdate { get; set; }
        public List<object> Added { get; } = [];

        public Task<ProductReview?> FindReviewAsync(Guid userId, Guid productId, CancellationToken ct) => Task.FromResult(Review);
        public Task<OrderItem?> FindDeliveredPurchaseAsync(Guid userId, Guid productId, CancellationToken ct) => Task.FromResult(Purchase);
        public Task<Order?> GetOwnedOrderForReturnAsync(Guid userId, Guid orderId, CancellationToken ct) => Task.FromResult(OwnedOrder);
        public Task<ReturnRequest?> FindReturnByOrderAsync(Guid userId, Guid orderId, CancellationToken ct) => Task.FromResult(ReturnByOrder);
        public Task<ReturnRequest?> GetReturnForUpdateAsync(Guid returnId, CancellationToken ct) => Task.FromResult(ReturnForUpdate);
        public void Add<T>(T entity) where T : class => Added.Add(entity);
        public Task SaveAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<T> InSerializableTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct) => operation(ct);
    }
}

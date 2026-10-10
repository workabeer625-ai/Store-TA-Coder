using Marina.Domain.Commerce;

namespace Marina.Application.Commerce;

public sealed record SubmitReviewCommand(
    Guid UserId,
    Guid ProductId,
    int Rating,
    string Title,
    string Body);

public sealed record ReturnLine(Guid OrderItemId, int Quantity);

public sealed record CreateReturnCommand(
    Guid UserId,
    Guid OrderId,
    string Reason,
    string? CustomerNote,
    IReadOnlyCollection<ReturnLine> Lines);

public interface IPostPurchaseRepository
{
    Task<ProductReview?> FindReviewAsync(Guid userId, Guid productId, CancellationToken ct);
    Task<OrderItem?> FindDeliveredPurchaseAsync(Guid userId, Guid productId, CancellationToken ct);
    Task<Order?> GetOwnedOrderForReturnAsync(Guid userId, Guid orderId, CancellationToken ct);
    Task<ReturnRequest?> FindReturnByOrderAsync(Guid userId, Guid orderId, CancellationToken ct);
    Task<ReturnRequest?> GetReturnForUpdateAsync(Guid returnId, CancellationToken ct);
    Task<T> InSerializableTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct);
    void Add<T>(T entity) where T : class;
    Task SaveAsync(CancellationToken ct);
}

public sealed class PostPurchaseService(IPostPurchaseRepository repository)
{
    public async Task<ProductReview> SubmitReviewAsync(SubmitReviewCommand command, CancellationToken ct)
    {
        var title = command.Title.Trim();
        var body = command.Body.Trim();
        if (command.Rating is < 1 or > 5 || title.Length > 120 || body.Length is < 10 or > 2000)
            throw new ArgumentException("A rating from 1 to 5 and review text from 10 to 2000 characters are required.");

        var purchase = await repository.FindDeliveredPurchaseAsync(command.UserId, command.ProductId, ct)
            ?? throw new InvalidOperationException("Only customers with a delivered purchase can review this product.");
        var review = await repository.FindReviewAsync(command.UserId, command.ProductId, ct);
        if (review is null)
        {
            review = new ProductReview
            {
                UserId = command.UserId,
                ProductId = command.ProductId,
                OrderItemId = purchase.Id
            };
            repository.Add(review);
        }

        review.Rating = command.Rating;
        review.Title = title;
        review.Body = body;
        review.Status = ReviewStatus.Pending;
        review.ModerationNote = null;
        await repository.SaveAsync(ct);
        return review;
    }

    public Task<ReturnRequest> CreateReturnAsync(CreateReturnCommand command, CancellationToken ct)
    {
        var reason = command.Reason.Trim();
        var note = command.CustomerNote?.Trim();
        if (reason.Length is < 3 or > 120 || note?.Length > 1000 || command.Lines.Count == 0 ||
            command.Lines.Any(x => x.Quantity is < 1 or > 20) ||
            command.Lines.Select(x => x.OrderItemId).Distinct().Count() != command.Lines.Count)
            throw new ArgumentException("Invalid return request.");

        return repository.InSerializableTransactionAsync(async token =>
        {
            if (await repository.FindReturnByOrderAsync(command.UserId, command.OrderId, token) is not null)
                throw new InvalidOperationException("A return request already exists for this order.");

            var order = await repository.GetOwnedOrderForReturnAsync(command.UserId, command.OrderId, token)
                ?? throw new KeyNotFoundException("Order not found.");
            if (order.Status != OrderStatus.Delivered)
                throw new InvalidOperationException("Only delivered orders can be returned.");

            var deliveredUtc = order.StatusHistory
                .Where(x => x.Status == OrderStatus.Delivered)
                .OrderByDescending(x => x.CreatedUtc)
                .Select(x => (DateTime?)x.CreatedUtc)
                .FirstOrDefault() ?? order.UpdatedUtc ?? order.CreatedUtc;
            if (deliveredUtc < DateTime.UtcNow.AddDays(-30))
                throw new InvalidOperationException("The 30-day return window has expired.");

            var orderItems = order.Items.ToDictionary(x => x.Id);
            foreach (var line in command.Lines)
                if (!orderItems.TryGetValue(line.OrderItemId, out var item) || line.Quantity > item.Quantity)
                    throw new ArgumentException("A returned item or quantity is invalid.");

            var request = new ReturnRequest
            {
                UserId = command.UserId,
                OrderId = order.Id,
                PublicNumber = $"RET-{DateTime.UtcNow:yyyy}-{Convert.ToHexString(Guid.NewGuid().ToByteArray())[..10]}",
                Reason = reason,
                CustomerNote = string.IsNullOrWhiteSpace(note) ? null : note,
                Status = ReturnStatus.Requested
            };
            foreach (var line in command.Lines)
                request.Items.Add(new ReturnRequestItem { OrderItemId = line.OrderItemId, Quantity = line.Quantity });
            request.StatusHistory.Add(new ReturnStatusHistory
            {
                Status = ReturnStatus.Requested,
                ActorId = command.UserId,
                Note = "Return requested by customer"
            });
            repository.Add(request);
            await repository.SaveAsync(token);
            return request;
        }, ct);
    }

    public Task<ReturnRequest> UpdateReturnStatusAsync(
        Guid returnId,
        Guid actorId,
        ReturnStatus next,
        string? adminNote,
        CancellationToken ct)
    {
        var note = adminNote?.Trim();
        if (note?.Length > 1000) throw new ArgumentException("The note is too long.");
        if (next == ReturnStatus.Refunded)
            throw new InvalidOperationException("Refund completion must be confirmed by the payment provider webhook.");

        return repository.InSerializableTransactionAsync(async token =>
        {
            var request = await repository.GetReturnForUpdateAsync(returnId, token)
                ?? throw new KeyNotFoundException("Return request not found.");
            if (!IsAllowed(request.Status, next))
                throw new InvalidOperationException($"Cannot change a return from {request.Status} to {next}.");

            if (next == ReturnStatus.Received && request.InventoryRestoredUtc is null)
            {
                foreach (var line in request.Items)
                {
                    var inventory = line.OrderItem.Variant.Inventory;
                    var old = inventory.Quantity;
                    inventory.Quantity += line.Quantity;
                    repository.Add(new StockMovement
                    {
                        VariantId = line.OrderItem.VariantId,
                        Type = StockMovementType.Return,
                        OldQuantity = old,
                        Delta = line.Quantity,
                        NewQuantity = inventory.Quantity,
                        Reason = "Accepted customer return",
                        ReferenceId = request.Id,
                        ActorId = actorId
                    });
                }
                request.InventoryRestoredUtc = DateTime.UtcNow;
                if (request.Order.Items.All(item => request.Items.Any(line => line.OrderItemId == item.Id && line.Quantity == item.Quantity)))
                request.Order.Status = OrderStatus.Returned;
                request.Order.StatusHistory.Add(new OrderStatusHistory
                {
                    Status = request.Order.Status,
                    ActorId = actorId,
                    Reason = $"Return {request.PublicNumber} received"
                });
            }

            request.Status = next;
            request.AdminNote = string.IsNullOrWhiteSpace(note) ? request.AdminNote : note;
            if (next is ReturnStatus.Rejected or ReturnStatus.Cancelled) request.ResolvedUtc = DateTime.UtcNow;
            request.StatusHistory.Add(new ReturnStatusHistory { Status = next, ActorId = actorId, Note = note });
            await repository.SaveAsync(token);
            return request;
        }, ct);
    }

    private static bool IsAllowed(ReturnStatus current, ReturnStatus next) => (current, next) switch
    {
        (ReturnStatus.Requested, ReturnStatus.Approved or ReturnStatus.Rejected or ReturnStatus.Cancelled) => true,
        (ReturnStatus.Approved, ReturnStatus.Received or ReturnStatus.Cancelled) => true,
        (ReturnStatus.Received, ReturnStatus.RefundPending) => true,
        _ => false
    };
}

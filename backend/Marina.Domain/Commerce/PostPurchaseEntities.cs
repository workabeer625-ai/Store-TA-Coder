using Marina.Domain.Common;

namespace Marina.Domain.Commerce;

public enum ReviewStatus
{
    Pending,
    Approved,
    Rejected
}

public enum ReturnStatus
{
    Requested,
    Approved,
    Rejected,
    Received,
    RefundPending,
    Refunded,
    Cancelled
}

public sealed class ProductReview : Entity
{
    public Guid UserId { get; set; }
    public Guid ProductId { get; set; }
    public Guid OrderItemId { get; set; }
    public int Rating { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;
    public string? ModerationNote { get; set; }
}

public sealed class ReturnRequest : Entity
{
    public Guid UserId { get; set; }
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public string PublicNumber { get; set; } = "";
    public ReturnStatus Status { get; set; } = ReturnStatus.Requested;
    public string Reason { get; set; } = "";
    public string? CustomerNote { get; set; }
    public string? AdminNote { get; set; }
    public DateTime? InventoryRestoredUtc { get; set; }
    public DateTime? ResolvedUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<ReturnRequestItem> Items { get; set; } = [];
    public ICollection<ReturnStatusHistory> StatusHistory { get; set; } = [];
}

public sealed class ReturnRequestItem : Entity
{
    public Guid ReturnRequestId { get; set; }
    public Guid OrderItemId { get; set; }
    public OrderItem OrderItem { get; set; } = null!;
    public int Quantity { get; set; }
}

public sealed class ReturnStatusHistory : Entity
{
    public Guid ReturnRequestId { get; set; }
    public ReturnStatus Status { get; set; }
    public Guid? ActorId { get; set; }
    public string? Note { get; set; }
}

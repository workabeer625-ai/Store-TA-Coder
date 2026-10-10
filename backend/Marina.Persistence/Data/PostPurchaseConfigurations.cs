using Marina.Domain.Catalog;
using Marina.Domain.Commerce;
using Marina.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marina.Persistence.Data;

public sealed class ProductReviewConfiguration : IEntityTypeConfiguration<ProductReview>
{
    public void Configure(EntityTypeBuilder<ProductReview> b)
    {
        b.HasIndex(x => new { x.UserId, x.ProductId }).IsUnique();
        b.HasIndex(x => new { x.ProductId, x.Status, x.CreatedUtc });
        b.Property(x => x.Title).HasMaxLength(120);
        b.Property(x => x.Body).HasMaxLength(2000);
        b.Property(x => x.ModerationNote).HasMaxLength(500);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<OrderItem>().WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("CK_ProductReview_Rating", "[Rating] >= 1 AND [Rating] <= 5"));
    }
}

public sealed class ReturnRequestConfiguration : IEntityTypeConfiguration<ReturnRequest>, IEntityTypeConfiguration<ReturnRequestItem>
{
    public void Configure(EntityTypeBuilder<ReturnRequest> b)
    {
        b.HasIndex(x => x.PublicNumber).IsUnique();
        b.HasIndex(x => new { x.UserId, x.OrderId }).IsUnique();
        b.HasIndex(x => new { x.Status, x.CreatedUtc });
        b.Property(x => x.PublicNumber).HasMaxLength(40);
        b.Property(x => x.Reason).HasMaxLength(120);
        b.Property(x => x.CustomerNote).HasMaxLength(1000);
        b.Property(x => x.AdminNote).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<ReturnRequestItem> b)
    {
        b.HasIndex(x => new { x.ReturnRequestId, x.OrderItemId }).IsUnique();
        b.HasOne<ReturnRequest>().WithMany(x => x.Items).HasForeignKey(x => x.ReturnRequestId);
        b.HasOne(x => x.OrderItem).WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("CK_ReturnRequestItem_Quantity", "[Quantity] > 0 AND [Quantity] <= 20"));
    }
}

public sealed class ReturnStatusHistoryConfiguration : IEntityTypeConfiguration<ReturnStatusHistory>
{
    public void Configure(EntityTypeBuilder<ReturnStatusHistory> b)
    {
        b.HasOne<ReturnRequest>().WithMany(x => x.StatusHistory).HasForeignKey(x => x.ReturnRequestId);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.HasIndex(x => new { x.ReturnRequestId, x.CreatedUtc });
    }
}

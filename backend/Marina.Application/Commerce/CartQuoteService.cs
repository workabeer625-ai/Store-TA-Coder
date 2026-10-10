using Marina.Domain.Commerce;

namespace Marina.Application.Commerce;

public sealed record CartQuoteLine(Guid VariantId, int Quantity);
public sealed record CartQuote(decimal Subtotal, decimal Discount, decimal Shipping, decimal Tax, decimal Total, string? CouponCode);

public sealed class CartQuoteService(ICommerceRepository repository)
{
    public async Task<CartQuote> QuoteAsync(Guid userId, IReadOnlyCollection<CartQuoteLine> lines, string? couponCode, CancellationToken ct)
    {
        if (lines.Count == 0 || lines.Any(x => x.Quantity is < 1 or > 20) ||
            lines.Select(x => x.VariantId).Distinct().Count() != lines.Count)
            throw new ArgumentException("The cart is empty or contains invalid quantities.");

        var variants = await repository.GetVariantsForCheckoutAsync(lines.Select(x => x.VariantId), ct);
        decimal subtotal = 0;
        foreach (var line in lines)
        {
            if (!variants.TryGetValue(line.VariantId, out var variant) || !variant.IsActive || !variant.Product.IsActive)
                throw new KeyNotFoundException("A product variant is unavailable.");
            if (variant.Inventory.Quantity - variant.Inventory.ReservedQuantity < line.Quantity)
                throw new InvalidOperationException("The requested product quantity is no longer available. Refresh your cart and try again.");
            subtotal += variant.Price * line.Quantity;
        }

        decimal discount = 0;
        string? normalizedCoupon = null;
        if (!string.IsNullOrWhiteSpace(couponCode))
        {
            normalizedCoupon = couponCode.Trim().ToUpperInvariant();
            var coupon = await repository.FindCouponAsync(normalizedCoupon, ct) ?? throw new ArgumentException("Coupon is invalid.");
            var now = DateTime.UtcNow;
            if (!coupon.IsActive || now < coupon.ValidFromUtc || now > coupon.ValidToUtc || subtotal < (coupon.MinimumPurchase ?? 0))
                throw new ArgumentException("Coupon is invalid or expired.");
            var totalUses = await repository.CouponUseCountAsync(coupon.Id, null, ct);
            var userUses = await repository.CouponUseCountAsync(coupon.Id, userId, ct);
            if ((coupon.MaxUses.HasValue && totalUses >= coupon.MaxUses) ||
                (coupon.MaxUsesPerCustomer.HasValue && userUses >= coupon.MaxUsesPerCustomer))
                throw new ArgumentException("Coupon usage limit reached.");
            discount = coupon.Type == DiscountType.Percentage ? subtotal * coupon.Value / 100 : coupon.Value;
            if (coupon.MaximumDiscount.HasValue) discount = Math.Min(discount, coupon.MaximumDiscount.Value);
            discount = Math.Min(discount, subtotal);
        }

        var shipping = subtotal - discount >= 300 ? 0 : 20;
        var tax = Math.Round((subtotal - discount + shipping) * .15m, 2);
        return new CartQuote(subtotal, discount, shipping, tax, subtotal - discount + shipping + tax, normalizedCoupon);
    }
}

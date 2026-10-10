using Marina.Application.Commerce;
using Marina.Domain.Catalog;
using Marina.Domain.Commerce;
using Xunit;

namespace Marina.UnitTests;

public sealed class CartQuoteServiceTests
{
    [Fact]
    public async Task Quote_uses_authoritative_price_and_free_shipping_threshold()
    {
        var repository = new QuoteRepository(price: 160m, stock: 5);
        var quote = await new CartQuoteService(repository).QuoteAsync(
            Guid.NewGuid(), [new CartQuoteLine(repository.Variant.Id, 2)], null, default);

        Assert.Equal(320m, quote.Subtotal);
        Assert.Equal(0m, quote.Shipping);
        Assert.Equal(48m, quote.Tax);
        Assert.Equal(368m, quote.Total);
    }

    [Fact]
    public async Task Quote_applies_coupon_limits_and_normalizes_code()
    {
        var repository = new QuoteRepository(price: 200m, stock: 5)
        {
            Coupon = new Coupon
            {
                Code = "MARINA10",
                Type = DiscountType.Percentage,
                Value = 10,
                MaximumDiscount = 30,
                ValidFromUtc = DateTime.UtcNow.AddDays(-1),
                ValidToUtc = DateTime.UtcNow.AddDays(1),
                IsActive = true
            }
        };
        var quote = await new CartQuoteService(repository).QuoteAsync(
            Guid.NewGuid(), [new CartQuoteLine(repository.Variant.Id, 2)], " marina10 ", default);

        Assert.Equal(30m, quote.Discount);
        Assert.Equal("MARINA10", quote.CouponCode);
        Assert.Equal(425.5m, quote.Total);
    }

    [Fact]
    public async Task Quote_rejects_quantity_beyond_available_inventory()
    {
        var repository = new QuoteRepository(price: 100m, stock: 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CartQuoteService(repository).QuoteAsync(
                Guid.NewGuid(), [new CartQuoteLine(repository.Variant.Id, 2)], null, default));
    }

    [Fact]
    public async Task Quote_rejects_inactive_product()
    {
        var repository = new QuoteRepository(100m, 5);
        repository.Variant.Product.IsActive = false;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => new CartQuoteService(repository)
            .QuoteAsync(Guid.NewGuid(), [new CartQuoteLine(repository.Variant.Id, 1)], null, default));
    }

    [Fact]
    public async Task Quote_accounts_for_reserved_inventory()
    {
        var repository = new QuoteRepository(100m, 5);
        repository.Variant.Inventory.ReservedQuantity = 4;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CartQuoteService(repository)
            .QuoteAsync(Guid.NewGuid(), [new CartQuoteLine(repository.Variant.Id, 2)], null, default));
    }

    [Fact]
    public async Task Quote_rejects_expired_coupon()
    {
        var repository = new QuoteRepository(100m, 5)
        {
            Coupon = ActiveCoupon(validTo: DateTime.UtcNow.AddMinutes(-1))
        };
        await Assert.ThrowsAsync<ArgumentException>(() => new CartQuoteService(repository)
            .QuoteAsync(Guid.NewGuid(), [new CartQuoteLine(repository.Variant.Id, 1)], "SAVE", default));
    }

    [Fact]
    public async Task Quote_rejects_coupon_below_minimum_purchase()
    {
        var repository = new QuoteRepository(100m, 5)
        {
            Coupon = ActiveCoupon(minimum: 200m)
        };
        await Assert.ThrowsAsync<ArgumentException>(() => new CartQuoteService(repository)
            .QuoteAsync(Guid.NewGuid(), [new CartQuoteLine(repository.Variant.Id, 1)], "SAVE", default));
    }

    [Fact]
    public async Task Quote_rejects_coupon_after_global_usage_limit()
    {
        var coupon = ActiveCoupon();
        coupon.MaxUses = 2;
        var repository = new QuoteRepository(100m, 5) { Coupon = coupon, GlobalUses = 2 };
        await Assert.ThrowsAsync<ArgumentException>(() => new CartQuoteService(repository)
            .QuoteAsync(Guid.NewGuid(), [new CartQuoteLine(repository.Variant.Id, 1)], "SAVE", default));
    }

    [Fact]
    public async Task Fixed_coupon_never_discounts_below_zero()
    {
        var coupon = ActiveCoupon();
        coupon.Type = DiscountType.FixedAmount;
        coupon.Value = 500m;
        var repository = new QuoteRepository(100m, 5) { Coupon = coupon };
        var quote = await new CartQuoteService(repository)
            .QuoteAsync(Guid.NewGuid(), [new CartQuoteLine(repository.Variant.Id, 1)], "SAVE", default);
        Assert.Equal(100m, quote.Discount);
        Assert.Equal(23m, quote.Total);
    }

    [Fact]
    public async Task Quote_rejects_duplicate_variant_lines()
    {
        var repository = new QuoteRepository(100m, 5);
        await Assert.ThrowsAsync<ArgumentException>(() => new CartQuoteService(repository).QuoteAsync(
            Guid.NewGuid(),
            [new CartQuoteLine(repository.Variant.Id, 1), new CartQuoteLine(repository.Variant.Id, 1)],
            null, default));
    }

    private static Coupon ActiveCoupon(DateTime? validTo = null, decimal? minimum = null) => new()
    {
        Code = "SAVE",
        Type = DiscountType.Percentage,
        Value = 10,
        MinimumPurchase = minimum,
        ValidFromUtc = DateTime.UtcNow.AddDays(-1),
        ValidToUtc = validTo ?? DateTime.UtcNow.AddDays(1),
        IsActive = true
    };

    private sealed class QuoteRepository : ICommerceRepository
    {
        public QuoteRepository(decimal price, int stock)
        {
            Variant = new ProductVariant
            {
                Product = new Product { IsActive = true },
                Price = price,
                Sku = "MAR-QUOTE-1",
                IsActive = true,
                Inventory = new Inventory { Quantity = stock }
            };
        }

        public ProductVariant Variant { get; }
        public Coupon? Coupon { get; init; }
        public int GlobalUses { get; init; }
        public int CustomerUses { get; init; }
        public Task<Order?> FindOrderByIdempotencyAsync(Guid userId, string key, CancellationToken ct) => Task.FromResult<Order?>(null);
        public Task<Address?> FindOwnedAddressAsync(Guid userId, Guid addressId, CancellationToken ct) => Task.FromResult<Address?>(null);
        public Task<IReadOnlyDictionary<Guid, ProductVariant>> GetVariantsForCheckoutAsync(IEnumerable<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ProductVariant>>(new Dictionary<Guid, ProductVariant> { [Variant.Id] = Variant });
        public Task<Coupon?> FindCouponAsync(string code, CancellationToken ct) => Task.FromResult(Coupon?.Code == code ? Coupon : null);
        public Task<int> CouponUseCountAsync(Guid couponId, Guid? userId, CancellationToken ct) =>
            Task.FromResult(userId.HasValue ? CustomerUses : GlobalUses);
        public Task<T> InSerializableTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct) => operation(ct);
        public Task ClearPurchasedCartAsync(Guid userId,IEnumerable<Guid> variantIds,CancellationToken ct)=>Task.CompletedTask;public void Add<T>(T entity) where T : class { }
        public Task SaveAsync(CancellationToken ct) => Task.CompletedTask;
    }
}

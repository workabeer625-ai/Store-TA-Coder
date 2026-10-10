using System.Security.Claims;
using Marina.Domain.Commerce;
using Marina.Application.Commerce;
using Marina.Persistence.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marina.Api.Controllers;

[ApiController, Authorize, Route("api/v1/cart")]
public sealed class CartController(MarinaDbContext db, CartQuoteService quotes) : ControllerBase
{
    public sealed record ItemRequest(Guid VariantId, int Quantity);
    public sealed record QuoteRequest(string? CouponCode);

    [HttpGet]
    public async Task<ActionResult> Get([FromQuery] string culture = "en")
    {
        culture = culture.Equals("ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";
        var cart = await db.Carts.AsNoTracking().Where(x => x.UserId == UserId()).Select(x => new
        {
            x.Id,
            Items = x.Items.Select(i => new { i.Id, i.VariantId, ProductId=i.Variant.ProductId, i.Quantity, i.Variant.Sku, i.Variant.Price, Available = i.Variant.Inventory.Quantity - i.Variant.Inventory.ReservedQuantity, Name = i.Variant.Product.Translations.Where(t => t.Culture == culture).Select(t => t.Name).FirstOrDefault(), Image = i.Variant.Product.Images.OrderBy(p => p.SortOrder).Select(p => p.Url).FirstOrDefault(), Color=i.Variant.Color!.Translations.Where(t=>t.Culture==culture).Select(t=>t.Name).FirstOrDefault(), Size=i.Variant.Size!.Translations.Where(t=>t.Culture==culture).Select(t=>t.Name).FirstOrDefault() }),
            Subtotal = x.Items.Sum(i => i.Quantity * i.Variant.Price)
        }).SingleOrDefaultAsync();
        return cart is null ? Ok(new { id = Guid.Empty, items = Array.Empty<object>(), subtotal = 0m }) : Ok(cart);
    }

    [HttpPost("items")]
    public async Task<IActionResult> Add(ItemRequest request)
    {
        if (request.VariantId == Guid.Empty || request.Quantity is < 1 or > 20) return BadRequest();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var variant = await db.ProductVariants.Include(x => x.Inventory).Include(x=>x.Product).SingleOrDefaultAsync(x => x.Id == request.VariantId && x.IsActive && !x.IsDeleted && x.Product.IsActive && !x.Product.IsDeleted && x.Product.Category.IsActive && !x.Product.Category.IsDeleted && (x.Product.Brand == null || (x.Product.Brand.IsActive && !x.Product.Brand.IsDeleted)) && x.Inventory != null && !x.Inventory.IsDeleted) ?? throw new KeyNotFoundException("Variant not found.");
        var cart = await db.Carts.Include(x => x.Items).SingleOrDefaultAsync(x => x.UserId == UserId());
        if (cart is null) { cart = new Cart { UserId = UserId() }; db.Carts.Add(cart); }
        var item = cart.Items.SingleOrDefault(x => x.VariantId == request.VariantId);
        var nextQuantity=item is null?request.Quantity:Math.Min(20,item.Quantity+request.Quantity);
        if (variant.Inventory.Quantity - variant.Inventory.ReservedQuantity < nextQuantity) return Conflict(new { message = "Insufficient stock." });
        if (item is null) cart.Items.Add(new CartItem { VariantId = request.VariantId, Quantity = request.Quantity });
        else item.Quantity = nextQuantity;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return NoContent();
    }

    [HttpPut("items/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateCartQuantityRequest request)
    {
        var quantity = request.Quantity;
        if (quantity is < 1 or > 20) return BadRequest();
        var item = await db.CartItems.Include(x => x.Cart).Include(x => x.Variant).ThenInclude(x => x.Inventory).SingleOrDefaultAsync(x => x.Id == id && x.Cart.UserId == UserId()) ?? throw new KeyNotFoundException("Cart item not found.");
        if (item.Variant.Inventory.Quantity - item.Variant.Inventory.ReservedQuantity < quantity) return Conflict(new { message = "Insufficient stock." });
        item.Quantity = quantity;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("items/{id:guid}")]
    public async Task<IActionResult> Remove(Guid id)
    {
        var item = await db.CartItems.Include(x => x.Cart).SingleOrDefaultAsync(x => x.Id == id && x.Cart.UserId == UserId()) ?? throw new KeyNotFoundException("Cart item not found.");
        db.CartItems.Remove(item);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        await db.CartItems.Where(x => x.Cart.UserId == UserId()).ExecuteDeleteAsync(ct);
        return NoContent();
    }

    [HttpPost("quote")]
    public async Task<ActionResult> Quote(QuoteRequest request, CancellationToken ct)
    {
        var lines = await db.CartItems.AsNoTracking().Where(x => x.Cart.UserId == UserId())
            .Select(x => new CartQuoteLine(x.VariantId, x.Quantity)).ToListAsync(ct);
        return Ok(await quotes.QuoteAsync(UserId(), lines, request.CouponCode, ct));
    }

    private Guid UserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

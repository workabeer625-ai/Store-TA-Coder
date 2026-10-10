using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Marina.Domain.Catalog;
using Marina.Domain.Commerce;
using Marina.Persistence.Data;
using Marina.Persistence.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Marina.IntegrationTests;

// Each run owns a fresh database. Never point this fixture at the application database.
public sealed class SqlRuntimeFactory : WebApplicationFactory<Program>
{
    public const string Password = "Runtime-Test!Password829";
    public string DatabaseName { get; } = "MarinaTest_" + Guid.NewGuid().ToString("N");
    public Guid VariantId { get; private set; }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("MARINA_TEST_SQL")
            ?? "Server=ABEER\\MSSQLSERVER2;Integrated Security=True;TrustServerCertificate=True") { InitialCatalog = DatabaseName };
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "isolated-runtime-test-signing-key-at-least-32-characters",
            ["Jwt:Issuer"] = "Marina.Api", ["Jwt:Audience"] = "Marina.App",
            ["Database:ApplyMigrations"] = "false", ["Development:SeedCatalog"] = "false"
            , ["Logging:LogLevel:Default"] = "Warning"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<MarinaDbContext>>();
            services.AddDbContext<MarinaDbContext>(options => options.UseSqlServer(connection.ConnectionString));
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes("isolated-runtime-test-signing-key-at-least-32-characters"));
                options.TokenValidationParameters.ValidIssuer = "Marina.Api";
                options.TokenValidationParameters.ValidAudience = "Marina.App";
            });
        });
    }

    public async Task InitializeDataAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarinaDbContext>();
        await db.Database.MigrateAsync();
        await DatabaseSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>());
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        foreach (var email in new[] { "runtime-a@example.test", "runtime-b@example.test" })
        {
            var user = new AppUser { Email = email, UserName = email, EmailConfirmed = true, DisplayName = email };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Customer")).Succeeded);
        }
        var category = await db.Categories.OrderBy(x => x.Slug).FirstAsync();
        var product = new Product { CategoryId = category.Id, Slug = "runtime-product", Translations =
            [new ProductTranslation { Culture = "en", Name = "Runtime Product" }, new ProductTranslation { Culture = "ar", Name = "منتج الاختبار" }] };
        var variant = new ProductVariant { Product = product, Sku = "RUNTIME-001", Price = 100, Inventory = new Inventory { Quantity = 40 } };
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();
        VariantId = variant.Id;
    }

    public async Task<HttpClient> LoginAsync(string email = "runtime-a@example.test")
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password, device = "SQL runtime tests" });
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", data.GetProperty("accessToken").GetString());
        return client;
    }

    public async Task DeleteOwnedDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarinaDbContext>();
        Assert.Equal(DatabaseName, db.Database.GetDbConnection().Database);
        Assert.StartsWith("MarinaTest_", DatabaseName);
        await db.Database.EnsureDeletedAsync();
    }
}

public sealed class SqlRuntimeTests
{
    [Fact]
    public async Task Checkout_COD_is_atomic_idempotent_and_clears_purchased_cart_lines()
    {
        await using var factory = new SqlRuntimeFactory();
        try
        {
            await factory.InitializeDataAsync();
            using var client = await factory.LoginAsync();
            (await client.PostAsJsonAsync("/api/v1/cart/items", new { variantId = factory.VariantId, quantity = 2 })).EnsureSuccessStatusCode();
            var addressResponse = await client.PostAsJsonAsync("/api/v1/customer/addresses", new
            { label = "Home", recipient = "Runtime Customer", phone = "+966500000000", city = "Riyadh", line1 = "Test address", isDefault = true });
            addressResponse.EnsureSuccessStatusCode();
            var address = await addressResponse.Content.ReadFromJsonAsync<JsonElement>();
            var request = new { addressId = address.GetProperty("id").GetGuid(), idempotencyKey = "runtime-checkout",
                items = new[] { new { variantId = factory.VariantId, quantity = 2 } } };
            var response = await client.PostAsJsonAsync("/api/v1/orders", request);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var order = await response.Content.ReadFromJsonAsync<JsonElement>();
            var again = await client.PostAsJsonAsync("/api/v1/orders", request);
            again.EnsureSuccessStatusCode();
            Assert.Equal(order.GetProperty("id").GetGuid(), (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
            Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/v1/cart")).GetProperty("items").EnumerateArray());
            Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/v1/orders")).GetProperty("items").EnumerateArray());
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MarinaDbContext>();
            Assert.Equal(1, await db.Orders.CountAsync());
            Assert.Equal(1, await db.OrderItems.CountAsync());
            Assert.Equal(38, (await db.Inventories.SingleAsync()).Quantity);
            var payment = await db.Payments.SingleAsync();
            Assert.Equal("CashOnDelivery", payment.Provider);
            Assert.Equal(PaymentStatus.Pending, payment.Status);
            Assert.Equal((await db.Orders.SingleAsync()).Total, payment.Amount);
            (await client.PostAsJsonAsync($"/api/v1/orders/{order.GetProperty("id").GetGuid()}/cancel", new { reason = "Runtime test" })).EnsureSuccessStatusCode();
            db.ChangeTracker.Clear();
            Assert.Equal(40, (await db.Inventories.SingleAsync()).Quantity);
        }
        finally { await factory.DeleteOwnedDatabaseAsync(); }
    }

    [Fact]
    public async Task SQL_constraints_reject_duplicate_reference_and_negative_price()
    {
        await using var factory = new SqlRuntimeFactory();
        try
        {
            await factory.InitializeDataAsync();
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MarinaDbContext>();
            db.Brands.Add(new Brand { Slug = "same-slug" });
            await db.SaveChangesAsync();
            db.Brands.Add(new Brand { Slug = "same-slug" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            var variant = await db.ProductVariants.SingleAsync();
            variant.Price = -1;
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            Assert.Equal(100, (await db.ProductVariants.SingleAsync()).Price);
        }
        finally { await factory.DeleteOwnedDatabaseAsync(); }
    }

    [Fact]
    public async Task Swagger_and_cart_add_persist_and_reload_on_SQL_Server()
    {
        await using var factory = new SqlRuntimeFactory();
        try
        {
            await factory.InitializeDataAsync();
            using var client = await factory.LoginAsync();
            var swagger = await client.GetAsync("/swagger/v1/swagger.json");
            swagger.EnsureSuccessStatusCode();
            Assert.Equal("3.0.1", (await swagger.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("openapi").GetString());
            for (var quantity = 1; quantity <= 2; quantity++)
            {
                var added = await client.PostAsJsonAsync("/api/v1/cart/items", new { variantId = factory.VariantId, quantity = 1 });
                Assert.True(added.IsSuccessStatusCode, $"POST cart: {added.StatusCode} {await added.Content.ReadAsStringAsync()}");
                var cart = await client.GetFromJsonAsync<JsonElement>("/api/v1/cart");
                var item = Assert.Single(cart.GetProperty("items").EnumerateArray());
                Assert.Equal(quantity, item.GetProperty("quantity").GetInt32());
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MarinaDbContext>();
                Assert.Equal(quantity, (await db.CartItems.SingleAsync()).Quantity);
            }
            // Reproduce adding a NEW line to an EXISTING cart, including an emptied cart.
            var existing = await client.GetFromJsonAsync<JsonElement>("/api/v1/cart");
            var id = existing.GetProperty("items")[0].GetProperty("id").GetGuid();
            (await client.DeleteAsync($"/api/v1/cart/items/{id}")).EnsureSuccessStatusCode();
            var readded = await client.PostAsJsonAsync("/api/v1/cart/items", new { variantId = factory.VariantId, quantity = 1 });
            Assert.True(readded.IsSuccessStatusCode, $"Re-add cart: {readded.StatusCode} {await readded.Content.ReadAsStringAsync()}");
            var after = await client.GetFromJsonAsync<JsonElement>("/api/v1/cart");
            id = after.GetProperty("items")[0].GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/v1/cart/items/{id}", new { quantity = 0 })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/v1/cart/items/{id}", new { quantity = 21 })).StatusCode);
            (await client.PutAsJsonAsync($"/api/v1/cart/items/{id}", new { quantity = 3 })).EnsureSuccessStatusCode();
            Assert.Equal(3, (await client.GetFromJsonAsync<JsonElement>("/api/v1/cart")).GetProperty("items")[0].GetProperty("quantity").GetInt32());
            using var second = await factory.LoginAsync("runtime-b@example.test");
            Assert.Equal(HttpStatusCode.NotFound, (await second.DeleteAsync($"/api/v1/cart/items/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await second.PutAsJsonAsync($"/api/v1/cart/items/{id}", new { quantity = 2 })).StatusCode);
            (await second.DeleteAsync("/api/v1/cart")).EnsureSuccessStatusCode();
            Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/v1/cart")).GetProperty("items").EnumerateArray());
            (await client.DeleteAsync("/api/v1/cart")).EnsureSuccessStatusCode();
            Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/v1/cart")).GetProperty("items").EnumerateArray());
            using var check = factory.Services.CreateScope();
            Assert.Equal(0, await check.ServiceProvider.GetRequiredService<MarinaDbContext>().CartItems.CountAsync());
        }
        finally { await factory.DeleteOwnedDatabaseAsync(); }
    }

    [Fact]
    public async Task Logged_out_disabled_and_password_reset_sessions_are_rejected()
    {
        await using var factory = new SqlRuntimeFactory();
        try
        {
            await factory.InitializeDataAsync();
            using var client = await factory.LoginAsync();
            (await client.PostAsync("/api/v1/auth/logout", null)).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/cart")).StatusCode);

            using var disabled = await factory.LoginAsync();
            using (var scope = factory.Services.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
                var user = (await users.FindByEmailAsync("runtime-a@example.test"))!;
                user.IsActive = false;
                Assert.True((await users.UpdateAsync(user)).Succeeded);
            }
            Assert.Equal(HttpStatusCode.Unauthorized, (await disabled.GetAsync("/api/v1/cart")).StatusCode);

            using var resetClient = await factory.LoginAsync("runtime-b@example.test");
            string token;
            using (var scope = factory.Services.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
                token = await users.GeneratePasswordResetTokenAsync((await users.FindByEmailAsync("runtime-b@example.test"))!);
            }
            (await resetClient.PostAsJsonAsync("/api/v1/auth/reset-password", new { email = "runtime-b@example.test", token, newPassword = "Replacement-Password!942" })).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Unauthorized, (await resetClient.GetAsync("/api/v1/cart")).StatusCode);
        }
        finally { await factory.DeleteOwnedDatabaseAsync(); }
    }
}

using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Marina.Api.Services;
using Marina.Application.Abstractions;
using Marina.Application.Commerce;
using Marina.Infrastructure.Security;
using Marina.Persistence.Commerce;
using Marina.Persistence.Data;
using Marina.Persistence.Identity;
using Marina.Persistence.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("DefaultConnection is required.");
var jwt = builder.Configuration.GetSection("Jwt");
var key = jwt["Key"] ?? throw new InvalidOperationException("Jwt:Key is required.");
builder.Services.AddDbContext<MarinaDbContext>(o => o.UseSqlServer(connection));
builder.Services.AddScoped<IApplicationDbContext>(x => x.GetRequiredService<MarinaDbContext>());
builder.Services.AddScoped<ICommerceRepository, CommerceRepository>();
builder.Services.AddScoped<CheckoutService>();
builder.Services.AddScoped<CartQuoteService>();
builder.Services.AddScoped<IPostPurchaseRepository, PostPurchaseRepository>();
builder.Services.AddScoped<PostPurchaseService>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<SessionTokenValidator>();
builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = context => context.HttpContext.RequestServices
            .GetRequiredService<SessionTokenValidator>().ValidateAsync(context)
    });
builder.Services.AddScoped<IPermissionResolver, PermissionResolver>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddSingleton<IAccountMessageSender, AccountEmailSender>();
if (builder.Environment.IsDevelopment()) builder.Services.AddSingleton<IMediaStorage, LocalDevelopmentMediaStorage>();
builder.Services.AddIdentityCore<AppUser>(o => { o.Password.RequiredLength = 12; o.Password.RequiredUniqueChars = 4; o.Password.RequireDigit = true; o.Password.RequireUppercase = true; o.Password.RequireLowercase = true; o.Password.RequireNonAlphanumeric = true; o.Lockout.AllowedForNewUsers = true; o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15); o.User.RequireUniqueEmail = true; }).AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<MarinaDbContext>().AddSignInManager().AddDefaultTokenProviders();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true, ValidIssuer = jwt["Issuer"], ValidAudience = jwt["Audience"], IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), ClockSkew = TimeSpan.FromSeconds(30) });
builder.Services.AddAuthorization(o => { foreach (var permission in new[] { "Products.View", "Products.Create", "Products.Edit", "Products.Delete", "Inventory.View", "Inventory.Adjust", "Orders.View", "Orders.UpdateStatus", "Orders.Cancel", "Customers.View", "Coupons.Manage", "Notifications.Manage", "Users.Manage", "Roles.Manage", "Reports.View", "AuditLogs.View", "Settings.Manage" }) o.AddPolicy(permission, p => p.RequireClaim("permission", permission)); o.AddPolicy("StaffMfa", p => p.RequireRole("SuperAdmin", "Admin", "StoreManager", "InventoryManager", "OrderManager", "ProductManager", "CustomerSupport").RequireClaim("mfa", "true")); });
builder.Services.AddRateLimiter(o => { o.RejectionStatusCode = 429; o.AddPolicy("auth", c => RateLimitPartition.GetFixedWindowLimiter($"auth:{c.Connection.RemoteIpAddress}", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })); o.AddPolicy("public", c => RateLimitPartition.GetSlidingWindowLimiter($"public:{c.Connection.RemoteIpAddress}", _ => new SlidingWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, QueueLimit = 0 })); o.AddPolicy("admin", c => RateLimitPartition.GetFixedWindowLimiter($"admin:{c.User.Identity?.Name}:{c.Connection.RemoteIpAddress}", _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })); });
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddCors(options => options.AddPolicy("trusted", policy =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
}));
builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
    options.CustomSchemaIds(type => (type.FullName ?? type.Name).Replace('+', '.')));
builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy());

var app = builder.Build();
if (app.Configuration.GetValue<bool>("Database:ApplyMigrations"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MarinaDbContext>();
    await db.Database.MigrateAsync();
    await DatabaseSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>());
    if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Development:SeedCatalog"))
        await DevelopmentDataSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(), new Dictionary<string,string>
        {
            ["Bootstrap:SuperAdminPassword"] = app.Configuration["Bootstrap:SuperAdminPassword"] ?? "",
            ["Development:StaffPassword"] = app.Configuration["Development:StaffPassword"] ?? "",
            ["Development:CustomerPassword"] = app.Configuration["Development:CustomerPassword"] ?? ""
        });
    if (app.Environment.IsDevelopment())
    {
        var bootstrapEmail = app.Configuration["Bootstrap:SuperAdminEmail"];
        var bootstrapPassword = app.Configuration["Bootstrap:SuperAdminPassword"];
        if (!string.IsNullOrWhiteSpace(bootstrapEmail) && !string.IsNullOrWhiteSpace(bootstrapPassword))
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var admin = await userManager.FindByEmailAsync(bootstrapEmail);
            if (admin is null)
            {
                admin = new AppUser { UserName = bootstrapEmail, Email = bootstrapEmail, DisplayName = "MARINA SuperAdmin", EmailConfirmed = true, RequiresMfa = true };
                var created = await userManager.CreateAsync(admin, bootstrapPassword);
                if (!created.Succeeded) throw new InvalidOperationException("Development SuperAdmin bootstrap failed: " + string.Join(" ", created.Errors.Select(x => x.Description)));
            }
            if (!await userManager.IsInRoleAsync(admin, "SuperAdmin")) await userManager.AddToRoleAsync(admin, "SuperAdmin");
        }
    }
}
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment()) app.UseHsts();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.Use(async (context, next) => { context.TraceIdentifier = context.Request.Headers.TryGetValue("X-Correlation-ID", out var supplied) && supplied.ToString().Length <= 100 ? supplied.ToString() : context.TraceIdentifier; context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier; context.Response.Headers["X-Content-Type-Options"] = "nosniff"; context.Response.Headers["Referrer-Policy"] = "no-referrer"; context.Response.Headers["X-Frame-Options"] = "DENY"; await next(); });
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCors("trusted");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");
app.Run();
public partial class Program;

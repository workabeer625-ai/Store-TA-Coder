using Microsoft.AspNetCore.Identity;
namespace Marina.Persistence.Identity;
public sealed class AppUser:IdentityUser<Guid> { public string DisplayName { get; set; }=""; public bool IsActive { get; set; }=true; public bool RequiresMfa { get; set; } public DateTime CreatedUtc { get; set; }=DateTime.UtcNow; public DateTime? LastLoginUtc { get; set; } }

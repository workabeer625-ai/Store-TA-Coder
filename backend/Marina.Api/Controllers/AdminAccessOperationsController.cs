using Marina.Persistence.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marina.Api.Controllers;

[ApiController]
[Authorize(Policy = "StaffMfa")]
[Route("api/v1/admin/access")]
public sealed class AdminAccessOperationsController(MarinaDbContext db) : ControllerBase
{
    [HttpGet("users/{id:guid}")]
    [Authorize(Policy = "Users.Manage")]
    public async Task<ActionResult>Details(Guid id)
    {
        var user = await db.Users.AsNoTracking().Where(x => x.Id == id).Select(x => new
        {
            x.Id, x.Email, x.DisplayName, x.PhoneNumber, x.IsActive, x.EmailConfirmed, x.TwoFactorEnabled, x.LockoutEnd, x.CreatedUtc, x.LastLoginUtc,
            Roles = db.UserRoles.Where(r => r.UserId == x.Id).Join(db.Roles, r => r.RoleId, role => role.Id, (_, role) => role.Name),
            Sessions = db.UserSessions.Where(s => s.UserId == x.Id && s.RevokedUtc == null).Select(s => new { s.Id, s.DeviceName, s.IpAddress, s.LastSeenUtc })
        }).SingleOrDefaultAsync();
        return user is null ? NotFound() : Ok(user);
    }

    [HttpGet("permissions")]
    [Authorize(Policy = "Roles.Manage")]
    public async Task<ActionResult>Permissions() => Ok(await db.Permissions.AsNoTracking().OrderBy(x => x.Name).ToListAsync());

    [HttpDelete("users/{userId:guid}/sessions/{sessionId:guid}")]
    [Authorize(Policy = "Users.Manage")]
    public async Task<IActionResult>RevokeSession(Guid userId, Guid sessionId)
    {
        var session = await db.UserSessions.SingleOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId);
        if (session is null) return NotFound();
        session.RevokedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }
}

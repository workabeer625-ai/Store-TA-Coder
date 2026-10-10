using System.Security.Claims;
using Marina.Application.Abstractions;
using Marina.Persistence.Data;
using Marina.Persistence.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Marina.Api.Services;

public sealed class SessionTokenValidator(MarinaDbContext db, UserManager<AppUser> users, IPermissionResolver permissions)
{
    public async Task ValidateAsync(TokenValidatedContext context)
    {
        var principal = context.Principal!;
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !Guid.TryParse(principal.FindFirstValue("session_id"), out var sessionId))
        {
            context.Fail("Invalid session.");
            return;
        }
        var ct = context.HttpContext.RequestAborted;
        var active = await db.UserSessions.AsNoTracking().AnyAsync(x => x.Id == sessionId && x.UserId == userId && x.RevokedUtc == null && !x.IsDeleted, ct);
        var user = await users.FindByIdAsync(userId.ToString());
        if (!active || user is null || !user.IsActive || !user.EmailConfirmed || await users.IsLockedOutAsync(user))
        {
            context.Fail("Session is no longer active.");
            return;
        }
        var roles = await users.GetRolesAsync(user);
        var currentPermissions = await permissions.ForRolesAsync(roles, ct);
        if (!roles.ToHashSet(StringComparer.Ordinal).SetEquals(principal.FindAll(ClaimTypes.Role).Select(x => x.Value)) ||
            !currentPermissions.ToHashSet(StringComparer.Ordinal).SetEquals(principal.FindAll("permission").Select(x => x.Value)))
            context.Fail("Account permissions have changed.");
    }
}

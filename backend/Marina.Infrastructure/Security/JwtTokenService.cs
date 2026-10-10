using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Marina.Application.Abstractions;
using Marina.Domain.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Marina.Infrastructure.Security;

public sealed class JwtTokenService(IConfiguration config, IApplicationDbContext db, IPermissionResolver permissionResolver) : ITokenService
{
    public async Task<AuthTokenPair> IssueAsync(Guid userId, string email, IEnumerable<string> roles,
        string device, string userAgent, string ipAddress, Guid? sessionId = null, CancellationToken ct = default)
    {
        var roleArray = roles.ToArray();
        var permissions = await permissionResolver.ForRolesAsync(roleArray, ct);
        return await IssueAsync(userId, email, roleArray, permissions, device, userAgent, ipAddress, sessionId, ct);
    }

    public async Task<AuthTokenPair> IssueAsync(Guid userId, string email,
        IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions,
        string device, string userAgent, string ipAddress, Guid? sessionId = null,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(10);
        UserSession session;
        if (sessionId.HasValue)
        {
            session = db.UserSessions.SingleOrDefault(x => x.Id == sessionId && x.UserId == userId && x.RevokedUtc == null)
                ?? throw new UnauthorizedAccessException("Session is no longer active.");
            session.LastSeenUtc = now;
        }
        else
        {
            session = new UserSession { UserId = userId, DeviceName = Limit(device, 120), UserAgent = Limit(userAgent, 400), IpAddress = Limit(ipAddress, 64) };
            db.Add(session);
        }

        var section = config.GetSection("Jwt");
        var signingKey = section["Key"] ?? throw new InvalidOperationException("Jwt:Key is required.");
        if (signingKey.Length < 32) throw new InvalidOperationException("Jwt:Key must contain at least 32 characters.");
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, userId.ToString()), new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()), new(ClaimTypes.NameIdentifier, userId.ToString()), new(JwtRegisteredClaimNames.Email, email), new("session_id", session.Id.ToString()) };
        claims.AddRange(roles.Select(x => new Claim(ClaimTypes.Role, x)));
        claims.AddRange(permissions.Select(x => new Claim("permission", x)));
        if (roles.Any(x => x != "Customer")) claims.Add(new Claim("mfa", "true"));
        var access = new JwtSecurityToken(section["Issuer"], section["Audience"], claims, now, expires,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256));
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        db.Add(new RefreshToken { UserSessionId = session.Id, TokenHash = Hash(raw), ExpiresUtc = now.AddDays(30) });
        await db.SaveChangesAsync(ct);
        return new(new JwtSecurityTokenHandler().WriteToken(access), raw, expires, session.Id);
    }

    public string Hash(string rawToken) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    private static string Limit(string value, int max) => string.IsNullOrWhiteSpace(value) ? "Unknown" : value[..Math.Min(value.Length, max)];
}

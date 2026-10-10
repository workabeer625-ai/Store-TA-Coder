using System.ComponentModel.DataAnnotations;
using Marina.Application.Abstractions;
using Marina.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Marina.Api.Controllers;

[ApiController, Route("api/v1/auth/mfa-login"), Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
public sealed class MfaLoginController(UserManager<AppUser> users, SignInManager<AppUser> signIn, ITokenService tokens) : ControllerBase
{
    public sealed record MfaLoginRequest([EmailAddress] string Email, [Required] string Password, [Required] string Code, string? Device);

    [HttpPost]
    public async Task<ActionResult> Login(MfaLoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive || !user.EmailConfirmed) return Unauthorized();
        var passwordResult = await signIn.CheckPasswordSignInAsync(user, request.Password, true);
        if (!passwordResult.Succeeded) return Unauthorized();
        var roles = await users.GetRolesAsync(user);
        if (roles.All(x => x == "Customer") && !user.TwoFactorEnabled) return BadRequest(new { message = "MFA challenge is for staff accounts." });
        var code = request.Code.Replace(" ", "").Replace("-", "");
        var valid = await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code);
        IEnumerable<string>? recoveryCodes = null;
        if (valid && !user.TwoFactorEnabled)
        {
            await users.SetTwoFactorEnabledAsync(user, true);
            user.RequiresMfa = false;
            recoveryCodes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 8);
        }
        else if (!valid && user.TwoFactorEnabled)
        {
            valid = (await users.RedeemTwoFactorRecoveryCodeAsync(user, code)).Succeeded;
        }
        if (!valid) return Unauthorized();
        var pair = await tokens.IssueAsync(user.Id, user.Email!, roles, request.Device ?? "Unknown", Request.Headers.UserAgent.ToString(), HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        return recoveryCodes is null ? Ok(pair) : Ok(new { pair.AccessToken, pair.RefreshToken, pair.AccessExpiresUtc, pair.SessionId, recoveryCodes });
    }
}

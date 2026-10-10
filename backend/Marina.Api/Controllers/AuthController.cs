using System.ComponentModel.DataAnnotations; using System.Security.Claims; using Marina.Application.Abstractions; using Marina.Domain.Security; using Marina.Persistence.Data; using Marina.Persistence.Identity; using Microsoft.AspNetCore.Authorization; using Microsoft.AspNetCore.Identity; using Microsoft.AspNetCore.Mvc; using Microsoft.EntityFrameworkCore;
namespace Marina.Api.Controllers;
[ApiController,Route("api/v1/auth")]
public sealed class AuthController(UserManager<AppUser>users,SignInManager<AppUser>signIn,ITokenService tokens,MarinaDbContext db,IAccountMessageSender messages):ControllerBase{
 public sealed record RegisterRequest([EmailAddress]string Email,[MinLength(12),MaxLength(128)]string Password,[Required,MaxLength(100)]string DisplayName);public sealed record LoginRequest([EmailAddress]string Email,[Required]string Password,string?Device);public sealed record RefreshRequest([Required]string RefreshToken,string?Device);public sealed record ResetRequest([EmailAddress]string Email,[Required,MaxLength(4000)]string Token,[MinLength(12),MaxLength(128)]string NewPassword);public sealed record VerifyEmailRequest([EmailAddress]string Email,[Required,MaxLength(4000)]string Token);public sealed record ChangePasswordRequest(string CurrentPassword,[MinLength(12),MaxLength(128)]string NewPassword);public sealed record MfaCodeRequest(string Code);
 [HttpPost("register"),Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]public async Task<ActionResult>Register(RegisterRequest r,CancellationToken ct){var email=r.Email.Trim().ToLowerInvariant();var user=new AppUser{UserName=email,Email=email,DisplayName=r.DisplayName.Trim()};var result=await users.CreateAsync(user,r.Password);if(!result.Succeeded)return IdentityErrors(result);await users.AddToRoleAsync(user,"Customer");var token=await users.GenerateEmailConfirmationTokenAsync(user);var delivered=await messages.SendEmailVerificationAsync(email,token,ct);if(!delivered){db.SecurityEvents.Add(new SecurityEvent{UserId=user.Id,EventType="EmailProviderUnavailable",Severity="Warning",IpAddress=Ip(),UserAgent=Agent(),CorrelationId=HttpContext.TraceIdentifier,Details="Verification message was not dispatched."});await db.SaveChangesAsync(ct);}return Created("",new{user.Id,user.Email,user.DisplayName,verificationRequired=true,deliveryAvailable=delivered});}
 [HttpPost("login"),Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]public async Task<ActionResult>Login(LoginRequest r){var user=await users.FindByEmailAsync(r.Email.Trim());if(user is null||!user.IsActive)return Unauthorized();if(!user.EmailConfirmed)return Unauthorized(new{message="Email verification is required."});var result=await signIn.CheckPasswordSignInAsync(user,r.Password,true);db.LoginAttempts.Add(new LoginAttempt{UserId=user.Id,NormalizedIdentifierHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(r.Email.Trim().ToUpperInvariant()))),IpAddress=Ip(),Succeeded=result.Succeeded,FailureReason=result.Succeeded?null:"InvalidCredentials"});if(!result.Succeeded)db.SecurityEvents.Add(new SecurityEvent{UserId=user.Id,EventType=result.IsLockedOut?"AccountLockout":"LoginFailure",Severity=result.IsLockedOut?"High":"Warning",IpAddress=Ip(),UserAgent=Agent(),CorrelationId=HttpContext.TraceIdentifier,Details=result.IsLockedOut?"Account locked after failed authentication attempts.":"Invalid credentials."});await db.SaveChangesAsync();if(!result.Succeeded)return result.IsLockedOut?StatusCode(423,new{message="Account temporarily locked."}):Unauthorized();var roles=await users.GetRolesAsync(user);if(roles.Any(IsStaff)||user.TwoFactorEnabled){string?sharedKey=null;if(!user.TwoFactorEnabled){sharedKey=await users.GetAuthenticatorKeyAsync(user);if(string.IsNullOrWhiteSpace(sharedKey)){await users.ResetAuthenticatorKeyAsync(user);sharedKey=await users.GetAuthenticatorKeyAsync(user);}}return Ok(new{mfaRequired=true,setupRequired=!user.TwoFactorEnabled,sharedKey,account=user.Email,issuer="MARINA",challengeId=user.Id});}user.LastLoginUtc=DateTime.UtcNow;var pair=await tokens.IssueAsync(user.Id,user.Email!,roles,r.Device??"Unknown",Agent(),Ip());return Ok(pair);}
 [HttpPost("refresh"), Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
 public async Task<ActionResult> Refresh(RefreshRequest request)
 {
     var hash = tokens.Hash(request.RefreshToken);
     var now = DateTime.UtcNow;
     await using var transaction = await db.Database.BeginTransactionAsync();
     // Atomically consume once. Concurrent callers cannot both mint a replacement.
     var consumed = await db.RefreshTokens.Where(x => x.TokenHash == hash && x.RevokedUtc == null &&
         x.ExpiresUtc > now && x.UserSession.RevokedUtc == null && !x.IsDeleted)
         .ExecuteUpdateAsync(set => set.SetProperty(x => x.RevokedUtc, now));
     var current = await db.RefreshTokens.Include(x => x.UserSession).SingleOrDefaultAsync(x => x.TokenHash == hash);
     if (current is null) return Unauthorized();
     if (consumed != 1)
     {
         if (current.RevokedUtc is not null)
         {
             current.IsReuseDetected = true;
             current.UserSession.RevokedUtc = now;
             db.SecurityEvents.Add(new SecurityEvent { UserId = current.UserSession.UserId,
                 EventType = "RefreshTokenReuse", Severity = "High", IpAddress = Ip(), UserAgent = Agent(),
                 CorrelationId = HttpContext.TraceIdentifier, Details = "Token family revoked." });
             await db.SaveChangesAsync();
         }
         await transaction.CommitAsync();
         return Unauthorized();
     }
     var user = await users.FindByIdAsync(current.UserSession.UserId.ToString());
     if (user is null || !user.IsActive || !user.EmailConfirmed || await users.IsLockedOutAsync(user))
     {
         current.UserSession.RevokedUtc = now;
         await db.SaveChangesAsync();
         await transaction.CommitAsync();
         return Unauthorized();
     }
     var pair = await tokens.IssueAsync(user.Id, user.Email!, await users.GetRolesAsync(user),
         request.Device ?? current.UserSession.DeviceName, Agent(), Ip(), current.UserSessionId);
     var replacementHash = tokens.Hash(pair.RefreshToken);
     current.ReplacedById = await db.RefreshTokens.Where(x => x.TokenHash == replacementHash).Select(x => x.Id).SingleAsync();
     await db.SaveChangesAsync();
     await transaction.CommitAsync();
     return Ok(pair);
 }
 [Authorize,HttpPost("logout")]public async Task<IActionResult>Logout(){var sessionId=Guid.TryParse(User.FindFirstValue("session_id"),out var id)?id:(Guid?)null;if(sessionId.HasValue){var s=await db.UserSessions.SingleOrDefaultAsync(x=>x.Id==sessionId);if(s is not null)s.RevokedUtc=DateTime.UtcNow;await db.SaveChangesAsync();}return NoContent();}
 [Authorize,HttpPost("logout-all")]public async Task<IActionResult>LogoutAll(){var id=UserId();var count=await db.UserSessions.Where(x=>x.UserId==id&&x.RevokedUtc==null).ExecuteUpdateAsync(x=>x.SetProperty(s=>s.RevokedUtc,DateTime.UtcNow));db.SecurityEvents.Add(new SecurityEvent{UserId=id,EventType="LogoutAll",IpAddress=Ip(),UserAgent=Agent(),CorrelationId=HttpContext.TraceIdentifier,Details=$"Revoked {count} active sessions."});await db.SaveChangesAsync();return NoContent();}
 [Authorize,HttpGet("sessions")]public async Task<ActionResult>Sessions()=>Ok(await db.UserSessions.AsNoTracking().Where(x=>x.UserId==UserId()&&x.RevokedUtc==null).Select(x=>new{x.Id,x.DeviceName,x.IpAddress,x.LastSeenUtc}).ToListAsync());
 [Authorize,HttpDelete("sessions/{id:guid}")]public async Task<IActionResult>Revoke(Guid id){var userId=UserId();var s=await db.UserSessions.SingleOrDefaultAsync(x=>x.Id==id&&x.UserId==userId)??throw new KeyNotFoundException("Session not found.");s.RevokedUtc=DateTime.UtcNow;db.SecurityEvents.Add(new SecurityEvent{UserId=userId,EventType="SessionRevoked",IpAddress=Ip(),UserAgent=Agent(),CorrelationId=HttpContext.TraceIdentifier,Details=$"Session {id} revoked by its owner."});await db.SaveChangesAsync();return NoContent();}
 [Authorize,HttpPost("change-password")]public async Task<IActionResult>ChangePassword(ChangePasswordRequest r){var user=await users.FindByIdAsync(UserId().ToString())??throw new UnauthorizedAccessException();var result=await users.ChangePasswordAsync(user,r.CurrentPassword,r.NewPassword);if(!result.Succeeded)return IdentityErrors(result);await db.UserSessions.Where(x=>x.UserId==user.Id).ExecuteUpdateAsync(x=>x.SetProperty(s=>s.RevokedUtc,DateTime.UtcNow));return NoContent();}
 [HttpPost("forgot-password"),Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]public async Task<IActionResult>Forgot(ForgotPasswordRequest request,CancellationToken ct){var email=request.Email;var user=await users.FindByEmailAsync(email.Trim());if(user is not null){var token=await users.GeneratePasswordResetTokenAsync(user);var delivered=await messages.SendPasswordResetAsync(user.Email!,token,ct);if(!delivered)db.SecurityEvents.Add(new SecurityEvent{UserId=user.Id,EventType="EmailProviderUnavailable",Severity="Warning",IpAddress=Ip(),UserAgent=Agent(),CorrelationId=HttpContext.TraceIdentifier,Details="Password reset message was not dispatched."});}await db.SaveChangesAsync(ct);return Accepted(new{message="If the account exists, reset instructions will be sent."});}
 [HttpPost("reset-password"),Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]public async Task<IActionResult>Reset(ResetRequest r){await using var transaction=await db.Database.BeginTransactionAsync();var user=await users.FindByEmailAsync(r.Email.Trim());if(user is null)return BadRequest();var result=await users.ResetPasswordAsync(user,r.Token,r.NewPassword);if(!result.Succeeded)return IdentityErrors(result);await db.UserSessions.Where(x=>x.UserId==user.Id).ExecuteUpdateAsync(x=>x.SetProperty(s=>s.RevokedUtc,DateTime.UtcNow));await transaction.CommitAsync();return NoContent();}
 [HttpPost("verify-email"),Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]public async Task<IActionResult>VerifyEmail(VerifyEmailRequest r){var user=await users.FindByEmailAsync(r.Email.Trim());if(user is null)return BadRequest();var result=await users.ConfirmEmailAsync(user,r.Token);return result.Succeeded?NoContent():IdentityErrors(result);}
 [HttpPost("resend-verification"),Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]public async Task<IActionResult>ResendVerification(ResendVerificationRequest request,CancellationToken ct){var email=request.Email;var user=await users.FindByEmailAsync(email.Trim());if(user is not null&&!user.EmailConfirmed){var token=await users.GenerateEmailConfirmationTokenAsync(user);await messages.SendEmailVerificationAsync(user.Email!,token,ct);}return Accepted(new{message="If verification is required, instructions will be sent."});}
 [Authorize,HttpPost("mfa/enroll")]public async Task<ActionResult>EnrollMfa(){var user=await users.FindByIdAsync(UserId().ToString())??throw new UnauthorizedAccessException();await users.ResetAuthenticatorKeyAsync(user);var key=await users.GetAuthenticatorKeyAsync(user);return Ok(new{sharedKey=key,account=user.Email,issuer="MARINA"});}
 [Authorize,HttpPost("mfa/confirm")]public async Task<ActionResult>ConfirmMfa(MfaCodeRequest r){var user=await users.FindByIdAsync(UserId().ToString())??throw new UnauthorizedAccessException();var valid=await users.VerifyTwoFactorTokenAsync(user,users.Options.Tokens.AuthenticatorTokenProvider,r.Code.Replace(" ",""));if(!valid)return BadRequest(new{message="Invalid authenticator code."});await users.SetTwoFactorEnabledAsync(user,true);user.RequiresMfa=false;var codes=await users.GenerateNewTwoFactorRecoveryCodesAsync(user,8);return Ok(new{recoveryCodes=codes});}
 private Guid UserId()=>Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);private string Ip()=>HttpContext.Connection.RemoteIpAddress?.ToString()??"unknown";private string Agent()=>Request.Headers.UserAgent.ToString();private static bool IsStaff(string role)=>role!="Customer";private ActionResult IdentityErrors(IdentityResult result)=>ValidationProblem(new ValidationProblemDetails(result.Errors.GroupBy(x=>x.Code).ToDictionary(x=>x.Key,x=>x.Select(y=>y.Description).ToArray())));
}

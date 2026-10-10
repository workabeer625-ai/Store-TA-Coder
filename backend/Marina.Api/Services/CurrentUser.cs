using System.Security.Claims; using Marina.Application.Abstractions;
namespace Marina.Api.Services;
public sealed class CurrentUser(IHttpContextAccessor accessor):ICurrentUser{private HttpContext?Context=>accessor.HttpContext;public Guid?UserId=>Guid.TryParse(Context?.User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:null;public string IpAddress=>Context?.Connection.RemoteIpAddress?.ToString()??"unknown";public string UserAgent=>Context?.Request.Headers.UserAgent.ToString()??"unknown";public string CorrelationId=>Context?.TraceIdentifier??Guid.NewGuid().ToString("N");}

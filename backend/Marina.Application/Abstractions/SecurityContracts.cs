namespace Marina.Application.Abstractions;
public sealed record AuthTokenPair(string AccessToken,string RefreshToken,DateTime AccessExpiresUtc,Guid SessionId);
public interface ITokenService { Task<AuthTokenPair> IssueAsync(Guid userId,string email,IEnumerable<string> roles,string device,string userAgent,string ipAddress,Guid? sessionId=null,CancellationToken ct=default); Task<AuthTokenPair> IssueAsync(Guid userId,string email,IReadOnlyCollection<string> roles,IReadOnlyCollection<string> permissions,string device,string userAgent,string ipAddress,Guid? sessionId=null,CancellationToken ct=default); string Hash(string rawToken); }
public interface ICurrentUser { Guid? UserId { get; } string IpAddress { get; } string UserAgent { get; } string CorrelationId { get; } }
public interface IPermissionResolver { Task<IReadOnlyCollection<string>> ForRolesAsync(IEnumerable<string> roleNames,CancellationToken ct=default); }
public sealed record PaymentWebhookResult(Guid PaymentId,string ProviderReference,string EventType,Marina.Domain.Commerce.PaymentStatus Status);
public interface IPaymentProvider { string Name { get; } Task<string> CreatePaymentAsync(Guid orderId,decimal amount,string currency,string idempotencyKey,CancellationToken ct); bool ValidateWebhook(ReadOnlyMemory<byte> payload,string signature); Task<PaymentWebhookResult> ParseWebhookAsync(ReadOnlyMemory<byte> payload,CancellationToken ct); }
public sealed record StoredMedia(string StorageKey,string PublicUrl,string ContentType,long Length);
public interface IMediaStorage { Task<StoredMedia> StoreAsync(Stream content,string contentType,string serverFileName,CancellationToken ct); Task DeleteAsync(string storageKey,CancellationToken ct); }
public interface IAccountMessageSender
{
    Task<bool> SendEmailVerificationAsync(string email, string token, CancellationToken ct);
    Task<bool> SendPasswordResetAsync(string email, string token, CancellationToken ct);
}

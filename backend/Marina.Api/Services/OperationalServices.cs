using Marina.Application.Abstractions;

namespace Marina.Api.Services;

public sealed class UnconfiguredAccountMessageSender(ILogger<UnconfiguredAccountMessageSender> logger) : IAccountMessageSender
{
    public Task<bool> SendEmailVerificationAsync(string email, string token, CancellationToken ct)
    {
        logger.LogWarning("Email verification delivery is unavailable because no email provider is configured.");
        return Task.FromResult(false);
    }

    public Task<bool> SendPasswordResetAsync(string email, string token, CancellationToken ct)
    {
        logger.LogWarning("Password reset delivery is unavailable because no email provider is configured.");
        return Task.FromResult(false);
    }
}

public sealed class LocalDevelopmentMediaStorage(IWebHostEnvironment environment) : IMediaStorage
{
    private readonly string root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "wwwroot", "uploads"));

    public async Task<StoredMedia> StoreAsync(Stream content, string contentType, string serverFileName, CancellationToken ct)
    {
        if (!environment.IsDevelopment()) throw new InvalidOperationException("Local media storage is development-only.");
        if (Path.GetFileName(serverFileName) != serverFileName) throw new ArgumentException("Invalid server filename.");
        Directory.CreateDirectory(root);
        var target = Path.GetFullPath(Path.Combine(root, serverFileName));
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Media path escaped the configured storage root.");
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await content.CopyToAsync(output, ct);
        return new StoredMedia($"uploads/{serverFileName}", $"/uploads/{serverFileName}", contentType, output.Length);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        if (!environment.IsDevelopment()) throw new InvalidOperationException("Local media storage is development-only.");
        var normalized = storageKey.Replace('/', Path.DirectorySeparatorChar);
        var target = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "wwwroot", normalized));
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Media path escaped the configured storage root.");
        if (File.Exists(target)) File.Delete(target);
        return Task.CompletedTask;
    }
}

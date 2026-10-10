using System.Net;
using System.Net.Mail;
using Marina.Application.Abstractions;

namespace Marina.Api.Services;

/// <summary>SMTP in deployed environments; private filesystem pickup in Development only.</summary>
public sealed class AccountEmailSender(IConfiguration configuration, IWebHostEnvironment environment,
    ILogger<AccountEmailSender> logger) : IAccountMessageSender
{
    public Task<bool> SendEmailVerificationAsync(string email, string token, CancellationToken ct) =>
        SendAsync(email, token, "verification", "Verify your MARINA email", ct);

    public Task<bool> SendPasswordResetAsync(string email, string token, CancellationToken ct) =>
        SendAsync(email, token, "reset-password", "Reset your MARINA password", ct);

    private async Task<bool> SendAsync(string email, string token, string route, string subject, CancellationToken ct)
    {
        var settings = configuration.GetSection("Email");
        var mode = settings["Mode"] ?? (environment.IsDevelopment() ? "DevelopmentPickup" : "Smtp");
        using var smtp = new SmtpClient();
        if (mode == "DevelopmentPickup")
        {
            if (!environment.IsDevelopment()) throw new InvalidOperationException("Email pickup is Development-only.");
            var directory = settings["PickupDirectory"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MARINA", "MailPickup");
            directory = Path.GetFullPath(directory);
            if (directory.StartsWith(Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot")), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Email pickup must not be publicly served.");
            Directory.CreateDirectory(directory);
            smtp.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
            smtp.PickupDirectoryLocation = directory;
        }
        else if (mode == "Smtp" && !string.IsNullOrWhiteSpace(settings["Host"]))
        {
            smtp.Host = settings["Host"]!;
            smtp.Port = settings.GetValue("Port", 587);
            smtp.EnableSsl = true;
            smtp.UseDefaultCredentials = false;
            if (!string.IsNullOrEmpty(settings["Username"]))
                smtp.Credentials = new NetworkCredential(settings["Username"], settings["Password"]);
        }
        else
        {
            logger.LogError("Email delivery is unavailable: configure Email:Host and sender settings.");
            return false;
        }
        var baseUrl = settings["AppUrl"] ?? "http://localhost:5210";
        var link = $"{baseUrl.TrimEnd('/')}/#/{route}?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
        using var message = new MailMessage(settings["From"] ?? "noreply@marina.test", email, subject,
            $"{subject}\n\nOpen: {link}\n\nOr paste this token into the MARINA screen:\n{token}\n\nIf you did not request this, ignore this message.");
        try
        {
            await smtp.SendMailAsync(message, ct);
            return true;
        }
        catch (SmtpException exception)
        {
            logger.LogError(exception, "Account email could not be delivered. No token is logged.");
            return false;
        }
    }
}

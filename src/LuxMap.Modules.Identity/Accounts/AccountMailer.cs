using System.Globalization;
using LuxMap.Modules.Identity.Entities;
using Microsoft.Extensions.Logging;

namespace LuxMap.Modules.Identity.Accounts;

/// <summary>Writes and sends the invitation and reset mails. Never logs a link or a raw token.</summary>
public sealed class AccountMailer(IEmailSender sender, EmailOptions options, ILogger<AccountMailer> logger)
{
    /// <summary>Vietnam keeps UTC+7 all year, so a fixed offset needs no time-zone database in the container.</summary>
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>
    /// Sends the link and reports whether the server accepted it. A failure is logged, not thrown: the
    /// account change it follows is already committed, and the admin can resend (D-3).
    /// </summary>
    public async Task<bool> TrySendAsync(AppUser user, AccountTokenPurpose purpose, string rawToken, DateTime expiresAt, CancellationToken ct)
    {
        var message = Compose(user, purpose, options.SetPasswordLink(rawToken), expiresAt);
        try
        {
            await sender.SendAsync(message, ct);
            logger.LogInformation("Sent the {Purpose} mail to account {UserId}.", purpose, user.UserId);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not send the {Purpose} mail to account {UserId}.", purpose, user.UserId);
            return false;
        }
    }

    public async Task<bool> TrySendEmailChangedAsync(AppUser user, CancellationToken ct)
    {
        var (subject, text, html) = AccountMailTemplate.RenderEmailChanged(
            user.FullName, user.Username, user.Email, options.WebAppBaseUrl, user.PasswordSetAt is not null);
        try
        {
            await sender.SendAsync(new EmailMessage(user.Email, user.FullName, subject, text, html), ct);
            logger.LogInformation("Sent email-change notification to account {UserId}.", user.UserId);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Do not log the SMTP exception: it may include the recipient's email address.
            logger.LogError("Could not send email-change notification to account {UserId}; failure type {FailureType}.",
                user.UserId, exception.GetType().Name);
            return false;
        }
    }

    internal static EmailMessage Compose(AppUser user, AccountTokenPurpose purpose, Uri link, DateTime expiresAt)
    {
        var expires = new DateTimeOffset(expiresAt, TimeSpan.Zero).ToOffset(VietnamOffset)
            .ToString("HH:mm 'ngày' dd/MM/yyyy", CultureInfo.InvariantCulture);

        var (subject, text, html) = AccountMailTemplate.Render(purpose, user.FullName, user.Username, link, expires);
        return new EmailMessage(user.Email, user.FullName, subject, text, html);
    }
}

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

    internal static EmailMessage Compose(AppUser user, AccountTokenPurpose purpose, Uri link, DateTime expiresAt)
    {
        var expires = new DateTimeOffset(expiresAt, TimeSpan.Zero).ToOffset(VietnamOffset)
            .ToString("HH:mm 'ngày' dd/MM/yyyy", CultureInfo.InvariantCulture);

        var (subject, opening) = purpose == AccountTokenPurpose.Invite
            ? ("LuxMap — Lời mời tạo tài khoản",
                "Quản trị hệ thống LuxMap đã tạo tài khoản cho bạn. Hãy đặt mật khẩu để bắt đầu sử dụng.")
            : ("LuxMap — Đặt lại mật khẩu",
                "Hệ thống nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn.");

        var body = $"""
            Chào {user.FullName},

            {opening}

            Tên đăng nhập: {user.Username}

            Mở liên kết sau để đặt mật khẩu (hết hạn lúc {expires}, giờ Việt Nam):
            {link}

            Liên kết chỉ dùng được một lần. Nếu bạn không yêu cầu việc này, hãy bỏ qua email.

            LuxMap
            """;

        return new EmailMessage(user.Email, user.FullName, subject, body);
    }
}

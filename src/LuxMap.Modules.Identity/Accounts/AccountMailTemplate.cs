using System.Text.Encodings.Web;
using System.Text.Unicode;
using LuxMap.Modules.Identity.Entities;

namespace LuxMap.Modules.Identity.Accounts;

/// <summary>
/// The invitation and reset mails, as HTML with a plain-text twin (multipart/alternative).
/// </summary>
/// <remarks>
/// Written the way mail clients need it, not the way a web page would be: layout with tables, every
/// style inline (some clients and forwarding paths drop or ignore <c>&lt;style&gt;</c> blocks), no images
/// (often blocked), line heights in px (Outlook's Word engine mishandles unitless ones), and a button
/// that is a link inside a coloured cell, with <c>mso-padding-alt</c> so Outlook keeps its size. Every value from an account is
/// HTML-encoded: a display name is typed by an admin and must not become markup in someone's inbox.
/// </remarks>
public static class AccountMailTemplate
{
    private const string Navy = "#0f2a44";
    private const string Amber = "#f5a623";
    private const string Ink = "#1f2933";
    private const string Muted = "#5f6b7a";

    /// <summary>Encodes markup characters but keeps Vietnamese letters as they are, not as <c>&amp;#x...;</c>.</summary>
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    public static (string Subject, string Text, string Html) Render(
        AccountTokenPurpose purpose, string fullName, string username, Uri link, string expires)
    {
        var invite = purpose == AccountTokenPurpose.Invite;
        var subject = invite ? "LuxMap — Lời mời tạo tài khoản" : "LuxMap — Đặt lại mật khẩu";
        var heading = invite ? "Chào mừng bạn đến với LuxMap" : "Đặt lại mật khẩu";
        var opening = invite
            ? "Quản trị hệ thống LuxMap đã tạo tài khoản cho bạn. Hãy đặt mật khẩu để bắt đầu sử dụng."
            : "Hệ thống nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn.";
        var action = invite ? "Đặt mật khẩu" : "Đặt mật khẩu mới";
        var preheader = invite
            ? $"Tài khoản {username} đã sẵn sàng — đặt mật khẩu trước {expires}."
            : $"Liên kết đặt lại mật khẩu cho {username}, hết hạn lúc {expires}.";

        var text = $"""
            Chào {fullName},

            {opening}

            Tên đăng nhập: {username}

            {action} tại liên kết sau (hết hạn lúc {expires}, giờ Việt Nam):
            {link}

            Liên kết chỉ dùng được một lần. Nếu bạn không yêu cầu việc này, hãy bỏ qua email.

            LuxMap
            """;

        var e = Encoder;
        var html = $"""
            <!DOCTYPE html>
            <html lang="vi">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="color-scheme" content="light">
            <title>{e.Encode(subject)}</title>
            </head>
            <body style="margin:0;padding:0;background-color:#f3f5f8;">
            <div style="display:none;max-height:0;overflow:hidden;opacity:0;color:#f3f5f8;">{e.Encode(preheader)}</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color:#f3f5f8;">
              <tr><td align="center" style="padding:32px 16px;">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:560px;background-color:#ffffff;border:1px solid #e3e8ef;border-radius:8px;overflow:hidden;font-family:'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
                  <tr><td style="background-color:{Navy};padding:24px 32px;border-bottom:4px solid {Amber};">
                    <div style="font-size:22px;font-weight:700;color:#ffffff;letter-spacing:0.3px;">LuxMap</div>
                    <div style="font-size:13px;color:#c9d4e2;padding-top:4px;">Quản lý chiếu sáng đường giao thông nông thôn</div>
                  </td></tr>
                  <tr><td style="padding:32px;color:{Ink};font-size:15px;line-height:24px;">
                    <h1 style="margin:0 0 16px;font-size:20px;line-height:26px;color:{Navy};">{e.Encode(heading)}</h1>
                    <p style="margin:0 0 12px;">Chào <strong>{e.Encode(fullName)}</strong>,</p>
                    <p style="margin:0 0 20px;">{e.Encode(opening)}</p>
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:0 0 24px;">
                      <tr><td style="background-color:#f6f8fb;border-left:4px solid {Amber};padding:12px 16px;">
                        <div style="font-size:12px;color:{Muted};text-transform:uppercase;letter-spacing:0.5px;">Tên đăng nhập</div>
                        <div style="font-size:16px;font-weight:600;color:{Navy};font-family:Consolas,'Courier New',monospace;padding-top:2px;">{e.Encode(username)}</div>
                      </td></tr>
                    </table>
                    <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:0 0 20px;">
                      <tr><td align="center" bgcolor="{Navy}" style="border-radius:6px;mso-padding-alt:12px 28px;">
                        <a href="{e.Encode(link.AbsoluteUri)}" target="_blank" style="display:inline-block;padding:12px 28px;font-size:15px;font-weight:600;color:#ffffff;text-decoration:none;border-radius:6px;">{e.Encode(action)}</a>
                      </td></tr>
                    </table>
                    <p style="margin:0 0 20px;font-size:14px;color:{Muted};">Liên kết hết hạn lúc <strong style="color:{Ink};">{e.Encode(expires)}</strong> (giờ Việt Nam) và chỉ dùng được một lần.</p>
                    <p style="margin:0 0 6px;font-size:13px;color:{Muted};">Nút không hoạt động? Sao chép liên kết sau vào trình duyệt:</p>
                    <p style="margin:0 0 24px;font-size:13px;word-break:break-all;"><a href="{e.Encode(link.AbsoluteUri)}" target="_blank" style="color:#1a5fa8;">{e.Encode(link.AbsoluteUri)}</a></p>
                    <p style="margin:0;padding-top:16px;border-top:1px solid #e3e8ef;font-size:13px;color:{Muted};">Nếu bạn không yêu cầu việc này, hãy bỏ qua email.</p>
                  </td></tr>
                </table>
                <p style="margin:16px 0 0;font-family:'Segoe UI',Roboto,Helvetica,Arial,sans-serif;font-size:12px;color:#8a96a8;">Email tự động từ hệ thống LuxMap. Vui lòng không trả lời email này.</p>
              </td></tr>
            </table>
            </body>
            </html>
            """;

        return (subject, text, html);
    }
}

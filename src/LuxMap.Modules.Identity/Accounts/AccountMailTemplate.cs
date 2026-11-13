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
    private const string AmberText = "#a86a00";
    private const string Ink = "#1f2933";
    private const string Muted = "#5f6b7a";
    private const string Line = "#e3e8ef";
    private const string Page = "#eef1f5";
    private const string Font = "'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

    /// <summary>Encodes markup characters but keeps Vietnamese letters as they are, not as <c>&amp;#x...;</c>.</summary>
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    public static (string Subject, string Text, string Html) Render(
        AccountTokenPurpose purpose, string fullName, string username, Uri link, string expires)
    {
        var invite = purpose == AccountTokenPurpose.Invite;
        var subject = invite ? "LuxMap — Lời mời tạo tài khoản" : "LuxMap — Đặt lại mật khẩu";
        var eyebrow = invite ? "Lời mời tham gia" : "Bảo mật tài khoản";
        var heading = invite ? "Chào mừng bạn đến với LuxMap" : "Đặt lại mật khẩu";
        var opening = invite
            ? "Quản trị hệ thống LuxMap đã tạo tài khoản cho bạn. Hãy đặt mật khẩu để bắt đầu sử dụng."
            : "Hệ thống nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn.";
        var action = invite ? "Đặt mật khẩu" : "Đặt mật khẩu mới";
        // Invited: no password yet, so sign-in is impossible until the link is used. Reset: the request
        // changes nothing until the link is used, so the current password still works.
        var ignoreNote = invite
            ? "Nếu bạn không nhận ra lời mời này, hãy bỏ qua email. Tài khoản chưa dùng được cho tới khi mật khẩu được đặt."
            : "Nếu bạn không yêu cầu đặt lại mật khẩu, hãy bỏ qua email. Mật khẩu hiện tại của bạn vẫn giữ nguyên.";
        var preheader = invite
            ? $"Tài khoản {username} đã sẵn sàng — đặt mật khẩu trước {expires}."
            : $"Liên kết đặt lại mật khẩu cho {username}, hết hạn lúc {expires}.";

        var text = $"""
            Chào {fullName},

            {opening}

            Tên đăng nhập: {username}

            {action} tại liên kết sau (hết hạn lúc {expires}, giờ Việt Nam, chỉ dùng được một lần):
            {link}

            {ignoreNote}

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
            <body style="margin:0;padding:0;background-color:{Page};">
            <div style="display:none;max-height:0;overflow:hidden;opacity:0;color:{Page};">{e.Encode(preheader)}</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color:{Page};">
              <tr><td align="center" style="padding:36px 16px;">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:520px;font-family:{Font};">
                  <tr><td style="padding:0 4px 18px;">
                    <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                      <tr>
                        <td valign="middle" style="width:12px;"><div style="width:12px;height:12px;border-radius:6px;background-color:{Amber};font-size:0;line-height:0;">&nbsp;</div></td>
                        <td style="padding-left:10px;font-size:20px;line-height:24px;font-weight:700;color:{Navy};letter-spacing:0.2px;">LuxMap</td>
                      </tr>
                    </table>
                  </td></tr>
                  <tr><td style="background-color:#ffffff;border:1px solid {Line};border-top:4px solid {Amber};border-radius:12px;padding:32px 32px 28px;color:{Ink};font-size:15px;line-height:24px;">
                    <div style="font-size:12px;line-height:16px;font-weight:700;color:{AmberText};text-transform:uppercase;letter-spacing:1px;padding-bottom:8px;">{e.Encode(eyebrow)}</div>
                    <h1 style="margin:0 0 20px;font-size:24px;line-height:30px;font-weight:700;color:{Navy};">{e.Encode(heading)}</h1>
                    <p style="margin:0 0 8px;">Chào <strong>{e.Encode(fullName)}</strong>,</p>
                    <p style="margin:0 0 24px;">{e.Encode(opening)}</p>
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:0 0 24px;">
                      <tr><td style="background-color:#f6f8fb;border:1px solid {Line};border-radius:8px;padding:14px 18px;">
                        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
                          <tr>
                            <td style="font-size:13px;line-height:20px;color:{Muted};">Tên đăng nhập</td>
                            <td align="right" style="font-size:16px;line-height:20px;font-weight:700;color:{Navy};font-family:Consolas,Menlo,'Courier New',monospace;">{e.Encode(username)}</td>
                          </tr>
                        </table>
                      </td></tr>
                    </table>
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:0 0 16px;">
                      <tr><td align="center" bgcolor="{Navy}" style="border-radius:8px;mso-padding-alt:14px 24px;">
                        <a href="{e.Encode(link.AbsoluteUri)}" target="_blank" style="display:block;padding:14px 24px;font-size:16px;line-height:20px;font-weight:700;color:#ffffff;text-decoration:none;border-radius:8px;">{e.Encode(action)}</a>
                      </td></tr>
                    </table>
                    <p style="margin:0 0 24px;font-size:13px;line-height:20px;color:{Muted};text-align:center;">Liên kết có hiệu lực đến <strong style="color:{Ink};">{e.Encode(expires)}</strong> (giờ Việt Nam) và chỉ dùng được một lần.</p>
                    <p style="margin:0;padding-top:18px;border-top:1px solid {Line};font-size:13px;line-height:20px;color:{Muted};">{e.Encode(ignoreNote)}</p>
                  </td></tr>
                  <tr><td align="center" style="padding:20px 8px 0;font-size:12px;line-height:18px;color:#8a96a8;">
                    LuxMap · Quản lý chiếu sáng đường giao thông nông thôn<br>
                    Email tự động — vui lòng không trả lời.
                  </td></tr>
                </table>
              </td></tr>
            </table>
            </body>
            </html>
            """;

        return (subject, text, html);
    }
}

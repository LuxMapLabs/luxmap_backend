using LuxMap.Modules.Identity.Accounts;
using LuxMap.Modules.Identity.Entities;
using MimeKit;

namespace LuxMap.Shared.Tests;

/// <summary>The invitation and reset mails: HTML with a plain-text twin, and nothing from an account becomes markup.</summary>
public class AccountMailTemplateTests
{
    private static readonly Uri Link = new("http://localhost:5173/set-password?token=abc-DEF_123&x=1");

    [Fact]
    public void A_display_name_or_username_with_markup_is_shown_as_text_never_as_html()
    {
        var (_, text, html) = AccountMailTemplate.Render(
            AccountTokenPurpose.Invite, "An <script>alert(1)</script>", "a\"><b>x", Link, "14:09 ngày 08/10/2026");

        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("\"><b>", html);
        Assert.Contains("An &lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("An <script>alert(1)</script>", text);
    }

    [Fact]
    public void Vietnamese_letters_stay_readable_in_the_html()
    {
        var (_, _, html) = AccountMailTemplate.Render(AccountTokenPurpose.Invite, "Nguyễn Văn An", "an", Link, "14:09 ngày 08/10/2026");

        Assert.Contains("Nguyễn Văn An", html);
        Assert.Contains("Đặt mật khẩu", html);
    }

    [Fact]
    public void Both_versions_carry_the_same_link_and_the_html_button_points_at_it()
    {
        var (_, text, html) = AccountMailTemplate.Render(AccountTokenPurpose.Reset, "An", "an", Link, "14:09 ngày 08/10/2026");

        Assert.Contains(Link.AbsoluteUri, text);
        Assert.Contains("href=\"http://localhost:5173/set-password?token=abc-DEF_123&amp;x=1\"", html);
    }

    [Theory]
    [InlineData(AccountTokenPurpose.Invite, "LuxMap — Lời mời tạo tài khoản", ">Đặt mật khẩu<")]
    [InlineData(AccountTokenPurpose.Reset, "LuxMap — Đặt lại mật khẩu", ">Đặt mật khẩu mới<")]
    public void Each_purpose_has_its_own_subject_and_button(AccountTokenPurpose purpose, string subject, string button)
    {
        var rendered = AccountMailTemplate.Render(purpose, "An", "an", Link, "14:09 ngày 08/10/2026");

        Assert.Equal(subject, rendered.Subject);
        Assert.Contains(button, rendered.Html);
    }

    /// <summary>Clients show the LAST alternative they can render: plain text must come first, HTML last.</summary>
    [Fact]
    public void The_mime_message_is_multipart_alternative_with_text_before_html()
    {
        var options = new EmailOptions
        {
            Host = "localhost",
            Port = 1025,
            FromAddress = "no-reply@luxmap.local",
            FromName = "LuxMap",
            WebAppBaseUrl = new Uri("http://localhost:5173"),
        };
        var mime = SmtpEmailSender.ToMime(new EmailMessage("an@example.invalid", "An", "Chủ đề", "văn bản", "<p>html</p>"), options);

        var alternative = Assert.IsType<MultipartAlternative>(mime.Body);
        Assert.Equal(["text/plain", "text/html"], alternative.Select(part => part.ContentType.MimeType));
        Assert.Equal("LuxMap", mime.From.Mailboxes.Single().Name);
    }
}

using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace LuxMap.Modules.Identity.Accounts;

/// <summary>One message to one recipient: plain text, plus an HTML version when there is one.</summary>
/// <param name="Body">The plain-text version, always present: what a client without HTML shows.</param>
public sealed record EmailMessage(string ToAddress, string ToName, string Subject, string Body, string? HtmlBody = null);

/// <summary>Sends account mail. Replaced by a recording fake in the tests; nothing else implements it.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

/// <summary>SMTP through MailKit (D-4): <c>System.Net.Mail.SmtpClient</c> is not recommended for new code.</summary>
public sealed class SmtpEmailSender(EmailOptions options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var mime = ToMime(message, options);

        using var client = new SmtpClient();

        // 465 is TLS from the first byte; anything else upgrades with STARTTLS. A server that takes a
        // login must do one or the other — "STARTTLS when available" would let a downgrade expose it.
        var security = !options.RequiresTls ? SecureSocketOptions.None
            : options.Port == 465 ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;

        await client.ConnectAsync(options.Host, options.Port, security, ct);
        if (options is { Username: { } username, Password: { } password })
        {
            await client.AuthenticateAsync(username, password, ct);
        }

        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(quit: true, ct);
    }

    /// <summary>
    /// multipart/alternative with the plain text FIRST and the HTML last: clients show the last part they
    /// can render, so an HTML client shows HTML and any other falls back to text.
    /// </summary>
    public static MimeMessage ToMime(EmailMessage message, EmailOptions options)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToAddress));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.Body, HtmlBody = message.HtmlBody }.ToMessageBody();
        return mime;
    }
}

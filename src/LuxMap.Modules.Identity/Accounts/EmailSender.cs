using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace LuxMap.Modules.Identity.Accounts;

/// <summary>One plain-text message to one recipient. Account mail needs nothing richer.</summary>
public sealed record EmailMessage(string ToAddress, string ToName, string Subject, string Body);

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
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToAddress));
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.Body };

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
}

namespace Aegis.Cafeteria.Services.Email;

using System.Net.Mail;

public sealed class SmtpEmailSender(ScheduledEmailOptions options)
{
    public async Task<string> SendAsync(IEnumerable<string> recipients, string subject, string body, CancellationToken cancellationToken = default)
    {
        using var message = new MailMessage { From = new MailAddress(options.FromAddress), Subject = subject, Body = body, IsBodyHtml = false };
        foreach (var recipient in recipients.Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase)) message.To.Add(recipient.Trim());
        if (message.To.Count == 0) return string.Empty;
        using var client = new SmtpClient(options.SmtpHost, options.SmtpPort) { EnableSsl = options.EnableSsl };
        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message, cancellationToken);
        return message.Headers["Message-ID"] ?? Guid.NewGuid().ToString("N");
    }
}

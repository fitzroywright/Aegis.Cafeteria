namespace Aegis.Cafeteria.Services.Email;

using System.Net.Mail;

public sealed class SmtpEmailSender
{
    private readonly ScheduledEmailOptions options;

    public SmtpEmailSender(ScheduledEmailOptions options)
    {
        this.options = options;
    }

    public Task<string> SendAsync
    (
          IEnumerable<string> recipients
        , string subject
        , string body
        , CancellationToken cancellationToken = default
    )
    {
        return SendAsync
        (
              recipients
            , subject
            , body
            , []
            , cancellationToken
        );
    }

    public async Task<string> SendAsync
    (
          IEnumerable<string> recipients
        , string subject
        , string body
        , IEnumerable<EmailAttachment> attachments
        , CancellationToken cancellationToken = default
    )
    {
        using MailMessage message = new()
        {
              From = new MailAddress(this.options.FromAddress)
            , Subject = subject
            , Body = body
            , IsBodyHtml = false
        };

        foreach (string recipient in recipients.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            message.To.Add(recipient.Trim());
        }

        if (message.To.Count == 0)
        {
            return string.Empty;
        }

        List<MemoryStream> streams = [];

        try
        {
            foreach (EmailAttachment attachment in attachments)
            {
                MemoryStream stream = new(attachment.Content, writable: false);
                streams.Add(stream);
                message.Attachments.Add(new Attachment(stream, attachment.FileName, attachment.ContentType));
            }

            using SmtpClient client = new(this.options.SmtpHost, this.options.SmtpPort)
            {
                EnableSsl = this.options.EnableSsl
            };

            cancellationToken.ThrowIfCancellationRequested();
            await client.SendMailAsync(message, cancellationToken);
            return message.Headers["Message-ID"] ?? Guid.NewGuid().ToString("N");
        }
        finally
        {
            foreach (MemoryStream stream in streams)
            {
                stream.Dispose();
            }
        }
    }
}

namespace Aegis.Cafeteria.Services.Messaging;

using Common.Messaging;
using Common.Messaging.Channels.WhatsApp;
using Common.Secrets;

public sealed class CafeteriaWhatsAppOptions
{
    public bool Enabled { get; init; }
    public string GraphApiVersion { get; init; } = "v23.0";
    public string PhoneNumberId { get; init; } = string.Empty;
    public string AccessTokenSecretName { get; init; } = "messaging/whatsapp/access-token";
}

public sealed class CafeteriaWhatsAppSender(
    IExternalMessageChannel channel,
    CafeteriaWhatsAppOptions options)
{
    public async Task SendAsync(
        Guid notificationId,
        string mobile,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (!options.Enabled) throw new InvalidOperationException("Cafeteria WhatsApp delivery is disabled.");
        if (string.IsNullOrWhiteSpace(mobile)) throw new ArgumentException("A mobile number is required.", nameof(mobile));
        if (channel.Channel != MessageChannel.WhatsApp) throw new InvalidOperationException("Cafeteria WhatsApp requires the Common.Messaging WhatsApp channel.");

        await channel.SendAsync(
            new MessageRecipient(mobile, mobile, Mobile: mobile, PreferredChannels: MessageChannel.WhatsApp),
            new MessageRequest
            {
                RecipientIds = [mobile],
                Title = subject,
                Body = body,
                Channels = MessageChannel.WhatsApp,
                Source = "Aegis.Cafeteria.Services",
                CorrelationId = notificationId.ToString("N")
            },
            notificationId,
            cancellationToken);
    }
}

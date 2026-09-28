namespace Aegis.Cafeteria.Services.Email;

using Common.Messaging;
using Common.Messaging.Channels.MicrosoftGraph;
using Common.Secrets;

public sealed class EmailDeliveryOptions
{
    public bool Enabled { get; init; }
    public string TenantId { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string SenderUpn { get; init; } = string.Empty;
    public string ClientSecretName { get; init; } = "messaging/msgraph/client-secret";

    public string? ConfigurationError()
    {
        if (!Enabled)
        {
            return "Email delivery is disabled.";
        }
        if (string.IsNullOrWhiteSpace(TenantId))
        {
            return "EmailDelivery:TenantId is required.";
        }
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            return "EmailDelivery:ClientId is required.";
        }
        if (string.IsNullOrWhiteSpace(SenderUpn))
        {
            return "EmailDelivery:SenderUpn is required.";
        }
        if (string.IsNullOrWhiteSpace(ClientSecretName))
        {
            return "EmailDelivery:ClientSecretName is required.";
        }
        return null;
    }
}

public sealed class CafeteriaMessagingSecretResolver(ISecretProvider secrets) : IChannelSecretResolver
{
    public Task<string?> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
        => secrets.GetAsync(secretName, cancellationToken);
}

public static class CafeteriaEmailDeliveryRegistration
{
    public static IServiceCollection AddCafeteriaEmailDelivery(this IServiceCollection services, IConfiguration configuration)
    {
        EmailDeliveryOptions options = configuration.GetSection("EmailDelivery").Get<EmailDeliveryOptions>() ?? new();
        services.AddSingleton(options);
        services.AddSingleton<IChannelSecretResolver, CafeteriaMessagingSecretResolver>();
        services.AddHttpClient("CafeteriaMicrosoftGraph", client => client.Timeout = TimeSpan.FromSeconds(60));
        services.AddSingleton<IExternalMessageChannel>(provider => new MicrosoftGraphEmailMessageChannel(
              provider.GetRequiredService<IHttpClientFactory>().CreateClient("CafeteriaMicrosoftGraph")
            , new MicrosoftGraphEmailOptions
            {
                  TenantId = options.TenantId
                , ClientId = options.ClientId
                , SenderUpn = options.SenderUpn
                , ClientSecretName = options.ClientSecretName
                , SkipSenderRecipient = false
            }
            , provider.GetRequiredService<IChannelSecretResolver>()));
        services.AddSingleton<CommonMessagingEmailSender>();
        return services;
    }
}

public sealed class CommonMessagingEmailSender(IExternalMessageChannel channel, EmailDeliveryOptions options)
{
    public async Task<string> SendAsync(
          Guid notificationId
        , string recipient
        , string subject
        , string body
        , IEnumerable<EmailAttachment> attachments
        , CancellationToken cancellationToken = default)
    {
        string? error = options.ConfigurationError();
        if (error is not null)
        {
            throw new InvalidOperationException(error);
        }
        if (channel.Channel != MessageChannel.MsEmail)
        {
            throw new InvalidOperationException("Cafeteria email requires the Common.Messaging Microsoft Graph email channel.");
        }

        string address = EmailAddressValidator.Normalize(recipient)
            ?? throw new ArgumentException("A recipient is required.", nameof(recipient));
        await channel.SendAsync(
              new MessageRecipient(address, address, address, PreferredChannels: MessageChannel.MsEmail)
            , new MessageRequest
            {
                  RecipientIds = [address]
                , Title = subject
                , Body = body
                , BodyIsHtml = false
                , Channels = MessageChannel.MsEmail
                , Source = "Aegis.Cafeteria.Services"
                , CorrelationId = notificationId.ToString("N")
                , Attachments = attachments.Select(attachment => new MessageAttachment(
                      attachment.FileName
                    , attachment.ContentType
                    , attachment.Content)).ToArray()
            }
            , notificationId
            , cancellationToken);

        // Graph sendMail returns acceptance, not an Internet Message-ID or final-delivery receipt.
        return "graph-accepted:" + notificationId.ToString("N");
    }
}

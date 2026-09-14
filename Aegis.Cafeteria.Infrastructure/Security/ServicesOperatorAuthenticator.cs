namespace Aegis.Cafeteria.Infrastructure.Security;

using Aegis.Cafeteria.Application.Security;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;

public sealed class PosServicesSession
{
    private readonly object syncRoot = new();
    private string? token;
    private DateTimeOffset expiresAt;

    public string? Token
    {
        get
        {
            lock (syncRoot)
            {
                return expiresAt > DateTimeOffset.UtcNow ? token : null;
            }
        }
    }

    public void Set(string value, DateTimeOffset expiration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        lock (syncRoot)
        {
            token = value;
            expiresAt = expiration;
        }
    }

    public void Clear()
    {
        lock (syncRoot)
        {
            token = null;
            expiresAt = default;
        }
    }
}

public sealed class PosServicesTokenHandler(PosServicesSession session) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? token = session.Token;
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Remove("X-Aegis-POS-Token");
            request.Headers.TryAddWithoutValidation("X-Aegis-POS-Token", token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}

public sealed class ServicesOperatorAuthenticator
(
      HttpClient http
    , PosServicesSession servicesSession
    , ILogger<ServicesOperatorAuthenticator> logger
) : IOperatorAuthenticator
{
    public async Task<OperatorIdentity?> AuthenticateAsync
    (
          string loginName
        , string password
        , CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(loginName) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        try
        {
            using HttpResponseMessage response = await http.PostAsJsonAsync
            (
                  "api/pos/authenticate"
                , new PosAuthenticationRequest(loginName.Trim(), password)
                , cancellationToken
            );

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                servicesSession.Clear();
                return null;
            }

            response.EnsureSuccessStatusCode();
            PosAuthenticationResponse? authentication = await response.Content.ReadFromJsonAsync<PosAuthenticationResponse>
            (
                cancellationToken: cancellationToken
            );

            if (authentication is null || string.IsNullOrWhiteSpace(authentication.Token))
            {
                servicesSession.Clear();
                return null;
            }

            servicesSession.Set(authentication.Token, authentication.ExpiresAt);
            logger.LogInformation
            (
                  "Cafeteria operator {UserId} authenticated through Cafeteria Services. Manager={IsManager}"
                , authentication.UserName
                , authentication.IsManager
            );

            return new OperatorIdentity
            (
                  authentication.UserName
                , authentication.DisplayName
                , authentication.IsManager
                , DateTimeOffset.UtcNow
            );
        }
        catch (Exception exception)
        {
            servicesSession.Clear();
            logger.LogWarning
            (
                  exception
                , "Cafeteria Services authentication failed for {LoginName}"
                , Normalize(loginName)
            );
            return null;
        }
    }

    private static string Normalize(string loginName)
    {
        string value = loginName.Trim();
        int slash = value.LastIndexOf('\\');
        if (slash >= 0 && slash < value.Length - 1)
        {
            value = value[(slash + 1)..];
        }

        int at = value.IndexOf('@');
        if (at > 0)
        {
            value = value[..at];
        }

        return value.ToLowerInvariant();
    }

    private sealed record PosAuthenticationRequest(string UserName, string Password);

    private sealed record PosAuthenticationResponse
    (
          string Token
        , string UserName
        , string DisplayName
        , bool IsManager
        , DateTimeOffset ExpiresAt
        , string AuthenticationSource
    );
}

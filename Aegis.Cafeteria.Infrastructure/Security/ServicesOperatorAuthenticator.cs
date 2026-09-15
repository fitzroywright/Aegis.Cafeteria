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

/// <summary>
/// Indicates that Cafeteria Services could not process an authentication request.
/// This is deliberately distinct from rejected credentials.
/// </summary>
public sealed class CafeteriaServicesAuthenticationException : Exception
{
    public CafeteriaServicesAuthenticationException(string message)
        : base(message)
    {
    }

    public CafeteriaServicesAuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
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

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync
            (
                  "api/pos/authenticate"
                , new PosAuthenticationRequest(loginName.Trim(), password)
                , cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            servicesSession.Clear();
            logger.LogWarning(exception, "Cafeteria Services authentication request timed out for {LoginName}", Normalize(loginName));
            throw new CafeteriaServicesAuthenticationException(
                "Cafeteria Services did not respond to the authentication request in time. Your credentials were not rejected.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            servicesSession.Clear();
            logger.LogWarning(exception, "Cafeteria Services authentication request could not be sent for {LoginName}", Normalize(loginName));
            throw new CafeteriaServicesAuthenticationException(
                "Cafeteria Services could not be reached. Your credentials were not rejected.",
                exception);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                servicesSession.Clear();
                logger.LogInformation("Cafeteria operator credentials were rejected for {LoginName}", Normalize(loginName));
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                servicesSession.Clear();
                string detail = $"Cafeteria Services returned HTTP {(int)response.StatusCode} ({response.StatusCode}) while processing authentication.";
                logger.LogWarning("{AuthenticationFailure} Login={LoginName}", detail, Normalize(loginName));
                throw new CafeteriaServicesAuthenticationException(detail + " Your credentials were not reported as invalid.");
            }

            PosAuthenticationResponse? authentication;
            try
            {
                authentication = await response.Content.ReadFromJsonAsync<PosAuthenticationResponse>
                (
                    cancellationToken: cancellationToken
                );
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                servicesSession.Clear();
                logger.LogWarning(exception, "Cafeteria Services returned an invalid authentication response for {LoginName}", Normalize(loginName));
                throw new CafeteriaServicesAuthenticationException(
                    "Cafeteria Services returned an invalid authentication response. Your credentials were not rejected.",
                    exception);
            }

            if (authentication is null || string.IsNullOrWhiteSpace(authentication.Token))
            {
                servicesSession.Clear();
                throw new CafeteriaServicesAuthenticationException(
                    "Cafeteria Services returned an incomplete authentication response. Your credentials were not rejected.");
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

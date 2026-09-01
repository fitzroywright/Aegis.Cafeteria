namespace Aegis.Cafeteria.Services;

using Common.Security.Abstractions;
using Common.Security.Authenticators;
using Common.Security.Models;
using Common.Security.Options;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;

/// <summary>
/// Authenticates Management Console credentials through Common.Security once, then exchanges the
/// password for a short-lived opaque Services token. The AD password is never retained for later
/// management API calls.
/// </summary>
public sealed class ManagementCommonSecurityStartupFilter
(
      ManagementCommonSecurityAuthenticator authentication
    , ManagementServiceTokenStore tokens
) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return application =>
        {
            application.Use
            (
                async (context, nextMiddleware) =>
                {
                    if (context.Request.Path.Equals("/api/management/authenticate", StringComparison.OrdinalIgnoreCase))
                    {
                        await AuthenticateAsync(context);
                        return;
                    }

                    if (context.Request.Headers.TryGetValue("X-Aegis-Management-Token", out var supplied)
                        && supplied.Count == 1
                        && tokens.TryGet(supplied[0]!, out ManagementServiceTokenSession? session))
                    {
                        List<Claim> claims =
                        [
                              new Claim(ClaimTypes.Name, session.UserName)
                            , new Claim("aegis_display_name", session.DisplayName)
                            , new Claim("aegis_authentication_source", "Common.Security")
                        ];

                        foreach (string group in authentication.AuthorizedGroups)
                        {
                            claims.Add(new Claim(ClaimTypes.Role, group));
                        }

                        ClaimsIdentity identity = new(claims, "Common.Security");
                        context.User = new ClaimsPrincipal(identity);
                    }

                    await nextMiddleware();
                }
            );

            next(application);
        };
    }

    private async Task AuthenticateAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        IPAddress? remoteAddress = context.Connection.RemoteIpAddress;
        if (remoteAddress is null || !IPAddress.IsLoopback(remoteAddress))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync
            (
                  new { error = "Management credential exchange is available only from the local Management service." }
                , context.RequestAborted
            );
            return;
        }

        ManagementCommonSecurityLoginRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<ManagementCommonSecurityLoginRequest>
            (
                context.RequestAborted
            );
        }
        catch
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid login request." }, context.RequestAborted);
            return;
        }

        if (request is null
            || string.IsNullOrWhiteSpace(request.UserName)
            || string.IsNullOrWhiteSpace(request.Password))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Username and password are required." }, context.RequestAborted);
            return;
        }

        AuthenticationResult result = authentication.Authenticate(request.UserName, request.Password);
        if (!result.IsAuthenticated || result.UserInfo is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync
            (
                  new { error = "The username or password was not accepted by Common.Security." }
                , context.RequestAborted
            );
            return;
        }

        if (!result.IsAdministrative)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync
            (
                  new { error = "The account is valid but is not authorized for Cafeteria Management." }
                , context.RequestAborted
            );
            return;
        }

        string userName = NormalizeUserName(request.UserName);
        string displayName = string.IsNullOrWhiteSpace(result.UserInfo.DisplayName)
            ? userName
            : result.UserInfo.DisplayName.Trim();
        ManagementServiceTokenSession session = tokens.Create(userName, displayName);

        await context.Response.WriteAsJsonAsync
        (
            new
            {
                  token = session.Token
                , userName = session.UserName
                , displayName = session.DisplayName
                , expiresAt = session.ExpiresAt
                , authenticationSource = "Common.Security"
            }
            , context.RequestAborted
        );
    }

    private static string NormalizeUserName(string loginName)
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
}

public sealed class ManagementCommonSecurityAuthenticator
{
    private readonly IAuthenticator authenticator;

    public ManagementCommonSecurityAuthenticator(IConfiguration configuration)
    {
        IConfigurationSection security = configuration.GetSection("Security");
        string domain = security["Domain"]?.Trim() ?? "fftpj.local";
        string searchBase = security["SearchBase"]?.Trim() ?? "DC=fftpj,DC=local";
        int port = security.GetValue<int?>("Port") ?? 636;
        int timeoutSeconds = security.GetValue<int?>("TimeoutSeconds") ?? 5;
        string[] servers = security
            .GetSection("Servers")
            .GetChildren()
            .Select(child => child.Value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();

        string[] configuredGroups = configuration
            .GetSection("ManagementApi:AuthorizedGroups")
            .GetChildren()
            .Select(child => child.Value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();

        AuthorizedGroups = configuredGroups.Length == 0
            ? ["Cafeteria Managers", "Cafeteria Administrators", "Domain Admins"]
            : configuredGroups;

        ActiveDirectoryAuthenticationOptions options = new()
        {
              Domain = domain
            , SearchBase = searchBase
            , Port = port
            , TimeoutSeconds = timeoutSeconds
            , Servers = servers
            , AdministrativeGroups = AuthorizedGroups
        };

        authenticator = new ActiveDirectory(options);
    }

    public IReadOnlyList<string> AuthorizedGroups { get; }

    public AuthenticationResult Authenticate(string loginName, string password)
    {
        return authenticator.Authenticate(loginName, password);
    }
}

public sealed class ManagementServiceTokenStore
{
    private readonly ConcurrentDictionary<string, ManagementServiceTokenSession> sessions = new(StringComparer.Ordinal);
    private readonly TimeSpan lifetime;

    public ManagementServiceTokenStore(IConfiguration configuration)
    {
        int configuredMinutes = configuration.GetValue<int?>("ManagementApi:SessionMinutes") ?? 30;
        lifetime = TimeSpan.FromMinutes(Math.Clamp(configuredMinutes, 5, 480));
    }

    public ManagementServiceTokenSession Create(string userName, string displayName)
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        ManagementServiceTokenSession session = new
        (
              token
            , userName
            , displayName
            , DateTimeOffset.UtcNow.Add(lifetime)
        );

        sessions[token] = session;
        return session;
    }

    public bool TryGet(string token, out ManagementServiceTokenSession? session)
    {
        if (!sessions.TryGetValue(token, out session))
        {
            return false;
        }

        if (session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            sessions.TryRemove(token, out _);
            session = null;
            return false;
        }

        session.ExpiresAt = DateTimeOffset.UtcNow.Add(lifetime);
        return true;
    }
}

public sealed class ManagementServiceTokenSession
(
      string token
    , string userName
    , string displayName
    , DateTimeOffset expiresAt
)
{
    public string Token { get; } = token;
    public string UserName { get; } = userName;
    public string DisplayName { get; } = displayName;
    public DateTimeOffset ExpiresAt { get; set; } = expiresAt;
}

public sealed record ManagementCommonSecurityLoginRequest(string UserName, string Password);

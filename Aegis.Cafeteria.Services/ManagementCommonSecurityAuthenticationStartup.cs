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
/// Exchanges Common.Security credentials for short-lived opaque Cafeteria Services sessions.
/// Passwords are used only for the authentication request and are never retained.
/// </summary>
public sealed class ManagementCommonSecurityStartupFilter
(
      ManagementCommonSecurityAuthenticator authentication
    , ManagementServiceTokenStore tokens
    , ManagementApiOptions management
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
                        await AuthenticateManagementAsync(context);
                        return;
                    }

                    if (context.Request.Path.Equals("/api/pos/authenticate", StringComparison.OrdinalIgnoreCase))
                    {
                        await AuthenticatePosAsync(context);
                        return;
                    }

                    if (TryApplySession(context, "X-Aegis-Management-Token", requireManager: true))
                    {
                        await nextMiddleware();
                        return;
                    }

                    if (TryApplySession(context, "X-Aegis-POS-Token", requireManager: false))
                    {
                        if (!string.IsNullOrWhiteSpace(management.ApiKey))
                        {
                            context.Request.Headers["X-Cafeteria-Api-Key"] = management.ApiKey;
                        }
                    }

                    await nextMiddleware();
                }
            );

            next(application);
        };
    }

    private bool TryApplySession(HttpContext context, string headerName, bool requireManager)
    {
        if (!context.Request.Headers.TryGetValue(headerName, out var supplied) || supplied.Count != 1)
        {
            return false;
        }

        string? token = supplied[0];
        if (string.IsNullOrWhiteSpace(token)
            || !tokens.TryGet(token, out ManagementServiceTokenSession? session)
            || session is null)
        {
            return false;
        }

        if (requireManager && !session.IsManager)
        {
            return false;
        }

        List<Claim> claims =
        [
              new Claim(ClaimTypes.Name, session.UserName)
            , new Claim("aegis_display_name", session.DisplayName)
            , new Claim("aegis_authentication_source", "Common.Security")
        ];

        if (session.IsManager)
        {
            foreach (string group in authentication.AuthorizedGroups)
            {
                claims.Add(new Claim(ClaimTypes.Role, group));
            }
        }

        ClaimsIdentity identity = new(claims, "Common.Security");
        context.User = new ClaimsPrincipal(identity);
        return true;
    }

    private async Task AuthenticateManagementAsync(HttpContext context)
    {
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

        AuthenticationExchange? exchange = await AuthenticateAsync(context);
        if (exchange is null)
        {
            return;
        }

        if (!exchange.Result.IsAdministrative)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync
            (
                  new { error = "The account is valid but is not authorized for Cafeteria Management." }
                , context.RequestAborted
            );
            return;
        }

        ManagementServiceTokenSession session = tokens.Create
        (
              exchange.UserName
            , exchange.DisplayName
            , isManager: true
        );

        await WriteSessionAsync(context, session, includeManagerFlag: false);
    }

    private async Task AuthenticatePosAsync(HttpContext context)
    {
        IPAddress? remoteAddress = context.Connection.RemoteIpAddress;
        bool isLoopback = remoteAddress is not null && IPAddress.IsLoopback(remoteAddress);
        if (!isLoopback && !context.Request.IsHttps)
        {
            context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            await context.Response.WriteAsJsonAsync
            (
                  new { error = "POS credential exchange requires HTTPS when Cafeteria Services is remote." }
                , context.RequestAborted
            );
            return;
        }

        AuthenticationExchange? exchange = await AuthenticateAsync(context);
        if (exchange is null)
        {
            return;
        }

        ManagementServiceTokenSession session = tokens.Create
        (
              exchange.UserName
            , exchange.DisplayName
            , exchange.Result.IsAdministrative
        );

        await WriteSessionAsync(context, session, includeManagerFlag: true);
    }

    private async Task<AuthenticationExchange?> AuthenticateAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return null;
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
            return null;
        }

        if (request is null
            || string.IsNullOrWhiteSpace(request.UserName)
            || string.IsNullOrWhiteSpace(request.Password))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Username and password are required." }, context.RequestAborted);
            return null;
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
            return null;
        }

        string userName = NormalizeUserName(request.UserName);
        string displayName = string.IsNullOrWhiteSpace(result.UserInfo.DisplayName)
            ? userName
            : result.UserInfo.DisplayName.Trim();
        return new AuthenticationExchange(result, userName, displayName);
    }

    private static Task WriteSessionAsync
    (
          HttpContext context
        , ManagementServiceTokenSession session
        , bool includeManagerFlag
    )
    {
        object response = includeManagerFlag
            ? new
            {
                  token = session.Token
                , userName = session.UserName
                , displayName = session.DisplayName
                , isManager = session.IsManager
                , expiresAt = session.ExpiresAt
                , authenticationSource = "Common.Security"
            }
            : new
            {
                  token = session.Token
                , userName = session.UserName
                , displayName = session.DisplayName
                , expiresAt = session.ExpiresAt
                , authenticationSource = "Common.Security"
            };

        return context.Response.WriteAsJsonAsync(response, context.RequestAborted);
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

    private sealed record AuthenticationExchange
    (
          AuthenticationResult Result
        , string UserName
        , string DisplayName
    );
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

    public ManagementServiceTokenSession Create(string userName, string displayName, bool isManager)
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        ManagementServiceTokenSession session = new
        (
              token
            , userName
            , displayName
            , isManager
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
    , bool isManager
    , DateTimeOffset expiresAt
)
{
    public string Token { get; } = token;
    public string UserName { get; } = userName;
    public string DisplayName { get; } = displayName;
    public bool IsManager { get; } = isManager;
    public DateTimeOffset ExpiresAt { get; set; } = expiresAt;
}

public sealed record ManagementCommonSecurityLoginRequest(string UserName, string Password);

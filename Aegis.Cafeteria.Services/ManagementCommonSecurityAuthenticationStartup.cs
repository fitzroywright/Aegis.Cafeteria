using System.Collections.Concurrent;
using System.Security.Cryptography;
using Common.Security;
using Common.Security.Authenticators;
using Microsoft.AspNetCore.Http;

public sealed class ManagementCommonSecurityAuthenticationStartup : IHostedService
{
    private readonly ManagementCommonSecurityAuthenticationRuntime runtime;

    public ManagementCommonSecurityAuthenticationStartup(ManagementCommonSecurityAuthenticationRuntime runtime)
    {
        this.runtime = runtime;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        runtime.MarkReady();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        runtime.MarkStopped();
        return Task.CompletedTask;
    }
}

public sealed class ManagementCommonSecurityAuthenticationRuntime
{
    private readonly ManagementCommonSecurityAuthenticator authenticator;
    private readonly ManagementServiceTokenStore tokenStore;
    private volatile bool ready;

    public ManagementCommonSecurityAuthenticationRuntime(
        ManagementCommonSecurityAuthenticator authenticator,
        ManagementServiceTokenStore tokenStore)
    {
        this.authenticator = authenticator;
        this.tokenStore = tokenStore;
    }

    public bool Ready => ready;

    public void MarkReady() => ready = true;
    public void MarkStopped() => ready = false;

    public Task AuthenticateAsync(HttpContext context)
    {
        return AuthenticationEndpointCore.AuthenticateAsync(context, authenticator, tokenStore);
    }
}

internal static class AuthenticationEndpointCore
{
    public static async Task AuthenticateAsync(
        HttpContext context,
        ManagementCommonSecurityAuthenticator authenticator,
        ManagementServiceTokenStore tokenStore)
    {
        ManagementLoginRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<ManagementLoginRequest>(context.RequestAborted);
        }
        catch
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid login request." }, context.RequestAborted);
            return;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Username and password are required." }, context.RequestAborted);
            return;
        }

        AuthenticationExchange exchange;
        try
        {
            exchange = Authenticate(authenticator, request.UserName, request.Password);
        }
        catch
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { error = "Authentication service is unavailable." }, context.RequestAborted);
            return;
        }

        if (!exchange.Result.Success)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid username or password." }, context.RequestAborted);
            return;
        }

        bool isManager = exchange.Result.Groups.Any(group => authenticator.AuthorizedGroups.Contains(group, StringComparer.OrdinalIgnoreCase));
        ManagementServiceTokenSession session = tokenStore.Create(exchange.UserName, exchange.DisplayName, isManager);

        object response = isManager
            ? new
            {
                  token = session.Token
                , userName = session.UserName
                , displayName = session.DisplayName
                , expiresAt = session.ExpiresAt
                , authenticationSource = "Common.Security"
                , managementAccess = true
            }
            : new
            {
                  token = session.Token
                , userName = session.UserName
                , displayName = session.DisplayName
                , expiresAt = session.ExpiresAt
                , authenticationSource = "Common.Security"
            };

        await context.Response.WriteAsJsonAsync(response, context.RequestAborted);
    }

    private static AuthenticationExchange Authenticate(
        ManagementCommonSecurityAuthenticator authenticator,
        string loginName,
        string password)
    {
        AuthenticationResult result = authenticator.Authenticate(loginName, password);
        string normalized = NormalizeUserName(loginName);
        string displayName = string.IsNullOrWhiteSpace(result.DisplayName) ? normalized : result.DisplayName;
        return new AuthenticationExchange(result, normalized, displayName);
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

        string? configuredDomain = security["Domain"]?.Trim();
        string domain = string.IsNullOrWhiteSpace(configuredDomain)
            ? "fftpj.local"
            : configuredDomain;

        string? configuredSearchBase = security["SearchBase"]?.Trim();
        string searchBase = string.IsNullOrWhiteSpace(configuredSearchBase)
            ? "DC=fftpj,DC=local"
            : configuredSearchBase;

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

        return true;
    }
}

public sealed record ManagementServiceTokenSession
(
      string Token
    , string UserName
    , string DisplayName
    , bool IsManager
    , DateTimeOffset ExpiresAt
);

public sealed record ManagementLoginRequest(string UserName, string Password);

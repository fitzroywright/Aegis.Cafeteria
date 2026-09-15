using System.Collections.Concurrent;
using System.Security.Cryptography;
using Common.Security;
using Common.Security.Abstractions;
using Common.Security.Authenticators;
using Common.Security.Models;
using Common.Security.Options;
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

    public AuthenticationResult Authenticate(string loginName, string password)
    {
        return authenticator.Authenticate(loginName, password);
    }

    public ManagementServiceTokenSession CreateSession(string userName, string displayName, bool isManager)
    {
        return tokenStore.Create(userName, displayName, isManager);
    }

    public bool TryGetSession(string token, out ManagementServiceTokenSession? session)
    {
        return tokenStore.TryGet(token, out session);
    }

    public bool IsManager(AuthenticationResult result)
    {
        if (result.IsAdministrative)
        {
            return true;
        }

        return result.GroupNames.Any(group => authenticator.AuthorizedGroups.Any(authorized =>
            group.Equals(authorized, StringComparison.OrdinalIgnoreCase)));
    }
}

public static class ManagementCommonSecurityAuthenticationExtensions
{
    public static IServiceCollection AddManagementCommonSecurityAuthentication(this IServiceCollection services)
    {
        services.AddSingleton<ManagementCommonSecurityAuthenticator>();
        services.AddSingleton<ManagementServiceTokenStore>();
        services.AddSingleton<ManagementCommonSecurityAuthenticationRuntime>();
        services.AddHostedService<ManagementCommonSecurityAuthenticationStartup>();
        return services;
    }

    public static IApplicationBuilder UseManagementCommonSecurityAuthentication(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (context.Request.Path.Equals("/api/management/authenticate", StringComparison.OrdinalIgnoreCase)
                && HttpMethods.IsPost(context.Request.Method))
            {
                ManagementCommonSecurityAuthenticationRuntime runtime = context.RequestServices
                    .GetRequiredService<ManagementCommonSecurityAuthenticationRuntime>();

                await HandleAuthenticationAsync(context, runtime);
                return;
            }

            await next();
        });
    }

    private static async Task HandleAuthenticationAsync(
        HttpContext context,
        ManagementCommonSecurityAuthenticationRuntime runtime)
    {
        if (!runtime.Ready)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                authenticated = false,
                error = "Authentication service is not ready."
            }, context.RequestAborted);
            return;
        }

        ManagementAuthenticationRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<ManagementAuthenticationRequest>(context.RequestAborted);
        }
        catch
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                authenticated = false,
                error = "Invalid authentication request."
            }, context.RequestAborted);
            return;
        }

        if (request is null
            || string.IsNullOrWhiteSpace(request.LoginName)
            || string.IsNullOrWhiteSpace(request.Password))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                authenticated = false,
                error = "Login name and password are required."
            }, context.RequestAborted);
            return;
        }

        AuthenticationResult result = runtime.Authenticate(request.LoginName, request.Password);
        if (!result.IsAuthenticated)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                authenticated = false,
                error = "Authentication failed."
            }, context.RequestAborted);
            return;
        }

        string userName = NormalizeUserName(request.LoginName);
        string displayName = result.UserInfo?.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = userName;
        }

        bool isManager = runtime.IsManager(result);
        ManagementServiceTokenSession session = runtime.CreateSession(userName, displayName, isManager);

        object response = isManager
            ? new
            {
                  token = session.Token
                , userName = session.UserName
                , displayName = session.DisplayName
                , expiresAt = session.ExpiresAt
                , isManager = true
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

        await context.Response.WriteAsJsonAsync(response, context.RequestAborted);
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

        string configuredDomain = security["Domain"]?.Trim() ?? string.Empty;
        string domain = string.IsNullOrWhiteSpace(configuredDomain)
            ? "fftpj.local"
            : configuredDomain;

        string configuredSearchBase = security["SearchBase"]?.Trim() ?? string.Empty;
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

public sealed record ManagementAuthenticationRequest(string LoginName, string Password);

public sealed record ManagementServiceTokenSession
(
      string Token
    , string UserName
    , string DisplayName
    , bool IsManager
    , DateTimeOffset ExpiresAt
);

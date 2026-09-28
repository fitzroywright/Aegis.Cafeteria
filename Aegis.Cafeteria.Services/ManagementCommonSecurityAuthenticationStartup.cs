using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Common.Security;
using Common.Security.Abstractions;
using Common.Security.Models;
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

    public void RemoveSession(string token)
    {
        tokenStore.Remove(token);
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
            ManagementCommonSecurityAuthenticationRuntime runtime = context.RequestServices
                .GetRequiredService<ManagementCommonSecurityAuthenticationRuntime>();

            if (context.Request.Path.Equals("/api/management/authenticate", StringComparison.OrdinalIgnoreCase))
            {
                await HandleManagementAuthenticationAsync(context, runtime);
                return;
            }

            if (context.Request.Path.Equals("/api/pos/authenticate", StringComparison.OrdinalIgnoreCase))
            {
                await HandlePosAuthenticationAsync(context, runtime);
                return;
            }

            if (TryApplySession(context, runtime, "X-Aegis-Management-Token", requireManager: true)
                || TryApplySession(context, runtime, "X-Aegis-POS-Token", requireManager: false))
            {
                await next();
                return;
            }

            await next();
        });
    }

    private static bool TryApplySession(
        HttpContext context,
        ManagementCommonSecurityAuthenticationRuntime runtime,
        string headerName,
        bool requireManager)
    {
        if (!context.Request.Headers.TryGetValue(headerName, out var supplied)
            || supplied.Count != 1
            || string.IsNullOrWhiteSpace(supplied[0])
            || !runtime.TryGetSession(supplied[0]!, out ManagementServiceTokenSession? session)
            || session is null
            || (requireManager && !session.IsManager))
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
            claims.Add(new Claim(ClaimTypes.Role, Aegis.Cafeteria.Services.CafeteriaRoles.Manager));
        }

        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Common.Security"));
        return true;
    }

    private static async Task HandleManagementAuthenticationAsync(
        HttpContext context,
        ManagementCommonSecurityAuthenticationRuntime runtime)
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
            await context.Response.WriteAsJsonAsync(new
            {
                authenticated = false,
                error = "Management credential exchange is available only from the local Management service."
            }, context.RequestAborted);
            return;
        }

        AuthenticationExchange? exchange = await AuthenticateAsync(context, runtime);
        if (exchange is null)
        {
            return;
        }

        if (!exchange.IsManager)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                authenticated = true,
                authorized = false,
                error = "The account is valid but is not authorized for Cafeteria Management."
            }, context.RequestAborted);
            return;
        }

        ManagementServiceTokenSession session = runtime.CreateSession(
            exchange.UserName,
            exchange.DisplayName,
            isManager: true);

        await WriteSessionAsync(context, session, includeManagerFlag: false);
    }

    private static async Task HandlePosAuthenticationAsync(
        HttpContext context,
        ManagementCommonSecurityAuthenticationRuntime runtime)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        IPAddress? remoteAddress = context.Connection.RemoteIpAddress;
        bool isLoopback = remoteAddress is not null && IPAddress.IsLoopback(remoteAddress);
        if (!isLoopback && !context.Request.IsHttps)
        {
            context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            await context.Response.WriteAsJsonAsync(new
            {
                authenticated = false,
                error = "POS credential exchange requires HTTPS when Cafeteria Services is remote."
            }, context.RequestAborted);
            return;
        }

        AuthenticationExchange? exchange = await AuthenticateAsync(context, runtime);
        if (exchange is null)
        {
            return;
        }

        ManagementServiceTokenSession session = runtime.CreateSession(
            exchange.UserName,
            exchange.DisplayName,
            exchange.IsManager);

        await WriteSessionAsync(context, session, includeManagerFlag: true);
    }

    private static async Task<AuthenticationExchange?> AuthenticateAsync(
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
            return null;
        }

        ManagementAuthenticationRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<ManagementAuthenticationRequest>(context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                authenticated = false,
                error = "Invalid authentication request."
            }, context.RequestAborted);
            return null;
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
            return null;
        }

        AuthenticationResult result;
        try
        {
            result = runtime.Authenticate(request.LoginName, request.Password);
        }
        catch (Exception exception)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                authenticated = false,
                error = "The authentication service could not process the request.",
                errorType = exception.GetType().Name
            }, context.RequestAborted);
            return null;
        }

        if (!result.IsAuthenticated || result.UserInfo is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                authenticated = false,
                error = "The username or password was not accepted by Common.Security."
            }, context.RequestAborted);
            return null;
        }

        string userName = NormalizeUserName(request.LoginName);
        string? displayName = result.UserInfo.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = userName;
        }

        return new AuthenticationExchange(result, userName, displayName, runtime.IsManager(result));
    }

    private static Task WriteSessionAsync(
        HttpContext context,
        ManagementServiceTokenSession session,
        bool includeManagerFlag)
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

    private sealed record AuthenticationExchange(
        AuthenticationResult Result,
        string UserName,
        string DisplayName,
        bool IsManager);
}

public sealed class ManagementCommonSecurityAuthenticator
{
    private readonly IAuthenticator authenticator;

    public ManagementCommonSecurityAuthenticator(
        IAuthenticator authenticator,
        IConfiguration configuration)
    {
        this.authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));

        string[] configuredGroups = configuration
            .GetSection("ManagementApi:AuthorizedGroups")
            .GetChildren()
            .Select(child => child.Value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();

        AuthorizedGroups = configuredGroups;
    }

    public IReadOnlyList<string> AuthorizedGroups { get; }

    public AuthenticationResult Authenticate(string loginName, string password)
        => authenticator.Authenticate(loginName, password);
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

    public void Remove(string token)
    {
        sessions.TryRemove(token, out _);
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


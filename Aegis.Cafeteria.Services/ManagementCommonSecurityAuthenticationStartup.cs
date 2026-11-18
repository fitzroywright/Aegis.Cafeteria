using Aegis.Cafeteria.Application.Security;
using Aegis.Cafeteria.Infrastructure;
using Aegis.Cafeteria.Infrastructure.Security;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Common.Security.Services;
using Common.Security.Hosting;
using Aegis.Cafeteria.Services;
using CafeteriaPermissions = Aegis.Cafeteria.Services.CafeteriaPermissions;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Security.Claims;
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

public sealed class ManagementCommonSecurityAuthenticationRuntime(
    CommonSecurityOperatorAuthenticator authenticator,
    OpaqueAccountSessions<ManagementSessionIdentity> sessions,
    IConfiguration configuration)
{
    private volatile bool ready;
    public bool Ready => ready;
    public void MarkReady() => ready = true;
    public void MarkStopped() => ready = false;
    public Task<OperatorIdentity?> AuthenticateAsync(string loginName, string password, CancellationToken cancellationToken)
        => authenticator.AuthenticateAsync(loginName, password, cancellationToken);
    public ManagementServiceTokenSession CreateSession(OperatorIdentity identity)
    {
        int minutes = Math.Clamp(configuration.GetValue<int?>("ManagementApi:SessionMinutes") ?? 30, 5, 480);
        var session = sessions.Create(identity.AccountId, identity.SessionVersion,
            new ManagementSessionIdentity(identity.UserId, identity.DisplayName, identity.IsManager), TimeSpan.FromMinutes(minutes));
        return Map(session);
    }
    public bool TryGetSession(string token, out ManagementServiceTokenSession? session)
    {
        var current = sessions.Find(token);
        session = current is null ? null : Map(current);
        return session is not null;
    }
    public void RemoveSession(string token) => sessions.Remove(token);
    private static ManagementServiceTokenSession Map(OpaqueAccountSession<ManagementSessionIdentity> session)
        => new(session.Token, session.Identity.UserName, session.Identity.DisplayName, session.Identity.IsManager,
            session.ExpiresAt, session.AccountId, session.SessionVersion);
}

public sealed record ManagementSessionIdentity(string UserName, string DisplayName, bool IsManager);

public static class ManagementCommonSecurityAuthenticationExtensions
{
    public static IServiceCollection AddManagementCommonSecurityAuthentication(
        this IServiceCollection services, IConfiguration configuration, string contentRootPath)
    {
        string connection = CafeteriaServiceDatabase.ResolveConnectionString(configuration, contentRootPath);
        services.AddSingleton<IDbContextFactory<LocalCafeteriaDbContext>>(new LocalDbContextFactory(
            new DbContextOptionsBuilder<LocalCafeteriaDbContext>().UseSqlite(connection).Options));
        services.AddCafeteriaOperatorSecurity(new OperatorSecurityOptions
        {
            AdministrativeGroups = configuration.GetSection("ManagementApi:AuthorizedGroups").Get<string[]>() ?? []
        });
        services.AddCommonAuthenticationRateLimiting();
        services.AddSingleton<OpaqueAccountSessions<ManagementSessionIdentity>>();
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

            if (TryApplySession(context, runtime, "X-Aegis-Management-Token", requireManager: true)
                || TryApplySession(context, runtime, "X-Aegis-POS-Token", requireManager: false))
            {
                await next();
                return;
            }

            await next();
        });
    }

    public static void MapOperatorSecurity(this WebApplication app)
    {
        app.MapPost("/api/management/authenticate", (HttpContext http,ManagementCommonSecurityAuthenticationRuntime runtime)=>HandleManagementAuthenticationAsync(http,runtime))
            .AllowAnonymous().RequireRateLimiting(AccountSecurityHosting.LoginRateLimit);
        app.MapPost("/api/pos/authenticate",(HttpContext http,ManagementCommonSecurityAuthenticationRuntime runtime)=>HandlePosAuthenticationAsync(http,runtime))
            .AllowAnonymous().RequireRateLimiting(AccountSecurityHosting.LoginRateLimit);
        app.MapCommonAccountAdministration("/api/management/security/accounts",AccountPopulation.Operator,
            new Dictionary<AuthenticationProviderKind,string>{{AuthenticationProviderKind.ActiveDirectory,"active-directory"}},
            http=>CafeteriaPermissions.HasPermission(http.User,CafeteriaPermissions.ManageUsers));
        app.MapPut("/api/management/security/accounts/{id}/role",async (string id,OperatorRoleRequest request,HttpContext http,
            Common.Security.Integration.RelationalAccountStore accounts,IDbContextFactory<LocalCafeteriaDbContext> factory,OperatorSecurityOptions options)=>
        {
            string? actor=http.User.FindFirstValue(AccountSecurityHosting.AccountIdClaim);
            if(actor is null || !CafeteriaPermissions.HasPermission(http.User,CafeteriaPermissions.ManageUsers) ||
                !long.TryParse(http.User.FindFirstValue(AccountSecurityHosting.SessionVersionClaim),out var stamp) ||
                !new AccountProvisioningService(accounts).IsCurrentSession(actor,stamp)) return Results.StatusCode(403);
            if(request.Role is not (CafeteriaRoleNames.Operator or CafeteriaRoleNames.Manager)) return Results.BadRequest(new {error="Choose Operator or Manager."});
            if(actor==id) return Results.BadRequest(new {error="Use another approved manager to change your own role."});
            var account=accounts.Find(id);if(account is null)return Results.NotFound();
            await new CafeteriaAuthorizationStore(factory,options).EnsureInitializedAsync(http.RequestAborted);
            accounts.ChangeAuthorization(id,actor,(connection,transaction)=>
            {
                using var command=connection.CreateCommand();command.Transaction=transaction;
                command.CommandText="DELETE FROM CafeteriaSecurityUserRoles WHERE lower(UserId)=lower($user);INSERT INTO CafeteriaSecurityUserRoles(UserId,RoleKey) VALUES($user,$role)";
                var user=command.CreateParameter();user.ParameterName="$user";user.Value=account.LoginName;command.Parameters.Add(user);
                var role=command.CreateParameter();role.ParameterName="$role";role.Value=request.Role;command.Parameters.Add(role);command.ExecuteNonQuery();
            });
            return Results.NoContent();
        }).RequireAuthorization();
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
              new Claim(global::Common.Security.Hosting.AccountSecurityHosting.AccountIdClaim,session.AccountId)
            , new Claim(global::Common.Security.Hosting.AccountSecurityHosting.SessionVersionClaim,session.SessionVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
            , new Claim(ClaimTypes.Name, session.UserName)
            , new Claim("aegis_display_name", session.DisplayName)
            , new Claim("aegis_authentication_source", "Common.Security")
        ];

        string? role=CafeteriaRoleResolver.ResolveManagementRole(context.RequestServices.GetRequiredService<IConfiguration>(),session.AccountId,session.IsManager);
        if(role is not null) claims.Add(new Claim(ClaimTypes.Role,role));

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

        ManagementServiceTokenSession session = runtime.CreateSession(exchange.Operator);

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

        ManagementServiceTokenSession session = runtime.CreateSession(exchange.Operator);

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

        OperatorIdentity? result;
        try
        {
            result = await runtime.AuthenticateAsync(request.LoginName, request.Password, context.RequestAborted);
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

        if (result is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                authenticated = false,
                error = "The username or password was not accepted by Common.Security."
            }, context.RequestAborted);
            return null;
        }

        return new AuthenticationExchange(result);
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
                , accountId = session.AccountId
                , sessionVersion = session.SessionVersion
            }
            : new
            {
                  token = session.Token
                , userName = session.UserName
                , displayName = session.DisplayName
                , expiresAt = session.ExpiresAt
                , authenticationSource = "Common.Security"
                , accountId = session.AccountId
                , sessionVersion = session.SessionVersion
            };

        return context.Response.WriteAsJsonAsync(response, context.RequestAborted);
    }

    private sealed record AuthenticationExchange(OperatorIdentity Operator)
    {
        public bool IsManager => Operator.IsManager;
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
    , string AccountId
    , long SessionVersion
);


public sealed record OperatorRoleRequest(string Role);

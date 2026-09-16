using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Aegis.Cafeteria.Management;

internal static class ManagementEndpoints
{
    private const string AuthenticationScheme = "AegisManagement";
    private static readonly string[] ProxyMethods = ["GET", "POST", "PUT", "DELETE", "PATCH"];

    public static void Map(WebApplication app, TimeSpan sessionLifetime)
    {
        MapAuthentication(app, sessionLifetime);
        MapSession(app);
        MapProxy(app);
        MapShell(app);
    }

    private static void MapAuthentication(WebApplication app, TimeSpan sessionLifetime)
    {
        app.MapGet("/login", async (HttpContext http, ManagementSessionStore sessions) =>
        {
            if (ManagementSessionAccessor.TryGet(http, sessions, out _)) return Results.Redirect("/");
            if (http.User.Identity?.IsAuthenticated == true) await http.SignOutAsync(AuthenticationScheme);
            return Results.Content(ManagementLoginPage.Build(), "text/html");
        });

        app.MapPost("/login", async (HttpContext http, IConfiguration configuration, ManagementSessionStore sessions, CancellationToken cancellationToken) =>
        {
            if (!http.Request.HasFormContentType) return Results.Content(ManagementLoginPage.Build("Enter your username and password to sign in."), "text/html", statusCode: StatusCodes.Status400BadRequest);
            IFormCollection form = await http.Request.ReadFormAsync(cancellationToken);
            string loginName = form["username"].ToString().Trim();
            string password = form["password"].ToString();
            if (string.IsNullOrWhiteSpace(loginName) || string.IsNullOrWhiteSpace(password)) return Results.Content(ManagementLoginPage.Build("Username and password are required."), "text/html", statusCode: StatusCodes.Status400BadRequest);

            ManagementAuthenticationResponse authentication;
            try { authentication = await ManagementServicesClient.AuthenticateAsync(loginName, password, configuration, cancellationToken); }
            catch (ManagementAuthenticationException exception) { return Results.Content(ManagementLoginPage.Build(exception.Message), "text/html", statusCode: exception.StatusCode); }
            catch (HttpRequestException) { return Results.Content(ManagementLoginPage.Build("Cafeteria Services could not be reached. Verify that Aegis.Cafeteria.Services is running."), "text/html", statusCode: StatusCodes.Status503ServiceUnavailable); }

            string sessionId = sessions.Create(authentication);
            Claim[] claims = [new(ClaimTypes.Name, authentication.UserName), new("aegis_display_name", authentication.DisplayName), new("aegis_authentication_source", authentication.AuthenticationSource), new(ManagementSessionAccessor.SessionClaim, sessionId)];
            await http.SignInAsync(AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationScheme)), new AuthenticationProperties { AllowRefresh = true, IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow.Add(sessionLifetime) });
            return Results.Redirect("/");
        });

        app.MapPost("/logout", async (HttpContext http, ManagementSessionStore sessions) =>
        {
            string? sessionId = http.User.FindFirst(ManagementSessionAccessor.SessionClaim)?.Value;
            if (!string.IsNullOrWhiteSpace(sessionId)) sessions.Remove(sessionId);
            await http.SignOutAsync(AuthenticationScheme);
            return Results.Redirect("/login");
        }).RequireAuthorization();
    }

    private static void MapSession(WebApplication app)
    {
        app.MapGet("/api/session", (HttpContext http, ManagementSessionStore sessions) =>
        {
            if (!ManagementSessionAccessor.TryGet(http, sessions, out ManagementSession? session)) return Results.Unauthorized();
            return Results.Ok(new { authenticated = true, userName = session!.UserName, displayName = session.DisplayName, authenticationSource = "Common.Security", expiresAt = session.ExpiresAt });
        }).RequireAuthorization();
        app.MapGet("/api/config", () => Results.Ok(new { servicesBaseUrl = "/api/services" })).RequireAuthorization();
    }

    private static void MapProxy(WebApplication app)
    {
        app.MapMethods("/api/services/{**path}", ProxyMethods, async (string? path, HttpContext http, IConfiguration configuration, ManagementSessionStore sessions, CancellationToken ct) => await ManagementServicesClient.ProxyAsync(path ?? string.Empty, http, configuration, sessions, ct)).RequireAuthorization();
        app.MapMethods("/engineering/{**path}", ProxyMethods, async (string? path, HttpContext http, IConfiguration configuration, ManagementSessionStore sessions, CancellationToken ct) => await ManagementServicesClient.ProxyAsync(string.IsNullOrWhiteSpace(path) ? "engineering" : $"engineering/{path}", http, configuration, sessions, ct)).RequireAuthorization();
        app.MapMethods("/api/engineering/{**path}", ProxyMethods, async (string? path, HttpContext http, IConfiguration configuration, ManagementSessionStore sessions, CancellationToken ct) => await ManagementServicesClient.ProxyAsync(string.IsNullOrWhiteSpace(path) ? "api/engineering" : $"api/engineering/{path}", http, configuration, sessions, ct)).RequireAuthorization();
    }

    private static void MapShell(WebApplication app)
    {
        app.MapGet("/", (HttpContext http, ManagementSessionStore sessions) => ManagementSessionAccessor.TryGet(http, sessions, out _) ? Results.File(Path.Combine(app.Environment.WebRootPath, "index.html"), "text/html") : Results.Redirect("/login"));
        app.MapFallbackToFile("index.html").RequireAuthorization();
    }
}

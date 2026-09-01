using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

const string ManagementAuthenticationScheme = "AegisManagement";
const string ManagementSessionClaim = "aegis_management_session";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

int configuredSessionMinutes = builder.Configuration.GetValue<int?>("Authentication:SessionMinutes") ?? 30;
int sessionMinutes = Math.Clamp(configuredSessionMinutes, 5, 480);
TimeSpan sessionLifetime = TimeSpan.FromMinutes(sessionMinutes);

builder.Services.AddSingleton(new ManagementSessionStore(sessionLifetime));
builder.Services
    .AddAuthentication(ManagementAuthenticationScheme)
    .AddCookie
    (
          ManagementAuthenticationScheme
        , options =>
        {
            options.LoginPath = "/login";
            options.AccessDeniedPath = "/login";
            options.Cookie.Name = "Aegis.Cafeteria.Management";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.ExpireTimeSpan = sessionLifetime;
            options.SlidingExpiration = true;
            options.Events.OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
        }
    );
builder.Services.AddAuthorization();

WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet
(
      "/login"
    , (HttpContext http) =>
    {
        if (http.User.Identity?.IsAuthenticated == true)
        {
            return Results.Redirect("/");
        }

        return Results.Content(ManagementLoginPage.Build(), "text/html");
    }
);

app.MapPost
(
      "/login"
    , async
    (
          HttpContext http
        , IConfiguration configuration
        , ManagementSessionStore sessions
        , CancellationToken cancellationToken
    ) =>
    {
        if (!http.Request.HasFormContentType)
        {
            return Results.Content
            (
                  ManagementLoginPage.Build("Enter your username and password to sign in.")
                , "text/html"
                , statusCode: StatusCodes.Status400BadRequest
            );
        }

        IFormCollection form = await http.Request.ReadFormAsync(cancellationToken);
        string loginName = form["username"].ToString().Trim();
        string password = form["password"].ToString();

        if (string.IsNullOrWhiteSpace(loginName) || string.IsNullOrWhiteSpace(password))
        {
            return Results.Content
            (
                  ManagementLoginPage.Build("Username and password are required.")
                , "text/html"
                , statusCode: StatusCodes.Status400BadRequest
            );
        }

        ManagementAuthenticationResponse authentication;
        try
        {
            authentication = await AuthenticateWithCommonSecurityAsync
            (
                  loginName
                , password
                , configuration
                , cancellationToken
            );
        }
        catch (ManagementAuthenticationException exception)
        {
            return Results.Content
            (
                  ManagementLoginPage.Build(exception.Message)
                , "text/html"
                , statusCode: exception.StatusCode
            );
        }
        catch (HttpRequestException)
        {
            return Results.Content
            (
                  ManagementLoginPage.Build("Cafeteria Services could not be reached. Verify that Aegis.Cafeteria.Services is running.")
                , "text/html"
                , statusCode: StatusCodes.Status503ServiceUnavailable
            );
        }

        string sessionId = sessions.Create(authentication);
        Claim[] claims =
        [
              new Claim(ClaimTypes.Name, authentication.UserName)
            , new Claim("aegis_display_name", authentication.DisplayName)
            , new Claim("aegis_authentication_source", authentication.AuthenticationSource)
            , new Claim(ManagementSessionClaim, sessionId)
        ];
        ClaimsIdentity identity = new(claims, ManagementAuthenticationScheme);
        ClaimsPrincipal principal = new(identity);
        AuthenticationProperties properties = new()
        {
              AllowRefresh = true
            , IsPersistent = false
            , ExpiresUtc = DateTimeOffset.UtcNow.Add(sessionLifetime)
        };

        await http.SignInAsync
        (
              ManagementAuthenticationScheme
            , principal
            , properties
        );

        return Results.Redirect("/");
    }
);

app.MapPost
(
      "/logout"
    , async (HttpContext http, ManagementSessionStore sessions) =>
    {
        string? sessionId = http.User.FindFirst(ManagementSessionClaim)?.Value;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            sessions.Remove(sessionId);
        }

        await http.SignOutAsync(ManagementAuthenticationScheme);
        return Results.Redirect("/login");
    }
).RequireAuthorization();

app.MapGet
(
      "/api/session"
    , (HttpContext http, ManagementSessionStore sessions) =>
    {
        if (!TryGetSession(http, sessions, out ManagementSession? session))
        {
            return Results.Unauthorized();
        }

        return Results.Ok
        (
            new
            {
                  authenticated = true
                , userName = session.UserName
                , displayName = session.DisplayName
                , authenticationSource = "Common.Security"
                , expiresAt = session.ExpiresAt
            }
        );
    }
).RequireAuthorization();

app.MapGet
(
      "/api/config"
    , () => Results.Ok(new { servicesBaseUrl = "/api/services" })
).RequireAuthorization();

string[] proxyMethods = ["GET", "POST", "PUT", "DELETE", "PATCH"];

app.MapMethods
(
      "/api/services/{**path}"
    , proxyMethods
    , async
    (
          string? path
        , HttpContext http
        , IConfiguration configuration
        , ManagementSessionStore sessions
        , CancellationToken cancellationToken
    ) =>
    {
        await ProxyToServicesAsync
        (
              path ?? string.Empty
            , http
            , configuration
            , sessions
            , cancellationToken
        );
    }
).RequireAuthorization();

app.MapMethods
(
      "/engineering/{**path}"
    , proxyMethods
    , async
    (
          string? path
        , HttpContext http
        , IConfiguration configuration
        , ManagementSessionStore sessions
        , CancellationToken cancellationToken
    ) =>
    {
        string relativePath = string.IsNullOrWhiteSpace(path) ? "engineering" : $"engineering/{path}";
        await ProxyToServicesAsync(relativePath, http, configuration, sessions, cancellationToken);
    }
).RequireAuthorization();

app.MapMethods
(
      "/api/engineering/{**path}"
    , proxyMethods
    , async
    (
          string? path
        , HttpContext http
        , IConfiguration configuration
        , ManagementSessionStore sessions
        , CancellationToken cancellationToken
    ) =>
    {
        string relativePath = string.IsNullOrWhiteSpace(path) ? "api/engineering" : $"api/engineering/{path}";
        await ProxyToServicesAsync(relativePath, http, configuration, sessions, cancellationToken);
    }
).RequireAuthorization();

app.MapGet
(
      "/"
    , (HttpContext http) =>
    {
        if (http.User.Identity?.IsAuthenticated != true)
        {
            return Results.Redirect("/login");
        }

        return Results.File
        (
              Path.Combine(app.Environment.WebRootPath, "index.html")
            , "text/html"
        );
    }
);

app.MapFallbackToFile("index.html").RequireAuthorization();

await app.RunAsync();

static async Task<ManagementAuthenticationResponse> AuthenticateWithCommonSecurityAsync
(
      string loginName
    , string password
    , IConfiguration configuration
    , CancellationToken cancellationToken
)
{
    string baseUrl = GetServicesBaseUrl(configuration);
    using HttpClient client = new();
    using HttpResponseMessage response = await client.PostAsJsonAsync
    (
          $"{baseUrl}/api/management/authenticate"
        , new { userName = loginName, password }
        , cancellationToken
    );

    string body = await response.Content.ReadAsStringAsync(cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
        string message = TryReadError(body)
            ?? response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "The username or password was not accepted by Common.Security.",
                HttpStatusCode.Forbidden => "Your account is valid but is not authorized for Cafeteria Management.",
                _ => $"Cafeteria Services returned {(int)response.StatusCode} during authentication."
            };

        throw new ManagementAuthenticationException((int)response.StatusCode, message);
    }

    ManagementAuthenticationResponse? authentication = JsonSerializer.Deserialize<ManagementAuthenticationResponse>
    (
          body
        , new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
    );

    if (authentication is null || string.IsNullOrWhiteSpace(authentication.Token))
    {
        throw new ManagementAuthenticationException
        (
              StatusCodes.Status502BadGateway
            , "Common.Security authentication completed but Cafeteria Services did not return a management session token."
        );
    }

    return authentication;
}

static async Task ProxyToServicesAsync
(
      string path
    , HttpContext http
    , IConfiguration configuration
    , ManagementSessionStore sessions
    , CancellationToken cancellationToken
)
{
    if (!TryGetSession(http, sessions, out ManagementSession? session))
    {
        http.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await http.Response.WriteAsync("Management session has expired. Sign in again.", cancellationToken);
        return;
    }

    string baseUrl = GetServicesBaseUrl(configuration);
    string relativePath = path.TrimStart('/');
    string targetUrl = $"{baseUrl}/{relativePath}{http.Request.QueryString}";

    using HttpClient client = new();
    using HttpRequestMessage request = new(new HttpMethod(http.Request.Method), targetUrl);
    request.Headers.TryAddWithoutValidation("X-Aegis-Management-Token", session.ServiceToken);

    if (http.Request.ContentLength.GetValueOrDefault() > 0)
    {
        request.Content = new StreamContent(http.Request.Body);
        if (!string.IsNullOrWhiteSpace(http.Request.ContentType))
        {
            request.Content.Headers.TryAddWithoutValidation("Content-Type", http.Request.ContentType);
        }
    }

    using HttpResponseMessage response = await client.SendAsync
    (
          request
        , HttpCompletionOption.ResponseHeadersRead
        , cancellationToken
    );

    byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
    string? contentType = response.Content.Headers.ContentType?.ToString();

    http.Response.StatusCode = (int)response.StatusCode;
    if (!string.IsNullOrWhiteSpace(contentType))
    {
        http.Response.ContentType = contentType;
    }

    await http.Response.Body.WriteAsync(body, cancellationToken);
}

static bool TryGetSession
(
      HttpContext http
    , ManagementSessionStore sessions
    , out ManagementSession? session
)
{
    string? sessionId = http.User.FindFirst(ManagementSessionClaim)?.Value;
    if (string.IsNullOrWhiteSpace(sessionId))
    {
        session = null;
        return false;
    }

    return sessions.TryGet(sessionId, out session);
}

static string GetServicesBaseUrl(IConfiguration configuration)
{
    return (configuration["CafeteriaServices:BaseUrl"] ?? "http://localhost:5088").TrimEnd('/');
}

static string? TryReadError(string body)
{
    if (string.IsNullOrWhiteSpace(body))
    {
        return null;
    }

    try
    {
        using JsonDocument document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("error", out JsonElement error))
        {
            return error.GetString();
        }
    }
    catch (JsonException)
    {
    }

    return null;
}

internal sealed class ManagementSessionStore(TimeSpan sessionLifetime)
{
    private readonly ConcurrentDictionary<string, ManagementSession> sessions = new(StringComparer.Ordinal);

    public string Create(ManagementAuthenticationResponse authentication)
    {
        string sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.Add(sessionLifetime);
        if (authentication.ExpiresAt > DateTimeOffset.UtcNow && authentication.ExpiresAt < expiresAt)
        {
            expiresAt = authentication.ExpiresAt;
        }

        ManagementSession session = new
        (
              authentication.Token
            , authentication.UserName
            , authentication.DisplayName
            , expiresAt
        );
        sessions[sessionId] = session;
        return sessionId;
    }

    public bool TryGet(string sessionId, out ManagementSession? session)
    {
        if (!sessions.TryGetValue(sessionId, out session))
        {
            return false;
        }

        if (session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            sessions.TryRemove(sessionId, out _);
            session = null;
            return false;
        }

        return true;
    }

    public void Remove(string sessionId)
    {
        sessions.TryRemove(sessionId, out _);
    }
}

internal sealed record ManagementSession
(
      string ServiceToken
    , string UserName
    , string DisplayName
    , DateTimeOffset ExpiresAt
);

internal sealed record ManagementAuthenticationResponse
(
      string Token
    , string UserName
    , string DisplayName
    , DateTimeOffset ExpiresAt
    , string AuthenticationSource
);

internal sealed class ManagementAuthenticationException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

internal static class ManagementLoginPage
{
    public static string Build(string? error = null)
    {
        string errorHtml = string.IsNullOrWhiteSpace(error)
            ? string.Empty
            : $"<div class=\"error\">{WebUtility.HtmlEncode(error)}</div>";

        return $$"""
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width,initial-scale=1" />
<title>Cafeteria Management - Sign In</title>
<style>
*{box-sizing:border-box}body{margin:0;min-height:100vh;display:grid;place-items:center;background:#eef3f7;font-family:Segoe UI,Arial,sans-serif;color:#263746}.card{width:min(470px,92vw);background:#fff;border-radius:16px;box-shadow:0 18px 50px rgba(23,48,70,.16);overflow:hidden}.head{background:#164269;color:white;padding:28px 32px;border-bottom:7px solid #eb8b28}.head h1{margin:0;font-size:25px}.head p{margin:8px 0 0;color:#dce8f1}.body{padding:32px}.body h2{margin:0 0 10px;font-size:20px}.body p{color:#60717f;line-height:1.5}label{display:block;margin-top:18px;font-weight:650;color:#334c60}input{width:100%;margin-top:7px;padding:12px 13px;border:1px solid #c8d2da;border-radius:8px;font:inherit}input:focus{outline:3px solid rgba(235,139,40,.22);border-color:#eb8b28}.signin{display:block;width:100%;margin-top:24px;padding:13px 18px;border:0;border-radius:8px;background:#164269;color:#fff;text-align:center;font-weight:700;font-size:15px;cursor:pointer}.note{font-size:12px;margin-top:18px;color:#7b8994}.error{margin:16px 0 0;padding:11px 13px;border-radius:8px;background:#fff0f0;color:#8a2929;border:1px solid #efc1c1}
</style>
</head>
<body>
<div class="card">
  <div class="head"><h1>Cafeteria Management</h1><p>Food For The Poor Jamaica</p></div>
  <div class="body">
    <h2>Management Sign In</h2>
    <p>Enter your FFP network credentials. Authentication is performed by Common.Security over the configured Active Directory LDAPS servers.</p>
    {{errorHtml}}
    <form method="post" action="/login">
      <label>Username<input name="username" autocomplete="username" autofocus required placeholder="username, FFPJM\\username or username@fftpj.local" /></label>
      <label>Password<input name="password" type="password" autocomplete="current-password" required /></label>
      <button class="signin" type="submit">Sign In</button>
    </form>
    <div class="note">Your password is used only for the Common.Security authentication exchange. After sign-in, the console stores only a short-lived opaque management token.</div>
  </div>
</div>
</body>
</html>
""";
    }
}

using Common.Security.Models;
using System.Net;

namespace Aegis.Cafeteria.Services;

internal static class ManagementConsoleEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapGet("/management", () => Results.Redirect("/management/"));

        app.MapGet("/management/login", (HttpContext http) =>
            http.User.IsInRole("CafeteriaManager")
                ? Results.Redirect("/management/")
                : Results.Content(BuildLoginPage(), "text/html"));

        app.MapPost("/management/login", SignInAsync);
        app.MapPost("/management/logout", SignOutAsync);

        app.MapGet("/management/api/session", GetSession)
            .RequireAuthorization("CafeteriaManagement");

        app.MapGet("/management/", (HttpContext http) =>
            http.User.IsInRole("CafeteriaManager")
                ? ManagementFile(app, "index.html", "text/html")
                : Results.Redirect("/management/login"));
        app.MapGet("/management/app.js", () => ManagementFile(app, "app.js", "text/javascript"))
            .RequireAuthorization("CafeteriaManagement");
        app.MapGet("/management/styles.css", () => ManagementFile(app, "styles.css", "text/css"))
            .RequireAuthorization("CafeteriaManagement");
    }

    private static async Task<IResult> SignInAsync(
        HttpContext http,
        ManagementCommonSecurityAuthenticationRuntime runtime,
        CancellationToken cancellationToken)
    {
        if (!http.Request.HasFormContentType)
        {
            return Results.Content(BuildLoginPage("Enter your username and password to sign in."), "text/html", statusCode: StatusCodes.Status400BadRequest);
        }

        IFormCollection form = await http.Request.ReadFormAsync(cancellationToken);
        string loginName = form["username"].ToString().Trim();
        string password = form["password"].ToString();

        if (string.IsNullOrWhiteSpace(loginName) || string.IsNullOrWhiteSpace(password))
        {
            return Results.Content(BuildLoginPage("Username and password are required."), "text/html", statusCode: StatusCodes.Status400BadRequest);
        }

        AuthenticationResult result;
        try
        {
            result = runtime.Authenticate(loginName, password);
        }
        catch
        {
            return Results.Content(BuildLoginPage("The authentication service could not process the request."), "text/html", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!result.IsAuthenticated || result.UserInfo is null)
        {
            return Results.Content(BuildLoginPage("The username or password was not accepted by Common.Security."), "text/html", statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!runtime.IsManager(result))
        {
            return Results.Content(BuildLoginPage("Your account is valid but is not authorized for Cafeteria Management."), "text/html", statusCode: StatusCodes.Status403Forbidden);
        }

        string userName = NormalizeUserName(loginName);
        string displayName = string.IsNullOrWhiteSpace(result.UserInfo.DisplayName)
            ? userName
            : result.UserInfo.DisplayName.Trim();

        ManagementServiceTokenSession session = runtime.CreateSession(userName, displayName, isManager: true);

        http.Response.Cookies.Append(
            ManagementCommonSecurityAuthenticationExtensions.ManagementConsoleCookieName,
            session.Token,
            new CookieOptions
            {
                  HttpOnly = true
                , Secure = http.Request.IsHttps
                , SameSite = SameSiteMode.Strict
                , Path = "/"
                , Expires = session.ExpiresAt
            });

        return Results.Redirect("/management/");
    }

    private static IResult SignOutAsync(
        HttpContext http,
        ManagementCommonSecurityAuthenticationRuntime runtime)
    {
        if (http.Request.Cookies.TryGetValue(
            ManagementCommonSecurityAuthenticationExtensions.ManagementConsoleCookieName,
            out string? token)
            && !string.IsNullOrWhiteSpace(token))
        {
            runtime.RemoveSession(token);
        }

        http.Response.Cookies.Delete(
            ManagementCommonSecurityAuthenticationExtensions.ManagementConsoleCookieName,
            new CookieOptions
            {
                  Secure = http.Request.IsHttps
                , SameSite = SameSiteMode.Strict
                , Path = "/"
            });

        return Results.Redirect("/management/login");
    }

    private static IResult GetSession(HttpContext http)
    {
        string userName = http.User.Identity?.Name ?? string.Empty;
        string displayName = http.User.FindFirst("aegis_display_name")?.Value ?? userName;

        return Results.Ok(new
        {
              authenticated = true
            , userName
            , displayName
            , authenticationSource = "Common.Security"
        });
    }

    private static IResult ManagementFile(WebApplication app, string fileName, string contentType)
    {
        string path = Path.Combine(app.Environment.WebRootPath ?? string.Empty, "management", fileName);
        return Results.File(path, contentType);
    }

    private static string NormalizeUserName(string loginName)
    {
        string value = loginName.Trim();
        int slash = value.LastIndexOf('\');
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

    private static string BuildLoginPage(string? error = null)
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
    <form method="post" action="/management/login">
      <label>Username<input name="username" autocomplete="username" autofocus required placeholder="username, FFPJM\username or username@fftpj.local" /></label>
      <label>Password<input name="password" type="password" autocomplete="current-password" required /></label>
      <button class="signin" type="submit">Sign In</button>
    </form>
    <div class="note">Your password is used only for the Common.Security authentication exchange. The browser receives only a short-lived HttpOnly management session cookie.</div>
  </div>
</div>
</body>
</html>
""";
    }
}

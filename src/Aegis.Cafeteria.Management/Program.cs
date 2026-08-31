using System.Net;
using System.Security.Principal;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
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

        return Results.Content
        (
              ManagementLoginPage.Html
            , "text/html"
        );
    }
);

app.MapGet
(
      "/signin"
    , async (HttpContext http) =>
    {
        if (http.User.Identity?.IsAuthenticated == true)
        {
            http.Response.Redirect("/");
            return;
        }

        await http.ChallengeAsync(NegotiateDefaults.AuthenticationScheme);
    }
);

app.MapGet
(
      "/api/session"
    , (HttpContext http) => Results.Ok
    (
        new
        {
              authenticated = http.User.Identity?.IsAuthenticated == true
            , userName = http.User.Identity?.Name
        }
    )
).RequireAuthorization();

app.MapGet
(
      "/api/config"
    , (IConfiguration configuration) => Results.Ok
    (
        new
        {
            servicesBaseUrl = configuration["CafeteriaServices:BaseUrl"] ?? "http://localhost:5088"
        }
    )
).RequireAuthorization();

app.MapMethods
(
      "/api/services/{**path}"
    , ["GET", "POST", "PUT", "DELETE", "PATCH"]
    , async
    (
          string? path
        , HttpContext http
        , IConfiguration configuration
        , CancellationToken cancellationToken
    ) =>
    {
        string baseUrl = (configuration["CafeteriaServices:BaseUrl"] ?? "http://localhost:5088").TrimEnd('/');
        string relativePath = string.IsNullOrWhiteSpace(path) ? string.Empty : path.TrimStart('/');
        string targetUrl = $"{baseUrl}/{relativePath}{http.Request.QueryString}";

        WindowsIdentity? windowsIdentity = http.User.Identity as WindowsIdentity;
        if (windowsIdentity is null || !windowsIdentity.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        HttpResponseMessage response = await WindowsIdentity.RunImpersonatedAsync
        (
              windowsIdentity.AccessToken
            , async () =>
            {
                using HttpClientHandler handler = new()
                {
                    UseDefaultCredentials = true
                };
                using HttpClient client = new(handler);
                using HttpRequestMessage request = new(new HttpMethod(http.Request.Method), targetUrl);

                if (http.Request.ContentLength.GetValueOrDefault() > 0)
                {
                    request.Content = new StreamContent(http.Request.Body);
                    if (!string.IsNullOrWhiteSpace(http.Request.ContentType))
                    {
                        request.Content.Headers.TryAddWithoutValidation("Content-Type", http.Request.ContentType);
                    }
                }

                return await client.SendAsync
                (
                      request
                    , HttpCompletionOption.ResponseHeadersRead
                    , cancellationToken
                );
            }
        );

        await using (response)
        {
            byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            string? contentType = response.Content.Headers.ContentType?.ToString();

            return Results.File
            (
                  body
                , contentType ?? "application/json"
                , statusCode: (int)response.StatusCode
            );
        }
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

internal static class ManagementLoginPage
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width,initial-scale=1" />
<title>Cafeteria Management - Sign In</title>
<style>
*{box-sizing:border-box}body{margin:0;min-height:100vh;display:grid;place-items:center;background:#eef3f7;font-family:Segoe UI,Arial,sans-serif;color:#263746}.card{width:min(470px,92vw);background:#fff;border-radius:16px;box-shadow:0 18px 50px rgba(23,48,70,.16);overflow:hidden}.head{background:#164269;color:white;padding:28px 32px;border-bottom:7px solid #eb8b28}.head h1{margin:0;font-size:25px}.head p{margin:8px 0 0;color:#dce8f1}.body{padding:32px}.body h2{margin:0 0 10px;font-size:20px}.body p{color:#60717f;line-height:1.5}.signin{display:block;width:100%;margin-top:24px;padding:13px 18px;border:0;border-radius:8px;background:#164269;color:#fff;text-decoration:none;text-align:center;font-weight:700;font-size:15px}.note{font-size:12px;margin-top:18px;color:#7b8994}
</style>
</head>
<body>
<div class="card">
  <div class="head"><h1>Cafeteria Management</h1><p>Food For The Poor Jamaica</p></div>
  <div class="body">
    <h2>Management Sign In</h2>
    <p>Use your FFP Windows account to access cafeteria management functions. Your Windows identity is used for authorization and audit records.</p>
    <a class="signin" href="/signin">Sign in with FFP Windows Account</a>
    <div class="note">Access is limited to authorized cafeteria management users.</div>
  </div>
</div>
</body>
</html>
""";
}

using System.Net;
using System.Security.Principal;

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

app.MapFallbackToFile("index.html").RequireAuthorization();

await app.RunAsync();

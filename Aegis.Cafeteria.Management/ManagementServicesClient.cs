using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Aegis.Cafeteria.Management;

internal static class ManagementServicesClient
{
    public static async Task<ManagementAuthenticationResponse> AuthenticateAsync(string loginName, string password, IConfiguration configuration, CancellationToken cancellationToken)
    {
        string baseUrl = GetBaseUrl(configuration);
        using HttpClient client = new();
        using HttpResponseMessage response = await client.PostAsJsonAsync($"{baseUrl}/api/management/authenticate", new { userName = loginName, password }, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string message = TryReadError(body) ?? response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "The username or password was not accepted by Common.Security.",
                HttpStatusCode.Forbidden => "Your account is valid but is not authorized for Cafeteria Management.",
                _ => $"Cafeteria Services returned {(int)response.StatusCode} during authentication."
            };
            throw new ManagementAuthenticationException((int)response.StatusCode, message);
        }

        ManagementAuthenticationResponse? authentication = JsonSerializer.Deserialize<ManagementAuthenticationResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (authentication is null || string.IsNullOrWhiteSpace(authentication.Token))
            throw new ManagementAuthenticationException(StatusCodes.Status502BadGateway, "Common.Security authentication completed but Cafeteria Services did not return a management session token.");

        return authentication;
    }

    public static async Task ProxyAsync(string path, HttpContext http, IConfiguration configuration, ManagementSessionStore sessions, CancellationToken cancellationToken)
    {
        if (!ManagementSessionAccessor.TryGet(http, sessions, out ManagementSession? session))
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await http.Response.WriteAsync("Management session has expired. Sign in again.", cancellationToken);
            return;
        }

        string targetUrl = $"{GetBaseUrl(configuration)}/{path.TrimStart('/')}{http.Request.QueryString}";
        using HttpClient client = new();
        using HttpRequestMessage request = new(new HttpMethod(http.Request.Method), targetUrl);
        request.Headers.TryAddWithoutValidation("X-Aegis-Management-Token", session!.ServiceToken);

        if (http.Request.ContentLength.GetValueOrDefault() > 0)
        {
            request.Content = new StreamContent(http.Request.Body);
            if (!string.IsNullOrWhiteSpace(http.Request.ContentType)) request.Content.Headers.TryAddWithoutValidation("Content-Type", http.Request.ContentType);
        }

        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        http.Response.StatusCode = (int)response.StatusCode;
        string? contentType = response.Content.Headers.ContentType?.ToString();
        if (!string.IsNullOrWhiteSpace(contentType)) http.Response.ContentType = contentType;
        await http.Response.Body.WriteAsync(body, cancellationToken);
    }

    private static string GetBaseUrl(IConfiguration configuration) => (configuration["CafeteriaServices:BaseUrl"] ?? "http://localhost:5088").TrimEnd('/');

    private static string? TryReadError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out JsonElement error) ? error.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

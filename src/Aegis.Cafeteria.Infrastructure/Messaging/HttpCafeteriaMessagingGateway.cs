namespace Aegis.Cafeteria.Infrastructure.Messaging;

using Aegis.Cafeteria.Application.Messaging;
using Aegis.Cafeteria.Infrastructure.Configuration;
using System.Net.Http.Headers;
using System.Net.Http.Json;

public sealed class HttpCafeteriaMessagingGateway(HttpClient http, CafeteriaMessagingOptions options) : ICafeteriaMessagingGateway
{
    public async Task SendAsync(CafeteriaEmployeeMessage message, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled) return;
        if (string.IsNullOrWhiteSpace(options.Endpoint)) throw new InvalidOperationException("Messaging:Endpoint is required when weekly messaging is enabled.");
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint) { Content = JsonContent.Create(message) };
        if (!string.IsNullOrWhiteSpace(options.BearerToken)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.BearerToken);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

public sealed class DisabledCafeteriaMessagingGateway : ICafeteriaMessagingGateway
{
    public Task SendAsync(CafeteriaEmployeeMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

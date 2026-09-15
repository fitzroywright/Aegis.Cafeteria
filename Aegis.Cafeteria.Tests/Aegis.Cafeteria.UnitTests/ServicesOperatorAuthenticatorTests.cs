using System.Net;
using System.Text;
using Aegis.Cafeteria.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aegis.Cafeteria.UnitTests;

public sealed class ServicesOperatorAuthenticatorTests
{
    [Fact]
    public async Task AuthenticateAsync_Unauthorized_ReturnsNullForRejectedCredentials()
    {
        ServicesOperatorAuthenticator sut = CreateAuthenticator(
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await sut.AuthenticateAsync("fwright", "wrong-password");

        Assert.Null(result);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task AuthenticateAsync_ServiceHttpFailure_DoesNotReportBadCredentials(HttpStatusCode statusCode)
    {
        ServicesOperatorAuthenticator sut = CreateAuthenticator(
            _ => new HttpResponseMessage(statusCode));

        CafeteriaServicesAuthenticationException exception = await Assert.ThrowsAsync<CafeteriaServicesAuthenticationException>(
            () => sut.AuthenticateAsync("fwright", "password"));

        Assert.Contains("credentials", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(((int)statusCode).ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthenticateAsync_TransportFailure_DoesNotReportBadCredentials()
    {
        var session = new PosServicesSession();
        using HttpClient http = new(new ThrowingHandler())
        {
            BaseAddress = new Uri("http://localhost:5088/")
        };
        var sut = new ServicesOperatorAuthenticator(
            http,
            session,
            NullLogger<ServicesOperatorAuthenticator>.Instance);

        CafeteriaServicesAuthenticationException exception = await Assert.ThrowsAsync<CafeteriaServicesAuthenticationException>(
            () => sut.AuthenticateAsync("fwright", "password"));

        Assert.Contains("could not be reached", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(session.Token);
    }

    [Fact]
    public async Task AuthenticateAsync_InvalidSuccessPayload_IsServiceFailure()
    {
        ServicesOperatorAuthenticator sut = CreateAuthenticator(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });

        await Assert.ThrowsAsync<CafeteriaServicesAuthenticationException>(
            () => sut.AuthenticateAsync("fwright", "password"));
    }

    [Fact]
    public async Task AuthenticateAsync_ValidResponse_ReturnsIdentityAndStoresToken()
    {
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        var session = new PosServicesSession();
        using HttpClient http = new(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"token":"ABC123","userName":"fwright","displayName":"Fitzroy Wright","isManager":true,"expiresAt":"{{expiresAt:O}}","authenticationSource":"Common.Security"}""",
                Encoding.UTF8,
                "application/json")
        }))
        {
            BaseAddress = new Uri("http://localhost:5088/")
        };
        var sut = new ServicesOperatorAuthenticator(
            http,
            session,
            NullLogger<ServicesOperatorAuthenticator>.Instance);

        var result = await sut.AuthenticateAsync("fwright", "password");

        Assert.NotNull(result);
        Assert.Equal("fwright", result.UserId);
        Assert.Equal("Fitzroy Wright", result.DisplayName);
        Assert.True(result.IsManager);
        Assert.Equal("ABC123", session.Token);
    }

    private static ServicesOperatorAuthenticator CreateAuthenticator(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var session = new PosServicesSession();
        var http = new HttpClient(new StubHandler(responder))
        {
            BaseAddress = new Uri("http://localhost:5088/")
        };
        return new ServicesOperatorAuthenticator(
            http,
            session,
            NullLogger<ServicesOperatorAuthenticator>.Instance);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new HttpRequestException("Synthetic connection failure.");
    }
}

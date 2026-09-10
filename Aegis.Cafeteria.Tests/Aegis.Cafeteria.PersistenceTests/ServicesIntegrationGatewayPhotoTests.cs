namespace Aegis.Cafeteria.PersistenceTests;

using System.Net;
using System.Net.Http.Json;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Services;

public sealed class ServicesIntegrationGatewayPhotoTests
{
    [Fact]
    public async Task GetCustomersAsync_RetriesColdAllNullPhotoResponse_AndReturnsWarmPhoto()
    {
        byte[] expectedPhoto = [10, 20, 30, 40];
        int requests = 0;
        using HttpClient client = new
        (
            new StubHandler
            (
                async (request, cancellationToken) =>
                {
                    Assert.EndsWith("api/ffp/customers", request.RequestUri?.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
                    requests++;
                    ExternalCustomerSnapshot[] response =
                    [
                        new ExternalCustomerSnapshot
                        {
                              CustomerId = 101
                            , Name = "Jane Doe"
                            , IsEligibleForCafeteriaCredit = true
                            , Photo = requests == 1 ? null : expectedPhoto
                        }
                    ];
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(response)
                    };
                }
            )
        )
        {
            BaseAddress = new Uri("http://cafeteria-services/")
        };

        ServicesIntegrationGateway gateway = new(client);
        IReadOnlyList<ExternalCustomerSnapshot> customers = await gateway.GetCustomersAsync();

        ExternalCustomerSnapshot customer = Assert.Single(customers);
        Assert.Equal(expectedPhoto, customer.Photo);
        Assert.True(requests >= 2);
    }

    [Fact]
    public async Task GetCustomersAsync_DoesNotRetryWhenFirstResponseAlreadyContainsPhoto()
    {
        int requests = 0;
        using HttpClient client = new
        (
            new StubHandler
            (
                async (request, cancellationToken) =>
                {
                    requests++;
                    ExternalCustomerSnapshot[] response =
                    [
                        new ExternalCustomerSnapshot
                        {
                              CustomerId = 101
                            , Name = "Jane Doe"
                            , IsEligibleForCafeteriaCredit = true
                            , Photo = [1, 2, 3]
                        }
                    ];
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(response)
                    };
                }
            )
        )
        {
            BaseAddress = new Uri("http://cafeteria-services/")
        };

        ServicesIntegrationGateway gateway = new(client);
        IReadOnlyList<ExternalCustomerSnapshot> customers = await gateway.GetCustomersAsync();

        Assert.NotNull(Assert.Single(customers).Photo);
        Assert.Equal(1, requests);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }
}

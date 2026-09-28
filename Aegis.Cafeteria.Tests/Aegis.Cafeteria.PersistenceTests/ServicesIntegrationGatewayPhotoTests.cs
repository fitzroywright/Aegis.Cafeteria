namespace Aegis.Cafeteria.PersistenceTests;

using System.Net;
using System.Net.Http.Json;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Services;

public sealed class ServicesIntegrationGatewayPhotoTests
{
    [Fact]
    public async Task GetCustomersAsync_RefreshesMissingPhotoOnNextRead()
    {
        byte[] expectedPhoto = [10, 20, 30, 40];
        int requests = 0;
        using HttpClient client = new
        (
            new StubHandler
            (
                (request, cancellationToken) =>
                {
                    Assert.True(request.RequestUri?.AbsoluteUri.EndsWith("api/reference/customers/batch?take=10", StringComparison.OrdinalIgnoreCase) == true);
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
                    return Task.FromResult
                    (
                        new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = JsonContent.Create(new CustomerReferenceBatch(response, null))
                        }
                    );
                }
            )
        )
        {
            BaseAddress = new Uri("http://cafeteria-services/")
        };

        ServicesIntegrationGateway gateway = new(client);
        IReadOnlyList<ExternalCustomerSnapshot> customers = await gateway.GetCustomersAsync();

        Assert.Null(Assert.Single(customers).Photo);
        Assert.Equal(1, requests);

        customers = await gateway.GetCustomersAsync();
        ExternalCustomerSnapshot customer = Assert.Single(customers);
        Assert.Equal(expectedPhoto, customer.Photo);
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task GetCustomersAsync_DoesNotRetryWhenFirstResponseAlreadyContainsPhoto()
    {
        int requests = 0;
        using HttpClient client = new
        (
            new StubHandler
            (
                (request, cancellationToken) =>
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
                    return Task.FromResult
                    (
                        new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = JsonContent.Create(new CustomerReferenceBatch(response, null))
                        }
                    );
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

    [Fact]
    public async Task GetCustomersAsync_FollowsBatchCursor_AndPreservesPhotos()
    {
        int requests = 0;
        using HttpClient client = new(new StubHandler((request, cancellationToken) =>
        {
            requests++;
            string expectedPath = requests == 1
                ? "/api/reference/customers/batch?take=10"
                : "/api/reference/customers/batch?take=10&afterCustomerId=101";
            Assert.Equal(expectedPath, request.RequestUri!.PathAndQuery);
            CustomerReferenceBatch batch = requests == 1
                ? new([new ExternalCustomerSnapshot { CustomerId = 101, Photo = [1, 2, 3] }], 101)
                : new([new ExternalCustomerSnapshot { CustomerId = 102, Photo = [4, 5, 6] }], null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(batch)
            });
        }))
        {
            BaseAddress = new Uri("http://cafeteria-services/")
        };

        IReadOnlyList<ExternalCustomerSnapshot> customers = await new ServicesIntegrationGateway(client).GetCustomersAsync();

        Assert.Equal(2, requests);
        Assert.Collection(customers,
            customer => { Assert.Equal(101, customer.CustomerId); Assert.Equal(new byte[] { 1, 2, 3 }, customer.Photo); },
            customer => { Assert.Equal(102, customer.CustomerId); Assert.Equal(new byte[] { 4, 5, 6 }, customer.Photo); });
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }
}

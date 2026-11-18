namespace Aegis.Cafeteria.PersistenceTests;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Services;
using Aegis.Cafeteria.Services.Persistence;
using Aegis.Cafeteria.Services.Realtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public sealed class ProductMigrationEndpointTests
{
    [Fact]
    public async Task RejectsReservedProductsBeforeWritingAnyEventsAndAcceptsSaleableProducts()
    {
        string file = SqliteTestDatabase.CreatePath("product-migration");
        try
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.Configuration["Migration:Enabled"] = "true";
            builder.WebHost.UseTestServer();
            builder.Services.AddDbContextFactory<ServiceDbContext>(options => options.UseSqlite($"Data Source={file}"));
            builder.Services.AddSignalR();
            builder.Services.AddSingleton<ServiceEventRealtimePublisher>();
            builder.Services.AddSingleton<CafeteriaOperationsFlowPublisher>();
            builder.Services.AddSingleton<ServiceEventStore>();
            builder.Services.AddAuthorization(options => options.AddPolicy("CafeteriaManagement", policy => policy.RequireAuthenticatedUser()));

            await using WebApplication app = builder.Build();
            app.Use((context, next) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Test Manager")], "Test"));
                return next(context);
            });
            app.UseAuthorization();
            MigrationEndpoints.Map(app);
            await app.StartAsync();

            var factory = app.Services.GetRequiredService<IDbContextFactory<ServiceDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            await db.Database.EnsureCreatedAsync();
            using HttpClient client = app.GetTestClient();

            string[] reservedCodes = [.. Enumerable.Range(0, 10).Select(i => $"FFP{i:D2}"),
                "ffp01", "fFp02", "  FFP03  ", "\t\u00A0fFp04\r\n", "FFP-JUICE", "FFP"];
            var lunch = new MigrationEndpoints.ProductMigrationItem(42, " LUNCH ", "Lunch", 700m);
            var reserved = reservedCodes.Select((code, index) =>
                new MigrationEndpoints.ProductMigrationItem(100 + index, code, "Reserved", 1m)).ToArray();

            using var rejected = await client.PostAsJsonAsync("/api/migration/products",
                new MigrationEndpoints.ProductMigrationRequest([lunch, .. reserved, new(0, "FFP-X", "", -1m)]));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            var problem = Assert.IsType<ValidationProblemDetails>(await rejected.Content.ReadFromJsonAsync<ValidationProblemDetails>());
            Assert.Equal(reserved.Length + 1, problem.Errors.Count);
            for (int index = 1; index <= reserved.Length + 1; index++)
                Assert.Equal("Product codes starting with FFP are reserved.", Assert.Single(problem.Errors[$"products[{index}].code"]));
            Assert.Empty(await db.Events.ToArrayAsync());
            Assert.Empty(await db.AuditTrail.ToArrayAsync());

            using var accepted = await client.PostAsJsonAsync("/api/migration/products",
                new MigrationEndpoints.ProductMigrationRequest([lunch, new(43, "JUICE-FFP", "Juice", 200m)]));
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            var events = await db.Events.OrderBy(row => row.Sequence).ToArrayAsync();
            Assert.All(events, row => Assert.Equal((int)CafeteriaServiceEventKind.ProductCatalogImport, row.Kind));
            Assert.Equal(new[] { "LUNCH", "JUICE-FFP" }, events.Select(row =>
                JsonSerializer.Deserialize<ProductCatalogImportServiceEvent>(row.PayloadJson)!.SourceCode));
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }
}

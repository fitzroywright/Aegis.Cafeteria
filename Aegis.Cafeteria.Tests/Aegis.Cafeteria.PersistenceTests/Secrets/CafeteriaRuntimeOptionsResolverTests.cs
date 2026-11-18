namespace Aegis.Cafeteria.PersistenceTests.Secrets;
using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

public sealed class CafeteriaRuntimeOptionsResolverTests
{
    [Fact]
    public async Task Resolve_UsesLocalOnlyPersistenceAndHasNoLocalOrCodedDailyRateSetting()
    {
        var values = new Dictionary<string,string?>
        {
            ["Persistence:LocalSqliteConnectionString"] = "Data Source=test.db",
            ["Persistence:TerminalId"] = "POS-1",
            ["FfpIntegration:Enabled"] = "false"
        };
        var c = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var result = await CafeteriaRuntimeOptionsResolver.ResolveAsync(c, new StubSecrets());
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "test.db"), new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(result.Persistence.LocalSqliteConnectionString).DataSource);
        Assert.Equal("POS-1", result.Persistence.TerminalId);
        Assert.DoesNotContain(typeof(CafeteriaRuntimeOptions).GetProperties(), x => x.Name == "Policy");
        var props = typeof(CafeteriaPersistenceOptions).GetProperties().Select(x=>x.Name).ToHashSet();
        Assert.DoesNotContain("CentralConnectionString", props);
        Assert.DoesNotContain("CentralProvider", props);
    }


    [Fact]
    public async Task Resolve_ServiceMode_UsesBitwardenApiKeyAndDoesNotRequestFfpDatabaseSecret()
    {
        var values = new Dictionary<string,string?>
        {
            ["CafeteriaServices:Enabled"] = "true",
            ["CafeteriaServices:ApiKey"] = "",
            ["CafeteriaServices:BaseUrl"] = "https://cafeteria-services.fftpj.local"
        };
        var c = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var secrets = new StubSecrets(new Dictionary<string,string?>
        {
            [ApplicationSecretNames.CafeteriaServicesApiKey] = "service-secret"
        });

        var result = await CafeteriaRuntimeOptionsResolver.ResolveAsync(c, secrets);

        Assert.True(result.Services.Enabled);
        Assert.Equal("service-secret", result.Services.ApiKey);
        Assert.Contains(ApplicationSecretNames.CafeteriaServicesApiKey, secrets.Requested);
    }

    [Fact]
    public async Task Resolve_ServiceMode_ConfiguredApiKeyOverridesSecretLookup()
    {
        var values = new Dictionary<string,string?>
        {
            ["CafeteriaServices:Enabled"] = "true",
            ["CafeteriaServices:ApiKey"] = "configured-key"
        };
        var c = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var secrets = new StubSecrets();

        var result = await CafeteriaRuntimeOptionsResolver.ResolveAsync(c, secrets);

        Assert.Equal("configured-key", result.Services.ApiKey);
        Assert.DoesNotContain(ApplicationSecretNames.CafeteriaServicesApiKey, secrets.Requested);
    }

    private sealed class StubSecrets : IApplicationSecretProvider
    {
        private readonly IReadOnlyDictionary<string,string?> values;
        public List<string> Requested { get; } = [];
        public StubSecrets(IReadOnlyDictionary<string,string?>? values = null)
            => this.values = values ?? new Dictionary<string,string?>();
        public Task<string?> GetSecretAsync(string name,CancellationToken cancellationToken=default)
        {
            Requested.Add(name);
            return Task.FromResult(values.GetValueOrDefault(name));
        }
    }
}

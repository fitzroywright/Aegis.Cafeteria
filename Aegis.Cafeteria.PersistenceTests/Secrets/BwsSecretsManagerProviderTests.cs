namespace Aegis.Cafeteria.PersistenceTests.Secrets;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Secrets;

public sealed class BwsSecretsManagerProviderTests
{
    [Fact]
    public async Task GetSecret_UsesConfiguredIdAndReturnsValue()
    {
        const string env = "AEGIS_TEST_BWS_TOKEN_1";
        Environment.SetEnvironmentVariable(env, "bootstrap-token");
        try
        {
            var id = Guid.NewGuid().ToString();
            var executor = new FakeExecutor($"{{\"id\":\"{id}\",\"value\":\"Server=test;Password=secret\"}}");
            IApplicationSecretProvider provider = new BwsSecretsManagerProvider(new BitwardenSecretsManagerOptions
            {
                AccessTokenEnvironmentVariable = env,
                ServerUrl = "https://bitwarden.example.org",
                SecretIds = new Dictionary<string, string> { ["MessagingBearerToken"] = id }
            }, executor);

            var value = await provider.GetRequiredSecretAsync("MessagingBearerToken");

            Assert.Equal("Server=test;Password=secret", value);
            Assert.Contains("--server-url", executor.LastArguments);
            Assert.Contains("https://bitwarden.example.org", executor.LastArguments);
            Assert.Contains(id, executor.LastArguments);
            Assert.Equal("bootstrap-token", executor.LastAccessToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable(env, null);
        }
    }

    [Fact]
    public async Task GetSecret_CachesValueInMemory()
    {
        const string env = "AEGIS_TEST_BWS_TOKEN_2";
        Environment.SetEnvironmentVariable(env, "bootstrap-token");
        try
        {
            var id = Guid.NewGuid().ToString();
            var executor = new FakeExecutor($"[{{\"id\":\"{id}\",\"value\":\"value-1\"}}]");
            IApplicationSecretProvider provider = new BwsSecretsManagerProvider(new BitwardenSecretsManagerOptions
            {
                AccessTokenEnvironmentVariable = env,
                SecretIds = new Dictionary<string, string> { ["X"] = id }
            }, executor);

            Assert.Equal("value-1", await provider.GetRequiredSecretAsync("X"));
            Assert.Equal("value-1", await provider.GetRequiredSecretAsync("X"));
            Assert.Equal(1, executor.CallCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable(env, null);
        }
    }

    [Fact]
    public async Task GetSecret_MissingBootstrapTokenFailsWithoutInvokingCli()
    {
        const string env = "AEGIS_TEST_BWS_TOKEN_MISSING";
        Environment.SetEnvironmentVariable(env, null);
        var executor = new FakeExecutor("{}");
        IApplicationSecretProvider provider = new BwsSecretsManagerProvider(new BitwardenSecretsManagerOptions
        {
            AccessTokenEnvironmentVariable = env,
            SecretIds = new Dictionary<string, string> { ["X"] = Guid.NewGuid().ToString() }
        }, executor);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRequiredSecretAsync("X"));
        Assert.Contains(env, ex.Message);
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public void ParseSecretValue_RejectsMissingValue()
    {
        Assert.Throws<InvalidOperationException>(() =>
            BwsSecretsManagerProvider.ParseSecretValue("{\"id\":\"abc\"}", "X"));
    }

    private sealed class FakeExecutor(string output) : IBwsCliExecutor
    {
        public int CallCount { get; private set; }
        public IReadOnlyList<string> LastArguments { get; private set; } = [];
        public string LastAccessToken { get; private set; } = string.Empty;

        public Task<BwsCommandResult> ExecuteAsync(
            string executable,
            IReadOnlyList<string> arguments,
            string accessToken,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastArguments = arguments.ToArray();
            LastAccessToken = accessToken;
            return Task.FromResult(new BwsCommandResult(0, output, string.Empty));
        }
    }
}

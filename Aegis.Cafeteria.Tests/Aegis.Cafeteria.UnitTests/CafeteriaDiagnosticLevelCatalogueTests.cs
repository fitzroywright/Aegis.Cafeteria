using Aegis.Cafeteria.Services.Engineering.Diagnostics;
using Common.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace Aegis.Cafeteria.UnitTests;

public sealed class CafeteriaDiagnosticLevelCatalogueTests
{
    private static IDiagnosticLevelLocalTest[] Catalogue() =>
    [
        new CafeteriaDatabaseConnectivityDiagnosticLevelTest(null!),
        new CafeteriaDatabaseIntegrityDiagnosticLevelTest(null!),
        new CafeteriaConfigurationDiagnosticLevelTest(null!),
        new CafeteriaRuntimeIdentityDiagnosticLevelTest(null!, null!),
        new CafeteriaDiagnosticStoreIntegrityDiagnosticLevelTest(null!),
        new CafeteriaWorkforceSourceDiagnosticLevelTest(null!),
        new CafeteriaHolidaySourceDiagnosticLevelTest(null!),
        new CafeteriaSecretsCredentialDiagnosticLevelTest(null!, null!),
        new CafeteriaPublicationQueueDiagnosticLevelTest(null!),
        new CafeteriaAccountingGatewayDiagnosticLevelTest(null!),
        new CafeteriaProductReferenceSourceDiagnosticLevelTest(null!),
        new CafeteriaMessagingDiagnosticLevelTest(null!),
        new CafeteriaFundingPolicyDiagnosticLevelTest(null!),
        new CafeteriaCustomerLookupDiagnosticLevelTest(null!),
        new CafeteriaRecoveryReadinessDiagnosticLevelTest(null!),
        new CafeteriaExhaustiveConsistencyDiagnosticLevelTest(null!, null!, null!)
    ];

    [Fact]
    public void Catalogue_has_unique_stable_ids()
    {
        IDiagnosticLevelLocalTest[] tests = Catalogue();

        Assert.Equal(tests.Length, tests.Select(x => x.TestId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(tests, test => Assert.True(test.TestId.StartsWith("CAFETERIA.SERVICES.", StringComparison.Ordinal)));
    }

    [Fact]
    public void Level_5_and_4_tests_are_non_destructive()
    {
        IDiagnosticLevelLocalTest[] tests = Catalogue();

        Assert.DoesNotContain(
            tests,
            test => test.IsDestructive &&
                test.Level is EngineeringDiagnosticLevel.Level5Scan or EngineeringDiagnosticLevel.Level4Analysis);
    }

    [Theory]
    [InlineData(EngineeringDiagnosticLevel.Level5Scan, 5)]
    [InlineData(EngineeringDiagnosticLevel.Level4Analysis, 13)]
    [InlineData(EngineeringDiagnosticLevel.Level3Verification, 14)]
    [InlineData(EngineeringDiagnosticLevel.Level2Repair, 15)]
    [InlineData(EngineeringDiagnosticLevel.Level1CriticalIntervention, 16)]
    public void Starfleet_levels_are_cumulative(EngineeringDiagnosticLevel requested, int expectedCafeteriaTests)
    {
        IDiagnosticLevelLocalTest[] selected = Catalogue()
            .Where(x => (int)x.Level >= (int)requested)
            .ToArray();

        Assert.Equal(expectedCafeteriaTests, selected.Length);
    }

    [Fact]
    public void Configuration_contract_advertises_standard_remote_DiagnosticLevel()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Aegis:Diagnostics:RequestCredentialSecretName"] =
                    CafeteriaDiagnosticLevelContract.RequestCredentialSecretName
            })
            .Build();

        System.Text.Json.Nodes.JsonObject capability =
            CafeteriaDiagnosticLevelContract.BuildCapability(
                configuration,
                "https://cafeteria.example.test");

        Assert.True(capability["supportsRemoteDiagnostics"]!.GetValue<bool>());
        Assert.Equal(
            CafeteriaDiagnosticLevelContract.RunPath,
            capability["diagnosticsRunPath"]!.GetValue<string>());
        Assert.Equal(
            CafeteriaDiagnosticLevelContract.RunsPath,
            capability["diagnosticsRunsPath"]!.GetValue<string>());
        Assert.Equal(
            CafeteriaDiagnosticLevelContract.AuthenticationScheme,
            capability["authenticationScheme"]!.GetValue<string>());
        Assert.Equal(
            CafeteriaDiagnosticLevelContract.RequestCredentialSecretName,
            capability["secretName"]!.GetValue<string>());

        int[] levels = capability["supportedLevels"]!
            .AsArray()
            .Select(x => x!.GetValue<int>())
            .ToArray();
        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, levels);
    }

    [Fact]
    public void Signed_run_request_round_trips()
    {
        const string secret = "cafeteria-diagnostic-test-secret";
        const string path = "/api/engineering/diagnostics/diagnostic-level/run";
        string timestamp = DateTimeOffset.UtcNow.ToString("O");
        string nonce = Guid.NewGuid().ToString("N");
        DateTimeOffset issued = DateTimeOffset.UtcNow;

        var request = new DiagnosticLevelRunRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EngineeringDiagnosticLevel.Level5Scan,
            "unit-test",
            null,
            "https://diagnostics.example.test/api/engineering/diagnostics/diagnostic-level/callback",
            issued,
            issued.AddMinutes(2));

        string signature = DiagnosticLevelRequestSigning.CreateRunRequestSignature(
            secret,
            path,
            timestamp,
            nonce,
            request);

        Assert.True(DiagnosticLevelRequestSigning.VerifyRunRequest(
            secret,
            path,
            timestamp,
            nonce,
            request,
            signature));
    }

    [Fact]
    public async Task Runtime_exposes_current_test_name_and_clears_it_at_completion()
    {
        var store = new CapturingRunStore();
        var test = new BlockingDiagnosticTest();
        var service = new DiagnosticLevelExecutionService(
            [test],
            store,
            new DiagnosticLevelExecutionOptions
            {
                Application = "Aegis.Cafeteria.Services",
                Component = "Services",
                InstanceId = "unit-test",
                CatalogVersion = "1"
            },
            new NoopCompletionNotifier());

        DateTimeOffset issued = DateTimeOffset.UtcNow;
        var request = new DiagnosticLevelRunRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EngineeringDiagnosticLevel.Level5Scan,
            "unit-test",
            null,
            null,
            issued,
            issued.AddMinutes(2));

        DiagnosticLevelRunAccepted accepted = await service.AcceptAsync(request);
        await test.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        DiagnosticLevelRunRecord? running = await store.GetAsync(accepted.RunId);
        Assert.NotNull(running);
        Assert.Equal(DiagnosticLevelExecutionState.Running, running.ExecutionState);
        Assert.Equal(test.TestId, running.CurrentTestId);
        Assert.Equal(test.Name, running.CurrentTestName);

        test.Release.SetResult(true);

        DiagnosticLevelRunRecord? completed = null;
        for (int attempt = 0; attempt < 50; attempt++)
        {
            completed = await store.GetAsync(accepted.RunId);
            if (completed?.ExecutionState == DiagnosticLevelExecutionState.Completed)
                break;
            await Task.Delay(20);
        }

        Assert.NotNull(completed);
        Assert.Equal(DiagnosticLevelExecutionState.Completed, completed.ExecutionState);
        Assert.Null(completed.CurrentTestId);
        Assert.Null(completed.CurrentTestName);
        Assert.NotNull(completed.CompletedAtUtc);
    }

    private sealed class BlockingDiagnosticTest : IDiagnosticLevelLocalTest
    {
        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string TestId => "CAFETERIA.SERVICES.TEST.BLOCKING";
        public string Name => "Blocking runtime probe";
        public string Owner => "Aegis.Cafeteria.Services";
        public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level5Scan;
        public bool IsDestructive => false;

        public async Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            return EngineeringDiagnosticPolicy.Passed(TestId, Name, "Probe completed.");
        }
    }

    private sealed class NoopCompletionNotifier : IDiagnosticLevelCompletionNotifier
    {
        public Task<DiagnosticLevelDeliveryState> NotifyAsync(
            DiagnosticLevelRunRequest request,
            DiagnosticLevelRunRecord run,
            CancellationToken cancellationToken = default)
            => Task.FromResult(DiagnosticLevelDeliveryState.NotApplicable);
    }

    private sealed class CapturingRunStore : IDiagnosticLevelRunStore
    {
        private readonly object gate = new();
        private readonly Dictionary<Guid, DiagnosticLevelRunRecord> runs = [];

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(
            DiagnosticLevelRunRecord run,
            CancellationToken cancellationToken = default)
        {
            lock (gate)
                runs[run.RunId] = run;
            return Task.CompletedTask;
        }

        public Task<DiagnosticLevelRunRecord?> GetAsync(
            Guid runId,
            CancellationToken cancellationToken = default)
        {
            lock (gate)
                return Task.FromResult(runs.TryGetValue(runId, out DiagnosticLevelRunRecord? run) ? run : null);
        }

        public Task<IReadOnlyList<DiagnosticLevelRunRecord>> QueryAsync(
            DiagnosticLevelHistoryQuery query,
            CancellationToken cancellationToken = default)
        {
            lock (gate)
                return Task.FromResult<IReadOnlyList<DiagnosticLevelRunRecord>>(
                    runs.Values.OrderByDescending(x => x.RequestedAtUtc).Take(query.Take).ToArray());
        }

        public Task PurgeExpiredAsync(
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

}
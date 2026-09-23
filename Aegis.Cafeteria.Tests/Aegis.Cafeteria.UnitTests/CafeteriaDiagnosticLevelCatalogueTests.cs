using Aegis.Cafeteria.Services.Engineering.Diagnostics;
using Common.Diagnostics;

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
    [InlineData(EngineeringDiagnosticLevel.Level4Analysis, 12)]
    [InlineData(EngineeringDiagnosticLevel.Level3Verification, 13)]
    [InlineData(EngineeringDiagnosticLevel.Level2Repair, 14)]
    [InlineData(EngineeringDiagnosticLevel.Level1CriticalIntervention, 15)]
    public void Starfleet_levels_are_cumulative(EngineeringDiagnosticLevel requested, int expectedCafeteriaTests)
    {
        IDiagnosticLevelLocalTest[] selected = Catalogue()
            .Where(x => (int)x.Level >= (int)requested)
            .ToArray();

        Assert.Equal(expectedCafeteriaTests, selected.Length);
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
}

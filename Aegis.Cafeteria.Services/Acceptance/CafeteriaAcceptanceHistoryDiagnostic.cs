using Common.Diagnostics;

namespace Aegis.Cafeteria.Services.Acceptance;

// Reading qualification evidence is safe at any production cadence. Executing the
// suite is deliberately separate and only available through the isolated command.
public sealed class CafeteriaAcceptanceHistoryDiagnostic(IConfiguration configuration) : IDiagnosticLevelLocalTest
{
    public string TestId => "CAFETERIA.SERVICES.ACCEPTANCE.EVIDENCE";
    public string Name => "Cafeteria acceptance evidence";
    public string Owner => "Aegis.Cafeteria.Services";
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level3Verification;
    public bool IsDestructive => false;
    public async Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string? root = configuration["Acceptance:RootDirectory"];
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return new(TestId, Name, EngineeringDiagnosticStatus.InterventionRequired,
                "No isolated acceptance evidence is configured.", Code: "Blocked");
        var store = new JsonEngineeringDiagnosticRunStore(Path.Combine(root, "diagnostic-runs.json"));
        var run = (await store.GetRecentAsync(1, cancellationToken)).FirstOrDefault();
        if (run is null)
            return new(TestId, Name, EngineeringDiagnosticStatus.InterventionRequired, "Acceptance has not run.", Code: "Unexecuted");
        bool passed = run.Status == EngineeringDiagnosticStatus.Passed && run.Environment == "Acceptance" && run.Checks.Count == CafeteriaAcceptanceCatalogue.Required.Count
            && run.RunState == EngineeringDiagnosticRunState.Completed && run.Checks.All(x => x.Code == "Passed");
        return new(TestId, Name, passed ? EngineeringDiagnosticStatus.Passed : EngineeringDiagnosticStatus.InterventionRequired,
            passed ? "Latest recorded acceptance passed. Release qualification still requires review and deployment evidence."
                : "Latest acceptance has failed, blocked or unexecuted proofs.",
            $"Run={run.RunId}; Completed={run.CompletedAt:O}; State={run.RunState}", "All mandatory proofs pass", passed.ToString());
    }
}

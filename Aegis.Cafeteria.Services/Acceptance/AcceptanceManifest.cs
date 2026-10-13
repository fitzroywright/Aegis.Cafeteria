namespace Aegis.Cafeteria.Services.Acceptance;

public sealed class AcceptanceManifest
{
    public string RootDirectory { get; set; } = "";
    public string SqlConnectionEnvironmentVariable { get; set; } = "CAFETERIA_ACCEPTANCE_FFP";
    public string ExpectedServer { get; set; } = "";
    public string ExpectedSqlLogin { get; set; } = "";
    public string ExpectedWindowsIdentity { get; set; } = "";
    public string ProductionInventoryVerifiedBy { get; set; } = "";
    public DateTimeOffset? ProductionInventoryVerifiedAtUtc { get; set; }
    public string[] ProductionSqlitePaths { get; set; } = [];
    public string IntegrationPluginPath { get; set; } = "";
    public string IntegrationPluginSha256 { get; set; } = "";
    public int[] FixtureEmployeeIds { get; set; } = [];
    public string FixtureCodePrefix { get; set; } = "CAF-ACCEPT-";
    public int Year { get; set; } = 2026;
    public int Month { get; set; } = 9;
    public int Level { get; set; } = 1;
    public decimal ExpectedPrice { get; set; }
    public decimal ExpectedCost { get; set; }
    public string[] ExpectedHolidays { get; set; } = [];
    public int ExpectedWorkingDays { get; set; }
}

public sealed class AcceptanceBlockedException(string message) : Exception(message);


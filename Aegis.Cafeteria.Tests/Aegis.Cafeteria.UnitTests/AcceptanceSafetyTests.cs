using System.Text.Json;
using Aegis.Cafeteria.Services.Acceptance;
using Common.Diagnostics;
using Microsoft.Data.SqlClient;

namespace Aegis.Cafeteria.UnitTests;

public sealed class AcceptanceSafetyTests
{
    [Fact]
    public void Unreviewed_or_changed_integration_plugins_are_rejected()
    {
        var manifest = Manifest(Path.GetTempPath());
        Assert.Throws<AcceptanceBlockedException>(() => AcceptanceDatabaseSafety.VerifyPlugin(manifest));
        manifest.IntegrationPluginPath = typeof(AcceptanceDatabaseSafety).Assembly.Location;
        manifest.IntegrationPluginSha256 = "wrong";
        Assert.Throws<AcceptanceBlockedException>(() => AcceptanceDatabaseSafety.VerifyPlugin(manifest));
    }
    private static AcceptanceManifest Manifest(string directory) => new()
    {
        RootDirectory = Path.Combine(directory, "acceptance"), ExpectedServer = "AcceptanceSql",
        ExpectedSqlLogin = "cafeteria_acceptance", ProductionInventoryVerifiedBy = "Engineer",
        ProductionInventoryVerifiedAtUtc = DateTimeOffset.UtcNow,
        ProductionSqlitePaths = [Path.Combine(directory, "production", "services.db"), Path.Combine(directory, "production", "runtime.db")],
        FixtureEmployeeIds = [991, 992], Year = 2001, Month = 1, ExpectedPrice = 100, ExpectedCost = 60, ExpectedWorkingDays = 22
    };
    private const string SafeSql = "Server=AcceptanceSql;Database=FFP;User Id=cafeteria_acceptance;Password=unit-test-only;Enlist=false";
    private const string TrustedSql = "Server=AcceptanceSql;Database=FFP;Trusted_Connection=True;Enlist=false";

    [Fact]
    public void Trusted_connection_accepts_an_explicit_windows_identity_without_a_password()
    {
        var m = Manifest(Path.GetTempPath());
        m.ExpectedSqlLogin = "";
        m.ExpectedWindowsIdentity = @"fftpj\fwright";
        var sql = new SqlConnectionStringBuilder(AcceptanceDatabaseSafety.Validate(m, TrustedSql));
        Assert.True(sql.IntegratedSecurity);
        Assert.Empty(sql.Password);
        Assert.False(sql.Enlist);
        Assert.False(sql.Pooling);
        Assert.Equal("FFP", sql.InitialCatalog);
    }

    [Theory]
    [InlineData("")]
    [InlineData("fwright")]
    [InlineData(@"fftpj\")]
    [InlineData(@"\fwright")]
    public void Trusted_connection_requires_a_pinned_domain_identity(string identity)
    {
        var m = Manifest(Path.GetTempPath()); m.ExpectedSqlLogin = ""; m.ExpectedWindowsIdentity = identity;
        Assert.Throws<AcceptanceBlockedException>(() => AcceptanceDatabaseSafety.Validate(m, TrustedSql));
    }

    [Fact]
    public void Trusted_connection_cannot_mix_sql_credentials_or_change_the_database()
    {
        var m = Manifest(Path.GetTempPath()); m.ExpectedSqlLogin = ""; m.ExpectedWindowsIdentity = @"fftpj\fwright";
        foreach (var sql in new[] {
            new SqlConnectionStringBuilder(TrustedSql) { InitialCatalog = "FFPJM" },
            new SqlConnectionStringBuilder(TrustedSql) { DataSource = "OtherSql" },
            new SqlConnectionStringBuilder(TrustedSql) { UserID = "other", Password = "test" },
            new SqlConnectionStringBuilder(SafeSql) })
            Assert.Throws<AcceptanceBlockedException>(() => AcceptanceDatabaseSafety.Validate(m, sql.ConnectionString));
        m.ExpectedSqlLogin = "cafeteria_acceptance";
        Assert.Throws<AcceptanceBlockedException>(() => AcceptanceDatabaseSafety.Validate(m, TrustedSql));
    }

    [Fact]
    public void Explicitly_selected_test_employees_do_not_require_a_code_rename()
    {
        var m = Manifest(Path.GetTempPath()); m.FixtureEmployeeIds = [61, 62]; m.FixtureCodePrefix = "";
        AcceptanceDatabaseSafety.Validate(m, SafeSql);
        m.FixtureEmployeeIds = [61, 61];
        Assert.Throws<AcceptanceBlockedException>(() => AcceptanceDatabaseSafety.Validate(m, SafeSql));
    }

    [Theory]
    [InlineData("FFPJM")]
    [InlineData("ffpjm")]
    [InlineData("master")]
    [InlineData("OtherTest")]
    [InlineData("")]
    public void Production_and_non_designated_catalogs_are_rejected(string catalog)
    {
        var sql = new SqlConnectionStringBuilder(SafeSql) { InitialCatalog = catalog };
        Assert.Throws<AcceptanceBlockedException>(() => AcceptanceDatabaseSafety.Validate(Manifest(Path.GetTempPath()), sql.ConnectionString));
    }

    [Fact]
    public void Dedicated_target_is_pinned_and_does_not_enlist_or_pool()
    {
        var result = new SqlConnectionStringBuilder(AcceptanceDatabaseSafety.Validate(Manifest(Path.GetTempPath()), SafeSql));
        Assert.False(result.Enlist); Assert.False(result.Pooling);
        Assert.Equal("FFP", result.InitialCatalog); Assert.Equal("Aegis.Cafeteria.Acceptance", result.ApplicationName);
    }

    [Theory]
    [InlineData("Server=ProductionSql;Database=FFP;User Id=cafeteria_acceptance;Password=test;Enlist=false")]
    [InlineData("Server=AcceptanceSql;Database=FFP;Integrated Security=true;Enlist=false")]
    [InlineData("Server=AcceptanceSql;Database=FFP;User Id=administrator;Password=test;Enlist=false")]
    [InlineData("Server=AcceptanceSql;Database=FFP;User Id=cafeteria_acceptance;Password=test;Failover Partner=ProductionSql;Enlist=false")]
    [InlineData("Server=AcceptanceSql;Database=FFP;User Id=cafeteria_acceptance;Password=test;AttachDbFilename=production.mdf;Enlist=false")]
    [InlineData("Server=AcceptanceSql;Database=FFP;User Id=cafeteria_acceptance;Password=test")]
    public void Ambient_or_indirect_connection_targets_are_rejected(string sql) =>
        Assert.Throws<AcceptanceBlockedException>(() => AcceptanceDatabaseSafety.Validate(Manifest(Path.GetTempPath()), sql));

    [Fact]
    public void Production_directory_cannot_be_used_as_acceptance_root()
    {
        var m = Manifest(Path.GetTempPath()); m.RootDirectory = Path.Combine(Path.GetTempPath(), "production");
        Assert.Throws<AcceptanceBlockedException>(() => AcceptanceDatabaseSafety.Validate(m, SafeSql));
    }

    [Fact]
    public void Unknown_production_inventory_blocks_writes()
    {
        var m = Manifest(Path.GetTempPath()); m.ProductionInventoryVerifiedBy = "";
        Assert.Throws<AcceptanceBlockedException>(() => AcceptanceDatabaseSafety.Validate(m, SafeSql));
    }

    [Fact]
    public void Symlink_ancestor_is_rejected()
    {
        if (OperatingSystem.IsWindows()) return; // Creation requires privilege on Windows.
        string root = Path.Combine(Path.GetTempPath(), "acceptance-symlink-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(root, "alias"), root);
            Assert.Throws<AcceptanceBlockedException>(() => AcceptanceDatabaseSafety.ValidateDirectory(Path.Combine(root, "alias", "db")));
        }
        finally { Directory.Delete(Path.Combine(root, "alias")); Directory.Delete(root); }
    }

    [Fact]
    public async Task Missing_credentials_produce_twenty_blocked_proofs_and_create_no_financial_database()
    {
        string directory = Path.Combine(Path.GetTempPath(), "acceptance-command-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var m = Manifest(directory); m.SqlConnectionEnvironmentVariable = "ABSENT_ACCEPTANCE_" + Guid.NewGuid().ToString("N");
            string manifest = Path.Combine(directory, "manifest.json");
            await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(m, CafeteriaAcceptanceCommand.JsonOptions));
            Assert.Equal(2, await CafeteriaAcceptanceCommand.RunAsync(["--cafeteria-acceptance", manifest]));
            string report = Directory.EnumerateFiles(m.RootDirectory, "acceptance.json", SearchOption.AllDirectories).Single();
            var evidence = JsonSerializer.Deserialize<AcceptanceEvidence>(await File.ReadAllTextAsync(report), CafeteriaAcceptanceCommand.JsonOptions)!;
            Assert.Equal(20, evidence.Proofs.Count);
            Assert.All(evidence.Proofs, p => Assert.Equal(AcceptanceProofState.Blocked, p.State));
            Assert.False(evidence.IsolationVerified);
            Assert.False(evidence.IntegrationProven(CafeteriaAcceptanceCatalogue.Required));
            Assert.Empty(Directory.EnumerateFiles(m.RootDirectory, "*.sqlite", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(directory, true); }
    }
}


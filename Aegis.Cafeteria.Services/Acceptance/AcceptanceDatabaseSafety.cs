using Microsoft.Data.SqlClient;
using System.Security.Cryptography;

namespace Aegis.Cafeteria.Services.Acceptance;

public static class AcceptanceDatabaseSafety
{
    public static void VerifyPlugin(AcceptanceManifest manifest)
    {
        if (!Path.IsPathFullyQualified(manifest.IntegrationPluginPath) || !File.Exists(manifest.IntegrationPluginPath)
            || string.IsNullOrWhiteSpace(manifest.IntegrationPluginSha256))
            throw new AcceptanceBlockedException("An explicitly reviewed integration plugin path and SHA256 are required.");
        string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifest.IntegrationPluginPath)));
        if (!string.Equals(actual, manifest.IntegrationPluginSha256, StringComparison.OrdinalIgnoreCase))
            throw new AcceptanceBlockedException("Integration plugin differs from the reviewed acceptance binary.");
    }

    public static string Validate( AcceptanceManifest manifest, string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new AcceptanceBlockedException("Dedicated acceptance SQL credentials are unavailable.");
        SqlConnectionStringBuilder sql;
        try { sql = new(connectionString); }
        catch (ArgumentException) { throw new AcceptanceBlockedException("Acceptance SQL connection configuration is invalid."); }
        if (!string.Equals(sql.InitialCatalog, "FFP", StringComparison.OrdinalIgnoreCase))
            throw new AcceptanceBlockedException("Only the FFP test database is permitted. FFPJM and every other catalog are rejected.");
        if (string.IsNullOrWhiteSpace(manifest.ExpectedServer) ||
            !string.Equals(sql.DataSource, manifest.ExpectedServer, StringComparison.OrdinalIgnoreCase))
            throw new AcceptanceBlockedException("SQL server does not match the explicit acceptance server.");
        if (sql.IntegratedSecurity || sql.Authentication != SqlAuthenticationMethod.NotSpecified
            || string.IsNullOrWhiteSpace(sql.Password)
            || string.IsNullOrWhiteSpace(manifest.ExpectedSqlLogin)
            || !string.Equals(sql.UserID, manifest.ExpectedSqlLogin, StringComparison.Ordinal))
            throw new AcceptanceBlockedException("A dedicated, explicitly identified SQL login is required; ambient authentication is forbidden.");
        if (!string.IsNullOrEmpty(sql.AttachDBFilename) || !string.IsNullOrEmpty(sql.FailoverPartner)
            || sql.Enlist)
            throw new AcceptanceBlockedException("Attach, failover and ambient transaction dependencies are forbidden. Set Enlist=false.");
        if (string.IsNullOrWhiteSpace(manifest.ProductionInventoryVerifiedBy)
            || manifest.ProductionInventoryVerifiedAtUtc is null
            || manifest.ProductionSqlitePaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2
            || manifest.ProductionInventoryVerifiedAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
            throw new AcceptanceBlockedException("Verified production service and runtime SQLite paths are required before test writes.");
        ValidateDirectory(manifest.RootDirectory);
        string root = Path.GetFullPath(manifest.RootDirectory);
        foreach (string production in manifest.ProductionSqlitePaths)
        {
            if (!Path.IsPathFullyQualified(production))
                throw new AcceptanceBlockedException("Production inventory must contain absolute paths for this execution host.");
            ValidateDirectory(Path.GetDirectoryName(production)!);
            var productionFile = new FileInfo(production);
            if (productionFile.Exists && (productionFile.LinkTarget is not null || productionFile.Attributes.HasFlag(FileAttributes.ReparsePoint)))
                throw new AcceptanceBlockedException("Production inventory must identify resolved physical database paths.");
            string productionDirectory = Path.GetDirectoryName(Path.GetFullPath(production))!;
            if (IsWithin(root, productionDirectory) || IsWithin(productionDirectory, root))
                throw new AcceptanceBlockedException("Acceptance and production directories must be disjoint.");
        }
        if (manifest.FixtureEmployeeIds.Length < 2 || manifest.FixtureEmployeeIds.Any(id => id <= 0)
            || manifest.FixtureEmployeeIds.Distinct().Count() != manifest.FixtureEmployeeIds.Length
            || !manifest.FixtureCodePrefix.StartsWith("CAF-ACCEPT-", StringComparison.Ordinal))
            throw new AcceptanceBlockedException("At least two unique dedicated acceptance employees with CAF-ACCEPT- codes are required.");
        if (manifest.Year < 2000 || manifest.Month is < 1 or > 12 || manifest.ExpectedWorkingDays <= 0
            || manifest.ExpectedPrice <= 0 || manifest.ExpectedCost < 0)
            throw new AcceptanceBlockedException("A deterministic completed-month policy fixture is required.");
        if (manifest.Level is not 1 and not 2)
            throw new AcceptanceBlockedException("Acceptance execution is limited to isolated Level 1 or Level 2 suites.");
        if (new DateOnly(manifest.Year, manifest.Month, 1) >= new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1))
            throw new AcceptanceBlockedException("The financial workflow fixture must use a completed month.");
        sql.Pooling = false;
        sql.ApplicationName = "Aegis.Cafeteria.Acceptance";
        sql.ConnectTimeout = 5;
        return sql.ConnectionString;
    }

    public static void ValidateDirectory(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new AcceptanceBlockedException("Acceptance paths must be absolute.");
        for (DirectoryInfo? current = new(Path.GetFullPath(path)); current is not null; current = current.Parent)
            if (current.Exists && (current.LinkTarget is not null || current.Attributes.HasFlag(FileAttributes.ReparsePoint)))
                throw new AcceptanceBlockedException("Symlinks and reparse points are forbidden in database paths.");
    }

    private static bool IsWithin(string path, string directory) =>
        string.Equals(path, directory, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    public static async Task VerifyServerAsync(string connectionString, AcceptanceManifest manifest, CancellationToken ct)
    {
        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(ct);
        await using SqlCommand check = new("""
            SELECT DB_NAME(), ORIGINAL_LOGIN(),
                HAS_DBACCESS('FFPJM'),
                IS_SRVROLEMEMBER('sysadmin'), IS_SRVROLEMEMBER('securityadmin'), IS_MEMBER('db_owner'),
                HAS_PERMS_BY_NAME(NULL, NULL, 'CONTROL SERVER'),
                HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CONTROL'),
                HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER'),
                HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION'),
                (SELECT COUNT(*) FROM sys.databases WHERE database_id = DB_ID() AND (is_trustworthy_on = 1 OR is_db_chaining_on = 1));
            """, connection);
        await using (SqlDataReader r = await check.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct) || r.GetString(0) != "FFP" || r.GetString(1) != manifest.ExpectedSqlLogin
                || Enumerable.Range(2, 7).Any(i => r.IsDBNull(i) || Convert.ToInt32(r.GetValue(i)) != 0)
                || r.IsDBNull(9) || Convert.ToInt32(r.GetValue(9)) != 1 || Convert.ToInt32(r.GetValue(10)) != 0)
                throw new AcceptanceBlockedException("SQL identity, production-access denial, least privilege or dependency visibility cannot be verified.");
        }
        // Validate the exact provider tables before any mutation. Reject executable indirect
        // paths rather than trying to classify arbitrary trigger/function SQL as safe.
        await using SqlCommand dependencies = new("""
            SELECT
              (SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('dbo') AND name IN ('Employees','EmployeeIDScans','LunchItems','Holidays')),
              (SELECT COUNT(*) FROM sys.triggers WHERE is_disabled=0 AND parent_id IN
                (OBJECT_ID('dbo.EmployeeIDScans'),OBJECT_ID('dbo.Employees'),OBJECT_ID('dbo.LunchItems'),OBJECT_ID('dbo.Holidays'))),
              (SELECT COUNT(*) FROM sys.sql_expression_dependencies WHERE
                 referencing_id IN (OBJECT_ID('dbo.EmployeeIDScans'),OBJECT_ID('dbo.Employees'),OBJECT_ID('dbo.LunchItems'),OBJECT_ID('dbo.Holidays'))
                 AND (referenced_server_name IS NOT NULL OR referenced_database_name IS NOT NULL OR referenced_id IS NULL)),
              (SELECT COUNT(*) FROM sys.columns WHERE is_computed=1 AND object_id IN
                (OBJECT_ID('dbo.EmployeeIDScans'),OBJECT_ID('dbo.Employees'),OBJECT_ID('dbo.LunchItems'),OBJECT_ID('dbo.Holidays'))),
              (SELECT COUNT(*) FROM sys.default_constraints d JOIN sys.sql_expression_dependencies e ON e.referencing_id=d.object_id
                WHERE d.parent_object_id IN (OBJECT_ID('dbo.EmployeeIDScans'),OBJECT_ID('dbo.Employees'),OBJECT_ID('dbo.LunchItems'),OBJECT_ID('dbo.Holidays'))),
              (SELECT COUNT(*) FROM sys.security_policies WHERE is_enabled=1);
            """, connection);
        await using (SqlDataReader r = await dependencies.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct) || Convert.ToInt32(r.GetValue(0)) != 4
                || Enumerable.Range(1, 5).Any(i => Convert.ToInt32(r.GetValue(i)) != 0))
                throw new AcceptanceBlockedException("FFP tables have triggers, computed columns, policy filters, executable defaults or unresolved dependencies. No writes are permitted.");
        }
        foreach (int id in manifest.FixtureEmployeeIds)
        {
            await using SqlCommand fixture = new("SELECT Code FROM dbo.Employees WHERE ID=@id", connection);
            fixture.Parameters.AddWithValue("@id", id);
            string? code = Convert.ToString(await fixture.ExecuteScalarAsync(ct));
            if (code is null || !code.StartsWith(manifest.FixtureCodePrefix, StringComparison.Ordinal))
                throw new AcceptanceBlockedException("An employee fixture is missing or belongs to unrelated FFP test data.");
        }
    }
}

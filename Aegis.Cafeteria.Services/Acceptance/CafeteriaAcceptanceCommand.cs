using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Common.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Aegis.Cafeteria.Services.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Cafeteria.Services.Acceptance;

public static class CafeteriaAcceptanceCommand
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: Aegis.Cafeteria.Services --cafeteria-acceptance <absolute-manifest.json>");
            return 2;
        }
        AcceptanceManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<AcceptanceManifest>(await File.ReadAllTextAsync(args[1]), JsonOptions)
                ?? throw new InvalidOperationException("Empty acceptance manifest.");
            AcceptanceDatabaseSafety.ValidateDirectory(manifest.RootDirectory);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Acceptance blocked: {e.GetType().Name}. Manifest and absolute output directory are required.");
            return 2;
        }

        Directory.CreateDirectory(manifest.RootDirectory);
        // Exclusive across CLI, deployment hook and Operations invocations on this host.
        await using FileStream runLock = new(Path.Combine(manifest.RootDirectory, ".acceptance.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Guid runId = Guid.NewGuid();
        string directory = Path.Combine(manifest.RootDirectory, runId.ToString("N"));
        Directory.CreateDirectory(directory);
        DateTimeOffset started = DateTimeOffset.UtcNow;
        var proofs = CafeteriaAcceptanceCatalogue.Required.Select(x => new AcceptanceProofResult(x.Id, x.Name,
            AcceptanceProofState.Unexecuted, "Deterministic scenario passes", "Not executed", "Pending execution", started)).ToArray();
        EngineeringDiagnosticRun diagnostic = new(runId, manifest.Level == 2 ? EngineeringDiagnosticLevel.Level2Repair : EngineeringDiagnosticLevel.Level1CriticalIntervention,
            "Aegis.Cafeteria.Services", "Acceptance", Environment.GetEnvironmentVariable("CAFETERIA_ACCEPTANCE_REQUESTED_BY") ?? Environment.UserName,
            Environment.GetEnvironmentVariable("CAFETERIA_ACCEPTANCE_REASON") ?? "Explicit isolated acceptance execution",
            started, started, EngineeringDiagnosticStatus.InterventionRequired, [],
            RunState: EngineeringDiagnosticRunState.Running, CurrentStage: "Safety verification", ProgressPercent: 0);
        AcceptanceEvidence evidence = new(diagnostic, CafeteriaAcceptanceCatalogue.Version,
            (typeof(CafeteriaAcceptanceCommand).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown")
                + "; SHA256=" + Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(typeof(CafeteriaAcceptanceCommand).Assembly.Location))),
            $"Local={PersistenceInitializer.CurrentLocalSchemaVersion}; Service=EF model", false, proofs,
            CafeteriaAcceptanceCatalogue.Required.Where(p => p.Level >= manifest.Level).Select(p => p.Id).ToArray());
        var diagnosticStore = new JsonEngineeringDiagnosticRunStore(Path.Combine(manifest.RootDirectory, "diagnostic-runs.json"));
        async Task SaveAsync(AcceptanceEvidence value)
        {
            value = value with { Diagnostic = value.ToDiagnosticRun() };
            string temporary = Path.Combine(directory, "acceptance.json.tmp");
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(temporary, Path.Combine(directory, "acceptance.json"), true);
            await diagnosticStore.SaveAsync(value.ToDiagnosticRun());
        }
        await SaveAsync(evidence);
        try
        {
            string sql = AcceptanceDatabaseSafety.Validate(manifest,
                Environment.GetEnvironmentVariable(manifest.SqlConnectionEnvironmentVariable));
            AcceptanceDatabaseSafety.VerifyPlugin(manifest);
            await AcceptanceDatabaseSafety.VerifyServerAsync(sql, manifest, CancellationToken.None);
            await AcceptanceFixtureJournal.RecoverAsync(manifest, sql);
            evidence = evidence with { IsolationVerified = true };
            evidence = evidence with { ApplicationVersion = evidence.ApplicationVersion + "; Integration SHA256=" + manifest.IntegrationPluginSha256 };
            await SaveAsync(evidence);

            // This child command never loads appsettings, environment configuration providers,
            // hosting startup plugins, remote configuration or production registration identity.
            Environment.SetEnvironmentVariable("ASPNETCORE_PREVENTHOSTINGSTARTUP", "true");
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            { Args = ["--preventHostingStartup=true"], EnvironmentName = "Acceptance", ContentRootPath = directory });
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServicePersistence:ConnectionString"] = $"Data Source={Path.Combine(directory, "services.sqlite")}",
                ["Persistence:LocalSqliteConnectionString"] = $"Data Source={Path.Combine(directory, "runtime.sqlite")}",
                ["IntegrationPlugin:Enabled"] = "true",
                ["IntegrationPlugin:AssemblyPath"] = Path.GetFullPath(manifest.IntegrationPluginPath),
                ["IntegrationPlugin:ConnectionString"] = sql
            });
            CafeteriaServicesHost.ConfigureAcceptance(builder);
            ServiceDescriptor original = builder.Services.Last(x => x.ServiceType == typeof(IWorkforceReferenceSource));
            builder.Services.RemoveAll<IWorkforceReferenceSource>();
            builder.Services.AddSingleton<IWorkforceReferenceSource>(sp => new AcceptanceFixtureWorkforce(
                (IWorkforceReferenceSource)(original.ImplementationFactory?.Invoke(sp)
                    ?? original.ImplementationInstance
                    ?? ActivatorUtilities.CreateInstance(sp, original.ImplementationType!)), manifest.FixtureEmployeeIds));
            await using WebApplication app = builder.Build();
            await CafeteriaServiceDatabase.InitializeAsync(app);
            await using (ServiceDbContext db = await app.Services.GetRequiredService<IDbContextFactory<ServiceDbContext>>().CreateDbContextAsync())
            {
                await db.Database.OpenConnectionAsync();
                await using var schema = db.Database.GetDbConnection().CreateCommand();
                schema.CommandText = "SELECT name, sql FROM sqlite_master WHERE type IN ('table','index') ORDER BY name";
                await using var reader = await schema.ExecuteReaderAsync();
                StringBuilder signature = new();
                while (await reader.ReadAsync()) signature.AppendLine(reader.GetString(0) + ":" + (reader.IsDBNull(1) ? "" : reader.GetString(1)));
                evidence = evidence with { SchemaVersion = $"Local={PersistenceInitializer.CurrentLocalSchemaVersion}; Service SHA256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature.ToString())))}" };
            }
            await using (SqlConnection connection = new(sql))
            {
                await connection.OpenAsync();
                await using SqlCommand schema = new("""
                    SELECT t.name, c.name, TYPE_NAME(c.user_type_id), c.max_length, c.is_nullable
                    FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id
                    WHERE t.schema_id=SCHEMA_ID('dbo') AND t.name IN ('Employees','EmployeeIDScans','LunchItems','Holidays')
                    ORDER BY t.name,c.column_id
                    """, connection);
                await using var reader = await schema.ExecuteReaderAsync();
                StringBuilder signature = new();
                while (await reader.ReadAsync()) signature.AppendLine(string.Join('|', Enumerable.Range(0, 5).Select(i => reader.GetValue(i).ToString())));
                evidence = evidence with { SchemaVersion = evidence.SchemaVersion + "; FFP SHA256=" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature.ToString()))) };
            }
            await SaveAsync(evidence);
            var suite = new CafeteriaAcceptanceSuite(app.Services, manifest, sql, directory);
            evidence = evidence with { Proofs = await suite.ExecuteAsync(async results =>
            {
                evidence = evidence with { Proofs = results };
                int completed = results.Count(p => evidence.SelectedProofIds!.Contains(p.Id) && p.State != AcceptanceProofState.Unexecuted);
                evidence = evidence with { Diagnostic = evidence.Diagnostic with
                { CurrentStage = results.LastOrDefault(p => p.State != AcceptanceProofState.Unexecuted)?.Name ?? "Acceptance",
                    ProgressPercent = completed * 100 / Math.Max(1, evidence.SelectedProofIds!.Count) } };
                await SaveAsync(evidence);
            }) };
        }
        catch (Exception e)
        {
            string reason = e is AcceptanceBlockedException ? e.Message
                : $"Environment or runner failed ({e.GetType().Name}). See local execution log; no passing evidence was substituted.";
            evidence = evidence with { Proofs = evidence.Proofs.Select(p => p.State == AcceptanceProofState.Unexecuted
                ? p with { State = AcceptanceProofState.Blocked, Actual = "Unavailable", Detail = reason, AtUtc = DateTimeOffset.UtcNow }
                : p).ToArray() };
            Console.Error.WriteLine(reason);
        }
        evidence = evidence with { Diagnostic = diagnostic with
        { CompletedAt = DateTimeOffset.UtcNow, RunState = EngineeringDiagnosticRunState.Completed, CurrentStage = "Completed", ProgressPercent = 100 } };
        await SaveAsync(evidence);
        Console.WriteLine($"Evidence: {Path.Combine(directory, "acceptance.json")}");
        Console.WriteLine($"Integration proven: {evidence.IntegrationProven(CafeteriaAcceptanceCatalogue.Required)}");
        bool selectedPassed = evidence.IsolationVerified && CafeteriaAcceptanceCatalogue.Required.Where(p => p.Level >= manifest.Level)
            .All(p => evidence.Proofs.Any(r => r.Id == p.Id && r.State == AcceptanceProofState.Passed));
        return selectedPassed ? 0
            : evidence.Proofs.Any(p => p.State == AcceptanceProofState.Failed) ? 1 : 2;
    }
}

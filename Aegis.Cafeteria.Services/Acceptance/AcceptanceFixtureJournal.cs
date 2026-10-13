using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Aegis.Cafeteria.Services.Acceptance;

internal sealed record EligibilityRestoration(int EmployeeId, bool IsActive, bool IsEligible, bool Restored = false);

/// <summary>Only dedicated fixture rows may be changed; recovery survives process termination.</summary>
internal static class AcceptanceFixtureJournal
{
    public static async Task RecoverAsync(AcceptanceManifest manifest, string connection)
    {
        foreach (string directory in Directory.EnumerateDirectories(manifest.RootDirectory))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) continue;
            AcceptanceDatabaseSafety.ValidateDirectory(directory);
            string path = Path.Combine(directory, "eligibility-restoration.json");
            if (File.Exists(path)) await RestoreAsync(path, manifest, connection);
        }
    }

    public static async Task RestoreAsync(string path, AcceptanceManifest manifest, string connection)
    {
        var baseline = JsonSerializer.Deserialize<EligibilityRestoration>(await File.ReadAllTextAsync(path))
            ?? throw new AcceptanceBlockedException("Fixture restoration journal is unreadable.");
        if (baseline.Restored) return;
        if (!manifest.FixtureEmployeeIds.Contains(baseline.EmployeeId))
            throw new AcceptanceBlockedException("Restoration journal references an employee outside the fixture namespace.");
        await using SqlConnection c = new(connection); await c.OpenAsync();
        await using SqlCommand cmd = new("UPDATE dbo.Employees SET IsActive=@active, EligibleForLunchTicket=@eligible WHERE ID=@id AND Code LIKE @prefix", c);
        cmd.Parameters.AddWithValue("@active", baseline.IsActive);
        cmd.Parameters.AddWithValue("@eligible", baseline.IsEligible);
        cmd.Parameters.AddWithValue("@id", baseline.EmployeeId);
        cmd.Parameters.AddWithValue("@prefix", manifest.FixtureCodePrefix + "%");
        if (await cmd.ExecuteNonQueryAsync() != 1)
            throw new AcceptanceBlockedException("Dedicated fixture restoration did not update exactly one employee.");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(baseline with { Restored = true }));
    }
}

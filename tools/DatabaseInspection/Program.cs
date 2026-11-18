using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text.Json;

// Read-only metadata inspection. Never calls application initialization or checkpoints.
foreach (string argument in args)
{
    string path = Path.GetFullPath(argument);
    if (!File.Exists(path)) throw new FileNotFoundException("Database must already exist", path);
    string before = Hash(path);
    var options = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
    var tables = new List<string>();
    var counts = new Dictionary<string, long>();
    string? schema = null;
    string? integrity;
    using (var connection = new SqliteConnection(options.ConnectionString))
    {
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA query_only=ON";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        using (var reader = command.ExecuteReader())
            while (reader.Read()) tables.Add(reader.GetString(0));
        foreach (string table in new[] { "Customers", "Orders", "ServiceEvents", "IntegrationEmployeeCache", "ValidatedPolicyCache" }.Where(tables.Contains))
        {
            command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
            counts[table] = Convert.ToInt64(command.ExecuteScalar());
        }
        if (tables.Contains("LocalSchemaInfo"))
        {
            command.CommandText = "SELECT SchemaVersion FROM LocalSchemaInfo WHERE Id=1";
            schema = command.ExecuteScalar()?.ToString();
        }
        command.CommandText = "PRAGMA quick_check";
        integrity = command.ExecuteScalar()?.ToString();
    }
    Console.WriteLine(JsonSerializer.Serialize(new { path, bytes = new FileInfo(path).Length, walBytes = File.Exists(path + "-wal") ? new FileInfo(path + "-wal").Length : 0, tables, counts, schema, quickCheck = integrity, mainFileUnchanged = before == Hash(path) }));
}

static string Hash(string path)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    return Convert.ToHexString(SHA256.HashData(stream));
}

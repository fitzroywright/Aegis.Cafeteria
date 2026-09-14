namespace Aegis.Cafeteria.Infrastructure.ActiveDirectory;

public sealed class EmployeeDirectoryMetadataCache
{
    private readonly object syncRoot = new();
    private IReadOnlyDictionary<string, ActiveDirectoryEmployeeRecord> records =
        new Dictionary<string, ActiveDirectoryEmployeeRecord>(StringComparer.OrdinalIgnoreCase);

    public ActiveDirectoryEmployeeRecord? Get(string? loginName)
    {
        string normalized = NormalizeLoginName(loginName);
        if (normalized.Length == 0)
        {
            return null;
        }

        lock (this.syncRoot)
        {
            return this.records.TryGetValue(normalized, out ActiveDirectoryEmployeeRecord? record)
                ? record
                : null;
        }
    }

    public void Replace(IReadOnlyDictionary<string, ActiveDirectoryEmployeeRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        Dictionary<string, ActiveDirectoryEmployeeRecord> snapshot =
            new(records, StringComparer.OrdinalIgnoreCase);

        lock (this.syncRoot)
        {
            this.records = snapshot;
        }
    }

    private static string NormalizeLoginName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string loginName = value.Trim();
        int slash = loginName.LastIndexOf('\\');
        if (slash >= 0 && slash < loginName.Length - 1)
        {
            loginName = loginName[(slash + 1)..];
        }

        int at = loginName.IndexOf('@');
        if (at > 0)
        {
            loginName = loginName[..at];
        }

        return loginName.Trim().ToLowerInvariant();
    }
}

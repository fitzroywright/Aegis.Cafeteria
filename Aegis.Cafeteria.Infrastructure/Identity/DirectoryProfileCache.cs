namespace Aegis.Cafeteria.Infrastructure.Identity;

using Common.Security.Models;

public sealed class DirectoryProfileCache
{
    private readonly object syncRoot = new();
    private IReadOnlyDictionary<string, DirectoryUserProfile> records =
        new Dictionary<string, DirectoryUserProfile>(StringComparer.OrdinalIgnoreCase);

    public DirectoryUserProfile? Get(string? loginName)
    {
        string normalized = NormalizeLoginName(loginName);
        if (normalized.Length == 0)
        {
            return null;
        }

        lock (this.syncRoot)
        {
            return this.records.TryGetValue(normalized, out DirectoryUserProfile? record)
                ? record
                : null;
        }
    }

    public void Replace(IReadOnlyDictionary<string, DirectoryUserProfile> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        Dictionary<string, DirectoryUserProfile> snapshot =
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

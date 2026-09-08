namespace Aegis.Cafeteria.Infrastructure.ActiveDirectory;

using System.DirectoryServices.Protocols;
using System.Net;

public sealed record ActiveDirectoryEmployeeRecord(
    string LoginName,
    string EmailAddress,
    string Department,
    string Extension,
    string Manager);

public sealed class ActiveDirectoryEmployeeDirectory
{
    private const string SearchBase = "DC=fftpj,DC=local";
    private static readonly string[] Servers = ["jmad01.fftpj.local", "jmad02.fftpj.local"];

    public IReadOnlyDictionary<string, ActiveDirectoryEmployeeRecord> GetByLoginNames(IEnumerable<string> loginNames)
    {
        string[] requested = loginNames
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeLoginName)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (requested.Length == 0)
        {
            return new Dictionary<string, ActiveDirectoryEmployeeRecord>(StringComparer.OrdinalIgnoreCase);
        }

        Exception? lastException = null;
        foreach (string server in Servers)
        {
            try
            {
                return ReadFromServer(server, requested);
            }
            catch (Exception exception) when (exception is LdapException or InvalidOperationException)
            {
                lastException = exception;
            }
        }

        if (lastException is not null)
        {
            throw new InvalidOperationException("Unable to query Active Directory employee metadata from the configured domain controllers.", lastException);
        }

        return new Dictionary<string, ActiveDirectoryEmployeeRecord>(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, ActiveDirectoryEmployeeRecord> ReadFromServer(string server, IReadOnlyCollection<string> loginNames)
    {
        LdapDirectoryIdentifier identifier = new(server, 636, true, false);
        using LdapConnection connection = new(identifier)
        {
            AuthType = AuthType.Negotiate,
            Timeout = TimeSpan.FromSeconds(5)
        };

        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.SecureSocketLayer = true;
        connection.Bind();

        string filter = loginNames.Count == 1
            ? $"(&(objectCategory=person)(objectClass=user)(sAMAccountName={EscapeFilter(loginNames.Single())}))"
            : $"(&(objectCategory=person)(objectClass=user)(|{string.Concat(loginNames.Select(value => $"(sAMAccountName={EscapeFilter(value)})"))}))";

        SearchRequest request = new(
            SearchBase,
            filter,
            SearchScope.Subtree,
            "sAMAccountName",
            "mail",
            "department",
            "ipPhone",
            "telephoneNumber",
            "manager");

        SearchResponse response = (SearchResponse)connection.SendRequest(request);
        Dictionary<string, ActiveDirectoryEmployeeRecord> result = new(StringComparer.OrdinalIgnoreCase);

        foreach (SearchResultEntry entry in response.Entries)
        {
            string loginName = ReadString(entry, "sAMAccountName");
            if (loginName.Length == 0)
            {
                continue;
            }

            string manager = ResolveManagerDisplayName(connection, ReadString(entry, "manager"));
            string extension = FirstNonBlank(ReadString(entry, "ipPhone"), ReadString(entry, "telephoneNumber"));

            result[NormalizeLoginName(loginName)] = new ActiveDirectoryEmployeeRecord(
                NormalizeLoginName(loginName),
                ReadString(entry, "mail"),
                ReadString(entry, "department"),
                extension,
                manager);
        }

        return result;
    }

    private static string ResolveManagerDisplayName(LdapConnection connection, string managerDistinguishedName)
    {
        if (string.IsNullOrWhiteSpace(managerDistinguishedName))
        {
            return string.Empty;
        }

        try
        {
            SearchRequest request = new(
                managerDistinguishedName,
                "(objectClass=*)",
                SearchScope.Base,
                "displayName");
            SearchResponse response = (SearchResponse)connection.SendRequest(request);
            return response.Entries.Count == 0 ? string.Empty : ReadString(response.Entries[0], "displayName");
        }
        catch (LdapException)
        {
            return string.Empty;
        }
    }

    private static string ReadString(SearchResultEntry entry, string attributeName)
    {
        DirectoryAttribute? attribute = entry.Attributes[attributeName];
        if (attribute is null || attribute.Count == 0)
        {
            return string.Empty;
        }

        object? value = attribute[0];
        return value switch
        {
            byte[] bytes => System.Text.Encoding.UTF8.GetString(bytes).Trim(),
            _ => Convert.ToString(value)?.Trim() ?? string.Empty
        };
    }

    private static string FirstNonBlank(string first, string second)
        => string.IsNullOrWhiteSpace(first) ? second : first;

    private static string NormalizeLoginName(string value)
    {
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

    private static string EscapeFilter(string value)
        => value
            .Replace("\\", "\\5c", StringComparison.Ordinal)
            .Replace("*", "\\2a", StringComparison.Ordinal)
            .Replace("(", "\\28", StringComparison.Ordinal)
            .Replace(")", "\\29", StringComparison.Ordinal)
            .Replace("\0", "\\00", StringComparison.Ordinal);
}

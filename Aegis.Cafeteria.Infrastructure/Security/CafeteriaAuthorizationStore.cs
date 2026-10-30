namespace Aegis.Cafeteria.Infrastructure.Security;

using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Common.Security.Abstractions;
using Common.Security.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;

public static class CafeteriaRoleNames
{
    public const string Operator = "Operator";
    public const string Manager = "Manager";
}

public static class CafeteriaPermissions
{
    public const string Access = "cafeteria.access";
    public const string ManagerAuthorize = "cafeteria.manager.authorize";
}

public sealed class CafeteriaAuthorizationStore : IAuthorizationStore
{
    private readonly IDbContextFactory<LocalCafeteriaDbContext> localFactory;
    private readonly OperatorSecurityOptions options;

    public CafeteriaAuthorizationStore(
        IDbContextFactory<LocalCafeteriaDbContext> localFactory,
        OperatorSecurityOptions options)
    {
        this.localFactory = localFactory ?? throw new ArgumentNullException(nameof(localFactory));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        await using LocalCafeteriaDbContext db = await localFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS CafeteriaSecurityRoles
            (
                RoleKey TEXT NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS CafeteriaSecurityRolePermissions
            (
                RoleKey TEXT NOT NULL,
                PermissionKey TEXT NOT NULL,
                PRIMARY KEY(RoleKey, PermissionKey),
                FOREIGN KEY(RoleKey) REFERENCES CafeteriaSecurityRoles(RoleKey) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS CafeteriaSecurityUserRoles
            (
                UserId TEXT NOT NULL,
                RoleKey TEXT NOT NULL,
                PRIMARY KEY(UserId, RoleKey),
                FOREIGN KEY(RoleKey) REFERENCES CafeteriaSecurityRoles(RoleKey) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS CafeteriaSecurityDirectoryGroupRoles
            (
                GroupName TEXT NOT NULL,
                RoleKey TEXT NOT NULL,
                PRIMARY KEY(GroupName, RoleKey),
                FOREIGN KEY(RoleKey) REFERENCES CafeteriaSecurityRoles(RoleKey) ON DELETE CASCADE
            );
            """,
            cancellationToken);

        await ExecuteAsync(db, "INSERT OR IGNORE INTO CafeteriaSecurityRoles(RoleKey,Name) VALUES($role,$name);", cancellationToken,
            ("$role", CafeteriaRoleNames.Operator), ("$name", "POS Operator"));
        await ExecuteAsync(db, "INSERT OR IGNORE INTO CafeteriaSecurityRoles(RoleKey,Name) VALUES($role,$name);", cancellationToken,
            ("$role", CafeteriaRoleNames.Manager), ("$name", "Cafeteria Manager"));
        await ExecuteAsync(db, "INSERT OR IGNORE INTO CafeteriaSecurityRolePermissions(RoleKey,PermissionKey) VALUES($role,$permission);", cancellationToken,
            ("$role", CafeteriaRoleNames.Operator), ("$permission", CafeteriaPermissions.Access));
        await ExecuteAsync(db, "INSERT OR IGNORE INTO CafeteriaSecurityRolePermissions(RoleKey,PermissionKey) VALUES($role,$permission);", cancellationToken,
            ("$role", CafeteriaRoleNames.Manager), ("$permission", CafeteriaPermissions.Access));
        await ExecuteAsync(db, "INSERT OR IGNORE INTO CafeteriaSecurityRolePermissions(RoleKey,PermissionKey) VALUES($role,$permission);", cancellationToken,
            ("$role", CafeteriaRoleNames.Manager), ("$permission", CafeteriaPermissions.ManagerAuthorize));

        foreach (string group in options.AdministrativeGroups.Where(group => !string.IsNullOrWhiteSpace(group)))
        {
            await ExecuteAsync(db,
                "INSERT OR IGNORE INTO CafeteriaSecurityDirectoryGroupRoles(GroupName,RoleKey) VALUES($group,$role);",
                cancellationToken,
                ("$group", group.Trim()),
                ("$role", CafeteriaRoleNames.Manager));
        }
    }

    public async Task<IReadOnlyCollection<string>> GetDirectRoleKeysAsync(string subjectId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using LocalCafeteriaDbContext db = await localFactory.CreateDbContextAsync(cancellationToken);
        return await QuerySingleColumnAsync(db,
            "SELECT RoleKey FROM CafeteriaSecurityUserRoles WHERE lower(UserId)=lower($user) ORDER BY RoleKey;",
            cancellationToken,
            ("$user", Normalize(subjectId)));
    }

    public async Task<IReadOnlyCollection<string>> GetRoleKeysForGroupsAsync(IReadOnlyCollection<string> groupNames, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        if (groupNames.Count == 0) return Array.Empty<string>();

        HashSet<string> results = new(StringComparer.OrdinalIgnoreCase);
        await using LocalCafeteriaDbContext db = await localFactory.CreateDbContextAsync(cancellationToken);
        foreach (string groupName in groupNames.Where(group => !string.IsNullOrWhiteSpace(group)))
        {
            IReadOnlyCollection<string> roles = await QuerySingleColumnAsync(db,
                "SELECT RoleKey FROM CafeteriaSecurityDirectoryGroupRoles WHERE lower(GroupName)=lower($group);",
                cancellationToken,
                ("$group", groupName.Trim()));
            results.UnionWith(roles);
        }
        return results.ToArray();
    }

    public async Task<IReadOnlyCollection<RoleDefinition>> GetRolesAsync(IReadOnlyCollection<string> roleKeys, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        List<RoleDefinition> roles = [];
        await using LocalCafeteriaDbContext db = await localFactory.CreateDbContextAsync(cancellationToken);
        foreach (string roleKey in roleKeys.Where(role => !string.IsNullOrWhiteSpace(role)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            IReadOnlyCollection<string> permissions = await QuerySingleColumnAsync(db,
                "SELECT PermissionKey FROM CafeteriaSecurityRolePermissions WHERE lower(RoleKey)=lower($role) ORDER BY PermissionKey;",
                cancellationToken,
                ("$role", roleKey.Trim()));
            roles.Add(new RoleDefinition(roleKey.Trim(), roleKey.Trim(), permissions));
        }
        return roles;
    }

    private static async Task ExecuteAsync(
        LocalCafeteriaDbContext db,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        DbConnection connection = db.Database.GetDbConnection();
        bool close = connection.State != System.Data.ConnectionState.Open;
        if (close) await connection.OpenAsync(cancellationToken);
        try
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            foreach ((string name, object value) in parameters)
            {
                DbParameter parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (close) await connection.CloseAsync();
        }
    }

    private static async Task<IReadOnlyCollection<string>> QuerySingleColumnAsync(
        LocalCafeteriaDbContext db,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        DbConnection connection = db.Database.GetDbConnection();
        bool close = connection.State != System.Data.ConnectionState.Open;
        if (close) await connection.OpenAsync(cancellationToken);
        try
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            foreach ((string name, object value) in parameters)
            {
                DbParameter parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }

            List<string> values = [];
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0)) values.Add(reader.GetString(0));
            }
            return values;
        }
        finally
        {
            if (close) await connection.CloseAsync();
        }
    }

    private static string Normalize(string value)
    {
        string result = value.Trim();
        int slash = result.LastIndexOf('\\');
        if (slash >= 0 && slash < result.Length - 1) result = result[(slash + 1)..];
        int at = result.IndexOf('@');
        if (at > 0) result = result[..at];
        return result.ToLowerInvariant();
    }
}

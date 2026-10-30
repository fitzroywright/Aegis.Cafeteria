namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Application.Security;
using Common.Security.Abstractions;
using Common.Security.Integration;
using Common.Security.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

internal sealed class SecurityAccountFixture : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "cafeteria-security-" + Guid.NewGuid().ToString("N") + ".db");
    internal RelationalAccountStore Accounts { get; }
    internal IDbContextFactory<LocalCafeteriaDbContext> Factory { get; }
    internal SecurityAccountFixture()
    {
        string connection = $"Data Source={path};Pooling=False;Foreign Keys=True";
        Accounts = new RelationalAccountStore(() => new SqliteConnection(connection), "Aegis.Cafeteria");
        Accounts.EnsureSchema();
        Factory = new LocalDbContextFactory(new DbContextOptionsBuilder<LocalCafeteriaDbContext>().UseSqlite(connection).Options);
    }
    internal VerifiedIdentity Identity(string alias = "fwright", string subject = "immutable-directory-user", params string[] groups)
        => new(new ExternalIdentityKey("active-directory", "immutable-directory", subject), AuthenticationProviderKind.ActiveDirectory,
            alias, alias == "fwright" ? "Fitzroy Wright" : alias, DateTimeOffset.UtcNow, "ldaps-password", groups: groups);
    internal SecurityAccount Approve(VerifiedIdentity identity, string id = "approved-account")
        => Accounts.Create(new AccountCreation(id, identity.LoginName, identity.DisplayName, identity.ProviderKind,
            AccountPopulation.Operator, SecurityAccountStatus.Active, identity.Key), "test-administrator");
    internal OperatorIdentity Session(SecurityAccount account, bool manager = true)
        => new(account.LoginName, account.DisplayName, manager, DateTimeOffset.UtcNow, account.Id, account.SessionVersion);
    public void Dispose() => File.Delete(path);
}

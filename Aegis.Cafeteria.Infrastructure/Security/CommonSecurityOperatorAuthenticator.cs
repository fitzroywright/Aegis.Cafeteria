namespace Aegis.Cafeteria.Infrastructure.Security;

using Aegis.Cafeteria.Application.Security;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Common.Security.Abstractions;
using Common.Security.Authorization;
using Common.Security.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Authenticates cafeteria operators through Common.Security. AD/LDAPS proves
/// identity and returns group membership; the Cafeteria database owns the
/// application roles and permissions used to decide manager authority.
/// </summary>
public sealed class CommonSecurityOperatorAuthenticator : IOperatorAuthenticator
{
    private readonly IAuthenticator authenticator;
    private readonly ILogger<CommonSecurityOperatorAuthenticator> logger;
    private readonly CafeteriaAuthorizationStore? authorizationStore;


    public CommonSecurityOperatorAuthenticator(
        IAuthenticator authenticator,
        ILogger<CommonSecurityOperatorAuthenticator> logger)
    {
        this.authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public CommonSecurityOperatorAuthenticator(
        IAuthenticator authenticator,
        IDbContextFactory<LocalCafeteriaDbContext> localFactory,
        OperatorSecurityOptions options,
        ILogger<CommonSecurityOperatorAuthenticator> logger)
    {
        this.authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        authorizationStore = new CafeteriaAuthorizationStore(
            localFactory ?? throw new ArgumentNullException(nameof(localFactory)),
            options ?? throw new ArgumentNullException(nameof(options)));
    }

    public async Task<OperatorIdentity?> AuthenticateAsync(
        string loginName,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(loginName) || string.IsNullOrWhiteSpace(password))
            return null;

        try
        {
            AuthenticationResult result = authenticator.Authenticate(loginName, password);

            if (!result.IsAuthenticated || result.UserInfo is null)
                return null;

            string userId = Normalize(loginName);
            string displayName = string.IsNullOrWhiteSpace(result.UserInfo.DisplayName)
                ? userId
                : result.UserInfo.DisplayName.Trim();

            bool isManager;
            if (authorizationStore is not null)
            {
                await authorizationStore.EnsureDefaultOperatorRoleAsync(userId, cancellationToken);
                PermissionAuthorizer authorizer = new(authorizationStore);
                AuthorizationDecision decision = await authorizer.AuthorizeAsync(
                    AuthorizationSubject.Create(userId, result.GroupNames),
                    CafeteriaPermissions.ManagerAuthorize,
                    cancellationToken);
                isManager = decision.Allowed;
            }
            else
            {
                // Compatibility for direct unit construction only. The DI runtime
                // constructor above always uses the database-backed authorization store.
                isManager = result.IsAdministrative;
            }

            logger.LogInformation(
                "Cafeteria operator {UserId} authenticated. Manager={IsManager}",
                userId,
                isManager);

            return new OperatorIdentity(
                userId,
                displayName,
                isManager,
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Common.Security authentication or authorization failed for {LoginName}",
                Normalize(loginName));
            return null;
        }
    }

    private static string Normalize(string loginName)
    {
        string value = loginName.Trim();

        int slash = value.LastIndexOf('\\');
        if (slash >= 0 && slash < value.Length - 1)
            value = value[(slash + 1)..];

        int at = value.IndexOf('@');
        if (at > 0)
            value = value[..at];

        return value.ToLowerInvariant();
    }

}

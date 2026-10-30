namespace Aegis.Cafeteria.Infrastructure.Security;

using Aegis.Cafeteria.Application.Security;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Common.Security.Abstractions;
using Common.Security.Authorization;
using Common.Security.Models;
using Common.Security.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public sealed class CommonSecurityOperatorAuthenticator(
    IAuthenticator authenticator,
    IDbContextFactory<LocalCafeteriaDbContext> localFactory,
    OperatorSecurityOptions options,
    IAccountStore accounts,
    ILogger<CommonSecurityOperatorAuthenticator> logger) : IOperatorAuthenticator
{
    public async Task<OperatorIdentity?> AuthenticateAsync(string loginName, string password, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(loginName) || string.IsNullOrWhiteSpace(password)) return null;
        AuthenticationResult result;
        try {result=authenticator.Authenticate(loginName,password);}
        catch(Exception exception) {logger.LogWarning(exception,"Operator provider authentication failed");return null;}
        return result.IsAuthenticated ? await AuthorizeVerifiedIdentityAsync(result.Identity!,cancellationToken) : null;
    }

    public async Task<OperatorIdentity?> AuthorizeVerifiedIdentityAsync(VerifiedIdentity identity,CancellationToken cancellationToken=default)
    {
        try
        {
            SecurityAccount account = new AccountProvisioningService(accounts).Provision(identity, AccountPopulation.Operator).Account;
            if (account.Status != SecurityAccountStatus.Active)
            {
                logger.LogInformation("Cafeteria operator account {AccountId} is Pending Access or disabled", account.Id);
                return null;
            }
            var roles = new CafeteriaAuthorizationStore(localFactory, options);
            await roles.EnsureInitializedAsync(cancellationToken);
            var authorizer = new PermissionAuthorizer(roles);
            var subject = AuthorizationSubject.Create(account.LoginName, identity.GroupNames);
            if (!(await authorizer.AuthorizeAsync(subject, CafeteriaPermissions.Access, cancellationToken)).Allowed) return null;
            bool manager = (await authorizer.AuthorizeAsync(subject, CafeteriaPermissions.ManagerAuthorize, cancellationToken)).Allowed;
            logger.LogInformation("Cafeteria operator {UserId} authenticated. Manager={IsManager}", account.LoginName, manager);
            return new OperatorIdentity(account.LoginName, account.DisplayName, manager, identity.AuthenticatedAt,
                account.Id, account.SessionVersion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Cafeteria operator authentication or application authorization failed");
            return null;
        }
    }
}

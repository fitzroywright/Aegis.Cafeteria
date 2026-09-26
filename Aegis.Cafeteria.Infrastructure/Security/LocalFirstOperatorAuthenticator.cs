namespace Aegis.Cafeteria.Infrastructure.Security;

using Aegis.Cafeteria.Application.Security;
using Microsoft.Extensions.Logging;

/// <summary>
/// Authenticates the operator locally through Common.Security first so Cafeteria Services
/// can never block POS login. After a successful local login, it establishes the
/// Cafeteria Services POS session token in the background using the same credentials.
/// </summary>
public sealed class LocalFirstOperatorAuthenticator(
    CommonSecurityOperatorAuthenticator localAuthenticator,
    ServicesOperatorAuthenticator? servicesAuthenticator,
    ILogger<LocalFirstOperatorAuthenticator> logger) : IOperatorAuthenticator
{
    public async Task<OperatorIdentity?> AuthenticateAsync(
        string loginName,
        string password,
        CancellationToken cancellationToken = default)
    {
        OperatorIdentity? local = await localAuthenticator.AuthenticateAsync(
            loginName,
            password,
            cancellationToken);

        if (local is null)
        {
            return null;
        }

        if (servicesAuthenticator is not null)
        {
            _ = Task.Run(() => EstablishServicesSessionAsync(loginName, password));
        }

        return local;
    }

    private async Task EstablishServicesSessionAsync(string loginName, string password)
    {
        try
        {
            _ = await servicesAuthenticator!.AuthenticateAsync(loginName, password);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Cafeteria Services session token could not be established for {LoginName}; POS remains available locally",
                loginName);
        }
    }
}

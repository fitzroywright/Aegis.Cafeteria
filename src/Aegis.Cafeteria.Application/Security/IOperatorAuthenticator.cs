namespace Aegis.Cafeteria.Application.Security;

public interface IOperatorAuthenticator
{
    Task<OperatorIdentity?> AuthenticateAsync(
        string loginName,
        string password,
        CancellationToken cancellationToken = default);
}

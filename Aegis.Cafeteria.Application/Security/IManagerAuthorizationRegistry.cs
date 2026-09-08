namespace Aegis.Cafeteria.Application.Security;

public interface IManagerAuthorizationRegistry
{
    void Grant(OperatorIdentity manager);
    bool IsAuthorized(string userId);
}

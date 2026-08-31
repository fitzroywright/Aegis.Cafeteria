namespace Aegis.Cafeteria.Application.Security;

public interface IOperatorSession
{
    OperatorIdentity? Current { get; }
    bool IsAuthenticated { get; }
    void SignIn(OperatorIdentity identity);
    void SignOut();
}

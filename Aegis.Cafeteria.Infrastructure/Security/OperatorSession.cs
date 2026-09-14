namespace Aegis.Cafeteria.Infrastructure.Security;

using Aegis.Cafeteria.Application.Security;

public sealed class OperatorSession : IOperatorSession
{
    private OperatorIdentity? current;
    public OperatorIdentity? Current => current;
    public bool IsAuthenticated => current is not null;

    public void SignIn(OperatorIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        current = identity;
    }

    public void SignOut() => current = null;
}

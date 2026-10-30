namespace Aegis.Cafeteria.Infrastructure.Security;
using Aegis.Cafeteria.Application.Security;
using Common.Security.Abstractions;
using Common.Security.Services;

public sealed class OperatorSession(IAccountStore accounts,TimeProvider? clock=null) : IOperatorSession
{
    private readonly OpaqueAccountSessions<OperatorIdentity> sessions=new(accounts,clock);
    private string? token;
    public OperatorIdentity? Current=>token is null ? null : sessions.Find(token)?.Identity;
    public bool IsAuthenticated=>Current is not null;
    public void SignIn(OperatorIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        SignOut();token=sessions.Create(identity.AccountId,identity.SessionVersion,identity,TimeSpan.FromHours(8)).Token;
    }
    public void SignOut() {if(token is not null)sessions.Remove(token);token=null;}
}

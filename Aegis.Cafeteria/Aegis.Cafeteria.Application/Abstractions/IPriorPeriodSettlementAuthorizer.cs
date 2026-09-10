namespace Aegis.Cafeteria.Application.Abstractions;

public interface IPriorPeriodSettlementAuthorizer
{
    bool CanApprove(string userId);
}

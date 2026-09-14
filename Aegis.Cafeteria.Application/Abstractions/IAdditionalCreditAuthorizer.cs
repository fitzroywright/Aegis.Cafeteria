namespace Aegis.Cafeteria.Application.Abstractions;

public interface IAdditionalCreditAuthorizer
{
    bool CanApprove(string userId);
}

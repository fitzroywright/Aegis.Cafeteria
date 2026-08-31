namespace Aegis.Cafeteria.Application.Abstractions;

public interface IAccountPeriodCloseAuthorizer
{
    bool CanClose(string userId);
}

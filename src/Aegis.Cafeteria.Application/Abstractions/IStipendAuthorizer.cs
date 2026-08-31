namespace Aegis.Cafeteria.Application.Abstractions;

public interface IStipendAuthorizer
{
    bool CanProcessStipend(string userId);
}

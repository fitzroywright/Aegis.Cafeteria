namespace Aegis.Cafeteria.Application.Abstractions;

public interface IOrderVoidAuthorizer
{
    bool CanVoid(string userId);
}

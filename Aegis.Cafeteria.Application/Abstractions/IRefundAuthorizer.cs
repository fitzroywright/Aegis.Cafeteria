namespace Aegis.Cafeteria.Application.Abstractions;

public interface IRefundAuthorizer
{
    bool CanRefund(string userId);
}

namespace Aegis.Cafeteria.Application.Orders;

using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Orders;

public interface IPostSaleObserver
{
    Task OnSaleCompletedAsync
    (
          CustomerAccountView account
        , Order order
        , CancellationToken cancellationToken = default
    );
}

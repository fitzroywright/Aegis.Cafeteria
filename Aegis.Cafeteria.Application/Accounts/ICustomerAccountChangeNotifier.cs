namespace Aegis.Cafeteria.Application.Accounts;

public interface ICustomerAccountChangeNotifier
{
    IDisposable Subscribe(Func<int, Task> handler);

    Task NotifyAsync
    (
        int customerId
    );
}

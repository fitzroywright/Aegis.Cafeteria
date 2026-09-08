namespace Aegis.Cafeteria.UnitTests.TestDoubles;

using Aegis.Cafeteria.Application.Abstractions;

internal sealed class AllowVoidAuthorizer : IOrderVoidAuthorizer
{
    public bool CanVoid(string userId) => true;
}

internal sealed class DenyVoidAuthorizer : IOrderVoidAuthorizer
{
    public bool CanVoid(string userId) => false;
}

internal sealed class AllowRefundAuthorizer : IRefundAuthorizer
{
    public bool CanRefund(string userId) => true;
}

internal sealed class DenyRefundAuthorizer : IRefundAuthorizer
{
    public bool CanRefund(string userId) => false;
}

internal sealed class AllowCloseAuthorizer : IAccountPeriodCloseAuthorizer
{
    public bool CanClose(string userId) => true;
}

internal sealed class DenyCloseAuthorizer : IAccountPeriodCloseAuthorizer
{
    public bool CanClose(string userId) => false;
}

internal sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly HashSet<Guid> _claimed = [];
    private readonly HashSet<Guid> _completed = [];

    public Task<bool> TryBeginAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        if (_claimed.Contains(operationId) || _completed.Contains(operationId)) return Task.FromResult(false);
        _claimed.Add(operationId);
        return Task.FromResult(true);
    }

    public Task CompleteAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        _claimed.Remove(operationId);
        _completed.Add(operationId);
        return Task.CompletedTask;
    }

    public Task AbandonAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        _claimed.Remove(operationId);
        return Task.CompletedTask;
    }
}

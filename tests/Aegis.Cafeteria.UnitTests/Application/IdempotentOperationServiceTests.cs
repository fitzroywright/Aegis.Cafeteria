namespace Aegis.Cafeteria.UnitTests.Application;

using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.UnitTests.TestDoubles;

public sealed class IdempotentOperationServiceTests
{
    [Fact]
    public async Task ExecuteAsync_SameOperationId_RunsOnlyOnce()
    {
        var service = new IdempotentOperationService(new InMemoryIdempotencyStore());
        var id = Guid.NewGuid();
        var calls = 0;
        var first = await service.ExecuteAsync(id, () => { calls++; return Task.CompletedTask; });
        var second = await service.ExecuteAsync(id, () => { calls++; return Task.CompletedTask; });
        Assert.True(first);
        Assert.False(second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ExecuteAsync_WhenOperationFails_AllowsRetry()
    {
        var service = new IdempotentOperationService(new InMemoryIdempotencyStore());
        var id = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(id, () => throw new InvalidOperationException("failure")));
        var retried = await service.ExecuteAsync(id, () => Task.CompletedTask);
        Assert.True(retried);
    }
}

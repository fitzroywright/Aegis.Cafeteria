namespace Aegis.Cafeteria.UnitTests.Integration;

using Aegis.Cafeteria.Application.Integration;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class BatchedEmployeeRefreshTests
{
    [Fact]
    public async Task BackgroundRefresh_ReturnsImmediately_RejectsOverlap_AndOutlivesItsCaller()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<CustomerReferenceBatch> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeSource source = new((cursor, ct) => { entered.SetResult(); return release.Task; });
        FakeCache cache = new();
        ReferenceDataRefreshService refresh = new(source, cache, new FakeStatus(), NullLogger<ReferenceDataRefreshService>.Instance);
        Assert.True(refresh.StartBackgroundRefresh(new DateOnly(2026, 9, 28)));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.True(refresh.BackgroundStatus.Running);
            Assert.False(refresh.StartBackgroundRefresh(new DateOnly(2026, 9, 28)));
        }
        finally
        {
            release.SetResult(new CustomerReferenceBatch([new() { CustomerId = 17 }], null));
            await refresh.WaitForBackgroundRefreshAsync();
        }
        Assert.True(cache.Completed);
        Assert.False(refresh.BackgroundStatus.Running);
        Assert.Contains("Refresh complete", refresh.BackgroundStatus.Message);
    }

    [Fact]
    public async Task BackgroundRefresh_RecordsFailureWithoutAnUnobservedTaskException()
    {
        FakeSource source = new((cursor, ct) => Task.FromException<CustomerReferenceBatch>(new TimeoutException("Test timeout")));
        ReferenceDataRefreshService refresh = new(source, new FakeCache(), new FakeStatus(), NullLogger<ReferenceDataRefreshService>.Instance);
        refresh.StartBackgroundRefresh(new DateOnly(2026, 9, 28));
        await refresh.WaitForBackgroundRefreshAsync();
        Assert.False(refresh.BackgroundStatus.Running);
        Assert.Contains("Test timeout", refresh.BackgroundStatus.Message);
    }

    [Fact]
    public async Task Refresh_SavesEachBatchBeforeFetchingNext_AndFinalizesOnlyAtEnd()
    {
        FakeCache cache = new();
        FakeStatus status = new();
        FakeSource source = new(async (cursor, ct) =>
        {
            await Task.Yield();
            if (cursor is null)
            {
                Assert.Empty(cache.Saved);
                return new CustomerReferenceBatch([new() { CustomerId = 2 }, new() { CustomerId = 9 }], 9);
            }
            Assert.Equal(new[] { 2, 9 }, cache.Saved);
            Assert.False(cache.Completed);
            Assert.Equal(9, cursor);
            return new CustomerReferenceBatch([new() { CustomerId = 27 }], null);
        });
        ReferenceDataRefreshService refresh = new(source, cache, status, NullLogger<ReferenceDataRefreshService>.Instance);

        Assert.Equal(3, await refresh.RefreshCustomersAsync());
        Assert.True(cache.Completed);
        Assert.Equal(new[] { 2, 9, 27 }, cache.CompletedIds.Order());
        Assert.Equal(new[] { (2, 1), (3, 2) }, status.Progress);
        Assert.True(status.Succeeded);
    }

    [Fact]
    public async Task Failure_PreservesSavedBatch_AndDoesNotReconcileMissingEmployees()
    {
        FakeCache cache = new();
        FakeStatus status = new();
        FakeSource source = new((cursor, ct) => cursor is null
            ? Task.FromResult(new CustomerReferenceBatch([new() { CustomerId = 7 }], 7))
            : Task.FromException<CustomerReferenceBatch>(new TimeoutException("Batch timed out")));
        ReferenceDataRefreshService refresh = new(source, cache, status, NullLogger<ReferenceDataRefreshService>.Instance);

        await Assert.ThrowsAsync<TimeoutException>(() => refresh.RefreshCustomersAsync());
        Assert.Equal(new[] { 7 }, cache.Saved);
        Assert.False(cache.Completed);
        Assert.False(status.Succeeded);
        Assert.IsType<TimeoutException>(status.Error);
    }

    [Fact]
    public async Task InvalidCursor_IsRejectedBeforeSavingOrReconciling()
    {
        FakeCache cache = new();
        FakeSource source = new((cursor, ct) => Task.FromResult(new CustomerReferenceBatch([], 10)));
        ReferenceDataRefreshService refresh = new(source, cache, new FakeStatus(), NullLogger<ReferenceDataRefreshService>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => refresh.RefreshCustomersAsync());
        Assert.Empty(cache.Saved);
        Assert.False(cache.Completed);
    }

    [Fact]
    public async Task ConcurrentRefresh_IsRejected_WithoutChangingRunningRefreshStatus()
    {
        TaskCompletionSource<CustomerReferenceBatch> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeCache cache = new();
        FakeStatus status = new();
        FakeSource source = new((cursor, ct) => pending.Task);
        ReferenceDataRefreshService refresh = new(source, cache, status, NullLogger<ReferenceDataRefreshService>.Instance);
        Task<int> running = refresh.RefreshCustomersAsync();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => refresh.RefreshCustomersAsync());
            Assert.Null(status.Error);
            Assert.Equal(1, status.Starts);
        }
        finally
        {
            pending.SetResult(new CustomerReferenceBatch([], null));
            await running;
        }
    }

    [Fact]
    public async Task Cancellation_DoesNotFinalize_AndAllowsAnotherRefresh()
    {
        FakeCache cache = new();
        FakeStatus status = new();
        using CancellationTokenSource cancellation = new();
        FakeSource source = new((cursor, ct) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<CustomerReferenceBatch>(ct);
        });
        ReferenceDataRefreshService refresh = new(source, cache, status, NullLogger<ReferenceDataRefreshService>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh.RefreshCustomersAsync(cancellation.Token));
        Assert.False(cache.Completed);
        FakeSource emptySource = new((cursor, ct) => Task.FromResult(new CustomerReferenceBatch([], null)));
        ReferenceDataRefreshService retry = new(emptySource, cache, status, NullLogger<ReferenceDataRefreshService>.Instance);
        Assert.Equal(0, await retry.RefreshCustomersAsync());
        Assert.True(cache.Completed);
    }

    private sealed class FakeSource(Func<int?, CancellationToken, Task<CustomerReferenceBatch>> read) : IWorkforceReferenceSource, IPagedWorkforceReferenceSource
    {
        public Task<CustomerReferenceBatch> GetCustomerBatchAsync(int? afterCustomerId, int take, CancellationToken cancellationToken = default)
        {
            Assert.Equal(10, take);
            return read(afterCustomerId, cancellationToken);
        }
        public Task<IReadOnlyList<ExternalCustomerSnapshot>> GetCustomersAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Unbounded read must not be used.");
        public Task<IReadOnlyCollection<DateOnly>> GetHolidaysAsync(int year, int month, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<DateOnly>>([]);
    }

    private sealed class FakeCache : IWorkforceReferenceCache, IBatchedWorkforceReferenceCache
    {
        public List<int> Saved { get; } = [];
        public HashSet<int> CompletedIds { get; } = [];
        public bool Completed { get; private set; }
        public Task UpsertCustomerBatchAsync(IReadOnlyCollection<ExternalCustomerSnapshot> customers, IReadOnlySet<int> previouslyRefreshedIds, CancellationToken cancellationToken = default)
        {
            Assert.True(previouslyRefreshedIds.SetEquals(Saved));
            Saved.AddRange(customers.Select(customer => customer.CustomerId));
            return Task.CompletedTask;
        }
        public Task CompleteCustomerRefreshAsync(IReadOnlySet<int> customerIds, CancellationToken cancellationToken = default)
        {
            Completed = true;
            CompletedIds.UnionWith(customerIds);
            return Task.CompletedTask;
        }
        public Task ReplaceCustomersAsync(IReadOnlyCollection<ExternalCustomerSnapshot> customers, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Full replacement must not be used.");
        public Task ReplaceHolidaysAsync(int year, int month, IReadOnlyCollection<DateOnly> holidays, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeStatus : ICustomerReferenceCacheStatus
    {
        public int Starts { get; private set; }
        public bool Succeeded { get; private set; }
        public Exception? Error { get; private set; }
        public List<(int, int)> Progress { get; } = [];
        public Task<CustomerReferenceCacheStatus> GetAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void RefreshStarted() => Starts++;
        public void RefreshProgress(int employeeCount, int batchCount) => Progress.Add((employeeCount, batchCount));
        public void RefreshSucceeded(int employeeCount) => Succeeded = true;
        public void RefreshFailed(Exception exception) => Error = exception;
    }
}

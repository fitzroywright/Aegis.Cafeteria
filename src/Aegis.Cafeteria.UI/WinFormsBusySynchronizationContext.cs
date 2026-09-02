namespace Aegis.Cafeteria.UI;

internal sealed class WinFormsBusySynchronizationContext : SynchronizationContext, IDisposable
{
    private readonly SynchronizationContext? previousContext;
    private readonly SynchronizationContext innerContext;
    private int pendingOperations;
    private int disposed;

    private WinFormsBusySynchronizationContext
    (
          SynchronizationContext? previousContext
        , SynchronizationContext innerContext
    )
    {
        this.previousContext = previousContext;
        this.innerContext = innerContext;
    }

    public static WinFormsBusySynchronizationContext Install()
    {
        SynchronizationContext? previousContext = SynchronizationContext.Current;
        SynchronizationContext innerContext = previousContext ?? new WindowsFormsSynchronizationContext();
        WinFormsBusySynchronizationContext context = new(previousContext, innerContext);
        SynchronizationContext.SetSynchronizationContext(context);
        return context;
    }

    public override void Post(SendOrPostCallback callback, object? state)
    {
        if (IsDisposed)
        {
            ThreadPool.QueueUserWorkItem(_ => callback(state));
            return;
        }

        try
        {
            this.innerContext.Post(callback, state);
        }
        catch (InvalidAsynchronousStateException)
        {
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
    }

    public override void Send(SendOrPostCallback callback, object? state)
    {
        if (IsDisposed)
        {
            callback(state);
            return;
        }

        try
        {
            this.innerContext.Send(callback, state);
        }
        catch (InvalidAsynchronousStateException)
        {
            callback(state);
        }
    }

    public override void OperationStarted()
    {
        if (IsDisposed)
        {
            return;
        }

        this.innerContext.OperationStarted();
        if (Interlocked.Increment(ref this.pendingOperations) == 1)
        {
            SetBusyState(true);
        }
    }

    public override void OperationCompleted()
    {
        if (IsDisposed)
        {
            return;
        }

        int remaining = Interlocked.Decrement(ref this.pendingOperations);
        if (remaining <= 0)
        {
            Interlocked.Exchange(ref this.pendingOperations, 0);
            SetBusyState(false);
        }

        try
        {
            this.innerContext.OperationCompleted();
        }
        catch (InvalidAsynchronousStateException)
        {
        }
    }

    public override SynchronizationContext CreateCopy()
    {
        return this;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref this.pendingOperations, 0);
        SetBusyState(false);
        SynchronizationContext.SetSynchronizationContext(this.previousContext);
    }

    private bool IsDisposed => Volatile.Read(ref this.disposed) != 0;

    private static void SetBusyState(bool isBusy)
    {
        foreach (Form form in System.Windows.Forms.Application.OpenForms)
        {
            if (!form.IsDisposed)
            {
                form.UseWaitCursor = isBusy;
            }
        }

        Cursor.Current = isBusy
            ? Cursors.WaitCursor
            : Cursors.Default;
    }
}

using System.ComponentModel;

namespace Aegis.Cafeteria.UI;

internal sealed class WinFormsBusySynchronizationContext : SynchronizationContext, IDisposable
{
    private readonly SynchronizationContext? previousContext;
    private readonly Control uiDispatcher;
    private int pendingOperations;
    private int disposed;

    private WinFormsBusySynchronizationContext
    (
          SynchronizationContext? previousContext
        , Control uiDispatcher
    )
    {
        this.previousContext = previousContext;
        this.uiDispatcher = uiDispatcher;
    }

    public static WinFormsBusySynchronizationContext Install()
    {
        SynchronizationContext? previousContext = SynchronizationContext.Current;

        // Create a dedicated WinForms handle on the UI thread before any async POS work starts.
        // Every continuation posted through this context is marshalled through this handle, so
        // there is no worker-thread fallback that can later touch WinForms controls directly.
        Control uiDispatcher = new();
        _ = uiDispatcher.Handle;

        WinFormsBusySynchronizationContext context = new(previousContext, uiDispatcher);
        SynchronizationContext.SetSynchronizationContext(context);
        return context;
    }

    public override void Post(SendOrPostCallback callback, object? state)
    {
        if (IsDisposed)
        {
            PostAfterDispose(callback, state);
            return;
        }

        try
        {
            this.uiDispatcher.BeginInvoke(() => callback(state));
        }
        catch (InvalidOperationException) when (IsDisposed || this.uiDispatcher.IsDisposed)
        {
            PostAfterDispose(callback, state);
        }
    }

    public override void Send(SendOrPostCallback callback, object? state)
    {
        if (IsDisposed)
        {
            SendAfterDispose(callback, state);
            return;
        }

        try
        {
            if (this.uiDispatcher.InvokeRequired)
            {
                this.uiDispatcher.Invoke(() => callback(state));
            }
            else
            {
                callback(state);
            }
        }
        catch (InvalidOperationException) when (IsDisposed || this.uiDispatcher.IsDisposed)
        {
            SendAfterDispose(callback, state);
        }
    }

    public override void OperationStarted()
    {
        if (IsDisposed)
        {
            return;
        }

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
        this.uiDispatcher.Dispose();
    }

    private bool IsDisposed => Volatile.Read(ref this.disposed) != 0;

    private void PostAfterDispose(SendOrPostCallback callback, object? state)
    {
        SynchronizationContext? context = this.previousContext;
        if (context is not null)
        {
            context.Post(callback, state);
            return;
        }

        ThreadPool.QueueUserWorkItem(_ => callback(state));
    }

    private void SendAfterDispose(SendOrPostCallback callback, object? state)
    {
        SynchronizationContext? context = this.previousContext;
        if (context is not null)
        {
            context.Send(callback, state);
            return;
        }

        callback(state);
    }

    private static void SetBusyState(bool isBusy)
    {
        Form[] forms = System.Windows.Forms.Application.OpenForms
            .Cast<Form>()
            .Where(form => !form.IsDisposed)
            .ToArray();

        foreach (Form form in forms)
        {
            if (form.InvokeRequired)
            {
                try
                {
                    form.BeginInvoke(() => form.UseWaitCursor = isBusy);
                }
                catch (InvalidOperationException)
                {
                }
            }
            else
            {
                form.UseWaitCursor = isBusy;
            }
        }

        if (forms.Any(form => !form.InvokeRequired))
        {
            Cursor.Current = isBusy
                ? Cursors.WaitCursor
                : Cursors.Default;
        }
    }
}

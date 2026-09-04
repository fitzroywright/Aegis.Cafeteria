using System.ComponentModel;

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
            PostToUiOrThreadPool(callback, state);
            return;
        }

        try
        {
            this.innerContext.Post(callback, state);
        }
        catch (InvalidAsynchronousStateException)
        {
            PostToUiOrThreadPool(callback, state);
        }
    }

    public override void Send(SendOrPostCallback callback, object? state)
    {
        if (IsDisposed)
        {
            SendToUiOrCurrentThread(callback, state);
            return;
        }

        try
        {
            this.innerContext.Send(callback, state);
        }
        catch (InvalidAsynchronousStateException)
        {
            SendToUiOrCurrentThread(callback, state);
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

    private static void PostToUiOrThreadPool(SendOrPostCallback callback, object? state)
    {
        Form? form = FindUiForm();
        if (form is not null && form.IsHandleCreated && !form.IsDisposed)
        {
            try
            {
                form.BeginInvoke(() => callback(state));
                return;
            }
            catch (InvalidOperationException)
            {
            }
        }

        ThreadPool.QueueUserWorkItem(_ => callback(state));
    }

    private static void SendToUiOrCurrentThread(SendOrPostCallback callback, object? state)
    {
        Form? form = FindUiForm();
        if (form is not null && form.IsHandleCreated && !form.IsDisposed)
        {
            try
            {
                if (form.InvokeRequired)
                {
                    form.Invoke(() => callback(state));
                }
                else
                {
                    callback(state);
                }
                return;
            }
            catch (InvalidOperationException)
            {
            }
        }

        callback(state);
    }

    private static Form? FindUiForm()
    {
        foreach (Form form in System.Windows.Forms.Application.OpenForms)
        {
            if (!form.IsDisposed && form.IsHandleCreated)
            {
                return form;
            }
        }

        return null;
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
namespace Aegis.Cafeteria.UI;

/// <summary>
/// Owns the splash window on a dedicated STA message-loop thread so async startup work
/// can safely update it without tying startup continuations to the WinForms UI thread.
/// </summary>
internal sealed class StartupSplashController : IDisposable
{
    private readonly ManualResetEventSlim created = new(false);
    private readonly Thread thread;
    private StartupSplashForm? form;
    private bool disposed;

    public StartupSplashController()
    {
        this.thread = new Thread(RunSplash)
        {
            IsBackground = true,
            Name = "Aegis.Cafeteria.StartupSplash"
        };
        this.thread.SetApartmentState(ApartmentState.STA);
        this.thread.Start();
        this.created.Wait();
    }

    public void Update(string status, string? detail = null)
    {
        var form = this.form;
        if (form is null || form.IsDisposed)
            return;

        try
        {
            form.BeginInvoke(() => form.SetStatus(status, detail));
        }
        catch (InvalidOperationException)
        {
            // Splash is already closing.
        }
    }

    public void Ready(string detail)
    {
        var form = this.form;
        if (form is null || form.IsDisposed)
            return;

        try
        {
            form.BeginInvoke(() => form.SetReady(detail));
        }
        catch (InvalidOperationException)
        {
            // Splash is already closing.
        }
    }

    public void Close()
    {
        var form = this.form;
        if (form is null || form.IsDisposed)
            return;

        try
        {
            form.BeginInvoke(form.Close);
        }
        catch (InvalidOperationException)
        {
            // Splash is already closing.
        }
    }

    private void RunSplash()
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        this.form = new StartupSplashForm();
        this.created.Set();
        System.Windows.Forms.Application.Run(this.form);
    }

    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;
        Close();

        if (this.thread.IsAlive)
            this.thread.Join(TimeSpan.FromSeconds(2));

        this.created.Dispose();
    }
}

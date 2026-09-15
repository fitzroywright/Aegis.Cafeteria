using Aegis.Cafeteria.Application.Security;

namespace Aegis.Cafeteria.UI;

public sealed class OperatorLoginForm : Form
{
    private static readonly Color HeaderBlue = Color.FromArgb(22, 66, 105);
    private static readonly Color AccentOrange = Color.FromArgb(235, 139, 40);
    private static readonly Color BodyText = Color.FromArgb(31, 44, 55);
    private static readonly Color MutedText = Color.FromArgb(105, 112, 118);

    private readonly IOperatorAuthenticator authenticator;
    private readonly IOperatorSession session;
    private readonly Action<string, string?>? showStartupProgress;
    private readonly Action? hideStartupProgress;
    private readonly TextBox userName = new() { Width = 310, Height = 32, BorderStyle = BorderStyle.FixedSingle, TabIndex = 0 };
    private readonly TextBox password = new() { Width = 310, Height = 32, BorderStyle = BorderStyle.FixedSingle, UseSystemPasswordChar = true, TabIndex = 1 };
    private readonly Button signIn = new() { Text = "Sign In", Width = 120, Height = 38, FlatStyle = FlatStyle.Flat, BackColor = HeaderBlue, ForeColor = Color.White, TabIndex = 2 };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.Firebrick, TabStop = false };

    public OperatorLoginForm
    (
          IOperatorAuthenticator authenticator
        , IOperatorSession session
        , Action<string, string?>? showStartupProgress = null
        , Action? hideStartupProgress = null
    )
    {
        this.authenticator = authenticator;
        this.session = session;
        this.showStartupProgress = showStartupProgress;
        this.hideStartupProgress = hideStartupProgress;

        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.White;
        ClientSize = new Size(660, 430);
        Text = "Cafeteria Sign In";
        Icon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 10);
        AcceptButton = signIn;

        Panel header = new() { BackColor = HeaderBlue, Dock = DockStyle.Top, Height = 118 };
        header.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 19F, FontStyle.Bold),
            Location = new Point(34, 24),
            Text = "FOOD FOR THE POOR JAMAICA"
        });
        header.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = Color.WhiteSmoke,
            Font = new Font("Segoe UI", 11F),
            Location = new Point(37, 70),
            Text = "Cafeteria"
        });

        Panel accent = new() { BackColor = AccentOrange, Dock = DockStyle.Top, Height = 7 };

        Label title = new()
        {
            AutoSize = true,
            ForeColor = BodyText,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            Location = new Point(36, 158),
            Text = "Operator sign in"
        };
        Label detail = new()
        {
            AutoSize = true,
            ForeColor = MutedText,
            Font = new Font("Segoe UI", 9.5F),
            Location = new Point(39, 194),
            Text = "Sign in with your FFTPJ Active Directory account."
        };

        Label userLabel = new() { AutoSize = true, ForeColor = BodyText, Location = new Point(40, 235), Text = "Username" };
        userName.Location = new Point(40, 257);
        Label passwordLabel = new() { AutoSize = true, ForeColor = BodyText, Location = new Point(40, 300), Text = "Password" };
        password.Location = new Point(40, 322);
        signIn.Location = new Point(40, 370);
        signIn.FlatAppearance.BorderSize = 0;
        status.Location = new Point(180, 379);

        Controls.Add(status);
        Controls.Add(signIn);
        Controls.Add(password);
        Controls.Add(passwordLabel);
        Controls.Add(userName);
        Controls.Add(userLabel);
        Controls.Add(detail);
        Controls.Add(title);
        Controls.Add(accent);
        Controls.Add(header);

        signIn.Click += async (_, _) => await SignInAsync();
        Shown += OnLoginShown;
    }

    private void OnLoginShown(object? sender, EventArgs e)
    {
        RestoreLoginWindow();
        userName.Focus();
    }

    private async Task SignInAsync()
    {
        status.Text = string.Empty;
        signIn.Enabled = false;
        string enteredUserName = userName.Text;
        string enteredPassword = password.Text;

        try
        {
            ShowStartupProgressWindow
            (
                  "Connecting to Cafeteria Services..."
                , "Authenticating the operator securely through Common.Security."
            );

            OperatorIdentity? identity = await authenticator.AuthenticateAsync(enteredUserName, enteredPassword);
            password.Clear();

            if (identity is null)
            {
                this.hideStartupProgress?.Invoke();
                RestoreLoginWindow();
                status.Text = "Username or password was not accepted.";
                password.Focus();
                return;
            }

            session.SignIn(identity);
            this.showStartupProgress?.Invoke
            (
                  "Cafeteria Services connected."
                , $"Signed in as {identity.DisplayName}. Starting online synchronization."
            );
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            this.hideStartupProgress?.Invoke();
            RestoreLoginWindow();
            status.Text = "Cafeteria Services is unavailable: " + ex.GetBaseException().Message;
            password.Clear();
            password.Focus();
        }
        finally
        {
            enteredPassword = string.Empty;
            signIn.Enabled = true;
        }
    }

    private void ShowStartupProgressWindow(string progressStatus, string progressDetail)
    {
        TopMost = false;
        Opacity = 0d;
        Enabled = false;
        this.showStartupProgress?.Invoke(progressStatus, progressDetail);
    }

    private void RestoreLoginWindow()
    {
        Enabled = true;
        Opacity = 1d;
        WindowState = FormWindowState.Normal;
        TopMost = true;
        BringToFront();
        Activate();
    }
}

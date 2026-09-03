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
    private readonly TextBox userName = new() { Width = 310, Height = 32, BorderStyle = BorderStyle.FixedSingle, TabIndex = 0 };
    private readonly TextBox password = new() { Width = 310, Height = 32, BorderStyle = BorderStyle.FixedSingle, UseSystemPasswordChar = true, TabIndex = 1 };
    private readonly Button signIn = new() { Text = "Sign In", Width = 120, Height = 38, FlatStyle = FlatStyle.Flat, BackColor = HeaderBlue, ForeColor = Color.White, TabIndex = 2 };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.Firebrick, TabStop = false };

    public OperatorLoginForm(IOperatorAuthenticator authenticator, IOperatorSession session)
    {
        this.authenticator = authenticator;
        this.session = session;

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
        FfpBranding.Apply(this);
        WindowState = FormWindowState.Normal;
        TopMost = true;
        BringToFront();
        Activate();
        userName.Focus();
    }

    private async Task SignInAsync()
    {
        status.Text = string.Empty;
        signIn.Enabled = false;
        try
        {
            OperatorIdentity? identity = await authenticator.AuthenticateAsync(userName.Text, password.Text);
            password.Clear();
            if (identity is null)
            {
                status.Text = "Sign-in failed.";
                password.Focus();
                return;
            }

            session.SignIn(identity);
            DialogResult = DialogResult.OK;
            Close();
        }
        finally
        {
            signIn.Enabled = true;
        }
    }
}

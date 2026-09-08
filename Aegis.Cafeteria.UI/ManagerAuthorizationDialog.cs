using Aegis.Cafeteria.Application.Security;

namespace Aegis.Cafeteria.UI;

public sealed class ManagerAuthorizationDialog : Form
{
    private readonly IOperatorAuthenticator authenticator;
    private readonly IManagerAuthorizationRegistry registry;
    private readonly TextBox userName = new() { Width = 270 };
    private readonly TextBox password = new() { Width = 270, UseSystemPasswordChar = true };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.Firebrick };
    private readonly Button authorize = new() { Text = "Authorize", Width = 110, Height = 34 };

    public OperatorIdentity? AuthorizedManager { get; private set; }

    public ManagerAuthorizationDialog(
        IOperatorAuthenticator authenticator,
        IManagerAuthorizationRegistry registry,
        string actionDescription)
    {
        this.authenticator = authenticator;
        this.registry = registry;
        Text = "Manager Authorization";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(380, 250);
        Font = new Font("Segoe UI", 10);
        AcceptButton = authorize;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26), RowCount = 7, ColumnCount = 1 };
        root.Controls.Add(new Label { Text = actionDescription, AutoSize = true, Font = new Font("Segoe UI", 12, FontStyle.Bold) });
        root.Controls.Add(new Label { Text = "A cafeteria manager must authorize this action.", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 4, 0, 14) });
        root.Controls.Add(new Label { Text = "Manager username", AutoSize = true });
        root.Controls.Add(userName);
        root.Controls.Add(new Label { Text = "Password", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        root.Controls.Add(password);
        var actions = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
        actions.Controls.Add(authorize);
        actions.Controls.Add(status);
        root.Controls.Add(actions);
        Controls.Add(root);

        authorize.Click += async (_, _) => await AuthorizeAsync();
        Shown += (_, _) => userName.Focus();
    }

    private async Task AuthorizeAsync()
    {
        status.Text = string.Empty;
        authorize.Enabled = false;
        try
        {
            var identity = await authenticator.AuthenticateAsync(userName.Text, password.Text);
            password.Clear();
            if (identity is null)
            {
                status.Text = "Authentication failed.";
                return;
            }
            if (!identity.IsManager)
            {
                status.Text = "This account is not a cafeteria manager.";
                return;
            }

            registry.Grant(identity);
            AuthorizedManager = identity;
            DialogResult = DialogResult.OK;
            Close();
        }
        finally
        {
            authorize.Enabled = true;
        }
    }
}

using Aegis.Cafeteria.Infrastructure.Services;

namespace Aegis.Cafeteria.UI;

internal sealed class OperationalAuditDialog : Form
{
    private readonly CafeteriaManagementClient client;
    private readonly TextBox actor = new() { Width = 160 };
    private readonly TextBox action = new() { Width = 180 };
    private readonly NumericUpDown customerId = new() { Minimum = 0, Maximum = int.MaxValue, Width = 100 };
    private readonly ComboBox result = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly NumericUpDown take = new() { Minimum = 1, Maximum = 500, Value = 100, Width = 75 };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly DataGridView grid = new()
    {
          Dock = DockStyle.Fill
        , ReadOnly = true
        , AllowUserToAddRows = false
        , AllowUserToDeleteRows = false
        , AutoGenerateColumns = true
        , AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells
    };
    private readonly Button load = new() { Text = "Load Audit", Width = 110, Height = 38 };
    private readonly Button integrity = new() { Text = "Verify Integrity", Width = 120, Height = 38 };

    public OperationalAuditDialog(CafeteriaManagementClient client)
    {
        this.client = client;

        Text = "Cafeteria Operational Audit";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1050, 620);
        Size = new Size(1200, 720);
        Font = new Font("Segoe UI", 10);

        this.result.Items.AddRange(["All", "Succeeded", "Failed"]);
        this.result.SelectedIndex = 0;
        this.load.Click += async (_, _) => await LoadAsync();
        this.integrity.Click += async (_, _) => await VerifyIntegrityAsync();
        Shown += async (_, _) => await LoadAsync();

        Controls.Add(BuildLayout());
    }

    private Control BuildLayout()
    {
        TableLayoutPanel root = new()
        {
              Dock = DockStyle.Fill
            , Padding = new Padding(20)
            , RowCount = 4
            , ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label
        {
              Text = "OPERATIONAL AUDIT"
            , AutoSize = true
            , Font = new Font("Segoe UI", 16, FontStyle.Bold)
            , ForeColor = Color.FromArgb(24, 45, 68)
        });

        FlowLayoutPanel filters = new() { AutoSize = true, Margin = new Padding(0, 12, 0, 12) };
        filters.Controls.Add(new Label { Text = "Actor", AutoSize = true, Margin = new Padding(0, 8, 5, 0) });
        filters.Controls.Add(this.actor);
        filters.Controls.Add(new Label { Text = "Action", AutoSize = true, Margin = new Padding(12, 8, 5, 0) });
        filters.Controls.Add(this.action);
        filters.Controls.Add(new Label { Text = "Customer", AutoSize = true, Margin = new Padding(12, 8, 5, 0) });
        filters.Controls.Add(this.customerId);
        filters.Controls.Add(new Label { Text = "Result", AutoSize = true, Margin = new Padding(12, 8, 5, 0) });
        filters.Controls.Add(this.result);
        filters.Controls.Add(new Label { Text = "Rows", AutoSize = true, Margin = new Padding(12, 8, 5, 0) });
        filters.Controls.Add(this.take);
        filters.Controls.Add(this.load);
        filters.Controls.Add(this.integrity);
        root.Controls.Add(filters);

        root.Controls.Add(this.grid);

        FlowLayoutPanel footer = new()
        {
              AutoSize = true
            , Dock = DockStyle.Fill
            , FlowDirection = FlowDirection.RightToLeft
        };
        Button close = new() { Text = "Close", Width = 95, Height = 38, DialogResult = DialogResult.Cancel };
        footer.Controls.Add(close);
        footer.Controls.Add(this.status);
        root.Controls.Add(footer);

        return root;
    }

    private async Task LoadAsync()
    {
        ToggleBusy(true);
        try
        {
            bool? succeeded = this.result.SelectedIndex switch
            {
                1 => true,
                2 => false,
                _ => null
            };
            int? selectedCustomer = this.customerId.Value > 0
                ? decimal.ToInt32(this.customerId.Value)
                : null;

            IReadOnlyList<AuditTrailSnapshot> rows = await this.client.GetAuditTrailAsync(
                  this.actor.Text
                , this.action.Text
                , selectedCustomer
                , succeeded
                , decimal.ToInt32(this.take.Value));

            this.grid.DataSource = rows.ToArray();
            this.status.Text = $"{rows.Count:N0} audit record(s) loaded.";
        }
        catch (Exception exception)
        {
            this.status.Text = $"Could not load audit: {exception.Message}";
        }
        finally
        {
            ToggleBusy(false);
        }
    }

    private async Task VerifyIntegrityAsync()
    {
        ToggleBusy(true);
        try
        {
            AuditIntegritySnapshot audit = await this.client.GetAuditIntegrityAsync();
            this.status.Text = audit.IsValid
                ? $"Audit chain valid. Verified {audit.VerifiedRows:N0} row(s); legacy rows: {audit.LegacyRows:N0}."
                : $"Audit integrity failed at row {audit.FirstInvalidAuditId}: {audit.Error}";
        }
        catch (Exception exception)
        {
            this.status.Text = $"Audit integrity check failed: {exception.Message}";
        }
        finally
        {
            ToggleBusy(false);
        }
    }

    private void ToggleBusy(bool busy)
    {
        this.load.Enabled = !busy;
        this.integrity.Enabled = !busy;
        UseWaitCursor = busy;
    }
}

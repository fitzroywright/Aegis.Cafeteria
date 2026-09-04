namespace Aegis.Cafeteria.UI;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;

public sealed class FfpDataToolsDialog : Form
{
    private readonly IProductCatalogMaintenance products;
    private readonly Button importLunchItems = new() { Text = "Import Lunch Items", Width = 200, Height = 40 };
    private readonly Button importCurrentMonth = new() { Text = "Import Current Month", Width = 220, Height = 40 };

    public FfpDataToolsDialog(IProductCatalogMaintenance products)
    {
        this.products = products;
        Text = "Cafeteria Administration - FFP Tools";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(720, 420);
        Font = new Font("Segoe UI", 10F);

        TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new Padding(28), RowCount = 6, ColumnCount = 1 };
        root.Controls.Add(new Label { Text = "FFP Data Tools", AutoSize = true, Font = new Font("Segoe UI", 18F, FontStyle.Bold) });
        root.Controls.Add(new Label { Text = "Controlled imports through Cafeteria Services.", AutoSize = true, Margin = new Padding(0, 4, 0, 20) });
        root.Controls.Add(new Label { Text = "Lunch Item Bootstrap", AutoSize = true, Font = new Font("Segoe UI", 11F, FontStyle.Bold) });
        root.Controls.Add(this.importLunchItems);
        root.Controls.Add(new Label { Text = "Current-Month Cutover Import", AutoSize = true, Font = new Font("Segoe UI", 11F, FontStyle.Bold), Margin = new Padding(0, 20, 0, 4) });
        root.Controls.Add(this.importCurrentMonth);
        Controls.Add(root);

        this.importLunchItems.Click += async (_, _) => await ImportLunchItemsAsync();
        this.importCurrentMonth.Click += async (_, _) => await ImportCurrentMonthAsync();
    }

    private async Task ImportLunchItemsAsync()
    {
        if (MessageBox.Show(this, "Import missing LunchItems from FFP? Existing products and prices will not be overwritten.", "FFP Tools", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        ProductCatalogImportResult result = await this.products.ImportFromFfpAsync();
        MessageBox.Show(this, $"Added: {result.AddedProducts}\nAlready in POS: {result.ExistingProducts}\nIgnored: {result.IgnoredFfpProducts}", "Import Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task ImportCurrentMonthAsync()
    {
        if (MessageBox.Show(this, "Import recognized FFP cafeteria transactions for the current month? This operation is safe to rerun.", "FFP Tools", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        FfpMonthImportResult result = await this.products.ImportCurrentMonthFromFfpAsync();
        MessageBox.Show(this, $"Imported transactions: {result.ImportedTransactions}\nAccounts read: {result.ImportedAccounts}\nThrough: {result.ThroughAt.LocalDateTime:MMM dd yyyy hh:mm tt}", "Import Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}

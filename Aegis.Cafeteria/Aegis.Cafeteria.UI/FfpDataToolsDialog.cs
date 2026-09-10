namespace Aegis.Cafeteria.UI;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;

public sealed class FfpDataToolsDialog : Form
{
    private static readonly Color Navy = Color.FromArgb(24, 45, 68);
    private static readonly Color Blue = Color.FromArgb(31, 111, 190);
    private static readonly Color Green = Color.FromArgb(23, 119, 76);
    private static readonly Color Canvas = Color.FromArgb(244, 247, 250);
    private static readonly Color Surface = Color.White;
    private static readonly Color Muted = Color.FromArgb(104, 117, 132);

    private readonly IProductCatalogMaintenance products;
    private readonly Button importLunchItems = ActionButton("Import Lunch Items", Blue, 190);
    private readonly Button importCurrentMonth = ActionButton("Import Current Month", Green, 205);
    private readonly Button refreshProducts = ActionButton("Refresh List", Navy, 130);
    private readonly DataGridView productGrid = CreateGrid();
    private readonly Label productStatus = new()
    {
        AutoSize = true,
        ForeColor = Muted,
        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
        Text = "Local POS catalog: loading..."
    };

    public FfpDataToolsDialog(IProductCatalogMaintenance products)
    {
        this.products = products;
        Text = "Cafeteria Administration - FFP Tools";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(940, 650);
        MinimumSize = new Size(820, 560);
        Font = new Font("Segoe UI", 10F);
        BackColor = Canvas;

        Controls.Add(BuildLayout());

        Shown += async (_, _) => await LoadProductsAsync();
        this.importLunchItems.Click += async (_, _) => await ImportLunchItemsAsync();
        this.importCurrentMonth.Click += async (_, _) => await ImportCurrentMonthAsync();
        this.refreshProducts.Click += async (_, _) => await LoadProductsAsync();
    }

    private Control BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            RowCount = 3,
            ColumnCount = 1,
            BackColor = Canvas
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        Panel header = new()
        {
            Dock = DockStyle.Top,
            Height = 92,
            BackColor = Navy,
            Padding = new Padding(22, 16, 22, 12),
            Margin = new Padding(0, 0, 0, 16)
        };
        Label title = new()
        {
            Text = "FFP DATA TOOLS",
            AutoSize = true,
            Font = new Font("Segoe UI", 20F, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(20, 14)
        };
        Label subtitle = new()
        {
            Text = "Controlled FFP imports and local POS catalog verification",
            AutoSize = true,
            Font = new Font("Segoe UI", 10F),
            ForeColor = Color.FromArgb(220, 231, 241),
            Location = new Point(22, 55)
        };
        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        Panel actions = new()
        {
            Dock = DockStyle.Top,
            Height = 116,
            BackColor = Surface,
            Padding = new Padding(18),
            Margin = new Padding(0, 0, 0, 16)
        };
        Label actionTitle = new()
        {
            Text = "FFP IMPORTS",
            AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = Navy,
            Location = new Point(18, 14)
        };
        Label actionHelp = new()
        {
            Text = "Import LunchItems to seed missing POS products or import recognized current-month FFP transactions.",
            AutoSize = true,
            ForeColor = Muted,
            Location = new Point(18, 39)
        };
        FlowLayoutPanel actionButtons = new()
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Location = new Point(18, 68),
            WrapContents = false
        };
        actionButtons.Controls.Add(this.importLunchItems);
        actionButtons.Controls.Add(this.importCurrentMonth);
        actionButtons.Controls.Add(this.refreshProducts);
        actions.Controls.Add(actionTitle);
        actions.Controls.Add(actionHelp);
        actions.Controls.Add(actionButtons);

        Panel catalog = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Padding = new Padding(18)
        };
        TableLayoutPanel catalogLayout = new()
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            BackColor = Surface
        };
        catalogLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        catalogLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        catalogLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        catalogLayout.Controls.Add(new Label
        {
            Text = "LOCAL POS PRODUCT CATALOG",
            AutoSize = true,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = Navy,
            Margin = new Padding(0, 0, 0, 4)
        });
        catalogLayout.Controls.Add(this.productStatus);
        this.productGrid.Margin = new Padding(0, 10, 0, 0);
        catalogLayout.Controls.Add(this.productGrid);
        catalog.Controls.Add(catalogLayout);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(actions, 0, 1);
        root.Controls.Add(catalog, 0, 2);
        return root;
    }

    private async Task LoadProductsAsync()
    {
        try
        {
            this.refreshProducts.Enabled = false;
            IReadOnlyList<ProductCatalogItem> rows = await this.products.GetAllAsync();
            this.productGrid.DataSource = rows
                .Select(item => new { item.Id, item.Name, item.Price })
                .ToArray();

            if (this.productGrid.Columns[nameof(ProductCatalogItem.Id)] is DataGridViewColumn idColumn)
                idColumn.HeaderText = "ID";
            if (this.productGrid.Columns[nameof(ProductCatalogItem.Name)] is DataGridViewColumn nameColumn)
            {
                nameColumn.HeaderText = "Product";
                nameColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            }
            if (this.productGrid.Columns[nameof(ProductCatalogItem.Price)] is DataGridViewColumn priceColumn)
            {
                priceColumn.HeaderText = "Price";
                priceColumn.DefaultCellStyle.Format = "C2";
                priceColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }

            this.productStatus.Text = rows.Count == 0
                ? "Local POS catalog is empty. Use Import Lunch Items to seed it from FFP."
                : $"Local POS catalog: {rows.Count} product(s) loaded.";
            this.productStatus.ForeColor = rows.Count == 0 ? Color.FromArgb(176, 52, 52) : Green;
        }
        catch (Exception exception)
        {
            this.productStatus.Text = "Unable to read the local POS product catalog: " + exception.Message;
            this.productStatus.ForeColor = Color.FromArgb(176, 52, 52);
        }
        finally
        {
            this.refreshProducts.Enabled = true;
        }
    }

    private async Task ImportLunchItemsAsync()
    {
        if (MessageBox.Show(this, "Import missing LunchItems from FFP? Existing products and prices will not be overwritten.", "FFP Tools", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        try
        {
            SetBusy(true);
            ProductCatalogImportResult result = await this.products.ImportFromFfpAsync();
            await LoadProductsAsync();
            MessageBox.Show(this, $"Added: {result.AddedProducts}\nAlready in POS: {result.ExistingProducts}\nReserved/ignored: {result.IgnoredFfpProducts}", "Import Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ImportCurrentMonthAsync()
    {
        if (MessageBox.Show(this, "Import recognized FFP cafeteria transactions for the current month? This operation is safe to rerun.", "FFP Tools", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        try
        {
            SetBusy(true);
            FfpMonthImportResult result = await this.products.ImportCurrentMonthFromFfpAsync();
            MessageBox.Show(this, $"Imported transactions: {result.ImportedTransactions}\nAccounts read: {result.ImportedAccounts}\nThrough: {result.ThroughAt.LocalDateTime:MMM dd yyyy hh:mm tt}", "Import Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        this.importLunchItems.Enabled = !busy;
        this.importCurrentMonth.Enabled = !busy;
        this.refreshProducts.Enabled = !busy;
    }

    private static Button ActionButton(string text, Color background, int width)
    {
        Button button = new()
        {
            Text = text,
            Width = width,
            Height = 36,
            BackColor = background,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 10, 0)
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private static DataGridView CreateGrid()
    {
        DataGridView grid = new()
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = true,
            BackgroundColor = Surface,
            BorderStyle = BorderStyle.FixedSingle,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            RowTemplate = { Height = 30 },
            EnableHeadersVisualStyles = false
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor = Navy;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Navy;
        grid.DefaultCellStyle.BackColor = Surface;
        grid.DefaultCellStyle.ForeColor = Navy;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(218, 232, 246);
        grid.DefaultCellStyle.SelectionForeColor = Navy;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
        return grid;
    }
}

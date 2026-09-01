namespace Aegis.Cafeteria.UI;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;

public sealed class CafeteriaAdministrationDialog : Form
{
    private static readonly Color HeaderBlue = Color.FromArgb(22, 66, 105);
    private static readonly Color AccentOrange = Color.FromArgb(235, 139, 40);
    private static readonly Color BodyText = Color.FromArgb(31, 44, 55);
    private static readonly Color MutedText = Color.FromArgb(105, 112, 118);
    private static readonly Color PageBackground = Color.FromArgb(244, 247, 249);
    private static readonly Color SectionBackground = Color.FromArgb(249, 250, 252);
    private static readonly Color GridLine = Color.FromArgb(225, 230, 235);
    private static readonly Color SelectionBackground = Color.FromArgb(222, 235, 246);
    private static readonly Color DeleteRed = Color.FromArgb(166, 54, 56);

    private readonly IProductCatalogMaintenance products;
    private readonly BindingSource productSource = new();
    private readonly DataGridView productGrid = new();
    private readonly Button importFromFfp = new();
    private readonly Button importCurrentMonth = new();

    public CafeteriaAdministrationDialog
    (
        IProductCatalogMaintenance products
    )
    {
        this.products = products;

        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = PageBackground;
        ClientSize = new Size(1120, 760);
        MinimumSize = new Size(980, 680);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10F);
        Text = "Cafeteria Administration - Price Maintenance";
        Icon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);

        ConfigureProductGrid();
        ConfigureActionButton(this.importFromFfp, "Import Lunch Items", HeaderBlue, 210);
        ConfigureActionButton(this.importCurrentMonth, "Import Current Month", AccentOrange, 230);

        this.importFromFfp.Click += async (_, _) => await ImportFromFfpAsync();
        this.importCurrentMonth.Click += async (_, _) => await ImportCurrentMonthAsync();

        Controls.Add(BuildPage());
        Shown += async (_, _) => await LoadProductsAsync();
    }

    private Control BuildPage()
    {
        TableLayoutPanel root = new()
        {
              Dock = DockStyle.Fill
            , BackColor = PageBackground
            , ColumnCount = 1
            , RowCount = 4
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 108F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 6F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add
        (
            new Panel
            {
                  Dock = DockStyle.Fill
                , BackColor = AccentOrange
                , Margin = Padding.Empty
            },
            0,
            1
        );
        root.Controls.Add(BuildContent(), 0, 2);
        root.Controls.Add(BuildFooter(), 0, 3);

        return root;
    }

    private Control BuildHeader()
    {
        Panel header = new()
        {
              Dock = DockStyle.Fill
            , BackColor = HeaderBlue
            , Margin = Padding.Empty
        };

        PictureBox applicationIconView = new()
        {
              BackColor = Color.Transparent
            , Location = new Point(30, 23)
            , Size = new Size(62, 62)
            , SizeMode = PictureBoxSizeMode.Zoom
            , TabStop = false
        };

        Icon? applicationIcon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);
        if (applicationIcon is not null)
        {
            applicationIconView.Image = applicationIcon.ToBitmap();
        }

        Label organization = new()
        {
              AutoSize = true
            , ForeColor = Color.White
            , Font = new Font("Segoe UI", 19F, FontStyle.Bold)
            , Location = new Point(111, 22)
            , Text = "FOOD FOR THE POOR JAMAICA"
        };

        Label application = new()
        {
              AutoSize = true
            , ForeColor = Color.WhiteSmoke
            , Font = new Font("Segoe UI", 11F)
            , Location = new Point(114, 68)
            , Text = "Cafeteria | Administration"
        };

        Label section = new()
        {
              Anchor = AnchorStyles.Top | AnchorStyles.Right
            , ForeColor = Color.White
            , Font = new Font("Segoe UI", 14F, FontStyle.Bold)
            , Location = new Point(760, 34)
            , Size = new Size(320, 38)
            , TextAlign = ContentAlignment.MiddleRight
            , Text = "Product & Price Maintenance"
        };

        header.Controls.Add(applicationIconView);
        header.Controls.Add(organization);
        header.Controls.Add(application);
        header.Controls.Add(section);
        return header;
    }

    private Control BuildContent()
    {
        TableLayoutPanel content = new()
        {
              Dock = DockStyle.Fill
            , BackColor = PageBackground
            , Padding = new Padding(24, 24, 24, 16)
            , ColumnCount = 2
            , RowCount = 1
        };

        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68F));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        Panel catalogCard = BuildCatalogCard();
        catalogCard.Margin = new Padding(0, 0, 12, 0);

        Panel toolsCard = BuildDataToolsCard();
        toolsCard.Margin = new Padding(12, 0, 0, 0);

        content.Controls.Add(catalogCard, 0, 0);
        content.Controls.Add(toolsCard, 1, 0);
        return content;
    }

    private Panel BuildCatalogCard()
    {
        Panel card = CreateCard();

        TableLayoutPanel layout = new()
        {
              Dock = DockStyle.Fill
            , BackColor = Color.White
            , ColumnCount = 1
            , RowCount = 3
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));

        Panel titleArea = new()
        {
              Dock = DockStyle.Fill
            , BackColor = Color.White
        };

        Label title = new()
        {
              AutoSize = true
            , ForeColor = BodyText
            , Font = new Font("Segoe UI", 15F, FontStyle.Bold)
            , Location = new Point(0, 2)
            , Text = "Product Catalog"
        };

        Label detail = new()
        {
              AutoSize = true
            , ForeColor = MutedText
            , Font = new Font("Segoe UI", 9.5F)
            , Location = new Point(2, 38)
            , Text = "Edit the products and prices used by this POS."
        };

        titleArea.Controls.Add(title);
        titleArea.Controls.Add(detail);

        FlowLayoutPanel actions = new()
        {
              Dock = DockStyle.Fill
            , BackColor = Color.White
            , FlowDirection = FlowDirection.LeftToRight
            , WrapContents = false
            , Padding = new Padding(0, 10, 0, 0)
        };

        Button add = new();
        ConfigureActionButton(add, "Add Product", HeaderBlue, 125);
        add.Click += async (_, _) => await AddProductAsync();

        Button save = new();
        ConfigureActionButton(save, "Save Selected", HeaderBlue, 145);
        save.Click += async (_, _) => await SaveSelectedProductAsync();

        Button delete = new();
        ConfigureActionButton(delete, "Delete Selected", DeleteRed, 145);
        delete.Click += async (_, _) => await DeleteSelectedProductAsync();

        actions.Controls.Add(add);
        actions.Controls.Add(save);
        actions.Controls.Add(delete);

        layout.Controls.Add(titleArea, 0, 0);
        layout.Controls.Add(this.productGrid, 0, 1);
        layout.Controls.Add(actions, 0, 2);
        card.Controls.Add(layout);
        return card;
    }

    private Panel BuildDataToolsCard()
    {
        Panel card = CreateCard();

        TableLayoutPanel layout = new()
        {
              Dock = DockStyle.Fill
            , BackColor = Color.White
            , ColumnCount = 1
            , RowCount = 4
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 178F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 218F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        Panel titleArea = new()
        {
              Dock = DockStyle.Fill
            , BackColor = Color.White
        };

        Label title = new()
        {
              AutoSize = true
            , ForeColor = BodyText
            , Font = new Font("Segoe UI", 15F, FontStyle.Bold)
            , Location = new Point(0, 2)
            , Text = "FFP Data Tools"
        };

        Label detail = new()
        {
              AutoSize = true
            , ForeColor = MutedText
            , Font = new Font("Segoe UI", 9.5F)
            , Location = new Point(2, 38)
            , Text = "Controlled imports through Cafeteria Services."
        };

        titleArea.Controls.Add(title);
        titleArea.Controls.Add(detail);

        Panel lunchItemsTool = BuildToolPanel
        (
              "Lunch Item Bootstrap"
            , "Adds LunchItems that are missing from this POS. Existing products and prices are never overwritten, and FFP-prefixed codes are ignored."
            , this.importFromFfp
        );
        lunchItemsTool.Margin = new Padding(0, 0, 0, 12);

        Panel transactionsTool = BuildToolPanel
        (
              "Current-Month Cutover Import"
            , "Imports legacy allocations, ID-card purchases and stipends for the current month. The import is idempotent and safe to rerun before cutover."
            , this.importCurrentMonth
        );
        transactionsTool.Margin = new Padding(0, 0, 0, 12);

        Label note = new()
        {
              Dock = DockStyle.Fill
            , ForeColor = MutedText
            , Font = new Font("Segoe UI", 9F)
            , Padding = new Padding(2, 12, 2, 0)
            , Text = "Imported legacy transactions remain traceable to their original EmployeeIDScans ID and do not overwrite native Aegis sales."
        };

        layout.Controls.Add(titleArea, 0, 0);
        layout.Controls.Add(lunchItemsTool, 0, 1);
        layout.Controls.Add(transactionsTool, 0, 2);
        layout.Controls.Add(note, 0, 3);
        card.Controls.Add(layout);
        return card;
    }

    private static Panel BuildToolPanel
    (
          string titleText
        , string detailText
        , Button actionButton
    )
    {
        Panel tool = new()
        {
              Dock = DockStyle.Fill
            , BackColor = SectionBackground
            , BorderStyle = BorderStyle.FixedSingle
            , Padding = new Padding(14)
        };

        TableLayoutPanel layout = new()
        {
              Dock = DockStyle.Fill
            , BackColor = SectionBackground
            , ColumnCount = 1
            , RowCount = 3
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));

        Label title = new()
        {
              Dock = DockStyle.Fill
            , ForeColor = BodyText
            , Font = new Font("Segoe UI", 11F, FontStyle.Bold)
            , Text = titleText
        };

        Label detail = new()
        {
              Dock = DockStyle.Fill
            , ForeColor = MutedText
            , Font = new Font("Segoe UI", 9F)
            , Text = detailText
        };

        FlowLayoutPanel buttonHost = new()
        {
              Dock = DockStyle.Fill
            , BackColor = SectionBackground
            , FlowDirection = FlowDirection.LeftToRight
            , WrapContents = false
            , Padding = new Padding(0, 4, 0, 0)
        };
        buttonHost.Controls.Add(actionButton);

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(detail, 0, 1);
        layout.Controls.Add(buttonHost, 0, 2);
        tool.Controls.Add(layout);
        return tool;
    }

    private Control BuildFooter()
    {
        Panel footer = new()
        {
              Dock = DockStyle.Fill
            , BackColor = Color.White
            , Padding = new Padding(24, 10, 24, 10)
            , Margin = Padding.Empty
        };

        Button close = new()
        {
              Anchor = AnchorStyles.Top | AnchorStyles.Right
            , BackColor = Color.White
            , FlatStyle = FlatStyle.Flat
            , ForeColor = HeaderBlue
            , Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            , Location = new Point(958, 10)
            , Size = new Size(120, 38)
            , Text = "Close"
        };
        close.FlatAppearance.BorderColor = HeaderBlue;
        close.FlatAppearance.BorderSize = 1;
        close.Click += (_, _) => Close();

        footer.Resize += (_, _) => close.Left = footer.ClientSize.Width - close.Width - 24;
        footer.Controls.Add(close);
        return footer;
    }

    private static Panel CreateCard()
    {
        return new Panel
        {
              Dock = DockStyle.Fill
            , BackColor = Color.White
            , BorderStyle = BorderStyle.FixedSingle
            , Padding = new Padding(20)
        };
    }

    private void ConfigureProductGrid()
    {
        this.productGrid.Dock = DockStyle.Fill;
        this.productGrid.Margin = new Padding(0, 4, 0, 4);
        this.productGrid.AutoGenerateColumns = false;
        this.productGrid.AllowUserToAddRows = false;
        this.productGrid.AllowUserToDeleteRows = false;
        this.productGrid.AllowUserToResizeRows = false;
        this.productGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        this.productGrid.MultiSelect = false;
        this.productGrid.RowHeadersVisible = false;
        this.productGrid.BackgroundColor = Color.White;
        this.productGrid.BorderStyle = BorderStyle.None;
        this.productGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        this.productGrid.GridColor = GridLine;
        this.productGrid.EnableHeadersVisualStyles = false;
        this.productGrid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        this.productGrid.ColumnHeadersHeight = 42;
        this.productGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        this.productGrid.RowTemplate.Height = 38;
        this.productGrid.EditMode = DataGridViewEditMode.EditOnEnter;

        this.productGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
              BackColor = HeaderBlue
            , ForeColor = Color.White
            , Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            , SelectionBackColor = HeaderBlue
            , SelectionForeColor = Color.White
            , Alignment = DataGridViewContentAlignment.MiddleLeft
            , Padding = new Padding(8, 0, 8, 0)
        };

        this.productGrid.DefaultCellStyle = new DataGridViewCellStyle
        {
              BackColor = Color.White
            , ForeColor = BodyText
            , SelectionBackColor = SelectionBackground
            , SelectionForeColor = BodyText
            , Font = new Font("Segoe UI", 10F)
            , Padding = new Padding(8, 0, 8, 0)
        };

        this.productGrid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = SectionBackground
        };

        this.productGrid.Columns.Add
        (
            new DataGridViewTextBoxColumn
            {
                  DataPropertyName = nameof(ProductEditRow.ProductId)
                , HeaderText = "ID"
                , ReadOnly = true
                , Width = 82
            }
        );

        this.productGrid.Columns.Add
        (
            new DataGridViewTextBoxColumn
            {
                  DataPropertyName = nameof(ProductEditRow.Name)
                , HeaderText = "Product"
                , AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            }
        );

        this.productGrid.Columns.Add
        (
            new DataGridViewTextBoxColumn
            {
                  DataPropertyName = nameof(ProductEditRow.Price)
                , HeaderText = "Price"
                , Width = 145
                , DefaultCellStyle = new DataGridViewCellStyle
                {
                      Alignment = DataGridViewContentAlignment.MiddleRight
                    , Format = "C2"
                    , Padding = new Padding(8, 0, 12, 0)
                }
            }
        );

        this.productGrid.DataSource = this.productSource;
    }

    private static void ConfigureActionButton
    (
          Button button
        , string text
        , Color backColor
        , int width
    )
    {
        button.Text = text;
        button.Width = width;
        button.Height = 38;
        button.AutoSize = false;
        button.BackColor = backColor;
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        button.Margin = new Padding(0, 0, 10, 0);
        button.FlatAppearance.BorderSize = 0;
    }

    private async Task LoadProductsAsync()
    {
        IReadOnlyList<ProductCatalogItem> rows = await this.products.GetAllAsync();
        this.productSource.DataSource = rows
            .Select
            (
                product => new ProductEditRow
                {
                      ProductId = product.Id
                    , Name = product.Name
                    , Price = product.Price
                }
            )
            .ToArray();
    }

    private async Task AddProductAsync()
    {
        ProductCatalogItem created = await this.products.AddAsync("New Product", 0m);
        await LoadProductsAsync();

        foreach (DataGridViewRow row in this.productGrid.Rows)
        {
            if (row.DataBoundItem is ProductEditRow product && product.ProductId == created.Id)
            {
                row.Selected = true;
                this.productGrid.CurrentCell = row.Cells[1];
                this.productGrid.BeginEdit(true);
                break;
            }
        }
    }

    private async Task SaveSelectedProductAsync()
    {
        this.productGrid.EndEdit();
        this.productSource.EndEdit();

        if (this.productGrid.CurrentRow?.DataBoundItem is not ProductEditRow row)
        {
            return;
        }

        ProductCatalogItem updated = await this.products.UpdateAsync
        (
              row.ProductId
            , row.Name
            , row.Price
        );

        row.Name = updated.Name;
        row.Price = updated.Price;
        this.productSource.ResetBindings(false);
    }

    private async Task DeleteSelectedProductAsync()
    {
        if (this.productGrid.CurrentRow?.DataBoundItem is not ProductEditRow row)
        {
            return;
        }

        DialogResult result = MessageBox.Show
        (
              this
            , $"Delete '{row.Name}' from the current POS catalog? Historical sales will remain unchanged."
            , "Delete Product"
            , MessageBoxButtons.YesNo
            , MessageBoxIcon.Warning
        );

        if (result != DialogResult.Yes)
        {
            return;
        }

        await this.products.DeleteAsync(row.ProductId);
        await LoadProductsAsync();
    }

    private async Task ImportFromFfpAsync()
    {
        DialogResult answer = MessageBox.Show
        (
              this
            , "Import LunchItems from FFP into the POS catalog?\n\nOnly missing products will be added. Existing POS products and prices will not be changed, no POS products will be deleted, and all codes beginning with FFP will be ignored."
            , "Import Lunch Items from FFP"
            , MessageBoxButtons.YesNo
            , MessageBoxIcon.Question
            , MessageBoxDefaultButton.Button2
        );

        if (answer != DialogResult.Yes)
        {
            return;
        }

        this.importFromFfp.Enabled = false;

        try
        {
            ProductCatalogImportResult result = await this.products.ImportFromFfpAsync();
            await LoadProductsAsync();

            MessageBox.Show
            (
                  this
                , $"Import complete.\n\nAdded: {result.AddedProducts}\nAlready in POS: {result.ExistingProducts}\nFFP system/config rows ignored: {result.IgnoredFfpProducts}"
                , "Lunch Items Imported"
                , MessageBoxButtons.OK
                , MessageBoxIcon.Information
            );
        }
        catch (Exception exception)
        {
            MessageBox.Show
            (
                  this
                , exception.Message
                , "Lunch Item Import Failed"
                , MessageBoxButtons.OK
                , MessageBoxIcon.Error
            );
        }
        finally
        {
            this.importFromFfp.Enabled = true;
        }
    }

    private async Task ImportCurrentMonthAsync()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        DialogResult answer = MessageBox.Show
        (
              this
            , $"Import all recognized FFP cafeteria transactions for {now:MMMM yyyy} through the current time?\n\nThis may be run repeatedly. Transactions already imported by their FFP EmployeeIDScans ID will be skipped. Imported rows are read-only legacy history and are not native Aegis orders."
            , "Import Current Month Transactions"
            , MessageBoxButtons.YesNo
            , MessageBoxIcon.Warning
            , MessageBoxDefaultButton.Button2
        );

        if (answer != DialogResult.Yes)
        {
            return;
        }

        this.importCurrentMonth.Enabled = false;

        try
        {
            FfpMonthImportResult result = await this.products.ImportCurrentMonthFromFfpAsync();
            MessageBox.Show
            (
                  this
                , $"Current-month import complete.\n\nPeriod: {result.Year:D4}-{result.Month:D2}\nThrough: {result.ThroughAt:g}\nNew transactions imported: {result.ImportedTransactions}\nEmployee account snapshots read: {result.ImportedAccounts}\nNet imported ScanValue: {result.NetImportedScanValue:N2}\n\nRerunning the import will skip transactions already imported."
                , "FFP Transactions Imported"
                , MessageBoxButtons.OK
                , MessageBoxIcon.Information
            );
        }
        catch (Exception exception)
        {
            MessageBox.Show
            (
                  this
                , exception.Message
                , "Current Month Import Failed"
                , MessageBoxButtons.OK
                , MessageBoxIcon.Error
            );
        }
        finally
        {
            this.importCurrentMonth.Enabled = true;
        }
    }

    private sealed class ProductEditRow
    {
        public int ProductId { get; init; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }
}

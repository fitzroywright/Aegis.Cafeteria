namespace Aegis.Cafeteria.UI;

using Aegis.Cafeteria.Application.Abstractions;

public sealed class CafeteriaAdministrationDialog : Form
{
    private readonly IProductCatalogMaintenance products;
    private readonly DataGridView productGrid = new()
    {
          Dock = DockStyle.Fill
        , AutoGenerateColumns = false
        , AllowUserToAddRows = false
        , AllowUserToDeleteRows = false
        , SelectionMode = DataGridViewSelectionMode.FullRowSelect
        , MultiSelect = false
    };
    private readonly BindingSource productSource = new();
    private readonly Button importFromFfp = new()
    {
          Text = "Import Lunch Items from FFP"
        , AutoSize = true
    };

    public CafeteriaAdministrationDialog
    (
        IProductCatalogMaintenance products
    )
    {
        this.products = products;

        Text = "POS Product & Price Maintenance";
        Width = 820;
        Height = 640;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10);

        Controls.Add(BuildPriceMaintenancePage());
        Shown += async (_, _) => await LoadProductsAsync();
    }

    private Control BuildPriceMaintenancePage()
    {
        Panel page = new()
        {
            Dock = DockStyle.Fill
        };

        this.productGrid.Columns.Add
        (
            new DataGridViewTextBoxColumn
            {
                  DataPropertyName = nameof(ProductEditRow.ProductId)
                , HeaderText = "ID"
                , ReadOnly = true
                , Width = 80
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
                , Width = 140
                , DefaultCellStyle = new DataGridViewCellStyle
                {
                      Alignment = DataGridViewContentAlignment.MiddleRight
                    , Format = "C2"
                }
            }
        );

        this.productGrid.DataSource = this.productSource;

        Button add = new()
        {
              Text = "Add Product"
            , AutoSize = true
        };
        add.Click += async (_, _) => await AddProductAsync();

        Button save = new()
        {
              Text = "Save Selected Product"
            , AutoSize = true
        };
        save.Click += async (_, _) => await SaveSelectedProductAsync();

        Button delete = new()
        {
              Text = "Delete Selected Product"
            , AutoSize = true
        };
        delete.Click += async (_, _) => await DeleteSelectedProductAsync();

        this.importFromFfp.Click += async (_, _) => await ImportFromFfpAsync();

        Label explanation = new()
        {
              AutoSize = true
            , MaximumSize = new Size(760, 0)
            , Text = "Products and current prices belong to this POS database. Import from FFP is a bootstrap tool only: it adds missing LunchItems, never overwrites or deletes POS products, and ignores every FFP-prefixed code. Completed sales preserve their own product description and unit price."
        };

        FlowLayoutPanel buttons = new()
        {
              Dock = DockStyle.Bottom
            , Height = 110
            , Padding = new Padding(8)
        };

        buttons.Controls.Add(add);
        buttons.Controls.Add(save);
        buttons.Controls.Add(delete);
        buttons.Controls.Add(this.importFromFfp);
        buttons.Controls.Add(explanation);

        page.Controls.Add(this.productGrid);
        page.Controls.Add(buttons);
        return page;
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

    private sealed class ProductEditRow
    {
        public int ProductId { get; init; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }
}

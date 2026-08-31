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

    public CafeteriaAdministrationDialog
    (
        IProductCatalogMaintenance products
    )
    {
        this.products = products;

        Text = "POS Product & Price Maintenance";
        Width = 780;
        Height = 620;
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

        Label explanation = new()
        {
              AutoSize = true
            , MaximumSize = new Size(700, 0)
            , Text = "Products and current prices belong to this POS database. Completed sales preserve their own product description and unit price, so changing a product here never rewrites historical reports."
        };

        FlowLayoutPanel buttons = new()
        {
              Dock = DockStyle.Bottom
            , Height = 92
            , Padding = new Padding(8)
        };

        buttons.Controls.Add(add);
        buttons.Controls.Add(save);
        buttons.Controls.Add(delete);
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

    private sealed class ProductEditRow
    {
        public int ProductId { get; init; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }
}

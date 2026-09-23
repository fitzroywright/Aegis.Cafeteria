namespace Aegis.Cafeteria.UI.Linux;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Presentation;

using Avalonia.Controls;
using Avalonia.Layout;

public sealed class ProductMaintenanceWindow : Window
{
    private readonly IProductCatalogMaintenance products;
    private readonly ListBox productList = new() { MinHeight = 320 };
    private readonly TextBox productName = new() { Watermark = "Product name" };
    private readonly TextBox productPrice = new() { Watermark = "Price" };
    private readonly TextBlock status = new();
    private IReadOnlyList<ProductCatalogItem> currentProducts = Array.Empty<ProductCatalogItem>();

    public ProductMaintenanceWindow(IProductCatalogMaintenance products)
    {
        this.products = products;

        Title = "POS Product & Price Maintenance";
        Width = 720;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildLayout();

        Opened += async (_, _) => await LoadProductsAsync();
        this.productList.SelectionChanged += (_, _) => LoadSelection();
    }

    private Control BuildLayout()
    {
        Grid root = new()
        {
              Margin = new Avalonia.Thickness(18)
            , RowSpacing = 10
        };
        root.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        root.Children.Add(this.productList);

        Grid editor = new() { ColumnSpacing = 10 };
        editor.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
        editor.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        editor.Children.Add(this.productName);
        Grid.SetColumn(this.productPrice, 1);
        editor.Children.Add(this.productPrice);
        Grid.SetRow(editor, 1);
        root.Children.Add(editor);

        StackPanel buttons = new()
        {
              Orientation = Orientation.Horizontal
            , Spacing = 8
        };
        Button add = new() { Content = "Add Product" };
        Button save = new() { Content = "Save Selected Product" };
        Button delete = new() { Content = "Delete Selected Product" };
        add.Click += async (_, _) => await AddProductAsync();
        save.Click += async (_, _) => await SaveProductAsync();
        delete.Click += async (_, _) => await DeleteProductAsync();
        buttons.Children.Add(add);
        buttons.Children.Add(save);
        buttons.Children.Add(delete);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        StackPanel footer = new() { Spacing = 4 };
        footer.Children.Add(new TextBlock
        {
            Text = "Products and current prices belong to this POS database. Historical sales retain their original product description and price.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        footer.Children.Add(this.status);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        return root;
    }

    private async Task LoadProductsAsync(int? selectProductId = null)
    {
        this.currentProducts = await this.products.GetAllAsync();
        this.productList.ItemsSource = this.currentProducts.Select(product => new ProductRow(product)).ToArray();

        if (selectProductId is int id)
        {
            this.productList.SelectedItem = this.productList.ItemsSource
                ?.Cast<ProductRow>()
                .SingleOrDefault(row => row.Product.Id == id);
        }
    }

    private void LoadSelection()
    {
        if (this.productList.SelectedItem is not ProductRow row)
        {
            return;
        }

        this.productName.Text = row.Product.Name;
        this.productPrice.Text = row.Product.Price.ToString("0.00");
    }

    private async Task AddProductAsync()
    {
        ProductCatalogItem created = await this.products.AddAsync("New Product", 0m);
        await LoadProductsAsync(created.Id);
        this.productName.Focus();
        this.productName.SelectAll();
        this.status.Text = "New product added. Enter the product name and price, then save.";
    }

    private async Task SaveProductAsync()
    {
        if (this.productList.SelectedItem is not ProductRow row)
        {
            this.status.Text = "Select a product first.";
            return;
        }

        if (!decimal.TryParse(this.productPrice.Text, out decimal price))
        {
            this.status.Text = "Enter a valid price.";
            return;
        }

        try
        {
            ProductCatalogItem updated = await this.products.UpdateAsync
            (
                  row.Product.Id
                , this.productName.Text ?? string.Empty
                , price
            );
            await LoadProductsAsync(updated.Id);
            this.status.Text = "Product saved.";
        }
        catch (Exception exception)
        {
            this.status.Text = exception.GetBaseException().Message;
        }
    }

    private async Task DeleteProductAsync()
    {
        if (this.productList.SelectedItem is not ProductRow row)
        {
            this.status.Text = "Select a product first.";
            return;
        }

        bool confirmed = await new ConfirmWindow
        (
              "Delete Product"
            , $"Delete '{row.Product.Name}' from the current POS catalog? Historical sales will remain unchanged."
        ).ShowDialog<bool>(this);

        if (!confirmed)
        {
            return;
        }

        await this.products.DeleteAsync(row.Product.Id);
        this.productName.Text = string.Empty;
        this.productPrice.Text = string.Empty;
        await LoadProductsAsync();
        this.status.Text = "Product deleted.";
    }

    private sealed record ProductRow(ProductCatalogItem Product)
    {
        public override string ToString()
        {
            return $"{Product.Id}  {Product.Name}  -  {CafeteriaDisplay.Money(Product.Price)}";
        }
    }
}

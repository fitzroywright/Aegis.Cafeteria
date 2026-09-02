using Aegis.Cafeteria.Application.Abstractions;

namespace Aegis.Cafeteria.UI;

internal sealed class CustomerSearchDialog : Form
{
    private readonly ICustomerIdentificationService? identification;
    private readonly TextBox search = new() { PlaceholderText = "Search employee name", Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer searchDebounce = new() { Interval = 250 };
    private CancellationTokenSource? searchCancellation;
    private readonly DataGridView grid = CreateGrid();
    private readonly PictureBox photo = new() { Dock = DockStyle.Top, Height = 190, SizeMode = PictureBoxSizeMode.Zoom };
    private readonly Label name = new() { AutoSize = true, Font = new Font("Segoe UI", 18, FontStyle.Bold), MaximumSize = new Size(330, 0) };
    private readonly Label department = DetailLabel();
    private readonly Label emailAddress = DetailLabel();
    private readonly Label extension = DetailLabel();
    private readonly Label count = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly Button select = new() { Text = "Select Customer", Width = 150, Height = 40, DialogResult = DialogResult.OK, Enabled = false };

    private readonly BindingSource source = new();

    public CustomerSearchCandidate? SelectedCustomer =>
        this.grid.SelectedRows.Count == 1
            ? this.grid.SelectedRows[0].DataBoundItem as CustomerSearchCandidate
            : null;

    public CustomerSearchDialog(IReadOnlyList<CustomerSearchCandidate> candidates, string? initialSearchText = null)
        : this(null, candidates, initialSearchText)
    {
    }

    public CustomerSearchDialog(
        ICustomerIdentificationService? identification,
        IReadOnlyList<CustomerSearchCandidate> candidates,
        string? initialSearchText = null)
        : this(identification)
    {
        if (!string.IsNullOrWhiteSpace(initialSearchText))
            this.search.Text = initialSearchText;
        LoadCandidates(candidates);
    }

    public CustomerSearchDialog(ICustomerIdentificationService? identification)
    {
        this.identification = identification;
        Text = "Customers - Cafeteria";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1120, 620);
        Size = new Size(1400, 720);
        BackColor = Color.FromArgb(244, 247, 250);
        Font = new Font("Segoe UI", 10);

        BuildLayout();
        ConfigureGrid();

        this.grid.DataSource = this.source;
        this.grid.SelectionChanged += (_, _) => ShowSelection();
        this.grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            DialogResult = DialogResult.OK;
            Close();
        };
        this.search.TextChanged += (_, _) =>
        {
            if (this.identification is null) return;
            this.searchDebounce.Stop();
            this.searchDebounce.Start();
        };
        this.searchDebounce.Tick += async (_, _) =>
        {
            this.searchDebounce.Stop();
            await SearchAsync();
        };
        this.search.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            this.searchDebounce.Stop();
            await SearchAsync();
        };
        FormClosed += (_, _) =>
        {
            this.searchDebounce.Stop();
            this.searchCancellation?.Cancel();
            this.searchCancellation?.Dispose();
        };

        Shown += async (_, _) =>
        {
            this.search.Focus();
            if (this.identification is not null && this.source.Count == 0) await SearchAsync();
        };
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(20)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        TableLayoutPanel heading = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = new Padding(0, 0, 0, 14) };
        heading.Controls.Add(new Label { Text = "Customers", AutoSize = true, Font = new Font("Segoe UI", 24, FontStyle.Bold), ForeColor = Color.FromArgb(28, 48, 71) });
        heading.Controls.Add(new Label { Text = "Search the local FFP employee cache.", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(2, 4, 0, 12) });
        TableLayoutPanel searchRow = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.Controls.Add(this.search, 0, 0);
        heading.Controls.Add(searchRow);
        heading.Controls.Add(this.count);
        root.Controls.Add(heading, 0, 0);

        SplitContainer body = new() { Dock = DockStyle.Fill, SplitterDistance = 1000, BackColor = BackColor };
        body.Panel1.Padding = new Padding(0, 0, 10, 0);
        body.Panel1.Controls.Add(this.grid);
        body.Panel2.Padding = new Padding(10, 0, 0, 0);
        body.Panel2.Controls.Add(BuildDetailsCard());
        root.Controls.Add(body, 0, 1);

        FlowLayoutPanel buttons = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 14, 0, 0) };
        Button cancel = new() { Text = "Cancel", Width = 110, Height = 40, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(this.select);
        root.Controls.Add(buttons, 0, 2);

        Controls.Add(root);
        AcceptButton = this.select;
        CancelButton = cancel;
    }

    private Control BuildDetailsCard()
    {
        Panel card = new() { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(22) };
        FlowLayoutPanel stack = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        this.photo.Width = 300;
        this.photo.BackColor = Color.FromArgb(235, 239, 243);
        stack.Controls.Add(this.photo);
        stack.Controls.Add(this.name);
        stack.Controls.Add(this.department);
        stack.Controls.Add(this.emailAddress);
        stack.Controls.Add(this.extension);
        card.Controls.Add(stack);
        return card;
    }

    private void ConfigureGrid()
    {
        this.grid.AutoGenerateColumns = false;

        DataGridViewTextBoxColumn employeeColumn = new()
        {
            DataPropertyName = nameof(CustomerSearchCandidate.Name),
            HeaderText = "Employee",
            FillWeight = 34,
            MinimumWidth = 220,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Padding = new Padding(8, 2, 4, 2)
            }
        };

        DataGridViewTextBoxColumn departmentColumn = new()
        {
            DataPropertyName = nameof(CustomerSearchCandidate.Department),
            HeaderText = "Department",
            FillWeight = 24,
            DefaultCellStyle = SupportingCellStyle()
        };

        DataGridViewTextBoxColumn emailColumn = new()
        {
            DataPropertyName = nameof(CustomerSearchCandidate.EmailAddress),
            HeaderText = "Email Address",
            FillWeight = 32,
            MinimumWidth = 220,
            DefaultCellStyle = SupportingCellStyle()
        };

        DataGridViewTextBoxColumn extensionColumn = new()
        {
            DataPropertyName = nameof(CustomerSearchCandidate.Extension),
            HeaderText = "Extension",
            FillWeight = 10,
            MinimumWidth = 95,
            DefaultCellStyle = SupportingCellStyle(DataGridViewContentAlignment.MiddleCenter)
        };

        this.grid.Columns.Add(employeeColumn);
        this.grid.Columns.Add(departmentColumn);
        this.grid.Columns.Add(emailColumn);
        this.grid.Columns.Add(extensionColumn);
    }

    private async Task SearchAsync()
    {
        if (this.identification is null) return;

        this.searchCancellation?.Cancel();
        this.searchCancellation?.Dispose();
        this.searchCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = this.searchCancellation.Token;
        string searchText = this.search.Text;

        try
        {
            IReadOnlyList<CustomerSearchCandidate> rows = await this.identification.SearchAsync(searchText, 50, cancellationToken);
            if (!cancellationToken.IsCancellationRequested && string.Equals(searchText, this.search.Text, StringComparison.Ordinal))
                LoadCandidates(rows);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void LoadCandidates(IReadOnlyList<CustomerSearchCandidate> candidates)
    {
        this.source.DataSource = candidates.ToList();
        this.count.Text = candidates.Count == 0 ? "No matching employees" : $"{candidates.Count} employee{(candidates.Count == 1 ? string.Empty : "s")} shown";
        this.grid.ClearSelection();
        this.grid.CurrentCell = null;
        ShowSelection();
    }

    private void ShowSelection()
    {
        CustomerSearchCandidate? customer = SelectedCustomer;
        this.select.Enabled = customer is not null;
        if (customer is null)
        {
            this.name.Text = "Select an employee";
            this.department.Text = string.Empty;
            this.emailAddress.Text = string.Empty;
            this.extension.Text = string.Empty;
            SetPhoto(null);
            return;
        }

        this.name.Text = customer.Name;
        this.department.Text = $"Department: {DisplayValue(customer.Department)}";
        this.emailAddress.Text = $"Email: {DisplayValue(customer.EmailAddress)}";
        this.extension.Text = $"Extension: {DisplayValue(customer.Extension)}";
        SetPhoto(customer.Photo);
    }

    private void SetPhoto(byte[]? bytes)
    {
        this.photo.Image?.Dispose();
        this.photo.Image = null;
        if (bytes is null || bytes.Length == 0) return;
        try
        {
            using MemoryStream stream = new(bytes);
            using Image temp = Image.FromStream(stream);
            this.photo.Image = new Bitmap(temp);
        }
        catch
        {
        }
    }

    private static string DisplayValue(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static Label DetailLabel() => new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI", 10),
        ForeColor = Color.FromArgb(82, 94, 108),
        Margin = new Padding(0, 8, 0, 0)
    };

    private static DataGridViewCellStyle SupportingCellStyle(DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft) => new()
    {
        Font = new Font("Segoe UI", 9f, FontStyle.Regular),
        ForeColor = Color.FromArgb(74, 86, 99),
        Alignment = alignment,
        Padding = new Padding(8, 2, 4, 2)
    };

    private static DataGridView CreateGrid() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = Color.White,
        BorderStyle = BorderStyle.None,
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
        GridColor = Color.FromArgb(232, 236, 240),
        RowTemplate = { Height = 44 },
        EnableHeadersVisualStyles = false,
        ColumnHeadersHeight = 44,
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(239, 243, 247),
            ForeColor = Color.FromArgb(47, 61, 76),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Padding = new Padding(8, 0, 4, 0),
            SelectionBackColor = Color.FromArgb(239, 243, 247),
            SelectionForeColor = Color.FromArgb(47, 61, 76)
        },
        DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White,
            ForeColor = Color.FromArgb(42, 53, 65),
            Font = new Font("Segoe UI", 9f),
            SelectionBackColor = Color.FromArgb(225, 238, 250),
            SelectionForeColor = Color.FromArgb(28, 48, 71)
        },
        AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(249, 251, 253),
            SelectionBackColor = Color.FromArgb(225, 238, 250),
            SelectionForeColor = Color.FromArgb(28, 48, 71)
        }
    };
}

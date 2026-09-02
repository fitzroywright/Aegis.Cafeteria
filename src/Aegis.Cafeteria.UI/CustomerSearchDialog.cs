using Aegis.Cafeteria.Application.Abstractions;

namespace Aegis.Cafeteria.UI;

internal sealed class CustomerSearchDialog : Form
{
    private static readonly Color Navy = Color.FromArgb(24, 45, 68);
    private static readonly Color Blue = Color.FromArgb(31, 111, 190);
    private static readonly Color Canvas = Color.FromArgb(244, 247, 250);
    private static readonly Color Muted = Color.FromArgb(104, 117, 132);
    private static readonly Color Green = Color.FromArgb(23, 119, 76);

    private readonly ICustomerIdentificationService? identification;
    private readonly TextBox search = new() { PlaceholderText = "Search employee name", Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10.5f) };
    private readonly System.Windows.Forms.Timer searchDebounce = new() { Interval = 250 };
    private CancellationTokenSource? searchCancellation;
    private readonly DataGridView grid = CreateGrid();
    private readonly PictureBox photo = new() { Dock = DockStyle.Top, Height = 190, SizeMode = PictureBoxSizeMode.Zoom };
    private readonly Label name = new() { AutoSize = true, Font = new Font("Segoe UI", 18, FontStyle.Bold), ForeColor = Navy, MaximumSize = new Size(330, 0) };
    private readonly Label department = DetailLabel();
    private readonly Label emailAddress = DetailLabel();
    private readonly Label extension = DetailLabel();
    private readonly Label count = new() { AutoSize = true, ForeColor = Muted, Font = new Font("Segoe UI", 9), Margin = new Padding(2, 6, 0, 0) };
    private readonly Button select = ActionButton("Select Customer", Green, 150);

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
        BackColor = Canvas;
        Font = new Font("Segoe UI", 10);

        this.select.DialogResult = DialogResult.OK;
        this.select.Enabled = false;

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
            RowCount = 4,
            BackColor = Canvas
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildHeader(), 0, 0);

        Panel searchCard = new()
        {
            Dock = DockStyle.Top,
            Height = 96,
            BackColor = Color.White,
            Padding = new Padding(20, 14, 20, 12),
            Margin = new Padding(20, 16, 20, 12)
        };
        TableLayoutPanel searchLayout = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        searchLayout.Controls.Add(new Label { Text = "EMPLOYEE SEARCH", AutoSize = true, ForeColor = Navy, Font = new Font("Segoe UI", 9, FontStyle.Bold) }, 0, 0);
        searchLayout.Controls.Add(this.search, 0, 1);
        searchLayout.Controls.Add(this.count, 0, 2);
        searchCard.Controls.Add(searchLayout);
        root.Controls.Add(searchCard, 0, 1);

        SplitContainer body = new()
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 1000,
            BackColor = Canvas,
            Margin = new Padding(20, 0, 20, 0)
        };
        body.Panel1.Padding = new Padding(0, 0, 8, 0);
        body.Panel1.Controls.Add(BuildGridCard());
        body.Panel2.Padding = new Padding(8, 0, 0, 0);
        body.Panel2.Controls.Add(BuildDetailsCard());
        root.Controls.Add(body, 0, 2);

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(20, 14, 20, 16),
            BackColor = Canvas
        };
        Button cancel = SecondaryButton("Cancel", 110);
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(this.select);
        root.Controls.Add(buttons, 0, 3);

        Controls.Add(root);
        AcceptButton = this.select;
        CancelButton = cancel;
    }

    private Control BuildHeader()
    {
        Panel header = new()
        {
            Dock = DockStyle.Top,
            Height = 82,
            BackColor = Navy,
            Padding = new Padding(22, 14, 22, 12)
        };
        FlowLayoutPanel stack = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        stack.Controls.Add(new Label { Text = "EMPLOYEE SEARCH", AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 16, FontStyle.Bold) });
        stack.Controls.Add(new Label { Text = "Food For The Poor Jamaica • Cafeteria", AutoSize = true, ForeColor = Color.FromArgb(193, 210, 225), Font = new Font("Segoe UI", 9) });
        header.Controls.Add(stack);
        return header;
    }

    private Control BuildGridCard()
    {
        Panel card = new() { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(1) };
        card.Controls.Add(this.grid);
        return card;
    }

    private Control BuildDetailsCard()
    {
        Panel card = new() { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(22) };
        FlowLayoutPanel stack = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        stack.Controls.Add(new Label { Text = "EMPLOYEE DETAILS", AutoSize = true, ForeColor = Blue, Font = new Font("Segoe UI", 9, FontStyle.Bold), Margin = new Padding(0, 0, 0, 12) });
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
                ForeColor = Navy,
                Padding = new Padding(10, 2, 4, 2)
            }
        };
        DataGridViewTextBoxColumn departmentColumn = new() { DataPropertyName = nameof(CustomerSearchCandidate.Department), HeaderText = "Department", FillWeight = 24, DefaultCellStyle = SupportingCellStyle() };
        DataGridViewTextBoxColumn emailColumn = new() { DataPropertyName = nameof(CustomerSearchCandidate.EmailAddress), HeaderText = "Email Address", FillWeight = 32, MinimumWidth = 220, DefaultCellStyle = SupportingCellStyle() };
        DataGridViewTextBoxColumn extensionColumn = new() { DataPropertyName = nameof(CustomerSearchCandidate.Extension), HeaderText = "Extension", FillWeight = 10, MinimumWidth = 95, DefaultCellStyle = SupportingCellStyle(DataGridViewContentAlignment.MiddleCenter) };

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

        if (candidates.Count == 1 && this.grid.Rows.Count == 1)
        {
            this.grid.CurrentCell = this.grid.Rows[0].Cells[0];
            this.grid.Rows[0].Selected = true;
            ShowSelection();
            return;
        }

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

    private static Label DetailLabel() => new() { AutoSize = true, Font = new Font("Segoe UI", 10), ForeColor = Muted, Margin = new Padding(0, 8, 0, 0) };

    private static DataGridViewCellStyle SupportingCellStyle(DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft) => new()
    {
        Font = new Font("Segoe UI", 9f, FontStyle.Regular),
        ForeColor = Color.FromArgb(74, 86, 99),
        Alignment = alignment,
        Padding = new Padding(10, 2, 4, 2)
    };

    private static Button ActionButton(string text, Color backColor, int width) => new()
    {
        Text = text,
        Width = width,
        Height = 42,
        FlatStyle = FlatStyle.Flat,
        BackColor = backColor,
        ForeColor = Color.White,
        Font = new Font("Segoe UI", 10, FontStyle.Bold),
        Cursor = Cursors.Hand,
        Margin = new Padding(8, 0, 0, 0)
    };

    private static Button SecondaryButton(string text, int width) => new()
    {
        Text = text,
        Width = width,
        Height = 42,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.White,
        ForeColor = Navy,
        Font = new Font("Segoe UI", 10, FontStyle.Bold),
        Cursor = Cursors.Hand,
        Margin = new Padding(8, 0, 0, 0)
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
        GridColor = Color.FromArgb(231, 235, 239),
        RowTemplate = { Height = 44 },
        EnableHeadersVisualStyles = false,
        ColumnHeadersHeight = 44,
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(238, 242, 246),
            ForeColor = Navy,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Padding = new Padding(10, 0, 4, 0),
            SelectionBackColor = Color.FromArgb(238, 242, 246),
            SelectionForeColor = Navy
        },
        DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White,
            ForeColor = Color.FromArgb(42, 53, 65),
            Font = new Font("Segoe UI", 9f),
            SelectionBackColor = Color.FromArgb(220, 235, 249),
            SelectionForeColor = Navy
        },
        AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(249, 251, 253),
            SelectionBackColor = Color.FromArgb(220, 235, 249),
            SelectionForeColor = Navy
        }
    };
}

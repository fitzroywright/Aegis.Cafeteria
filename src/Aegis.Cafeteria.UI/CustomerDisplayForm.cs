using Aegis.Cafeteria.Application.Reporting;

namespace Aegis.Cafeteria.UI;

/// <summary>
/// Read-only customer-facing display. This form is shown only when a second monitor is available.
/// It deliberately contains no private identifiers such as employee number, TRN, NFC tag or barcode.
/// </summary>
public sealed class CustomerDisplayForm : Form
{
    private static readonly Color Navy = Color.FromArgb(24, 45, 68);
    private static readonly Color Blue = Color.FromArgb(31, 111, 190);
    private static readonly Color Canvas = Color.FromArgb(244, 247, 250);
    private static readonly Color Green = Color.FromArgb(23, 119, 76);
    private static readonly Color Red = Color.FromArgb(176, 52, 52);
    private static readonly Color Muted = Color.FromArgb(104, 117, 132);

    private readonly PictureBox photo = new()
    {
        Width = 210,
        Height = 240,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.FromArgb(230, 235, 240),
        Margin = new Padding(0, 0, 28, 0)
    };

    private readonly Label name = new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI", 30, FontStyle.Bold),
        ForeColor = Navy,
        MaximumSize = new Size(900, 0)
    };

    private readonly Label eligibility = new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI", 13, FontStyle.Bold),
        Padding = new Padding(12, 7, 12, 7),
        Margin = new Padding(0, 12, 0, 0)
    };

    private readonly Label period = new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI", 13),
        ForeColor = Muted,
        Margin = new Padding(0, 14, 0, 0)
    };

    private readonly Label available = MoneyValue();
    private readonly Label subsidyRemaining = MoneyValue();
    private readonly Label nonSubsidyRemaining = MoneyValue();
    private readonly Label carriedDebt = MoneyValue();
    private readonly Label employeeAmountDue = MoneyValue();

    private readonly Label historyTitle = new()
    {
        AutoSize = true,
        Text = "Current month transactions",
        Font = new Font("Segoe UI", 18, FontStyle.Bold),
        ForeColor = Navy,
        Margin = new Padding(0, 0, 0, 12)
    };

    private readonly Label noHistory = new()
    {
        AutoSize = true,
        Text = "No transactions in the current month.",
        Font = new Font("Segoe UI", 13),
        ForeColor = Muted,
        Visible = false,
        Margin = new Padding(4, 16, 0, 0)
    };

    private readonly DataGridView history = CreateGrid();
    private readonly BindingSource historySource = new();

    public CustomerDisplayForm()
    {
        Text = "Cafeteria - Customer Display";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        BackColor = Canvas;
        Font = new Font("Segoe UI", 11);
        BuildLayout();
        this.history.DataSource = this.historySource;
    }

    public void PlaceOn(Screen screen)
    {
        // Move the window onto the customer-facing monitor before maximizing it.
        // Windows maximizes a form on the monitor containing its normal bounds.
        StartPosition = FormStartPosition.Manual;
        WindowState = FormWindowState.Normal;
        Bounds = screen.WorkingArea;
        WindowState = CustomerDisplayPolicy.UseMaximizedWindow
            ? FormWindowState.Maximized
            : FormWindowState.Normal;
    }

    public void ShowCustomer(
        CustomerAccountView account,
        IReadOnlyList<CustomerMonthlyTransactionView> currentMonthTransactions,
        int currentYear,
        int currentMonth)
    {
        this.name.Text = account.Customer.Name;
        this.period.Text = account.Customer.IsCashCustomer
            ? "Cash customer"
            : $"Current cafeteria period: {new DateTime(account.Period.Year, account.Period.Month, 1):MMMM yyyy}";

        this.eligibility.Text = account.Customer.IsCashCustomer
            ? "CASH CUSTOMER"
            : account.Customer.IsEligibleForCredit
                ? "ELIGIBLE FOR CAFETERIA CREDIT"
                : "CASH PURCHASES ONLY";
        this.eligibility.ForeColor = account.Customer.IsEligibleForCredit ? Green : account.Customer.IsCashCustomer ? Blue : Red;
        this.eligibility.BackColor = account.Customer.IsEligibleForCredit
            ? Color.FromArgb(226, 246, 236)
            : account.Customer.IsCashCustomer
                ? Color.FromArgb(234, 243, 252)
                : Color.FromArgb(255, 232, 232);

        if (account.Customer.IsEligibleForCredit && !account.Customer.IsCashCustomer)
        {
            this.available.Text = Money(account.FinancialStatus.TotalAvailableToSpend);
            this.subsidyRemaining.Text = Money(account.FinancialStatus.SubsidyAmountRemaining);
            this.nonSubsidyRemaining.Text = Money(account.FinancialStatus.NonSubsidyAmountRemaining);
            this.carriedDebt.Text = Money(account.FinancialStatus.CarriedForwardDebt);
            this.employeeAmountDue.Text = Money(account.FinancialStatus.EmployeeAmountDue);
        }
        else
        {
            this.available.Text = "—";
            this.subsidyRemaining.Text = "—";
            this.nonSubsidyRemaining.Text = "—";
            this.carriedDebt.Text = "—";
            this.employeeAmountDue.Text = "—";
        }

        SetPhoto(account.Customer.Photo);
        this.historyTitle.Text = $"Transactions — {new DateTime(currentYear, currentMonth, 1):MMMM yyyy}";
        this.historySource.DataSource = currentMonthTransactions.Select(x => new
        {
            Date = x.OccurredAt.LocalDateTime,
            x.Kind,
            x.Description,
            Amount = x.Amount,
            Account = x.CafeteriaAccountAmount,
            Cash = x.CashAmount
        }).ToList();
        this.noHistory.Visible = currentMonthTransactions.Count == 0;
        this.history.Visible = currentMonthTransactions.Count > 0;
    }

    public void ClearCustomer()
    {
        this.historySource.DataSource = null;
        this.photo.Image?.Dispose();
        this.photo.Image = null;
        Hide();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(44, 34, 44, 34),
            BackColor = Canvas
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var brand = new Label
        {
            AutoSize = true,
            Text = "CAFETERIA",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Blue,
            Margin = new Padding(0, 0, 0, 22)
        };
        root.Controls.Add(brand, 0, 0);

        var identityCard = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = Color.White,
            Padding = new Padding(30),
            Margin = new Padding(0, 0, 0, 28)
        };
        var identity = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1 };
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        identity.Controls.Add(this.photo, 0, 0);

        var details = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        details.Controls.Add(this.name);
        details.Controls.Add(this.eligibility);
        details.Controls.Add(this.period);
        details.Controls.Add(BuildFinancialGrid());
        identity.Controls.Add(details, 1, 0);
        identityCard.Controls.Add(identity);
        root.Controls.Add(identityCard, 0, 1);

        var historyCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(28)
        };
        historyCard.Controls.Add(this.history);
        historyCard.Controls.Add(this.noHistory);
        historyCard.Controls.Add(this.historyTitle);
        this.history.Dock = DockStyle.Fill;
        this.historyTitle.Dock = DockStyle.Top;
        this.noHistory.Dock = DockStyle.Top;
        root.Controls.Add(historyCard, 0, 2);
        Controls.Add(root);
    }

    private Control BuildFinancialGrid()
    {
        var grid = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 5,
            RowCount = 2,
            Margin = new Padding(0, 26, 0, 0)
        };
        for (var i = 0; i < 5; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddFinancial(grid, 0, "AVAILABLE TO SPEND", this.available);
        AddFinancial(grid, 1, "SUBSIDY REMAINING", this.subsidyRemaining);
        AddFinancial(grid, 2, "NON-SUBSIDY REMAINING", this.nonSubsidyRemaining);
        AddFinancial(grid, 3, "CARRIED DEBT", this.carriedDebt);
        AddFinancial(grid, 4, "EMPLOYEE AMOUNT DUE", this.employeeAmountDue);
        return grid;
    }

    private static void AddFinancial(TableLayoutPanel grid, int column, string caption, Label value)
    {
        var label = new Label
        {
            AutoSize = true,
            Text = caption,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Muted,
            Margin = new Padding(0, 0, 38, 4)
        };
        value.Margin = new Padding(0, 0, 38, 0);
        grid.Controls.Add(label, column, 0);
        grid.Controls.Add(value, column, 1);
    }

    private static Label MoneyValue() => new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI", 19, FontStyle.Bold),
        ForeColor = Navy,
        Text = "—"
    };

    private static DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            Font = new Font("Segoe UI", 12),
            ColumnHeadersHeight = 42,
            RowTemplate = { Height = 38 }
        };
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(230, 238, 247);
        grid.DefaultCellStyle.SelectionForeColor = Navy;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        grid.DataBindingComplete += (_, _) =>
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                var alignment = column.DefaultCellStyle.Alignment;
                if (alignment == DataGridViewContentAlignment.NotSet)
                    alignment = DataGridViewContentAlignment.MiddleLeft;
                column.HeaderCell.Style.Alignment = alignment;
            }
        };
        return grid;
    }

    private void SetPhoto(byte[]? bytes)
    {
        this.photo.Image?.Dispose();
        this.photo.Image = null;
        if (bytes is null || bytes.Length == 0) return;
        try
        {
            using var stream = new MemoryStream(bytes);
            using var temp = Image.FromStream(stream);
            this.photo.Image = new Bitmap(temp);
        }
        catch
        {
            // A bad directory photo must never prevent the customer display from working.
        }
    }

    private static string Money(decimal value) => value.ToString("C2");
}

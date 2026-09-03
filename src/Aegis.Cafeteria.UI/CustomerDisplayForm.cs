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
    private static readonly Color Purple = Color.FromArgb(120, 86, 180);
    private static readonly Color Orange = Color.FromArgb(202, 111, 26);
    private readonly Panel customerContent = new() { Dock = DockStyle.Fill, BackColor = Canvas };
    private readonly CustomerDisplayIdleClockControl idleClock = new();
    private readonly PictureBox photo = new() { Width = 210, Height = 240, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(230, 235, 240), Margin = new Padding(0, 0, 28, 0) };
    private readonly Label name = new() { AutoSize = true, Font = new Font("Segoe UI", 30, FontStyle.Bold), ForeColor = Navy, MaximumSize = new Size(900, 0) };
    private readonly Label creditLimitSummary = new() { AutoSize = true, Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = Blue, Margin = new Padding(0, 3, 0, 0), Visible = false };
    private readonly Label eligibility = new() { AutoSize = true, Font = new Font("Segoe UI", 13, FontStyle.Bold), Padding = new Padding(12, 7, 12, 7), Margin = new Padding(0, 12, 0, 0) };
    private readonly Label period = new() { AutoSize = true, Font = new Font("Segoe UI", 13), ForeColor = Muted, Margin = new Padding(0, 14, 0, 0) };
    private readonly Label available = MoneyValue(Green);
    private readonly Label subsidyRemaining = MoneyValue(Purple);
    private readonly Label payrollDeduction = MoneyValue(Orange);
    private readonly Label carriedDebt = MoneyValue(Red);
    private readonly Label employeeAmountDue = MoneyValue(Red);
    private readonly Label historyTitle = new() { AutoSize = true, Text = "Current month transactions", Font = new Font("Segoe UI", 18, FontStyle.Bold), ForeColor = Navy, Margin = new Padding(0, 0, 0, 12) };
    private readonly Label noHistory = new() { AutoSize = true, Text = "No transactions in the current month.", Font = new Font("Segoe UI", 13), ForeColor = Muted, Visible = false, Margin = new Padding(4, 16, 0, 0) };
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
        ConfigureHistoryGrid();
        this.history.DataSource = this.historySource;
        ShowIdleClock();
    }

    public void PlaceOn(Screen screen)
    {
        StartPosition = FormStartPosition.Manual;
        WindowState = FormWindowState.Normal;
        Bounds = screen.WorkingArea;
        WindowState = CustomerDisplayPolicy.UseMaximizedWindow ? FormWindowState.Maximized : FormWindowState.Normal;
        if (!Visible)
        {
            ShowIdleClock();
            Show();
            BringToFront();
        }
    }

    public void ShowCustomer(CustomerAccountView account, IReadOnlyList<CustomerMonthlyTransactionView> currentMonthTransactions, int currentYear, int currentMonth)
    {
        this.idleClock.StopClock();
        this.idleClock.Visible = false;
        this.customerContent.Visible = true;
        this.customerContent.BringToFront();
        this.name.Text = account.Customer.Name;
        this.period.Text = account.Customer.IsCashCustomer ? "Cash customer" : $"Current cafeteria period: {new DateTime(account.Period.Year, account.Period.Month, 1):MMMM yyyy}";
        this.eligibility.Text = account.Customer.IsCashCustomer ? "CASH CUSTOMER" : account.Customer.IsEligibleForCredit ? "ELIGIBLE FOR CAFETERIA CREDIT" : "CASH PURCHASES ONLY";
        this.eligibility.ForeColor = account.Customer.IsEligibleForCredit ? Green : account.Customer.IsCashCustomer ? Blue : Red;
        this.eligibility.BackColor = account.Customer.IsEligibleForCredit ? Color.FromArgb(226, 246, 236) : account.Customer.IsCashCustomer ? Color.FromArgb(234, 243, 252) : Color.FromArgb(255, 232, 232);
        if (account.Customer.IsEligibleForCredit && !account.Customer.IsCashCustomer)
        {
            this.creditLimitSummary.Text = $"Credit Limit: {Money(account.FinancialStatus.CreditLimit)}";
            this.creditLimitSummary.Visible = true;
            this.available.Text = Money(account.FinancialStatus.TotalAvailableToSpend);
            this.subsidyRemaining.Text = Money(account.FinancialStatus.SubsidyAmountRemaining);
            decimal amountToDeductFromPayroll = Math.Max(0m, account.Period.EmployeeFundedAllocation - account.FinancialStatus.NonSubsidyAmountRemaining);
            decimal outstandingBalance = Math.Max(0m, account.FinancialStatus.EmployeeAmountDue - amountToDeductFromPayroll);
            this.payrollDeduction.Text = Money(amountToDeductFromPayroll);
            this.carriedDebt.Text = Money(account.FinancialStatus.CarriedForwardDebt);
            this.employeeAmountDue.Text = Money(outstandingBalance);
        }
        else
        {
            this.creditLimitSummary.Text = string.Empty;
            this.creditLimitSummary.Visible = false;
            this.available.Text = "—";
            this.subsidyRemaining.Text = "—";
            this.payrollDeduction.Text = "—";
            this.carriedDebt.Text = "—";
            this.employeeAmountDue.Text = "—";
        }
        SetPhoto(account.Customer.Photo);
        this.historyTitle.Text = $"Transactions — {new DateTime(currentYear, currentMonth, 1):MMMM yyyy}";
        this.historySource.DataSource = currentMonthTransactions.Select(transaction =>
        {
            decimal coveredAmount = transaction.CafeteriaAccountAmount + transaction.CashAmount;
            decimal balance = transaction.Amount - coveredAmount;
            return new CustomerTransactionHistoryRow(transaction.OccurredAt.LocalDateTime, transaction.Kind, transaction.Description, transaction.Amount, transaction.CafeteriaAccountAmount, transaction.CashAmount, balance);
        }).ToList();
        this.noHistory.Visible = currentMonthTransactions.Count == 0;
        this.history.Visible = currentMonthTransactions.Count > 0;
    }

    public void ClearCustomer()
    {
        this.historySource.DataSource = null;
        this.photo.Image?.Dispose();
        this.photo.Image = null;
        ShowIdleClock();
    }

    private void ShowIdleClock()
    {
        this.customerContent.Visible = false;
        this.idleClock.Visible = true;
        this.idleClock.BringToFront();
        this.idleClock.StartClock();
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(44, 34, 44, 34), BackColor = Canvas };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Label brand = new() { AutoSize = true, Text = "CAFETERIA", Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = Blue, Margin = new Padding(0, 0, 0, 22) };
        root.Controls.Add(brand, 0, 0);
        Panel identityCard = new() { Dock = DockStyle.Top, AutoSize = true, BackColor = Color.White, Padding = new Padding(30), Margin = new Padding(0, 0, 0, 28) };
        TableLayoutPanel identity = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1 };
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        identity.Controls.Add(this.photo, 0, 0);
        FlowLayoutPanel details = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        details.Controls.Add(this.name);
        details.Controls.Add(this.creditLimitSummary);
        details.Controls.Add(this.eligibility);
        details.Controls.Add(this.period);
        details.Controls.Add(BuildFinancialGrid());
        identity.Controls.Add(details, 1, 0);
        identityCard.Controls.Add(identity);
        root.Controls.Add(identityCard, 0, 1);
        Panel historyCard = new() { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(28) };
        historyCard.Controls.Add(this.history);
        historyCard.Controls.Add(this.noHistory);
        historyCard.Controls.Add(this.historyTitle);
        this.history.Dock = DockStyle.Fill;
        this.historyTitle.Dock = DockStyle.Top;
        this.noHistory.Dock = DockStyle.Top;
        root.Controls.Add(historyCard, 0, 2);
        this.customerContent.Controls.Add(root);
        Controls.Add(this.customerContent);
        Controls.Add(this.idleClock);
    }

    private Control BuildFinancialGrid()
    {
        TableLayoutPanel grid = new() { AutoSize = true, ColumnCount = 5, RowCount = 2, Margin = new Padding(0, 26, 0, 0) };
        for (int index = 0; index < 5; index++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddFinancial(grid, 0, "AVAILABLE TO SPEND", this.available);
        AddFinancial(grid, 1, "SUBSIDY REMAINING", this.subsidyRemaining);
        AddFinancial(grid, 2, "FROM NEXT PAYCHECK", this.payrollDeduction);
        AddFinancial(grid, 3, "DEBT BROUGHT FORWARD", this.carriedDebt);
        AddFinancial(grid, 4, "OUTSTANDING BALANCE", this.employeeAmountDue);
        return grid;
    }

    private void ConfigureHistoryGrid()
    {
        this.history.AutoGenerateColumns = false;
        this.history.Columns.Clear();
        this.history.Columns.Add(TextColumn(nameof(CustomerTransactionHistoryRow.Date), "Date", 16));
        this.history.Columns.Add(TextColumn(nameof(CustomerTransactionHistoryRow.TransactionType), "Transaction Type", 16));
        this.history.Columns.Add(TextColumn(nameof(CustomerTransactionHistoryRow.Description), "Description", 26));
        this.history.Columns.Add(MoneyColumn(nameof(CustomerTransactionHistoryRow.AmountDue), "Amount Due", 12));
        this.history.Columns.Add(MoneyColumn(nameof(CustomerTransactionHistoryRow.FromCard), "From Card", 11));
        this.history.Columns.Add(MoneyColumn(nameof(CustomerTransactionHistoryRow.CashPayment), "Cash Payment", 11));
        this.history.Columns.Add(MoneyColumn(nameof(CustomerTransactionHistoryRow.Balance), "Balance", 11));
        this.history.Columns[0].DefaultCellStyle.Format = "M/d/yyyy h:mm tt";
        this.history.CellFormatting += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex < 0 || eventArgs.ColumnIndex < 0 || this.history.Columns[eventArgs.ColumnIndex].DataPropertyName != nameof(CustomerTransactionHistoryRow.Balance) || eventArgs.Value is not decimal balance || balance <= 0m) return;
            eventArgs.CellStyle.ForeColor = Red;
            eventArgs.CellStyle.Font = new Font(this.history.Font, FontStyle.Bold);
        };
    }

    private static DataGridViewTextBoxColumn TextColumn(string propertyName, string headerText, float fillWeight) => new() { DataPropertyName = propertyName, HeaderText = headerText, FillWeight = fillWeight, SortMode = DataGridViewColumnSortMode.Automatic, HeaderCell = { Style = { Alignment = DataGridViewContentAlignment.MiddleLeft } } };
    private static DataGridViewTextBoxColumn MoneyColumn(string propertyName, string headerText, float fillWeight)
    {
        DataGridViewTextBoxColumn column = TextColumn(propertyName, headerText, fillWeight);
        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        column.DefaultCellStyle.Format = "C2";
        column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        return column;
    }
    private static void AddFinancial(TableLayoutPanel grid, int column, string caption, Label value)
    {
        Label label = new() { AutoSize = true, Text = caption, Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Muted, Margin = new Padding(0, 0, 38, 4) };
        grid.Controls.Add(label, column, 0);
        grid.Controls.Add(value, column, 1);
    }
    private void SetPhoto(byte[]? bytes)
    {
        this.photo.Image?.Dispose();
        this.photo.Image = null;
        if (bytes is null || bytes.Length == 0) return;
        try { using MemoryStream stream = new(bytes); this.photo.Image = Image.FromStream(stream).Clone() as Image; }
        catch { this.photo.Image = null; }
    }
    private static Label MoneyValue(Color color) => new() { AutoSize = true, Font = new Font("Segoe UI", 22, FontStyle.Bold), ForeColor = color, Margin = new Padding(0, 0, 38, 0) };
    private static DataGridView CreateGrid()
    {
        DataGridView grid = new()
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
            AutoGenerateColumns = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, EnableHeadersVisualStyles = false, ColumnHeadersHeight = 44,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, RowTemplate = { Height = 38 },
            DefaultCellStyle = { BackColor = Color.White, ForeColor = Navy, SelectionBackColor = Color.FromArgb(226, 236, 246), SelectionForeColor = Navy, Padding = new Padding(8, 3, 8, 3) },
            AlternatingRowsDefaultCellStyle = { BackColor = Color.FromArgb(248, 250, 252) },
            ColumnHeadersDefaultCellStyle = { BackColor = Navy, ForeColor = Color.White, SelectionBackColor = Navy, SelectionForeColor = Color.White, Font = new Font("Segoe UI", 11, FontStyle.Bold), Padding = new Padding(8, 4, 8, 4), WrapMode = DataGridViewTriState.False },
            GridColor = Color.FromArgb(226, 231, 236)
        };
        return grid;
    }
    private static string Money(decimal value) => value.ToString("C2");
    private sealed record CustomerTransactionHistoryRow(DateTime Date, string TransactionType, string Description, decimal AmountDue, decimal FromCard, decimal CashPayment, decimal Balance);
}

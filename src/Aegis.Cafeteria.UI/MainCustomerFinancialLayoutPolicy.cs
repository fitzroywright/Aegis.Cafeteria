namespace Aegis.Cafeteria.UI;

internal static class MainCustomerFinancialLayoutPolicy
{
    private const string AvailableCaption = "AVAILABLE TO SPEND";
    private const string DebtCaption = "Debt brought forward";
    private const string CreditSalesCaption = "Credit sales this month";
    private const string CustomerPaymentsCaption = "Customer Payments";
    private const string EmployeeAmountDueCaption = "Employee Amount Due";
    private const string AppliedControlName = "AvailableToSpendFinancialRow";

    public static void Apply(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);

        if (form is not MainForm || form.Controls.Find(AppliedControlName, true).Length > 0)
        {
            return;
        }

        Label? availableCaption = Descendants(form)
            .OfType<Label>()
            .FirstOrDefault(label => string.Equals(label.Text, AvailableCaption, StringComparison.Ordinal));

        FlowLayoutPanel? availableStack = availableCaption?.Parent as FlowLayoutPanel;
        Panel? highlight = availableStack?.Parent as Panel;
        Label? availableValue = availableStack?.Controls
            .OfType<Label>()
            .FirstOrDefault(label => !ReferenceEquals(label, availableCaption));

        TableLayoutPanel? finances = Descendants(form)
            .OfType<TableLayoutPanel>()
            .FirstOrDefault(table => table.Controls
                .OfType<Label>()
                .Any(label => string.Equals(label.Text, DebtCaption, StringComparison.OrdinalIgnoreCase)));

        if (availableValue is null || highlight is null || finances is null)
        {
            return;
        }

        Control? debtValue = FindValueForCaption(finances, DebtCaption);
        Control? creditSalesValue = FindValueForCaption(finances, CreditSalesCaption);
        Control? paymentsValue = FindValueForCaption(finances, CustomerPaymentsCaption);
        Control? outstandingValue = FindValueForCaption(finances, EmployeeAmountDueCaption);

        if (debtValue is null || creditSalesValue is null || paymentsValue is null || outstandingValue is null)
        {
            return;
        }

        availableStack.Controls.Remove(availableValue);
        highlight.Parent?.Controls.Remove(highlight);
        highlight.Dispose();

        availableValue.Name = AppliedControlName;
        availableValue.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        availableValue.ForeColor = Color.FromArgb(24, 45, 68);
        availableValue.Anchor = AnchorStyles.Right;

        finances.SuspendLayout();
        try
        {
            finances.Controls.Clear();
            finances.RowStyles.Clear();
            finances.RowCount = 5;

            AddRow(finances, 0, "Available to Spend", availableValue);
            AddRow(finances, 1, "Debt Brought Forward", debtValue);
            AddRow(finances, 2, "Credit Sales This Month", creditSalesValue);
            AddRow(finances, 3, "Employee Payments", paymentsValue);
            AddRow(finances, 4, "Outstanding Balance", outstandingValue);
        }
        finally
        {
            finances.ResumeLayout(true);
        }
    }

    private static Control? FindValueForCaption(TableLayoutPanel finances, string captionText)
    {
        Label? caption = finances.Controls
            .OfType<Label>()
            .FirstOrDefault(label => string.Equals(label.Text, captionText, StringComparison.OrdinalIgnoreCase));

        if (caption is null)
        {
            return null;
        }

        int row = finances.GetRow(caption);
        return finances.Controls
            .Cast<Control>()
            .FirstOrDefault(control => !ReferenceEquals(control, caption) && finances.GetRow(control) == row && finances.GetColumn(control) == 1);
    }

    private static void AddRow(TableLayoutPanel finances, int row, string captionText, Control value)
    {
        finances.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Label caption = new()
        {
            Text = captionText,
            AutoSize = true,
            ForeColor = Color.FromArgb(104, 117, 132),
            Padding = new Padding(0, 5, 0, 5)
        };

        value.Anchor = AnchorStyles.Right;
        finances.Controls.Add(caption, 0, row);
        finances.Controls.Add(value, 1, row);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;

            foreach (Control descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}

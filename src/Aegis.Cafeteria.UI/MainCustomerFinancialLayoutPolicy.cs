namespace Aegis.Cafeteria.UI;

using System.Reflection;

internal static class MainCustomerFinancialLayoutPolicy
{
    private const string AppliedControlName = "AvailableToSpendFinancialRow";

    public static void Apply(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);

        if (form is not MainForm mainForm)
        {
            return;
        }

        Label? available = GetField<Label>(mainForm, "available");
        Label? carriedDebt = GetField<Label>(mainForm, "carriedDebt");
        Label? currentCreditDebt = GetField<Label>(mainForm, "currentCreditDebt");
        Label? customerPayments = GetField<Label>(mainForm, "customerPaymentsValue");
        Label? employeeAmountDue = GetField<Label>(mainForm, "employeeAmountDue");

        if (available is null
            || carriedDebt is null
            || currentCreditDebt is null
            || customerPayments is null
            || employeeAmountDue is null)
        {
            return;
        }

        if (string.Equals(available.Name, AppliedControlName, StringComparison.Ordinal)
            && available.Parent is TableLayoutPanel)
        {
            return;
        }

        TableLayoutPanel? finances = carriedDebt.Parent as TableLayoutPanel;
        if (finances is null)
        {
            return;
        }

        Control? oldAvailableContainer = available.Parent;
        Panel? highlight = oldAvailableContainer?.Parent as Panel;
        TableLayoutPanel? customerRoot = highlight?.Parent as TableLayoutPanel;
        int highlightRow = customerRoot is null || highlight is null
            ? -1
            : customerRoot.GetRow(highlight);

        oldAvailableContainer?.Controls.Remove(available);

        if (highlight is not null)
        {
            highlight.Parent?.Controls.Remove(highlight);
            highlight.Dispose();
        }

        if (customerRoot is not null && highlightRow >= 0)
        {
            Panel divider = new()
            {
                Name = "CustomerFinancialDivider",
                Dock = DockStyle.Top,
                Height = 4,
                BackColor = Color.FromArgb(235, 139, 40),
                Margin = new Padding(0, 8, 0, 12)
            };
            customerRoot.Controls.Add(divider, 0, highlightRow);
        }

        available.Name = AppliedControlName;
        available.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        available.ForeColor = Color.FromArgb(24, 45, 68);
        available.Anchor = AnchorStyles.Right;

        finances.SuspendLayout();
        try
        {
            finances.Controls.Clear();
            finances.RowStyles.Clear();
            finances.RowCount = 5;

            AddRow(finances, 0, "Available to Spend", available);
            AddRow(finances, 1, "Debt Brought Forward", carriedDebt);
            AddRow(finances, 2, "Credit Sales This Month", currentCreditDebt);
            AddRow(finances, 3, "Employee Payments", customerPayments);
            AddRow(finances, 4, "Outstanding Balance", employeeAmountDue);
        }
        finally
        {
            finances.ResumeLayout(true);
        }
    }

    private static T? GetField<T>(MainForm mainForm, string fieldName)
        where T : Control
    {
        FieldInfo? field = typeof(MainForm).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(mainForm) as T;
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
}

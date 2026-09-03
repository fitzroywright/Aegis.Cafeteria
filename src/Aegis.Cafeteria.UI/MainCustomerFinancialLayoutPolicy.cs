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

        Label? debtCaption = CaptionFor(finances, carriedDebt);
        Label? creditSalesCaption = CaptionFor(finances, currentCreditDebt);
        Label? paymentsCaption = CaptionFor(finances, customerPayments);
        Label? outstandingCaption = CaptionFor(finances, employeeAmountDue);

        if (debtCaption is null
            || creditSalesCaption is null
            || paymentsCaption is null
            || outstandingCaption is null)
        {
            return;
        }

        Control? oldAvailableContainer = available.Parent;
        Panel? highlight = oldAvailableContainer?.Parent as Panel;

        oldAvailableContainer?.Controls.Remove(available);
        if (highlight is not null)
        {
            highlight.Parent?.Controls.Remove(highlight);
            highlight.Dispose();
        }

        Label availableCaption = new()
        {
            Text = "Available to Spend",
            AutoSize = true,
            ForeColor = debtCaption.ForeColor,
            Font = debtCaption.Font,
            BackColor = debtCaption.BackColor,
            Padding = debtCaption.Padding,
            Margin = debtCaption.Margin
        };

        available.Name = AppliedControlName;
        available.Font = carriedDebt.Font;
        available.ForeColor = carriedDebt.ForeColor;
        available.BackColor = carriedDebt.BackColor;
        available.Padding = carriedDebt.Padding;
        available.Margin = carriedDebt.Margin;
        available.AutoSize = carriedDebt.AutoSize;
        available.Anchor = carriedDebt.Anchor;

        debtCaption.Text = "Debt Brought Forward";
        creditSalesCaption.Text = "Credit Sales This Month";
        paymentsCaption.Text = "Employee Payments";
        outstandingCaption.Text = "Outstanding Balance";

        HashSet<Control> keep =
        [
            availableCaption,
            available,
            debtCaption,
            carriedDebt,
            creditSalesCaption,
            currentCreditDebt,
            paymentsCaption,
            customerPayments,
            outstandingCaption,
            employeeAmountDue
        ];

        finances.SuspendLayout();
        try
        {
            Control[] unwanted = finances.Controls
                .Cast<Control>()
                .Where(control => !keep.Contains(control))
                .ToArray();

            foreach (Control control in unwanted)
            {
                finances.Controls.Remove(control);
            }

            finances.Controls.Add(availableCaption, 0, 0);
            finances.Controls.Add(available, 1, 0);

            SetRow(finances, debtCaption, carriedDebt, 1);
            SetRow(finances, creditSalesCaption, currentCreditDebt, 2);
            SetRow(finances, paymentsCaption, customerPayments, 3);
            SetRow(finances, outstandingCaption, employeeAmountDue, 4);
        }
        finally
        {
            finances.ResumeLayout(true);
        }
    }

    private static Label? CaptionFor(TableLayoutPanel finances, Control value)
    {
        int row = finances.GetRow(value);
        return finances.Controls
            .OfType<Label>()
            .FirstOrDefault(control => !ReferenceEquals(control, value)
                && finances.GetRow(control) == row
                && finances.GetColumn(control) == 0);
    }

    private static void SetRow(TableLayoutPanel finances, Label caption, Control value, int row)
    {
        finances.SetRow(caption, row);
        finances.SetColumn(caption, 0);
        finances.SetRow(value, row);
        finances.SetColumn(value, 1);
    }

    private static T? GetField<T>(MainForm mainForm, string fieldName)
        where T : Control
    {
        FieldInfo? field = typeof(MainForm).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(mainForm) as T;
    }
}

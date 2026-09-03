namespace Aegis.Cafeteria.UI;

internal static class MainCustomerFinancialLayoutPolicy
{
    private const string AvailableCaption = "AVAILABLE TO SPEND";
    private const string DebtCaption = "Debt brought forward";
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
                .Any(label => string.Equals(label.Text, DebtCaption, StringComparison.Ordinal)));

        Label? debtCaption = finances?.Controls
            .OfType<Label>()
            .FirstOrDefault(label => string.Equals(label.Text, DebtCaption, StringComparison.Ordinal));

        if (availableValue is null || highlight is null || finances is null || debtCaption is null)
        {
            return;
        }

        int debtRow = finances.GetRow(debtCaption);
        Control[] controlsToMove = finances.Controls
            .Cast<Control>()
            .Where(control => finances.GetRow(control) >= debtRow)
            .ToArray();

        foreach (Control control in controlsToMove)
        {
            finances.SetRow(control, finances.GetRow(control) + 1);
        }

        availableStack.Controls.Remove(availableValue);
        highlight.Parent?.Controls.Remove(highlight);
        highlight.Dispose();

        availableValue.Name = AppliedControlName;
        availableValue.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        availableValue.ForeColor = Color.FromArgb(24, 45, 68);
        availableValue.Anchor = AnchorStyles.Right;

        Label caption = new()
        {
            Text = "Available to Spend",
            AutoSize = true,
            ForeColor = Color.FromArgb(104, 117, 132),
            Padding = new Padding(0, 5, 0, 5)
        };

        finances.RowCount++;
        finances.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        finances.Controls.Add(caption, 0, debtRow);
        finances.Controls.Add(availableValue, 1, debtRow);
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

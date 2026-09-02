namespace Aegis.Cafeteria.UI;

internal static class PosCashierUiPolicy
{
    public static void Apply(Form mainForm)
    {
        ArgumentNullException.ThrowIfNull(mainForm);

        foreach (Control control in Descendants(mainForm))
        {
            if (control is Button button)
            {
                if (string.Equals(button.Text, "Void Current Sale", StringComparison.Ordinal)
                    || string.Equals(button.Text, "New Customer", StringComparison.Ordinal))
                {
                    button.Visible = false;
                    button.Enabled = false;
                    continue;
                }

                if (string.Equals(button.Text, "Customer Payment...", StringComparison.Ordinal))
                {
                    button.Text = "Employee Payment...";
                    button.Width = 190;
                    button.MinimumSize = new Size(190, button.Height);
                }
            }

            if (control is Label label)
            {
                if (label.Text.StartsWith("VOID CURRENT SALE", StringComparison.OrdinalIgnoreCase)
                    || label.Text.StartsWith("VOID AVAILABLE", StringComparison.OrdinalIgnoreCase))
                {
                    label.Visible = false;
                    continue;
                }

                if (string.Equals(label.Text, "CUSTOMER PAYMENT", StringComparison.Ordinal))
                {
                    label.Text = "EMPLOYEE PAYMENT";
                    continue;
                }

                if (string.Equals(label.Text, "Debt brought forward", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(label.Text, "Carried Debt", StringComparison.OrdinalIgnoreCase))
                {
                    label.Text = "Debt Brought Forward";
                }
            }
        }

        MoveReadyCustomerInstructions(mainForm);
    }

    private static void MoveReadyCustomerInstructions(Form mainForm)
    {
        Label? readyTitle = Descendants(mainForm)
            .OfType<Label>()
            .FirstOrDefault(label => string.Equals
            (
                label.Text,
                "Ready for customer",
                StringComparison.OrdinalIgnoreCase
            ));
        Label? recentTitle = Descendants(mainForm)
            .OfType<Label>()
            .FirstOrDefault(label => string.Equals
            (
                label.Text,
                "RECENT TRANSACTIONS - TODAY",
                StringComparison.OrdinalIgnoreCase
            ));

        if (readyTitle?.Parent is not FlowLayoutPanel readyStack
            || recentTitle?.Parent is not Panel recentPanel)
        {
            return;
        }

        DataGridView? recentGrid = recentPanel.Controls
            .OfType<DataGridView>()
            .FirstOrDefault();
        if (recentGrid is null)
        {
            return;
        }

        readyStack.Parent?.Controls.Remove(readyStack);

        recentPanel.SuspendLayout();
        recentPanel.Controls.Clear();

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        readyStack.Dock = DockStyle.Fill;
        readyStack.AutoSize = true;
        readyStack.Margin = new Padding(0, 0, 0, 12);
        recentTitle.Dock = DockStyle.Fill;
        recentTitle.Height = 32;
        recentGrid.Dock = DockStyle.Fill;

        layout.Controls.Add(readyStack, 0, 0);
        layout.Controls.Add(recentTitle, 0, 1);
        layout.Controls.Add(recentGrid, 0, 2);
        recentPanel.Controls.Add(layout);
        recentPanel.ResumeLayout(true);
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;

            foreach (Control descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}

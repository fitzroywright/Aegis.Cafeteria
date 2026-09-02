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
                if (string.Equals(button.Text, "Void Current Sale", StringComparison.Ordinal))
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

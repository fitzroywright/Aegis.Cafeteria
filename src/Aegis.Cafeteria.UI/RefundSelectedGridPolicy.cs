namespace Aegis.Cafeteria.UI;

using System.Reflection;

internal static class RefundSelectedGridPolicy
{
    private static readonly HashSet<MainForm> attachedForms = [];

    public static void Attach(MainForm mainForm)
    {
        ArgumentNullException.ThrowIfNull(mainForm);

        if (!attachedForms.Add(mainForm))
        {
            return;
        }

        FieldInfo? transactionsField = typeof(MainForm).GetField
        (
              "transactions"
            , BindingFlags.Instance | BindingFlags.NonPublic
        );

        if (transactionsField?.GetValue(mainForm) is not DataGridView transactions)
        {
            return;
        }

        transactions.DataBindingComplete += (_, _) => ApplyFilter(transactions);
        mainForm.FormClosed += (_, _) => attachedForms.Remove(mainForm);
        ApplyFilter(transactions);
    }

    private static void ApplyFilter(DataGridView transactions)
    {
        if (transactions.IsDisposed || !transactions.Columns.Contains("Kind"))
        {
            return;
        }

        transactions.CurrentCell = null;

        foreach (DataGridViewRow row in transactions.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            string kind = Convert.ToString(row.Cells["Kind"].Value)?.Trim() ?? string.Empty;
            bool isPurchase = string.Equals(kind, "Purchase", StringComparison.OrdinalIgnoreCase);

            row.Selected = false;
            row.Visible = isPurchase;
        }

        DataGridViewRow? firstPurchase = transactions.Rows
            .Cast<DataGridViewRow>()
            .FirstOrDefault(row => row.Visible && !row.IsNewRow);

        if (firstPurchase is not null)
        {
            DataGridViewCell? firstVisibleCell = firstPurchase.Cells
                .Cast<DataGridViewCell>()
                .FirstOrDefault(cell => cell.Visible);

            if (firstVisibleCell is not null)
            {
                transactions.CurrentCell = firstVisibleCell;
            }
        }
    }
}

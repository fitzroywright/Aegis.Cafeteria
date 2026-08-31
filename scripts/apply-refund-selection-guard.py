from pathlib import Path

windows_path = Path("src/Aegis.Cafeteria.UI/MainForm.cs")
windows = windows_path.read_text(encoding="utf-8")
old_windows = '''            var rowType = selectedRow.GetType();
            var kind = rowType.GetProperty("Kind")?.GetValue(selectedRow)?.ToString();
            var referenceValue = rowType.GetProperty("ReferenceId")?.GetValue(selectedRow);
            if (string.Equals(kind, "Purchase", StringComparison.OrdinalIgnoreCase) && referenceValue is Guid orderId)
                return await this.orderQuery.GetAsync(orderId);'''
new_windows = '''            Type rowType = selectedRow.GetType();
            string? kind = rowType.GetProperty("Kind")?.GetValue(selectedRow)?.ToString();
            object? referenceValue = rowType.GetProperty("ReferenceId")?.GetValue(selectedRow);

            if (string.Equals(kind, "Refund", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show
                (
                      this
                    , "Refund entries cannot be refunded. Select the original purchase instead."
                    , "Refund"
                    , MessageBoxButtons.OK
                    , MessageBoxIcon.Information
                );
                return null;
            }

            if (string.Equals(kind, "Purchase", StringComparison.OrdinalIgnoreCase) && referenceValue is Guid orderId)
            {
                return await this.orderQuery.GetAsync(orderId);
            }'''
if old_windows not in windows:
    raise SystemExit("Windows refund selection marker not found")
windows_path.write_text(windows.replace(old_windows, new_windows, 1), encoding="utf-8")

linux_path = Path("src/Aegis.Cafeteria.UI.Linux/MainWindow.cs")
linux = linux_path.read_text(encoding="utf-8")
old_linux = '''        if (transactions.SelectedItem is not TransactionRow selected ||
            !string.Equals(selected.Transaction.Kind, "Purchase", StringComparison.OrdinalIgnoreCase))
        {
            status.Text = "Select a purchase from this month's transactions to refund.";
            return;
        }'''
new_linux = '''        if (transactions.SelectedItem is not TransactionRow selected)
        {
            status.Text = "Select a purchase from this month's transactions to refund.";
            return;
        }

        if (string.Equals(selected.Transaction.Kind, "Refund", StringComparison.OrdinalIgnoreCase))
        {
            status.Text = "Refund entries cannot be refunded. Select the original purchase instead.";
            return;
        }

        if (!string.Equals(selected.Transaction.Kind, "Purchase", StringComparison.OrdinalIgnoreCase))
        {
            status.Text = "Select a purchase from this month's transactions to refund.";
            return;
        }'''
if old_linux not in linux:
    raise SystemExit("Linux refund selection marker not found")
linux_path.write_text(linux.replace(old_linux, new_linux, 1), encoding="utf-8")

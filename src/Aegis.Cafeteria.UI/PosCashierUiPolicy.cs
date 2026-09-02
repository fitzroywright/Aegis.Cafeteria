namespace Aegis.Cafeteria.UI;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Orders;
using System.Reflection;

internal static class PosCashierUiPolicy
{
    private static readonly Color Orange = Color.FromArgb(235, 139, 40);
    private static readonly Color Red = Color.FromArgb(176, 52, 52);

    public static void Apply(
        Form mainForm,
        IOrderQuery orderQuery,
        ICustomerIdentificationService identification)
    {
        ArgumentNullException.ThrowIfNull(mainForm);
        ArgumentNullException.ThrowIfNull(orderQuery);
        ArgumentNullException.ThrowIfNull(identification);

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
                    continue;
                }

                if (string.Equals(label.Text, "Employee Amount Due", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(label.Text, "Outstanding Employee Balance", StringComparison.OrdinalIgnoreCase))
                {
                    label.Text = "Outstanding Balance";
                    continue;
                }

                if (string.Equals
                (
                    label.Text,
                    "Record money received from the employee to reduce the Employee Amount Due shown above.",
                    StringComparison.Ordinal
                )
                    || string.Equals
                    (
                        label.Text,
                        "Record money received from the employee to reduce the Outstanding Employee Balance shown above.",
                        StringComparison.Ordinal
                    ))
                {
                    label.Text = "Record money received from the employee to reduce the Outstanding Balance shown above.";
                }
            }
        }

        StyleOutstandingBalance(mainForm);
        SimplifyCustomerFunding(mainForm);
        MoveReadyCustomerInstructions(mainForm);
        ConfigureRecentTransactionNavigation(mainForm, orderQuery, identification);
    }

    private static void StyleOutstandingBalance(Form mainForm)
    {
        Label? caption = Descendants(mainForm)
            .OfType<Label>()
            .FirstOrDefault(label => string.Equals(label.Text, "Outstanding Balance", StringComparison.OrdinalIgnoreCase));
        if (caption?.Parent is not TableLayoutPanel table)
        {
            return;
        }

        TableLayoutPanelCellPosition position = table.GetPositionFromControl(caption);
        if (position.Row < 0)
        {
            return;
        }

        Control? value = table.GetControlFromPosition(1, position.Row);
        if (value is not null)
        {
            value.ForeColor = Red;
        }
    }

    private static void SimplifyCustomerFunding(Form mainForm)
    {
        Label? availableCaption = Descendants(mainForm)
            .OfType<Label>()
            .FirstOrDefault(label => string.Equals(label.Text, "AVAILABLE TO SPEND", StringComparison.Ordinal));
        if (availableCaption?.Parent is not FlowLayoutPanel availableStack)
        {
            return;
        }

        Label? availableValue = availableStack.Controls
            .OfType<Label>()
            .FirstOrDefault(label => !ReferenceEquals(label, availableCaption));
        Panel? highlightPanel = availableStack.Parent as Panel;

        FlowLayoutPanel? customerInfo = Descendants(mainForm)
            .OfType<FlowLayoutPanel>()
            .FirstOrDefault(panel => panel.FlowDirection == FlowDirection.TopDown
                && panel.Controls.OfType<Label>().Any(label => Math.Abs(label.Font.Size - 21F) < 0.2F));
        if (availableValue is null || customerInfo is null)
        {
            return;
        }

        Label? fundingStatus = customerInfo.Controls
            .OfType<Label>()
            .FirstOrDefault(label => label.Padding.Left == 8
                && label.Padding.Top == 4
                && label.Margin.Top == 8);
        if (fundingStatus is null)
        {
            return;
        }

        customerInfo.Controls.Remove(fundingStatus);
        availableStack.Controls.Remove(availableValue);

        FlowLayoutPanel fundingRow = new()
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 0)
        };
        Label availableInlineCaption = new()
        {
            AutoSize = true,
            Text = "Available to Spend:",
            Margin = new Padding(12, 0, 4, 0)
        };

        fundingStatus.Margin = Padding.Empty;
        availableValue.Margin = Padding.Empty;
        availableValue.AutoSize = true;

        void SyncFundingStyle()
        {
            availableInlineCaption.Font = fundingStatus.Font;
            availableInlineCaption.ForeColor = fundingStatus.ForeColor;
            availableInlineCaption.BackColor = fundingStatus.BackColor;
            availableInlineCaption.Padding = fundingStatus.Padding;

            availableValue.Font = fundingStatus.Font;
            availableValue.ForeColor = fundingStatus.ForeColor;
            availableValue.BackColor = fundingStatus.BackColor;
            availableValue.Padding = fundingStatus.Padding;
        }

        fundingStatus.ForeColorChanged += (_, _) => SyncFundingStyle();
        fundingStatus.BackColorChanged += (_, _) => SyncFundingStyle();
        fundingStatus.FontChanged += (_, _) => SyncFundingStyle();
        SyncFundingStyle();

        fundingRow.Controls.Add(fundingStatus);
        fundingRow.Controls.Add(availableInlineCaption);
        fundingRow.Controls.Add(availableValue);
        customerInfo.Controls.Add(fundingRow);
        customerInfo.Controls.SetChildIndex(fundingRow, 1);

        if (highlightPanel is not null)
        {
            highlightPanel.SuspendLayout();
            highlightPanel.Controls.Clear();
            highlightPanel.AutoSize = false;
            highlightPanel.Visible = true;
            highlightPanel.Height = 5;
            highlightPanel.MinimumSize = new Size(0, 5);
            highlightPanel.BackColor = Orange;
            highlightPanel.Padding = Padding.Empty;
            highlightPanel.Margin = new Padding(0, 0, 0, 12);
            highlightPanel.ResumeLayout(true);
        }

        HideFinancialRow(mainForm, "Monthly cafeteria credit");
        HideFinancialRow(mainForm, "Company subsidy remaining");
        HideFinancialRow(mainForm, "Employee-funded remaining");
        HideFinancialRow(mainForm, "Customer Responsibility (before payments)");
    }

    private static void ConfigureRecentTransactionNavigation(
        Form mainForm,
        IOrderQuery orderQuery,
        ICustomerIdentificationService identification)
    {
        Label? recentTitle = Descendants(mainForm)
            .OfType<Label>()
            .FirstOrDefault(label => string.Equals
            (
                label.Text,
                "RECENT TRANSACTIONS - TODAY",
                StringComparison.OrdinalIgnoreCase
            ));
        if (recentTitle?.Parent is not TableLayoutPanel recentLayout)
        {
            return;
        }

        DataGridView? recentGrid = recentLayout.Controls
            .OfType<DataGridView>()
            .FirstOrDefault();
        if (recentGrid is null)
        {
            return;
        }

        recentGrid.CellDoubleClick += async (_, eventArgs) =>
        {
            if (eventArgs.RowIndex < 0 || eventArgs.RowIndex >= recentGrid.Rows.Count)
            {
                return;
            }

            object? row = recentGrid.Rows[eventArgs.RowIndex].DataBoundItem;
            PropertyInfo? orderIdProperty = row?.GetType().GetProperty("OrderId");
            if (orderIdProperty?.GetValue(row) is not Guid orderId || orderId == Guid.Empty)
            {
                return;
            }

            try
            {
                Order? order = await orderQuery.GetAsync(orderId);
                if (order is null || order.CustomerId <= 0)
                {
                    return;
                }

                CustomerAccountView? account = await identification.LoadAsync(order.CustomerId);
                if (account is null)
                {
                    return;
                }

                MethodInfo? showCustomer = mainForm.GetType().GetMethod
                (
                    "ShowCustomer",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                showCustomer?.Invoke(mainForm, [account]);
            }
            catch (Exception exception)
            {
                MessageBox.Show
                (
                    mainForm,
                    exception.GetBaseException().Message,
                    "Open Customer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        };
    }

    private static void HideFinancialRow(Form mainForm, string captionText)
    {
        Label? caption = Descendants(mainForm)
            .OfType<Label>()
            .FirstOrDefault(label => string.Equals(label.Text, captionText, StringComparison.OrdinalIgnoreCase));
        if (caption?.Parent is not TableLayoutPanel table)
        {
            return;
        }

        TableLayoutPanelCellPosition position = table.GetPositionFromControl(caption);
        if (position.Row < 0)
        {
            return;
        }

        caption.Visible = false;
        Control? value = table.GetControlFromPosition(1, position.Row);
        if (value is not null)
        {
            value.Visible = false;
        }

        while (table.RowStyles.Count <= position.Row)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        table.RowStyles[position.Row].SizeType = SizeType.Absolute;
        table.RowStyles[position.Row].Height = 0F;
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

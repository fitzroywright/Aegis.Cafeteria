namespace Aegis.Cafeteria.UI;

using System.Reflection;
using System.Runtime.CompilerServices;
using Aegis.Cafeteria.Application.Reporting;

internal static class CustomerActivitySummaryPolicy
{
    private static readonly Color Muted = Color.FromArgb(104, 117, 132);
    private static bool applied;
    private static bool updating;

    [ModuleInitializer]
    internal static void Initialize()
    {
        System.Windows.Forms.Application.Idle += OnApplicationIdle;
    }

    private static void OnApplicationIdle(object? sender, EventArgs e)
    {
        if (applied)
        {
            return;
        }

        MainForm? mainForm = System.Windows.Forms.Application.OpenForms
            .OfType<MainForm>()
            .FirstOrDefault();
        if (mainForm is null)
        {
            return;
        }

        Apply(mainForm);
        applied = true;
    }

    private static void Apply(MainForm mainForm)
    {
        Label? fundingStatus = GetField<Label>(mainForm, "fundingStatus");
        Label? customerName = GetField<Label>(mainForm, "customerName");
        if (fundingStatus is null || customerName is null)
        {
            return;
        }

        fundingStatus.Padding = Padding.Empty;
        fundingStatus.Margin = new Padding(0, 7, 0, 0);
        fundingStatus.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
        fundingStatus.ForeColor = Muted;
        fundingStatus.BackColor = Color.Transparent;
        fundingStatus.Visible = true;

        customerName.TextChanged += (_, _) => Refresh(mainForm, fundingStatus);
        fundingStatus.TextChanged += (_, _) => Refresh(mainForm, fundingStatus);
        Refresh(mainForm, fundingStatus);
    }

    private static void Refresh(MainForm mainForm, Label summary)
    {
        if (updating)
        {
            return;
        }

        try
        {
            updating = true;

            CustomerAccountView? account = GetField<CustomerAccountView>(mainForm, "currentCustomer");
            if (account is null)
            {
                summary.Text = string.Empty;
                summary.Visible = false;
                return;
            }

            CustomerMonthlyTransactionView[] purchases = account.Transactions
                .Where(transaction => string.Equals(transaction.Kind, "Purchase", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(transaction => transaction.OccurredAt)
                .ToArray();

            summary.Visible = true;
            if (purchases.Length == 0)
            {
                summary.Text = "No purchases this month";
                return;
            }

            string noun = purchases.Length == 1 ? "purchase" : "purchases";
            DateTimeOffset lastPurchase = purchases[0].OccurredAt;
            summary.Text = $"This month: {purchases.Length} {noun}  •  Last purchase: {lastPurchase.LocalDateTime:MMM d, h:mm tt}";
        }
        finally
        {
            updating = false;
        }
    }

    private static T? GetField<T>(MainForm mainForm, string fieldName)
        where T : class
    {
        FieldInfo? field = typeof(MainForm).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(mainForm) as T;
    }
}

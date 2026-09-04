namespace Aegis.Cafeteria.UI;

using System.Reflection;
using System.Runtime.CompilerServices;

internal static class MainUiPresentationPolicy
{
    private const string CurrentDashboardTitle = "CAFETERIA SALES DASHBOARD";
    private const string DailyDashboardTitle = "CAFETERIA DAILY SALES DASHBOARD";
    private static bool applied;

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

        RenameDashboard(mainForm);
        RemoveVoidAvailabilityMessage(mainForm);
        applied = true;
        System.Windows.Forms.Application.Idle -= OnApplicationIdle;
    }

    private static void RenameDashboard(MainForm mainForm)
    {
        Label? dashboardTitle = Descendants(mainForm)
            .OfType<Label>()
            .FirstOrDefault(label => string.Equals(label.Text, CurrentDashboardTitle, StringComparison.Ordinal));

        if (dashboardTitle is not null)
        {
            dashboardTitle.Text = DailyDashboardTitle;
        }
    }

    private static void RemoveVoidAvailabilityMessage(MainForm mainForm)
    {
        FieldInfo? field = typeof(MainForm).GetField("voidAvailability", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(mainForm) is not Label voidAvailability)
        {
            return;
        }

        voidAvailability.Text = string.Empty;
        voidAvailability.Visible = false;
        voidAvailability.Parent?.Controls.Remove(voidAvailability);
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

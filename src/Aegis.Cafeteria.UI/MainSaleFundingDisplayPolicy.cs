namespace Aegis.Cafeteria.UI;

using System.Reflection;

internal static class MainSaleFundingDisplayPolicy
{
    public static void Apply(MainForm mainForm)
    {
        ArgumentNullException.ThrowIfNull(mainForm);

        Label? fundingStatus = GetField<Label>(mainForm, "fundingStatus");
        if (fundingStatus is not null)
        {
            fundingStatus.Visible = false;
        }

        Label? available = GetField<Label>(mainForm, "available");
        if (available?.Parent?.Parent is Panel availablePanel)
        {
            availablePanel.Visible = false;
        }
    }

    private static T? GetField<T>(MainForm mainForm, string fieldName)
        where T : Control
    {
        FieldInfo? field = typeof(MainForm).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(mainForm) as T;
    }
}

namespace Aegis.Cafeteria.UI;

using System.Reflection;
using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;

internal static class FfpDataToolsUiPolicy
{
    private const string FfpDataToolsTitle = "FFP Data Tools";
    private static bool menuAdded;

    public static void Apply(CafeteriaUiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        System.Windows.Forms.Application.Idle += (_, _) =>
        {
            foreach (CafeteriaAdministrationDialog dialog in System.Windows.Forms.Application.OpenForms.OfType<CafeteriaAdministrationDialog>())
                HideDataTools(dialog);

            MainForm? mainForm = System.Windows.Forms.Application.OpenForms.OfType<MainForm>().FirstOrDefault();
            if (mainForm is null)
                return;

            RefundSelectedGridPolicy.Attach(mainForm);

            if (!options.ShowFfpDataTools || menuAdded)
                return;

            AddMenu(mainForm);
            menuAdded = true;
        };
    }

    private static void AddMenu(MainForm mainForm)
    {
        MenuStrip? menu = mainForm.MainMenuStrip;
        ToolStripMenuItem? administration = menu?.Items.OfType<ToolStripMenuItem>()
            .FirstOrDefault(item => string.Equals(item.Text, "Administration", StringComparison.Ordinal));
        if (administration is null)
            return;

        ToolStripMenuItem item = new("FFP Tools...");
        item.Click += async (_, _) => await OpenAsync(mainForm);
        administration.DropDownItems.Insert(Math.Min(1, administration.DropDownItems.Count), item);
    }

    private static async Task OpenAsync(MainForm mainForm)
    {
        FieldInfo? servicesField = typeof(MainForm).GetField("services", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo? authenticatorField = typeof(MainForm).GetField("operatorAuthenticator", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo? registryField = typeof(MainForm).GetField("managerAuthorization", BindingFlags.Instance | BindingFlags.NonPublic);
        if (servicesField?.GetValue(mainForm) is not IServiceProvider services ||
            authenticatorField?.GetValue(mainForm) is not Aegis.Cafeteria.Application.Security.IOperatorAuthenticator authenticator ||
            registryField?.GetValue(mainForm) is not Aegis.Cafeteria.Application.Security.IManagerAuthorizationRegistry registry)
            return;

        using ManagerAuthorizationDialog authorization = new(authenticator, registry, "FFP data tools");
        if (authorization.ShowDialog(mainForm) != DialogResult.OK)
            return;

        IProductCatalogMaintenance maintenance = services.GetRequiredService<IProductCatalogMaintenance>();
        using FfpDataToolsDialog dialog = new(maintenance);
        dialog.ShowDialog(mainForm);
        await RefreshMainProductGridAsync(mainForm);
    }

    private static async Task RefreshMainProductGridAsync(MainForm mainForm)
    {
        MethodInfo? loadProducts = typeof(MainForm).GetMethod
        (
              "LoadProductsAsync"
            , BindingFlags.Instance | BindingFlags.NonPublic
            , binder: null
            , types: [typeof(string)]
            , modifiers: null
        );

        if (loadProducts?.Invoke(mainForm, [null]) is Task task)
        {
            await task;
        }
    }

    private static void HideDataTools(CafeteriaAdministrationDialog dialog)
    {
        Label? title = Descendants(dialog).OfType<Label>()
            .FirstOrDefault(label => string.Equals(label.Text, FfpDataToolsTitle, StringComparison.Ordinal));
        if (title is null)
            return;

        Panel? toolsCard = Ancestors(title).OfType<Panel>()
            .FirstOrDefault(panel => panel.BorderStyle == BorderStyle.FixedSingle);
        if (toolsCard?.Parent is not TableLayoutPanel content || content.ColumnCount < 2)
            return;

        toolsCard.Visible = false;
        content.ColumnStyles[0].SizeType = SizeType.Percent;
        content.ColumnStyles[0].Width = 100F;
        content.ColumnStyles[1].SizeType = SizeType.Percent;
        content.ColumnStyles[1].Width = 0F;
        content.PerformLayout();
    }

    private static IEnumerable<Control> Ancestors(Control control)
    {
        Control? current = control.Parent;
        while (current is not null)
        {
            yield return current;
            current = current.Parent;
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child))
                yield return descendant;
        }
    }
}

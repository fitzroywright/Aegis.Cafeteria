namespace Aegis.Cafeteria.UI;

using Aegis.Cafeteria.Infrastructure.Configuration;

internal static class FfpDataToolsUiPolicy
{
    private const string FfpDataToolsTitle = "FFP Data Tools";

    public static void Apply(CafeteriaUiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.ShowFfpDataTools)
        {
            return;
        }

        System.Windows.Forms.Application.Idle += (_, _) =>
        {
            foreach (CafeteriaAdministrationDialog dialog in System.Windows.Forms.Application.OpenForms
                         .OfType<CafeteriaAdministrationDialog>())
            {
                HideDataTools(dialog);
            }
        };
    }

    private static void HideDataTools(CafeteriaAdministrationDialog dialog)
    {
        Label? title = Descendants(dialog)
            .OfType<Label>()
            .FirstOrDefault(label => string.Equals(label.Text, FfpDataToolsTitle, StringComparison.Ordinal));

        if (title is null)
        {
            return;
        }

        Panel? toolsCard = Ancestors(title)
            .OfType<Panel>()
            .FirstOrDefault(panel => panel.BorderStyle == BorderStyle.FixedSingle);
        if (toolsCard?.Parent is not TableLayoutPanel content || content.ColumnCount < 2)
        {
            return;
        }

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
            {
                yield return descendant;
            }
        }
    }
}

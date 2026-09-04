using System.Reflection;
using System.Runtime.CompilerServices;

namespace Aegis.Cafeteria.UI;

internal static class FfpApplicationBranding
{
    private const string IconResourceName = "Aegis.Cafeteria.UI.Assets.FFP.ico";
    private static readonly Icon? ApplicationIcon = LoadApplicationIcon();

    [ModuleInitializer]
    internal static void Initialize()
    {
        System.Windows.Forms.Application.Idle += ApplyToOpenForms;
    }

    private static void ApplyToOpenForms(object? sender, EventArgs eventArgs)
    {
        if (ApplicationIcon is null)
        {
            return;
        }

        foreach (Form form in System.Windows.Forms.Application.OpenForms)
        {
            if (form.IsDisposed || ReferenceEquals(form.Icon, ApplicationIcon))
            {
                continue;
            }

            form.Icon = ApplicationIcon;
        }
    }

    private static Icon? LoadApplicationIcon()
    {
        Assembly assembly = typeof(FfpApplicationBranding).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(IconResourceName);
        if (stream is null)
        {
            return null;
        }

        using Icon source = new(stream);
        return (Icon)source.Clone();
    }
}

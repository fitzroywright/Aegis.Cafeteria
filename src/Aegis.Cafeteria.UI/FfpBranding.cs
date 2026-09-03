namespace Aegis.Cafeteria.UI;

internal static class FfpBranding
{
    private const string LogoControlName = "FfpBrandLogo";

    public static void Apply(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);

        if (form.IsDisposed || form.Controls.Find(LogoControlName, true).Length > 0)
        {
            return;
        }

        Icon? applicationIcon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);
        if (applicationIcon is null)
        {
            return;
        }

        int size = form.ClientSize.Width >= 900 ? 56 : 44;
        PictureBox logo = new()
        {
            Name = LogoControlName,
            Image = applicationIcon.ToBitmap(),
            Size = new Size(size, size),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            TabStop = false,
            Enabled = false
        };

        logo.Location = new Point
        (
              Math.Max(8, form.ClientSize.Width - logo.Width - 14)
            , 10
        );

        form.Controls.Add(logo);
        logo.BringToFront();
        form.FormClosed += (_, _) => logo.Image?.Dispose();
    }
}

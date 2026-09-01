namespace Aegis.Cafeteria.UI;

/// <summary>
/// Lightweight Monday-to-Friday bar chart for the POS idle dashboard.
/// All supplied categories are rendered, including zero-value days.
/// </summary>
internal sealed class WeekdayBarChartControl : Control
{
    private static readonly Color[] Palette =
    [
        Color.FromArgb(31, 111, 190),
        Color.FromArgb(23, 119, 76),
        Color.FromArgb(235, 139, 40),
        Color.FromArgb(120, 86, 180),
        Color.FromArgb(45, 156, 166)
    ];

    private IReadOnlyList<BarValue> values = [];
    private string title = string.Empty;
    private string valueFormat = "C0";

    public WeekdayBarChartControl()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        MinimumSize = new Size(300, 210);
    }

    public void SetData(string title, IEnumerable<(string Label, decimal Value)> values, string valueFormat)
    {
        this.title = title;
        this.valueFormat = valueFormat;
        this.values = values
            .Select((x, index) => new BarValue(x.Label, Math.Max(0m, x.Value), Palette[index % Palette.Length]))
            .ToArray();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using Font titleFont = new("Segoe UI", 10, FontStyle.Bold);
        using Font axisFont = new("Segoe UI", 8.5f);
        using Font valueFont = new("Segoe UI", 8.5f, FontStyle.Bold);
        using SolidBrush titleBrush = new(Color.FromArgb(24, 45, 68));
        using SolidBrush mutedBrush = new(Color.FromArgb(104, 117, 132));
        using Pen axisPen = new(Color.FromArgb(218, 224, 230), 1f);

        e.Graphics.DrawString(this.title, titleFont, titleBrush, new PointF(8, 6));

        if (this.values.Count == 0)
        {
            e.Graphics.DrawString("No weekday data available", axisFont, mutedBrush, new PointF(8, 38));
            return;
        }

        Rectangle plot = new(38, 42, Math.Max(120, Width - 50), Math.Max(90, Height - 78));
        e.Graphics.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);

        decimal maximum = this.values.Max(x => x.Value);
        if (maximum <= 0m)
            maximum = 1m;

        int slotWidth = Math.Max(30, plot.Width / this.values.Count);
        int barWidth = Math.Max(18, Math.Min(54, slotWidth - 18));

        for (int index = 0; index < this.values.Count; index++)
        {
            BarValue value = this.values[index];
            int centerX = plot.Left + (slotWidth * index) + (slotWidth / 2);
            int barHeight = value.Value <= 0m
                ? 2
                : Math.Max(4, (int)Math.Round((double)(value.Value / maximum) * (plot.Height - 28)));
            Rectangle bar = new(centerX - (barWidth / 2), plot.Bottom - barHeight, barWidth, barHeight);

            using SolidBrush barBrush = new(value.Color);
            e.Graphics.FillRectangle(barBrush, bar);

            string amount = value.Value.ToString(this.valueFormat);
            SizeF amountSize = e.Graphics.MeasureString(amount, valueFont);
            float amountX = centerX - (amountSize.Width / 2f);
            float amountY = Math.Max(28f, bar.Top - amountSize.Height - 3f);
            e.Graphics.DrawString(amount, valueFont, titleBrush, amountX, amountY);

            SizeF labelSize = e.Graphics.MeasureString(value.Label, axisFont);
            e.Graphics.DrawString(value.Label, axisFont, mutedBrush, centerX - (labelSize.Width / 2f), plot.Bottom + 5f);
        }
    }

    private sealed record BarValue(string Label, decimal Value, Color Color);
}

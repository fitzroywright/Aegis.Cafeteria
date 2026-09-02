namespace Aegis.Cafeteria.UI;

/// <summary>
/// Lightweight Monday-to-Friday bar chart for the POS idle dashboard.
/// Supports either one value per day or paired morning/afternoon values.
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
    private IReadOnlyList<DaypartBarValue> daypartValues = [];
    private string title = string.Empty;
    private string valueFormat = "C0";
    private bool daypartModeEnabled;

    public WeekdayBarChartControl()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        MinimumSize = new Size(300, 210);
    }

    public void SetData(string title, IEnumerable<(string Label, decimal Value)> values, string valueFormat)
    {
        if (this.daypartModeEnabled)
        {
            return;
        }

        this.title = title;
        this.valueFormat = valueFormat;
        this.values = values
            .Select((item, index) => new BarValue(item.Label, Math.Max(0m, item.Value), Palette[index % Palette.Length]))
            .ToArray();
        Invalidate();
    }

    public void SetDaypartData(
        string title,
        IEnumerable<(string Label, decimal MorningValue, decimal AfternoonValue)> values,
        string valueFormat)
    {
        this.daypartModeEnabled = true;
        this.title = title;
        this.valueFormat = valueFormat;
        this.daypartValues = values
            .Select((item, index) =>
            {
                Color afternoonColor = Palette[index % Palette.Length];
                return new DaypartBarValue
                (
                      item.Label
                    , Math.Max(0m, item.MorningValue)
                    , Math.Max(0m, item.AfternoonValue)
                    , Lighten(afternoonColor, 0.48f)
                    , afternoonColor
                );
            })
            .ToArray();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        if (this.daypartModeEnabled)
        {
            DrawDaypartChart(e.Graphics);
            return;
        }

        DrawSingleValueChart(e.Graphics);
    }

    private void DrawDaypartChart(Graphics graphics)
    {
        using Font titleFont = new("Segoe UI", 10, FontStyle.Bold);
        using Font subtitleFont = new("Segoe UI", 8.2f);
        using Font axisFont = new("Segoe UI", 8.5f);
        using Font valueFont = new("Segoe UI", 7.8f, FontStyle.Bold);
        using SolidBrush titleBrush = new(Color.FromArgb(24, 45, 68));
        using SolidBrush mutedBrush = new(Color.FromArgb(104, 117, 132));
        using Pen axisPen = new(Color.FromArgb(218, 224, 230), 1f);

        graphics.DrawString(this.title, titleFont, titleBrush, new PointF(8, 6));
        graphics.DrawString("Light: before 11:00 AM  •  Dark: 11:00 AM onward", subtitleFont, mutedBrush, new PointF(8, 24));

        if (this.daypartValues.Count == 0)
        {
            graphics.DrawString("No weekday data available", axisFont, mutedBrush, new PointF(8, 48));
            return;
        }

        Rectangle plot = new(38, 54, Math.Max(120, Width - 50), Math.Max(90, Height - 92));
        graphics.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);

        decimal maximum = this.daypartValues
            .SelectMany(value => new[] { value.MorningValue, value.AfternoonValue })
            .DefaultIfEmpty(0m)
            .Max();
        if (maximum <= 0m)
        {
            maximum = 1m;
        }

        int groupWidth = Math.Max(50, plot.Width / this.daypartValues.Count);
        int barWidth = Math.Clamp((int)Math.Round(groupWidth * 0.28), 14, 34);
        int pairWidth = barWidth * 2;

        for (int index = 0; index < this.daypartValues.Count; index++)
        {
            DaypartBarValue value = this.daypartValues[index];
            int groupCenter = plot.Left + (groupWidth * index) + (groupWidth / 2);
            int pairLeft = groupCenter - (pairWidth / 2);

            DrawDaypartBar
            (
                  graphics
                , plot
                , pairLeft
                , barWidth
                , value.MorningValue
                , maximum
                , value.MorningColor
                , valueFont
                , titleBrush
                , alignValueToSeam: true
            );
            DrawDaypartBar
            (
                  graphics
                , plot
                , pairLeft + barWidth
                , barWidth
                , value.AfternoonValue
                , maximum
                , value.AfternoonColor
                , valueFont
                , titleBrush
                , alignValueToSeam: false
            );

            SizeF labelSize = graphics.MeasureString(value.Label, axisFont);
            graphics.DrawString
            (
                  value.Label
                , axisFont
                , mutedBrush
                , groupCenter - (labelSize.Width / 2f)
                , plot.Bottom + 5f
            );
        }
    }

    private void DrawDaypartBar(
        Graphics graphics,
        Rectangle plot,
        int left,
        int width,
        decimal value,
        decimal maximum,
        Color color,
        Font valueFont,
        Brush valueBrush,
        bool alignValueToSeam)
    {
        int barHeight = value <= 0m
            ? 2
            : Math.Max(4, (int)Math.Round((double)(value / maximum) * (plot.Height - 30)));
        Rectangle bar = new(left, plot.Bottom - barHeight, width, barHeight);

        using SolidBrush barBrush = new(color);
        graphics.FillRectangle(barBrush, bar);

        string amount = value.ToString(this.valueFormat);
        SizeF amountSize = graphics.MeasureString(amount, valueFont);
        float amountY = Math.Max(39f, bar.Top - amountSize.Height - 2f);
        float amountX = alignValueToSeam
            ? bar.Right - amountSize.Width - 2f
            : bar.Left + 2f;
        graphics.DrawString(amount, valueFont, valueBrush, amountX, amountY);
    }

    private void DrawSingleValueChart(Graphics graphics)
    {
        using Font titleFont = new("Segoe UI", 10, FontStyle.Bold);
        using Font axisFont = new("Segoe UI", 8.5f);
        using Font valueFont = new("Segoe UI", 8.5f, FontStyle.Bold);
        using SolidBrush titleBrush = new(Color.FromArgb(24, 45, 68));
        using SolidBrush mutedBrush = new(Color.FromArgb(104, 117, 132));
        using Pen axisPen = new(Color.FromArgb(218, 224, 230), 1f);

        graphics.DrawString(this.title, titleFont, titleBrush, new PointF(8, 6));

        if (this.values.Count == 0)
        {
            graphics.DrawString("No weekday data available", axisFont, mutedBrush, new PointF(8, 38));
            return;
        }

        Rectangle plot = new(38, 42, Math.Max(120, Width - 50), Math.Max(90, Height - 78));
        graphics.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);

        decimal maximum = this.values.Max(value => value.Value);
        if (maximum <= 0m)
        {
            maximum = 1m;
        }

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
            graphics.FillRectangle(barBrush, bar);

            string amount = value.Value.ToString(this.valueFormat);
            SizeF amountSize = graphics.MeasureString(amount, valueFont);
            float amountX = centerX - (amountSize.Width / 2f);
            float amountY = Math.Max(28f, bar.Top - amountSize.Height - 3f);
            graphics.DrawString(amount, valueFont, titleBrush, amountX, amountY);

            SizeF labelSize = graphics.MeasureString(value.Label, axisFont);
            graphics.DrawString(value.Label, axisFont, mutedBrush, centerX - (labelSize.Width / 2f), plot.Bottom + 5f);
        }
    }

    private static Color Lighten(Color color, float amount)
    {
        int red = color.R + (int)Math.Round((255 - color.R) * amount);
        int green = color.G + (int)Math.Round((255 - color.G) * amount);
        int blue = color.B + (int)Math.Round((255 - color.B) * amount);
        return Color.FromArgb(Math.Clamp(red, 0, 255), Math.Clamp(green, 0, 255), Math.Clamp(blue, 0, 255));
    }

    private sealed record BarValue(string Label, decimal Value, Color Color);

    private sealed record DaypartBarValue(
        string Label,
        decimal MorningValue,
        decimal AfternoonValue,
        Color MorningColor,
        Color AfternoonColor);
}

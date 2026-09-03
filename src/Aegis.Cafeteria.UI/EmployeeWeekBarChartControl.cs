namespace Aegis.Cafeteria.UI;

/// <summary>
/// Compact horizontal weekday participation chart used by the POS home dashboard.
/// Each bar compares unique eligible employees served with the total eligible population.
/// </summary>
internal sealed class EmployeeWeekBarChartControl : Control
{
    private static readonly Color[] Palette =
    [
        Color.FromArgb(31, 111, 190),
        Color.FromArgb(61, 139, 104),
        Color.FromArgb(235, 139, 40),
        Color.FromArgb(111, 89, 169),
        Color.FromArgb(53, 142, 151)
    ];

    private IReadOnlyList<BarItem> items = [];
    private string title = "EMPLOYEES SERVED VS ELIGIBLE — THIS WEEK";
    private int eligibleEmployeeCount;

    public EmployeeWeekBarChartControl()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        MinimumSize = new Size(260, 300);
    }

    public void SetData
    (
        string title,
        int eligibleEmployeeCount,
        IEnumerable<(string Label, int Served)> values
    )
    {
        this.title = title;
        this.eligibleEmployeeCount = Math.Max(0, eligibleEmployeeCount);
        this.items = values
            .Select((item, index) => new BarItem(item.Label, Math.Max(0, item.Served), Palette[index % Palette.Length]))
            .ToArray();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using Font titleFont = new("Segoe UI", 9.5F, FontStyle.Bold);
        using Font subtitleFont = new("Segoe UI", 8.5F);
        using Font labelFont = new("Segoe UI", 8.5F, FontStyle.Bold);
        using Font valueFont = new("Segoe UI", 8.3F);
        using SolidBrush titleBrush = new(Color.FromArgb(24, 45, 68));
        using SolidBrush mutedBrush = new(Color.FromArgb(104, 117, 132));
        using SolidBrush trackBrush = new(Color.FromArgb(235, 239, 243));
        using StringFormat weekdayFormat = new()
        {
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.None,
            LineAlignment = StringAlignment.Center,
            Alignment = StringAlignment.Near
        };

        e.Graphics.DrawString(this.title, titleFont, titleBrush, new RectangleF(8, 6, Math.Max(1, Width - 16), 24));
        e.Graphics.DrawString
        (
            $"Eligible employees: {this.eligibleEmployeeCount:N0}",
            subtitleFont,
            mutedBrush,
            new RectangleF(8, 29, Math.Max(1, Width - 16), 22)
        );

        if (this.items.Count == 0)
        {
            e.Graphics.DrawString("No employee activity this week", valueFont, mutedBrush, new PointF(8, 58));
            return;
        }

        int top = 58;
        int bottomPadding = 10;
        float widestLabel = this.items
            .Select(item => e.Graphics.MeasureString(item.Label, labelFont).Width)
            .DefaultIfEmpty(72F)
            .Max();
        int labelWidth = Math.Max(82, (int)Math.Ceiling(widestLabel) + 10);
        int valueWidth = Math.Min(120, Math.Max(92, Width / 3));
        int barLeft = labelWidth + 10;
        int barRight = Math.Max(barLeft + 10, Width - valueWidth - 10);
        int availableBarWidth = Math.Max(10, barRight - barLeft);
        int availableHeight = Math.Max(80, Height - top - bottomPadding);
        int rowHeight = Math.Max(28, availableHeight / Math.Max(1, this.items.Count));
        int barHeight = Math.Max(10, Math.Min(22, rowHeight - 12));

        for (int index = 0; index < this.items.Count; index++)
        {
            BarItem item = this.items[index];
            int rowTop = top + (index * rowHeight);
            int barTop = rowTop + Math.Max(3, (rowHeight - barHeight) / 2);
            decimal participation = this.eligibleEmployeeCount == 0
                ? 0m
                : Math.Min(1m, (decimal)item.Served / this.eligibleEmployeeCount);
            int barWidth = item.Served == 0
                ? 0
                : Math.Max(4, (int)Math.Round(availableBarWidth * participation));

            RectangleF labelRect = new(6, rowTop, labelWidth, rowHeight);
            e.Graphics.DrawString(item.Label, labelFont, titleBrush, labelRect, weekdayFormat);

            e.Graphics.FillRectangle(trackBrush, barLeft, barTop, availableBarWidth, barHeight);
            if (barWidth > 0)
            {
                using SolidBrush barBrush = new(item.Color);
                e.Graphics.FillRectangle(barBrush, barLeft, barTop, barWidth, barHeight);
            }

            decimal percentage = participation * 100m;
            string value = $"{item.Served:N0} / {this.eligibleEmployeeCount:N0} ({percentage:0.#}%)";
            RectangleF valueRect = new(barRight + 6, rowTop, valueWidth, rowHeight);
            e.Graphics.DrawString(value, valueFont, titleBrush, valueRect);
        }
    }

    private sealed record BarItem(string Label, int Served, Color Color);
}

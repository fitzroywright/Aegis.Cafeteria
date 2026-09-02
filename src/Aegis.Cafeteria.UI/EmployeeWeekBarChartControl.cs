namespace Aegis.Cafeteria.UI;

/// <summary>
/// Compact horizontal weekday bar chart used by the POS home dashboard.
/// Values represent unique employees served on each business day.
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
    private string title = "EMPLOYEES SERVED BY DAY — THIS WEEK";

    public EmployeeWeekBarChartControl()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        MinimumSize = new Size(220, 300);
    }

    public void SetData(string title, IEnumerable<(string Label, int Value)> values)
    {
        this.title = title;
        this.items = values
            .Select((item, index) => new BarItem(item.Label, Math.Max(0, item.Value), Palette[index % Palette.Length]))
            .ToArray();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using Font titleFont = new("Segoe UI", 9.5F, FontStyle.Bold);
        using Font labelFont = new("Segoe UI", 8.5F, FontStyle.Bold);
        using Font valueFont = new("Segoe UI", 8.5F);
        using SolidBrush titleBrush = new(Color.FromArgb(24, 45, 68));
        using SolidBrush mutedBrush = new(Color.FromArgb(104, 117, 132));
        using Pen guidePen = new(Color.FromArgb(225, 230, 235));

        e.Graphics.DrawString(this.title, titleFont, titleBrush, new RectangleF(8, 6, Math.Max(1, Width - 16), 36));

        if (this.items.Count == 0)
        {
            e.Graphics.DrawString("No employee activity this week", valueFont, mutedBrush, new PointF(8, 48));
            return;
        }

        int top = 48;
        int bottomPadding = 10;
        int labelWidth = Math.Min(78, Math.Max(58, Width / 4));
        int valueWidth = 38;
        int barLeft = labelWidth + 10;
        int barRight = Math.Max(barLeft + 10, Width - valueWidth - 12);
        int availableBarWidth = Math.Max(10, barRight - barLeft);
        int availableHeight = Math.Max(80, Height - top - bottomPadding);
        int rowHeight = Math.Max(28, availableHeight / Math.Max(1, this.items.Count));
        int barHeight = Math.Max(10, Math.Min(22, rowHeight - 12));
        int maxValue = Math.Max(1, this.items.Max(item => item.Value));

        for (int index = 0; index < this.items.Count; index++)
        {
            BarItem item = this.items[index];
            int rowTop = top + (index * rowHeight);
            int barTop = rowTop + Math.Max(3, (rowHeight - barHeight) / 2);
            float ratio = (float)item.Value / maxValue;
            int barWidth = item.Value == 0 ? 2 : Math.Max(4, (int)Math.Round(availableBarWidth * ratio));

            RectangleF labelRect = new(6, rowTop, labelWidth, rowHeight);
            e.Graphics.DrawString(item.Label, labelFont, titleBrush, labelRect);

            int guideY = barTop + (barHeight / 2);
            e.Graphics.DrawLine(guidePen, barLeft, guideY, barRight, guideY);

            using SolidBrush barBrush = new(item.Color);
            e.Graphics.FillRectangle(barBrush, barLeft, barTop, barWidth, barHeight);

            RectangleF valueRect = new(barRight + 6, rowTop, valueWidth, rowHeight);
            e.Graphics.DrawString(item.Value.ToString("N0"), valueFont, titleBrush, valueRect);
        }
    }

    private sealed record BarItem(string Label, int Value, Color Color);
}

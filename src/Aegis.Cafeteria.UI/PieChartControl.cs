namespace Aegis.Cafeteria.UI;

/// <summary>
/// Lightweight WinForms pie chart used by the idle dashboard without adding a charting package dependency.
/// Values are expected to be non-negative. Small categories can be pre-grouped by the caller.
/// </summary>
internal sealed class PieChartControl : Control
{
    private static readonly Color[] Palette =
    [
        Color.FromArgb(31, 111, 190),
        Color.FromArgb(23, 119, 76),
        Color.FromArgb(235, 139, 40),
        Color.FromArgb(120, 86, 180),
        Color.FromArgb(45, 156, 166),
        Color.FromArgb(194, 75, 75),
        Color.FromArgb(130, 140, 150)
    ];

    private IReadOnlyList<PieSlice> slices = [];
    private string title = string.Empty;
    private string valueFormat = "N0";

    public PieChartControl()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        MinimumSize = new Size(260, 210);
    }

    public void SetData(string title, IEnumerable<(string Label, decimal Value)> values, string valueFormat)
    {
        this.title = title;
        this.valueFormat = valueFormat;
        this.slices = values
            .Where(x => x.Value > 0m)
            .Select((x, index) => new PieSlice(x.Label, x.Value, Palette[index % Palette.Length]))
            .ToArray();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var titleFont = new Font("Segoe UI", 10, FontStyle.Bold);
        using var legendFont = new Font("Segoe UI", 8.5f);
        using var emptyFont = new Font("Segoe UI", 9);
        using var titleBrush = new SolidBrush(Color.FromArgb(24, 45, 68));
        using var mutedBrush = new SolidBrush(Color.FromArgb(104, 117, 132));

        e.Graphics.DrawString(this.title, titleFont, titleBrush, new PointF(8, 6));

        if (this.slices.Count == 0)
        {
            e.Graphics.DrawString("No items sold today", emptyFont, mutedBrush, new PointF(8, 38));
            return;
        }

        var total = this.slices.Sum(x => x.Value);
        var pieSize = Math.Max(90, Math.Min(150, Math.Min(Height - 50, Width / 2 - 18)));
        var pieRect = new Rectangle(8, 36, pieSize, pieSize);
        var startAngle = -90f;
        for (var i = 0; i < this.slices.Count; i++)
        {
            var slice = this.slices[i];
            var sweep = i == this.slices.Count - 1
                ? 270f - startAngle
                : (float)(slice.Value / total * 360m);
            using var brush = new SolidBrush(slice.Color);
            e.Graphics.FillPie(brush, pieRect, startAngle, sweep);
            startAngle += sweep;
        }

        var legendX = pieRect.Right + 14;
        var legendY = 36f;
        var legendWidth = Math.Max(80, Width - legendX - 8);
        foreach (var slice in this.slices)
        {
            using var brush = new SolidBrush(slice.Color);
            e.Graphics.FillRectangle(brush, legendX, legendY + 3, 10, 10);
            var pct = total <= 0m ? 0m : slice.Value / total;
            var value = slice.Value.ToString(this.valueFormat);
            var text = $"{slice.Label}  {value}  ({pct:P0})";
            var textRect = new RectangleF(legendX + 15, legendY, legendWidth - 15, 32);
            e.Graphics.DrawString(text, legendFont, titleBrush, textRect);
            legendY += 25;
            if (legendY > Height - 20) break;
        }
    }

    private sealed record PieSlice(string Label, decimal Value, Color Color);
}

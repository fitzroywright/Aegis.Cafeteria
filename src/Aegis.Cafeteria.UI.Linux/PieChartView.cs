using Avalonia;
using Avalonia.Controls;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using Avalonia.Media;

namespace Aegis.Cafeteria.UI.Linux;

public sealed class PieChartView : Grid
{
    private readonly Canvas canvas = new() { Width = 190, Height = 190 };
    private readonly StackPanel legend = new() { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock title = new() { FontSize = 15, FontWeight = FontWeight.Bold };
    private readonly TextBlock empty = new() { Text = "No items sold today", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private string format = "N0";

    private static readonly Color[] Palette =
    [
        Color.Parse("#1F6FBE"), Color.Parse("#17774C"), Color.Parse("#D48A1F"),
        Color.Parse("#9A5BC5"), Color.Parse("#B03434"), Color.Parse("#5A7188")
    ];

    public PieChartView()
    {
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        Children.Add(title);
        var body = new Grid { ColumnSpacing = 14, Margin = new Thickness(0, 8, 0, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        body.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        canvas.Children.Add(empty);
        body.Children.Add(canvas);
        Grid.SetColumn(legend, 1); body.Children.Add(legend);
        Grid.SetRow(body, 1); Children.Add(body);
    }

    public void SetData(string caption, IReadOnlyList<(string Label, decimal Value)> values, string valueFormat)
    {
        title.Text = caption;
        format = valueFormat;
        canvas.Children.Clear();
        legend.Children.Clear();
        var data = values.Where(x => x.Value > 0m).ToArray();
        var total = data.Sum(x => x.Value);
        if (total <= 0m)
        {
            canvas.Children.Add(new TextBlock { Text = "No items sold today", Width = 190, Height = 190, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            return;
        }

        var angle = -90d;
        for (var i = 0; i < data.Length; i++)
        {
            var sweep = 360d * (double)(data[i].Value / total);
            var geometry = BuildWedge(new Point(95, 95), 88, angle, sweep);
            canvas.Children.Add(new ShapePath { Data = geometry, Fill = new SolidColorBrush(Palette[i % Palette.Length]), Stroke = Brushes.White, StrokeThickness = 1 });
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            item.Children.Add(new Border { Width = 11, Height = 11, Background = new SolidColorBrush(Palette[i % Palette.Length]), CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center });
            item.Children.Add(new TextBlock { Text = $"{data[i].Label}: {Format(data[i].Value)}", TextWrapping = TextWrapping.Wrap });
            legend.Children.Add(item);
            angle += sweep;
        }
    }

    private string Format(decimal value) => format.StartsWith("C", StringComparison.OrdinalIgnoreCase) ? value.ToString("C0") : value.ToString("N0");

    private static Geometry BuildWedge(Point center, double radius, double startDegrees, double sweepDegrees)
    {
        var points = new List<Point> { center };
        var steps = Math.Max(4, (int)Math.Ceiling(Math.Abs(sweepDegrees) / 8d));
        for (var i = 0; i <= steps; i++)
        {
            var angle = (startDegrees + sweepDegrees * i / steps) * Math.PI / 180d;
            points.Add(new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle)));
        }
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], true);
            foreach (var point in points.Skip(1)) context.LineTo(point);
            context.EndFigure(true);
        }
        return geometry;
    }
}

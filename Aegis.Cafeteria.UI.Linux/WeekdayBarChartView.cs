namespace Aegis.Cafeteria.UI.Linux;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

public sealed class WeekdayBarChartView : Grid
{
    private readonly TextBlock title = new()
    {
          FontSize = 15
        , FontWeight = FontWeight.Bold
        , Foreground = new SolidColorBrush(Color.Parse("#182D44"))
    };

    private readonly Grid bars = new()
    {
          ColumnSpacing = 10
        , VerticalAlignment = VerticalAlignment.Stretch
    };

    private string valueFormat = "C0";

    public WeekdayBarChartView()
    {
        MinHeight = 210;
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        Children.Add(this.title);

        Border host = new()
        {
              Background = Brushes.White
            , Padding = new Thickness(8, 10, 8, 4)
            , Child = this.bars
        };

        Grid.SetRow(host, 1);
        Children.Add(host);
    }

    public void SetData
    (
          string caption
        , IEnumerable<(string Label, decimal Value)> values
        , string valueFormat
    )
    {
        this.title.Text = caption;
        this.valueFormat = valueFormat;
        this.bars.Children.Clear();
        this.bars.ColumnDefinitions.Clear();

        (string Label, decimal Value)[] data = values
            .Select(value => (value.Label, Math.Max(0m, value.Value)))
            .ToArray();

        if (data.Length == 0)
        {
            this.bars.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            this.bars.Children.Add
            (
                new TextBlock
                {
                      Text = "No weekday data available"
                    , Foreground = new SolidColorBrush(Color.Parse("#687584"))
                    , HorizontalAlignment = HorizontalAlignment.Center
                    , VerticalAlignment = VerticalAlignment.Center
                }
            );
            return;
        }

        decimal maximum = data.Max(value => value.Value);
        if (maximum <= 0m)
        {
            maximum = 1m;
        }

        for (int index = 0; index < data.Length; index++)
        {
            this.bars.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            Grid column = new()
            {
                  RowSpacing = 5
                , HorizontalAlignment = HorizontalAlignment.Stretch
                , VerticalAlignment = VerticalAlignment.Stretch
            };
            column.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            column.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            column.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            TextBlock amount = new()
            {
                  Text = Format(data[index].Value)
                , FontSize = 11
                , FontWeight = FontWeight.Bold
                , HorizontalAlignment = HorizontalAlignment.Center
                , Foreground = new SolidColorBrush(Color.Parse("#182D44"))
            };
            column.Children.Add(amount);

            Grid plot = new()
            {
                  MinHeight = 125
                , VerticalAlignment = VerticalAlignment.Stretch
            };

            double ratio = Math.Clamp((double)(data[index].Value / maximum), 0d, 1d);
            Border bar = new()
            {
                  Width = 38
                , MinHeight = data[index].Value > 0m ? 4 : 2
                , Height = Math.Max(2, 118 * ratio)
                , Background = BarBrush(index)
                , HorizontalAlignment = HorizontalAlignment.Center
                , VerticalAlignment = VerticalAlignment.Bottom
                , CornerRadius = new CornerRadius(3, 3, 0, 0)
            };

            plot.Children.Add(bar);
            Grid.SetRow(plot, 1);
            column.Children.Add(plot);

            TextBlock label = new()
            {
                  Text = data[index].Label
                , FontSize = 11
                , HorizontalAlignment = HorizontalAlignment.Center
                , Foreground = new SolidColorBrush(Color.Parse("#687584"))
            };
            Grid.SetRow(label, 2);
            column.Children.Add(label);

            Grid.SetColumn(column, index);
            this.bars.Children.Add(column);
        }
    }

    private string Format(decimal value)
        => this.valueFormat.StartsWith("C", StringComparison.OrdinalIgnoreCase)
            ? value.ToString("C0")
            : value.ToString("N0");

    private static IBrush BarBrush(int index)
    {
        string[] palette =
        [
              "#1F6FBE"
            , "#17774C"
            , "#EB8B28"
            , "#7856B4"
            , "#2D9CA6"
        ];

        return new SolidColorBrush(Color.Parse(palette[index % palette.Length]));
    }
}

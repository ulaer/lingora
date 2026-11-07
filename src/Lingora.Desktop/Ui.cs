using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Lingora.Desktop;

/// <summary>Kodla oluşturulan yazı öğeleri için küçük yardımcılar (yalnızca metin).</summary>
internal static class Ui
{
    public static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);

    public static string Upper(string text) => text.ToUpper(Turkish);

    public static TextBlock Text(string text, string? styleKey = "Body", Brush? foreground = null, double? fontSize = null)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (styleKey is not null)
        {
            block.Style = (Style)Application.Current.FindResource(styleKey);
        }

        if (foreground is not null)
        {
            block.Foreground = foreground;
        }

        if (fontSize is { } size)
        {
            block.FontSize = size;
        }

        return block;
    }

    /// <summary>Solda soluk etiket, sağda seçilebilir değer.</summary>
    public static Grid Row(string label, string value, Brush? valueBrush = null, double valueSize = 15)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBlock = Text(label, "Muted");
        labelBlock.Margin = new Thickness(0, 2, 12, 0);
        grid.Children.Add(labelBlock);

        var valueBox = new TextBox
        {
            Text = value,
            Style = (Style)Application.Current.FindResource("ReadOnlyText"),
            FontSize = valueSize,
        };
        if (valueBrush is not null)
        {
            valueBox.Foreground = valueBrush;
        }

        Grid.SetColumn(valueBox, 1);
        grid.Children.Add(valueBox);
        return grid;
    }
}

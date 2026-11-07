using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Lingora.Desktop;

/// <summary>Hedef, Ayarlar ve Sözlük için küçük, ana pencereye bağlı ek sayfa penceresi.</summary>
internal sealed class PageWindow : Window
{
    private const double CaptionHeight = 44;

    public PageWindow(string title, UserControl page, double width, double height)
    {
        Title = title;
        Width = width;
        Height = height;
        MinWidth = 640;
        MinHeight = 460;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Background = Ui.Brush("BgGradient");
        Foreground = Ui.Brush("TextBrush");
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        FontSize = 14;

        Host = new Border { Child = page };

        // Özel başlık: soluk başlık yazısı + yalnızca kapat düğmesi; zeminle aynı, ayrı şerit yok.
        var caption = new DockPanel { Height = CaptionHeight, Margin = new Thickness(24, 0, 8, 0) };
        var close = WindowFrame.CaptionButton("", "Kapat", (_, _) => Close(), isClose: true);
        DockPanel.SetDock(close, Dock.Right);
        caption.Children.Add(close);
        caption.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            Foreground = Ui.Brush("TextMutedBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var root = new DockPanel();
        DockPanel.SetDock(caption, Dock.Top);
        root.Children.Add(caption);
        root.Children.Add(Host);
        Frame = WindowFrame.Wrap(root);
        Content = Frame;

        WindowFrame.Apply(this, CaptionHeight);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    /// <summary>Sayfayı saran öğe (testlerde ekran görüntüsü için).</summary>
    public Border Host { get; }

    /// <summary>Başlık dahil tüm pencere içeriği.</summary>
    public Border Frame { get; }

    /// <summary>Sayfayı pencereden ayırır; aynı sayfa bir sonraki açılışta yeniden kullanılır.</summary>
    public void Detach() => Host.Child = null;
}

/// <summary>
/// Windows'un gri başlık çubuğunu kaldırıp içeriği pencerenin en üstüne kadar uzatır.
/// Küçült/büyüt/kapat düğmeleri uygulamanın kendi üst çubuğunda çizilir.
/// </summary>
internal static class WindowFrame
{
    public static void Apply(Window window, double captionHeight)
    {
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = captionHeight,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });

        // Büyütülmüş pencere ekran kenarından taşar; taşan kısmı iç boşlukla telafi et.
        window.StateChanged += (_, _) => UpdateMaximizedPadding(window);
        window.SourceInitialized += (_, _) => UpdateMaximizedPadding(window);
    }

    /// <summary>İçeriği ince bir kenar çizgisiyle saran kök öğe.</summary>
    public static Border Wrap(UIElement content) => new()
    {
        BorderBrush = Ui.Brush("WindowEdgeBrush"),
        BorderThickness = new Thickness(1),
        Background = Ui.Brush("BgGradient"),
        Child = content,
    };

    public static Button CaptionButton(string glyph, string tooltip, RoutedEventHandler click, bool isClose = false)
    {
        var button = new Button
        {
            Content = glyph,
            ToolTip = tooltip,
            Style = (Style)Application.Current.FindResource(isClose ? "CloseCaptionButton" : "CaptionButton"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        WindowChrome.SetIsHitTestVisibleInChrome(button, true);
        button.Click += click;
        return button;
    }

    private static void UpdateMaximizedPadding(Window window)
    {
        if (window.Content is not Border frame)
        {
            return;
        }

        if (window.WindowState == WindowState.Maximized)
        {
            var inset = MaximizedInset(window);
            frame.BorderThickness = new Thickness(0);
            frame.Padding = new Thickness(inset);
        }
        else
        {
            frame.BorderThickness = new Thickness(1);
            frame.Padding = new Thickness(0);
        }
    }

    private static double MaximizedInset(Window window)
    {
        try
        {
            const int SmCxSizeFrame = 32;
            const int SmCxPaddedBorder = 92;
            var pixels = GetSystemMetrics(SmCxSizeFrame) + GetSystemMetrics(SmCxPaddedBorder);
            var dpi = VisualTreeHelper.GetDpi(window).DpiScaleX;
            return pixels > 0 ? pixels / dpi : 8;
        }
        catch (DllNotFoundException)
        {
            return 8;
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}

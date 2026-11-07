using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lingora.Core;
using Lingora.Core.Lexicon;

namespace Lingora.Desktop.Pages;

/// <summary>
/// Sözlük: Gemini'nin değerlendirmelerde önerdiği eksik kelime, kalıp ve tamlamalar.
/// Arama, sıralama ve silme; aynı terim tekrar gelirse tek kartta hata sayısı artar.
/// </summary>
public partial class VocabularyPage : UserControl
{
    private readonly PracticeSession _session;

    public VocabularyPage(PracticeSession session)
    {
        _session = session;
        InitializeComponent();
        Refresh();
    }

    /// <summary>Sözlükten kayıt silindi.</summary>
    public event EventHandler? Changed;

    /// <summary>Şu an listelenen kayıtlar (arama ve sıralamaya göre).</summary>
    internal IReadOnlyList<VocabularyEntry> Shown { get; private set; } = [];

    internal VocabularySort Sort =>
        SortMistakes.IsChecked == true ? VocabularySort.MostMistakes
        : SortAlpha.IsChecked == true ? VocabularySort.Alphabetical
        : VocabularySort.Recent;

    public void Refresh()
    {
        if (List is null)
        {
            return; // InitializeComponent sırasında
        }

        var book = _session.Vocabulary;
        CountText.Text = book.Count == 0 ? string.Empty : $"{book.Count} kayıt";
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        Shown = book.Query(SearchBox.Text, Sort);
        List.Children.Clear();
        foreach (var entry in Shown)
        {
            List.Children.Add(Card(entry));
        }

        EmptyText.Visibility = Shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = book.Count == 0
            ? "Sözlüğün henüz boş.\nÇeviri gönderdikçe eksik kelime, kalıp ve tamlamalar burada birikecek."
            : "Aramana uyan kayıt yok.";
    }

    /// <summary>Kaydı onay sormadan siler (buton onayı ayrıca ister).</summary>
    internal void Delete(VocabularyEntry entry)
    {
        if (_session.Vocabulary.Remove(entry))
        {
            Refresh();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private Border Card(VocabularyEntry entry)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var body = new StackPanel();

        // Terim + tür etiketi + tekrar sayısı
        var header = new WrapPanel();
        var term = new TextBlock
        {
            Text = entry.Term,
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        header.Children.Add(term);
        header.Children.Add(Pill(entry.Kind, Ui.Brush("AccentBrush")));
        if (entry.MistakeCount > 1)
        {
            header.Children.Add(Pill($"{entry.MistakeCount} kez", Ui.Brush("WarnBrush")));
        }

        body.Children.Add(header);

        var meaning = Ui.Text(entry.Meaning, "Body");
        meaning.FontSize = 15;
        meaning.Margin = new Thickness(0, 6, 0, 0);
        body.Children.Add(meaning);

        foreach (var example in entry.Examples)
        {
            var line = Ui.Text(example, "Muted");
            line.FontStyle = FontStyles.Italic;
            line.Margin = new Thickness(0, 6, 0, 0);
            body.Children.Add(line);
        }

        grid.Children.Add(body);

        var delete = new Button
        {
            Style = (Style)FindResource("IconButton"),
            ToolTip = "Sözlükten sil",
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(10, -4, -6, 0),
            Content = new TextBlock
            {
                Text = "",
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 14,
                Foreground = Ui.Brush("TextMutedBrush"),
            },
            Tag = entry,
        };
        delete.Click += OnDeleteClick;
        Grid.SetColumn(delete, 1);
        grid.Children.Add(delete);

        return new Border
        {
            Background = Ui.Brush("GlassBrush"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 14, 14, 14),
            Margin = new Thickness(0, 0, 0, 10),
            Child = grid,
            Tag = entry,
        };
    }

    private static Border Pill(string text, Brush brush) => new()
    {
        CornerRadius = new CornerRadius(6),
        Background = Ui.Brush("GlassBrush"),
        Padding = new Thickness(8, 2, 8, 3),
        Margin = new Thickness(0, 2, 6, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 12, Foreground = brush },
    };

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: VocabularyEntry entry })
        {
            return;
        }

        var owner = Window.GetWindow(this);
        var answer = owner is null
            ? MessageBoxResult.Yes
            : MessageBox.Show(owner, $"\"{entry.Term}\" sözlükten silinsin mi?", "Sözlük", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
        {
            Delete(entry);
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void OnSortChecked(object sender, RoutedEventArgs e) => Refresh();
}

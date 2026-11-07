using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lingora.Core;
using Lingora.Core.Journal;
using Lingora.Core.Lexicon;
using Lingora.Core.Models;

namespace Lingora.Desktop.Pages;

/// <summary>Sonuçtaki bir hata/ipucu satırının "Ayrıntılı açıkla" bağlantısı ve açıklama paneli.</summary>
internal sealed class ExplainSlot(FeedbackKind kind, int index, Button button, Panel panel, TextBox text)
{
    /// <summary>Açıklamanın harf harf yazılması (ilk cevaptaki gibi).</summary>
    public Typewriter Typing { get; } = new(TimeSpan.FromMilliseconds(15), charsPerTick: 3);

    public const string ShowLabel = "Ayrıntılı açıkla  ›";
    public const string HideLabel = "Açıklamayı gizle  ˄";

    public FeedbackKind Kind { get; } = kind;

    public int Index { get; } = index;

    public Button Button { get; } = button;

    public Panel Panel { get; } = panel;

    public TextBox Text { get; } = text;

    public bool Loaded { get; set; }

    public bool Busy { get; set; }
}

/// <summary>
/// Çalışma ekranı: çevrilecek cümle, çerçevesiz çeviri alanı, Gönder butonu ve Gemini'nin
/// değerlendirmesi. Beklerken yalnızca dönen noktalar görünür; cevap hızlıca yazılarak gelir.
/// </summary>
public partial class PracticePage : UserControl
{
    private static readonly string[] ThinkingFrames = [".", "..", "..."];

    private readonly PracticeSession _session;
    private readonly DispatcherTimer _thinkingTimer;
    private readonly Typewriter _typing = new(TimeSpan.FromMilliseconds(15), charsPerTick: 3);
    private readonly List<ExplainSlot> _explainSlots = new();
    private PracticeItem? _shownItem;
    private JournalEntry? _entry;
    private int _thinkingFrame;

    public PracticePage(PracticeSession session)
    {
        _session = session;
        InitializeComponent();

        _thinkingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _thinkingTimer.Tick += (_, _) =>
        {
            _thinkingFrame = (_thinkingFrame + 1) % ThinkingFrames.Length;
            ThinkingText.Text = ThinkingFrames[_thinkingFrame];
        };

        Refresh();

        if (_session.Warnings.Count > 0)
        {
            ShowMessage("Uyarı", string.Join("\n", _session.Warnings), isWarning: true);
        }
        else if (_session.State == SessionState.NoCorpus)
        {
            ShowMessage(
                "Başlarken",
                "1. Sağ üstteki dişli > API Anahtarı: Google AI Studio anahtarınızı girin.\n"
                + "2. Ayarlar > Veri Dosyası (TXT): cümle dosyanızı (ör. tur.txt) yükleyin.\n"
                + "3. Sağ üstten yönü seçin (TR → EN / EN → TR), çevirinizi yazıp Enter'a basın.",
                isWarning: false);
        }
    }

    public event EventHandler? OpenDataSettingsRequested;

    public event EventHandler? OpenApiSettingsRequested;

    /// <summary>Bir çeviri başarıyla değerlendirildi (günlük sayaç güncellenir).</summary>
    public event EventHandler? Evaluated;

    /// <summary>Günlüğe yeni bir açıklama eklendi.</summary>
    public event EventHandler? JournalChanged;

    public bool IsBusy { get; private set; }

    /// <summary>Cevabın yazılarak gelmesi. Testlerde kapatılır.</summary>
    internal bool AnimateResults { get; set; } = true;

    internal bool IsTyping => _typing.IsRunning;

    /// <summary>Oturum değiştiyse (mod, yeni dosya, zorluk) ekranı günceller; cümle değiştiyse alanları temizler.</summary>
    public void Refresh()
    {
        var settings = _session.Settings;
        SourceCaption.Text = Ui.Upper(settings.Direction.SourceLanguage())
            + (_session.Current is null ? string.Empty : $"   ·   #{_session.CurrentLine.ToString("N0", Ui.Turkish)}");
        Placeholder.Text = $"{settings.Direction.TargetLanguage()} çevirini yaz…";

        var current = _session.Current;
        if (current is null)
        {
            SourceText.Text = "Henüz cümle dosyası yüklenmedi.";
            SourceText.Foreground = Ui.Brush("TextMutedBrush");
            LoadDataButton.Visibility = Visibility.Visible;
        }
        else
        {
            SourceText.Text = current.Source;
            SourceText.Foreground = Brushes.White;
            LoadDataButton.Visibility = Visibility.Collapsed;
        }

        if (!ReferenceEquals(current, _shownItem))
        {
            _shownItem = current;
            InputBox.Clear();
            if (current is not null)
            {
                if (_session.Notice is { } notice)
                {
                    ShowMessage("Bilgi", notice, isWarning: false);
                }
                else
                {
                    HideResult();
                }
            }
        }

        SendButton.Content = _session.State == SessionState.ShowingResult ? "Sonraki cümle" : "Gönder";
        UpdatePlaceholder();
    }

    public void FocusInput()
    {
        InputBox.Focus();
        Keyboard.Focus(InputBox);
    }

    public void Skip()
    {
        if (IsBusy || _session.Current is null)
        {
            return;
        }

        _typing.Cancel();
        _session.NextSentence();
        Refresh();
        FocusInput();
    }

    /// <summary>Yazılmakta olan cevabı anında tamamlar.</summary>
    internal void CompleteTyping() => _typing.Complete();

    /// <summary>Cevap animasyonunu elle bir adım ilerletir (demo kaydı için). Bitmediyse true.</summary>
    internal bool StepTyping() => _typing.StepOnce();

    /// <summary>Gönder butonu ve Enter tuşu: duruma göre gönder, sonraki cümleye geç veya ayarlara yönlendir.</summary>
    public async Task SendAsync()
    {
        if (IsBusy)
        {
            return;
        }

        // Cevap hâlâ yazılıyorsa ilk Enter onu bitirir.
        if (_typing.IsRunning)
        {
            _typing.Complete();
            return;
        }

        switch (_session.State)
        {
            case SessionState.ShowingResult:
                Skip();
                return;
            case SessionState.NoCorpus:
                OpenDataSettingsRequested?.Invoke(this, EventArgs.Empty);
                return;
        }

        var item = _session.Current;
        var text = InputBox.Text;
        if (item is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            ShowMessage("Çeviri boş", "Lütfen önce çevirinizi yazın.", isWarning: true);
            FocusInput();
            return;
        }

        if (!_session.GetApiKey().HasKey)
        {
            ShowMessage("API anahtarı yok", "Gemini'ye gönderebilmek için Ayarlar > API Anahtarı sayfasından anahtarınızı girin.", isWarning: true);
            OpenApiSettingsRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        SetBusy(true);
        HideResult();
        StartThinking();

        SubmitOutcome outcome;
        try
        {
            outcome = await _session.SubmitAsync(text);
        }
        catch (Exception ex)
        {
            outcome = new SubmitOutcome(false, $"Beklenmeyen hata: {ex.Message}");
        }
        finally
        {
            StopThinking();
            SetBusy(false);
        }

        if (outcome.Success && outcome.Evaluation is not null)
        {
            _entry = outcome.Entry;
            ShowEvaluation(item, text.Trim(), outcome.Evaluation, outcome.Words);
            Evaluated?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            ShowMessage("Gönderilemedi", outcome.Text, isWarning: true);
        }

        Refresh();
        FocusInput();
    }

    private void ShowEvaluation(PracticeItem item, string userText, Evaluation evaluation, VocabularyMergeResult? words)
    {
        _typing.Cancel();
        ResultPanel.Visibility = Visibility.Visible;
        ResultBody.Children.Clear();
        _explainSlots.Clear();

        string headline;
        if (evaluation.RawText is not null)
        {
            headline = "Gemini yanıtı";
            ResultHeadline.Foreground = Ui.Brush("TextBrush");
        }
        else
        {
            var score = evaluation.Score is { } s ? $"{s} / 100" : "–";
            var verdict = evaluation.IsCorrect switch
            {
                true => "Doğru",
                false => "Düzeltilmeli",
                null => "Değerlendirildi",
            };
            headline = $"{score}   ·   {verdict}";
            ResultHeadline.Foreground = evaluation.IsCorrect == false ? Ui.Brush("WarnBrush") : Ui.Brush("AccentBrush");
        }

        _typing.Add(ResultHeadline, t => ResultHeadline.Text = t, headline);

        if (evaluation.RawText is not null)
        {
            AddField("Yanıt", evaluation.RawText);
        }
        else
        {
            if (evaluation.CorrectedTranslation.Length > 0
                && !string.Equals(evaluation.CorrectedTranslation, userText, StringComparison.Ordinal))
            {
                AddField("Düzeltilmiş", evaluation.CorrectedTranslation, Ui.Brush("AccentBrush"));
            }

            if (evaluation.NaturalTranslation.Length > 0)
            {
                AddField("Doğal çeviri", evaluation.NaturalTranslation);
            }

            if (evaluation.Mistakes.Count == 0)
            {
                AddField("Hatalar", "Hata bulunmadı.");
            }
            else
            {
                AddFeedbackField("Hatalar", evaluation.Mistakes, FeedbackKind.Mistake, bullet: true);
            }

            if (evaluation.MinorIssues.Count > 0)
            {
                AddField(
                    "Küçük hatalar (puanı etkilemez)",
                    string.Join("\n", evaluation.MinorIssues.Select(m => "• " + m)),
                    Ui.Brush("TextMutedBrush"));
            }

            if (evaluation.Tip.Length > 0)
            {
                AddFeedbackField("İpucu", [evaluation.Tip], FeedbackKind.Tip, bullet: false);
            }

            if (evaluation.Vocabulary.Count > 0)
            {
                AddField(
                    "Öğrenilecekler",
                    string.Join("\n", evaluation.Vocabulary.Select(v => $"{v.Term}  —  {v.Meaning}")),
                    Ui.Brush("AccentBrush"));
            }
        }

        if (item.References.Count > 0)
        {
            AddField("Referans", string.Join("   |   ", item.References), Ui.Brush("TextMutedBrush"));
        }

        if (words is { Total: > 0 })
        {
            AddNote(VocabularyNote(words));
        }

        if (AnimateResults)
        {
            _typing.Start();
        }
        else
        {
            _typing.Complete();
        }
    }

    /// <summary>Üstte küçük soluk etiket, altında seçilebilir değer; yazma animasyonuna eklenir.</summary>
    private void AddField(string label, string value, Brush? valueBrush = null)
    {
        var field = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        field.Children.Add(new TextBlock
        {
            Text = Ui.Upper(label),
            Style = (Style)FindResource("FieldLabel"),
        });

        var box = new TextBox
        {
            Style = (Style)FindResource("ReadOnlyText"),
            FontSize = 16,
            Foreground = valueBrush ?? Ui.Brush("TextBrush"),
        };
        field.Children.Add(box);
        ResultBody.Children.Add(field);
        _typing.Add(field, t => box.Text = t, value);
    }

    /// <summary>
    /// Hata/ipucu satırları: her satırın altında "Ayrıntılı açıkla" bağlantısı ve
    /// açılınca AI açıklamasının göründüğü panel (günlüğe kaydedilir, ikinci kez AI çağrılmaz).
    /// </summary>
    private void AddFeedbackField(string label, IReadOnlyList<string> items, FeedbackKind kind, bool bullet)
    {
        var field = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        field.Children.Add(new TextBlock { Text = Ui.Upper(label), Style = (Style)FindResource("FieldLabel") });
        ResultBody.Children.Add(field);

        for (var i = 0; i < items.Count; i++)
        {
            var row = new StackPanel { Margin = new Thickness(0, i == 0 ? 0 : 6, 0, 0) };
            var box = new TextBox
            {
                Style = (Style)FindResource("ReadOnlyText"),
                FontSize = 16,
                Foreground = Ui.Brush("TextBrush"),
            };
            row.Children.Add(box);

            if (_entry is not null)
            {
                var slot = CreateExplainSlot(kind, i);
                row.Children.Add(slot.Button);
                row.Children.Add(slot.Panel);
                _explainSlots.Add(slot);
            }

            field.Children.Add(row);
            var text = bullet ? "• " + items[i] : items[i];
            _typing.Add(i == 0 ? field : row, t => box.Text = t, text);
        }
    }

    private ExplainSlot CreateExplainSlot(FeedbackKind kind, int index)
    {
        var button = new Button
        {
            Style = (Style)FindResource("ExplainButton"),
            Content = ExplainSlot.ShowLabel,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        // İlk cevap gibi: zemin ve çerçeve yok, yalnızca yazı.
        var text = new TextBox
        {
            Style = (Style)FindResource("ReadOnlyText"),
            FontSize = 15.5,
            Foreground = Ui.Brush("TextBrush"),
        };
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 10), Visibility = Visibility.Collapsed };
        panel.Children.Add(text);
        var slot = new ExplainSlot(kind, index, button, panel, text);
        button.Click += async (_, _) => await ToggleExplanationAsync(slot);
        return slot;
    }

    /// <summary>Sonuçtaki açıklama yuvaları (testler için).</summary>
    internal IReadOnlyList<ExplainSlot> ExplainSlots => _explainSlots;

    /// <summary>Açıklamayı ister ya da açıkken gizler/gösterir.</summary>
    internal async Task ToggleExplanationAsync(ExplainSlot slot)
    {
        if (slot.Loaded)
        {
            slot.Typing.Complete();
            var show = slot.Panel.Visibility != Visibility.Visible;
            slot.Panel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            slot.Button.Content = show ? ExplainSlot.HideLabel : ExplainSlot.ShowLabel;
            return;
        }

        if (_entry is not { } entry || slot.Busy)
        {
            return;
        }

        slot.Busy = true;
        slot.Button.IsEnabled = false;
        slot.Button.Content = "Açıklanıyor…";
        slot.Panel.Visibility = Visibility.Visible;
        slot.Text.Foreground = Ui.Brush("TextMutedBrush");
        slot.Text.FontSize = 22;

        // Beklerken ana ekrandaki gibi yalnızca dönen noktalar.
        var frame = 0;
        slot.Text.Text = ThinkingFrames[0];
        var dots = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        dots.Tick += (_, _) => slot.Text.Text = ThinkingFrames[++frame % ThinkingFrames.Length];
        dots.Start();

        ExplainOutcome outcome;
        try
        {
            outcome = await _session.ExplainAsync(entry, slot.Kind, slot.Index);
        }
        catch (Exception ex)
        {
            outcome = new ExplainOutcome(false, $"Beklenmeyen hata: {ex.Message}");
        }
        finally
        {
            dots.Stop();
        }

        slot.Busy = false;
        slot.Button.IsEnabled = true;
        slot.Text.FontSize = 15.5;
        if (outcome.Success)
        {
            slot.Loaded = true;
            slot.Text.Foreground = Ui.Brush("TextBrush");
            slot.Button.Content = ExplainSlot.HideLabel;
            slot.Typing.Add(slot.Panel, t => slot.Text.Text = t, outcome.Text);
            if (AnimateResults)
            {
                slot.Typing.Start();
            }
            else
            {
                slot.Typing.Complete();
            }

            JournalChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            slot.Text.Foreground = Ui.Brush("WarnBrush");
            slot.Text.Text = outcome.Text;
            slot.Button.Content = "Tekrar dene";
        }
    }

    internal static string VocabularyNote(VocabularyMergeResult words)
    {
        var parts = new List<string>();
        if (words.Added.Count > 0)
        {
            parts.Add($"{words.Added.Count} yeni terim sözlüğe eklendi");
        }

        if (words.Repeated.Count > 0)
        {
            parts.Add($"{words.Repeated.Count} terim zaten sözlükteydi, tekrar sayısı arttı");
        }

        return string.Join("  ·  ", parts) + ".";
    }

    /// <summary>Sonucun altında soluk, küçük bir bilgi satırı (yazma animasyonuna dahil).</summary>
    private void AddNote(string text)
    {
        var note = new TextBlock
        {
            Style = (Style)FindResource("Muted"),
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0),
        };
        ResultBody.Children.Add(note);
        _typing.Add(note, t => note.Text = t, text);
    }

    private void ShowMessage(string title, string message, bool isWarning)
    {
        _typing.Cancel();
        ResultPanel.Visibility = Visibility.Visible;
        ResultHeadline.Text = title;
        ResultHeadline.Visibility = Visibility.Visible;
        ResultHeadline.Foreground = isWarning ? Ui.Brush("WarnBrush") : Ui.Brush("TextBrush");
        ResultBody.Children.Clear();
        var body = Ui.Text(message, "Body");
        body.FontSize = 15;
        body.Foreground = Ui.Brush("TextMutedBrush");
        ResultBody.Children.Add(body);
    }

    private void HideResult()
    {
        _typing.Cancel();
        ResultPanel.Visibility = Visibility.Collapsed;
        ResultBody.Children.Clear();
    }

    private void StartThinking()
    {
        _thinkingFrame = 0;
        ThinkingText.Text = ThinkingFrames[0];
        ThinkingText.Visibility = Visibility.Visible;
        _thinkingTimer.Start();
    }

    private void StopThinking()
    {
        _thinkingTimer.Stop();
        ThinkingText.Visibility = Visibility.Collapsed;
    }

    private void SetBusy(bool busy)
    {
        IsBusy = busy;
        SendButton.IsEnabled = !busy;
        InputBox.IsReadOnly = busy;
    }

    private void UpdatePlaceholder() =>
        Placeholder.Visibility = InputBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnInputTextChanged(object sender, TextChangedEventArgs e) => UpdatePlaceholder();

    private async void OnSendClick(object sender, RoutedEventArgs e) => await SendAsync();

    private void OnLoadDataClick(object sender, RoutedEventArgs e) =>
        OpenDataSettingsRequested?.Invoke(this, EventArgs.Empty);

    private async void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            await SendAsync();
        }
    }
}

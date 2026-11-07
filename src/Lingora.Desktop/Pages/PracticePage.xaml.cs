using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lingora.Core;
using Lingora.Core.Lexicon;
using Lingora.Core.Models;

namespace Lingora.Desktop.Pages;

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
    private PracticeItem? _shownItem;
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

            AddField("Hatalar", evaluation.Mistakes.Count == 0
                ? "Hata bulunmadı."
                : string.Join("\n", evaluation.Mistakes.Select(m => "• " + m)));

            if (evaluation.MinorIssues.Count > 0)
            {
                AddField(
                    "Küçük hatalar (puanı etkilemez)",
                    string.Join("\n", evaluation.MinorIssues.Select(m => "• " + m)),
                    Ui.Brush("TextMutedBrush"));
            }

            if (evaluation.Tip.Length > 0)
            {
                AddField("İpucu", evaluation.Tip);
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

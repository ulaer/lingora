using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lingora.Core;
using Lingora.Core.Game;
using Lingora.Core.Models;

namespace Lingora.Desktop.Pages;

/// <summary>
/// Oyun: game-assets sahneleri. "Resmi anlat" (Gemini değerlendirir; istem resmi kesin olgularla tarif eder)
/// ve "Doğru cümleyi seç" (4 seçenek, yerel). Sonuçlar günlük hedefe, günlüğe ve sözlüğe sayılır.
/// </summary>
public partial class GamePage : UserControl
{
    private static readonly string[] ThinkingFrames = [".", "..", "..."];

    private readonly PracticeSession _session;
    private readonly GameLibrary? _library;
    private readonly SvgScene? _renderer;
    private readonly Random _random;
    private readonly DispatcherTimer _thinking;
    private readonly Typewriter _typing = new(TimeSpan.FromMilliseconds(15), charsPerTick: 3);
    private int _thinkingFrame;
    private bool _showingResult;
    private int _played;
    private int _correct;

    public GamePage(PracticeSession session, GameLibrary? library, Random? random = null)
    {
        _session = session;
        _library = library;
        _random = random ?? new Random();
        _renderer = library is null ? null : new SvgScene(library);
        InitializeComponent();

        _thinking = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _thinking.Tick += (_, _) => ThinkingText.Text = ThinkingFrames[++_thinkingFrame % ThinkingFrames.Length];

        if (library is null)
        {
            MissingText.Text = "Oyun dosyaları (game-assets) bulunamadı. Uygulamayı yeniden yayınlayın ya da game-assets klasörünü uygulama klasörüne kopyalayın.";
            MissingText.Visibility = Visibility.Visible;
            ContentScroll.Visibility = Visibility.Collapsed;
            return;
        }

        UpdateScore();
        NextScene();
    }

    /// <summary>Bir sonuç alındı (üst çubuktaki bugün sayacı için).</summary>
    public event EventHandler? Evaluated;

    /// <summary>Cevabın yazılarak gelmesi. Testlerde kapatılır.</summary>
    internal bool AnimateResults { get; set; } = true;

    internal bool IsBusy { get; private set; }

    /// <summary>Ekrandaki sahne.</summary>
    internal GameScene? Scene { get; private set; }

    /// <summary>Seçmeli moddaki soru.</summary>
    internal ChoiceQuestion? Question { get; private set; }

    internal bool IsChooseMode => ModeChoose.IsChecked == true;

    private TranslationDirection Direction => _session.Settings.Direction;

    private string TargetUpper => Ui.Upper(Direction.TargetLanguage());

    /// <summary>Pencere her açılışta: yön değişmiş olabilir, yazıları güncelle.</summary>
    public void Refresh()
    {
        if (_library is null || Scene is null)
        {
            return;
        }

        if (IsChooseMode && Question is not null && Question.Options[Question.CorrectIndex] != Scene.Sentence(Direction))
        {
            NextScene();
            return;
        }

        UpdateTexts();
    }

    internal void NextScene()
    {
        if (_library is null || IsBusy)
        {
            return;
        }

        _typing.Cancel();
        _showingResult = false;
        Scene = PickVisibleScene();
        SceneImage.Source = _renderer!.Render(Scene);
        InputBox.Text = string.Empty;
        InputBox.IsReadOnly = false;
        SendButton.Content = "Gönder";
        ResultPanel.Visibility = Visibility.Collapsed;
        ResultBody.Children.Clear();

        Question = ChoiceBuilder.Build(_library, Scene, Direction, _random);
        BuildOptions();
        UpdateTexts();
        ContentScroll.ScrollToTop();
        FocusInput();
    }

    /// <summary>Belirli bir sahneyi gösterir (tanıtım görselleri ve testler için).</summary>
    internal void ShowSceneForMedia(GameScene scene)
    {
        NextScene();
        Scene = scene;
        SceneImage.Source = _renderer!.Render(scene);
        Question = ChoiceBuilder.Build(_library!, scene, Direction, _random);
        BuildOptions();
        UpdateTexts();
    }

    /// <summary>Bu oranın altında nesne arkasına karışıyor sayılır (ör. sarı tişört önünde sarı elma).</summary>
    internal const double MinObjectVisibility = 0.6;

    /// <summary>Nesnesi arkasındaki renge karışmayan bir sahne seçer.</summary>
    private GameScene PickVisibleScene()
    {
        GameScene scene = _library!.Pick(_random);
        for (var attempt = 0; attempt < 25 && _renderer!.ObjectVisibility(scene) < MinObjectVisibility; attempt++)
        {
            scene = _library.Pick(_random);
        }

        return scene;
    }

    internal void FocusInput()
    {
        if (!IsChooseMode)
        {
            InputBox.Focus();
            Keyboard.Focus(InputBox);
        }
    }

    private void UpdateTexts()
    {
        var subject = _library!.SubjectIn(Scene!, Direction);
        HintText.Text = $"İpucu · özne: {subject}";
        DescribeCaption.Text = $"RESMİ {TargetUpper} BİR CÜMLEYLE ANLAT";
        Placeholder.Text = Direction == TranslationDirection.TurkishToEnglish
            ? "Resimde ne oluyor? İngilizce yaz…"
            : "Resimde ne oluyor? Türkçe yaz…";
        ChooseCaption.Text = $"RESMİ ANLATAN DOĞRU {TargetUpper} CÜMLE HANGİSİ?";
        Placeholder.Visibility = InputBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateScore() =>
        ScoreText.Text = _played == 0
            ? "Resimler game-assets kütüphanesinden; sonuçlar bugünkü hedefe ve günlüğe sayılır."
            : $"Bu oturum: {_played} resim · {_correct} doğru";

    // ---------- Resmi anlat ----------

    /// <summary>Gönder / sonraki resim (Enter).</summary>
    internal async Task SendAsync()
    {
        if (_library is null || Scene is null || IsBusy)
        {
            return;
        }

        if (_typing.IsRunning)
        {
            _typing.Complete();
            return;
        }

        if (_showingResult)
        {
            NextScene();
            return;
        }

        var text = InputBox.Text.Trim();
        if (text.Length == 0)
        {
            ShowMessage("Cümle boş", "Önce resmi anlatan bir cümle yaz.");
            return;
        }

        IsBusy = true;
        SendButton.IsEnabled = false;
        InputBox.IsReadOnly = true;
        ResultPanel.Visibility = Visibility.Collapsed;
        _thinkingFrame = 0;
        ThinkingText.Text = ThinkingFrames[0];
        ThinkingText.Visibility = Visibility.Visible;
        _thinking.Start();

        SubmitOutcome outcome;
        try
        {
            outcome = await _session.SubmitSceneAsync(_library, Scene, text);
        }
        catch (Exception ex)
        {
            outcome = new SubmitOutcome(false, $"Beklenmeyen hata: {ex.Message}");
        }
        finally
        {
            _thinking.Stop();
            ThinkingText.Visibility = Visibility.Collapsed;
            SendButton.IsEnabled = true;
            IsBusy = false;
        }

        if (outcome.Success && outcome.Evaluation is { } evaluation)
        {
            _played++;
            if (evaluation.IsCorrect == true)
            {
                _correct++;
            }

            UpdateScore();
            ShowEvaluation(text, evaluation, outcome.Words);
            _showingResult = true;
            SendButton.Content = "Sonraki resim";
            Evaluated?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            InputBox.IsReadOnly = false;
            ShowMessage("Gönderilemedi", outcome.Text);
        }
    }

    private void ShowEvaluation(string userText, Evaluation evaluation, Lingora.Core.Lexicon.VocabularyMergeResult? words)
    {
        _typing.Cancel();
        ResultBody.Children.Clear();
        ResultPanel.Visibility = Visibility.Visible;

        if (evaluation.RawText is not null)
        {
            ResultHeadline.Foreground = Ui.Brush("TextBrush");
            _typing.Add(ResultHeadline, t => ResultHeadline.Text = t, "Gemini yanıtı");
            AddField("Yanıt", evaluation.RawText);
        }
        else
        {
            var score = evaluation.Score is { } s ? $"{s} / 100" : "–";
            var verdict = evaluation.IsCorrect switch { true => "Resme uygun", false => "Düzeltilmeli", null => "Değerlendirildi" };
            ResultHeadline.Foreground = evaluation.IsCorrect == false ? Ui.Brush("WarnBrush") : Ui.Brush("AccentBrush");
            _typing.Add(ResultHeadline, t => ResultHeadline.Text = t, $"{score}   ·   {verdict}");

            if (evaluation.CorrectedTranslation.Length > 0 && evaluation.CorrectedTranslation != userText)
            {
                AddField("Düzeltilmiş", evaluation.CorrectedTranslation, Ui.Brush("AccentBrush"));
            }

            if (evaluation.NaturalTranslation.Length > 0)
            {
                AddField("Doğal anlatım", evaluation.NaturalTranslation);
            }

            AddField("Hatalar", evaluation.Mistakes.Count == 0 ? "Hata bulunmadı." : string.Join("\n", evaluation.Mistakes.Select(m => "• " + m)));
            if (evaluation.MinorIssues.Count > 0)
            {
                AddField("Küçük hatalar (puanı etkilemez)", string.Join("\n", evaluation.MinorIssues.Select(m => "• " + m)), Ui.Brush("TextMutedBrush"));
            }

            if (evaluation.Tip.Length > 0)
            {
                AddField("İpucu", evaluation.Tip);
            }

            if (evaluation.Vocabulary.Count > 0)
            {
                AddField("Öğrenilecekler", string.Join("\n", evaluation.Vocabulary.Select(v => $"{v.Term}  —  {v.Meaning}")), Ui.Brush("AccentBrush"));
            }
        }

        AddField("Örnek cümle", Scene!.Sentence(Direction), Ui.Brush("TextMutedBrush"));
        if (words is { Total: > 0 })
        {
            AddField("Sözlük", PracticePage.VocabularyNote(words), Ui.Brush("TextMutedBrush"));
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

    private void AddField(string label, string value, Brush? brush = null)
    {
        var field = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        field.Children.Add(new TextBlock { Text = Ui.Upper(label), Style = (Style)FindResource("FieldLabel") });
        var box = new TextBox { Style = (Style)FindResource("ReadOnlyText"), FontSize = 16, Foreground = brush ?? Ui.Brush("TextBrush") };
        field.Children.Add(box);
        ResultBody.Children.Add(field);
        _typing.Add(field, t => box.Text = t, value);
    }

    private void ShowMessage(string title, string message)
    {
        _typing.Cancel();
        ResultPanel.Visibility = Visibility.Visible;
        ResultHeadline.Foreground = Ui.Brush("WarnBrush");
        ResultHeadline.Text = title;
        ResultBody.Children.Clear();
        var body = Ui.Text(message, "Body");
        body.FontSize = 15;
        body.Foreground = Ui.Brush("TextMutedBrush");
        ResultBody.Children.Add(body);
    }

    // ---------- Doğru cümleyi seç ----------

    /// <summary>Seçenek düğmeleri (testler için).</summary>
    internal IReadOnlyList<Button> OptionButtons => OptionsPanel.Children.OfType<Button>().ToList();

    private void BuildOptions()
    {
        OptionsPanel.Children.Clear();
        ChoiceFeedback.Text = string.Empty;
        NextChoiceButton.Visibility = Visibility.Collapsed;
        if (Question is null)
        {
            return;
        }

        for (var i = 0; i < Question.Options.Count; i++)
        {
            var index = i;
            var button = new Button
            {
                Style = (Style)FindResource("ChoiceButton"),
                Content = new TextBlock { Text = Question.Options[i], TextWrapping = TextWrapping.Wrap },
            };
            button.Click += (_, _) => Choose(index);
            OptionsPanel.Children.Add(button);
        }
    }

    /// <summary>Seçeneği işaretler, sonucu kaydeder (yanlışsa doğrusu gösterilir).</summary>
    internal void Choose(int index)
    {
        if (Question is null || NextChoiceButton.Visibility == Visibility.Visible)
        {
            return;
        }

        var correct = index == Question.CorrectIndex;
        _session.RecordChoice(Question, index);
        _played++;
        if (correct)
        {
            _correct++;
        }

        var buttons = OptionButtons;
        for (var i = 0; i < buttons.Count; i++)
        {
            buttons[i].IsEnabled = false;
            if (i == Question.CorrectIndex)
            {
                buttons[i].Background = Ui.Brush("SuccessSoftBrush");
                buttons[i].BorderBrush = Ui.Brush("SuccessBrush");
            }
            else if (i == index)
            {
                buttons[i].Background = Ui.Brush("ErrorSoftBrush");
                buttons[i].BorderBrush = Ui.Brush("ErrorBrush");
            }
            else
            {
                buttons[i].Opacity = 0.55;
            }
        }

        ChoiceFeedback.Foreground = correct ? Ui.Brush("SuccessBrush") : Ui.Brush("WarnBrush");
        ChoiceFeedback.Text = correct
            ? "Doğru! Resimdeki her ayrıntıyla uyuşuyor."
            : $"Doğrusu: {Question.Correct}";
        NextChoiceButton.Visibility = Visibility.Visible;
        NextChoiceButton.Focus();
        UpdateScore();
        Evaluated?.Invoke(this, EventArgs.Empty);
    }

    private void OnNextChoiceClick(object sender, RoutedEventArgs e) => NextScene();

    // ---------- Ortak ----------

    private void OnModeChecked(object sender, RoutedEventArgs e)
    {
        if (DescribePanel is null)
        {
            return; // InitializeComponent sırasında
        }

        var choose = IsChooseMode;
        DescribePanel.Visibility = choose ? Visibility.Collapsed : Visibility.Visible;
        ChoosePanel.Visibility = choose ? Visibility.Visible : Visibility.Collapsed;
        ResultPanel.Visibility = choose || !_showingResult ? Visibility.Collapsed : Visibility.Visible;
        if (_library is not null && Scene is not null && (_showingResult || NextChoiceButton.Visibility == Visibility.Visible))
        {
            NextScene();
        }

        FocusInput();
    }

    private async void OnSendClick(object sender, RoutedEventArgs e) => await SendAsync();

    private void OnNextSceneClick(object sender, RoutedEventArgs e) => NextScene();

    private void OnInputTextChanged(object sender, TextChangedEventArgs e) =>
        Placeholder.Visibility = InputBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private async void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            await SendAsync();
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Lingora.Core;
using Lingora.Core.Gemini;
using Lingora.Core.Models;
using Lingora.Core.Storage;
using Lingora.Desktop;
using Lingora.Desktop.Pages;

namespace Lingora.Tests;

/// <summary>
/// WPF arayüzünü tek bir STA iş parçacığında çalıştırır. Pencere ekranda gösterilmez;
/// içerik bellekte yerleştirilip çizilir. LINGORA_SCREENSHOTS ayarlıysa PNG kaydedilir.
/// </summary>
internal static class WpfHost
{
    private static readonly Lazy<Dispatcher> UiDispatcher = new(Start);

    public static void Run(Action action) => UiDispatcher.Value.Invoke(action);

    public static Task RunAsync(Func<Task> action) => UiDispatcher.Value.InvokeAsync(action).Task.Unwrap();

    public static void Layout(FrameworkElement root, double width = 1000, double height = 720)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));

        // Alt öğelerdeki değişiklikleri (metin, görünürlük) de işle; yoksa eski çizim yakalanır.
        root.UpdateLayout();
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
    }

    public static void Screenshot(FrameworkElement root, string name, double width = 1000, double height = 720)
    {
        Layout(root, width, height);
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);

        var directory = Environment.GetEnvironmentVariable("LINGORA_SCREENSHOTS");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }

    /// <summary>Ek pencerede açılan sayfayı, penceredeki gibi gradyan zeminle çizer.</summary>
    public static void ScreenshotPage(UserControl page, string name, double width, double height)
    {
        var host = new Border { Background = (Brush)Application.Current.FindResource("BgGradient"), Child = page };
        try
        {
            Screenshot(host, name, width, height);
        }
        finally
        {
            host.Child = null;
        }
    }

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            var app = new App();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WPF test UI",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }
}

[CollectionDefinition("WPF", DisableParallelization = true)]
public class WpfCollection
{
}

[Collection("WPF")]
public class DesktopUiTests
{
    private const string FakeKey = "AIzaTEST-desktop-ui-key-0000";

    private sealed record Setup(TempDir Dir, PracticeSession Session, FakeEvaluator Evaluator, GeminiClient Gemini) : IDisposable
    {
        public void Dispose() => Dir.Dispose();
    }

    private static Setup Create(bool withCorpus = true, bool withKey = true)
    {
        var dir = new TempDir();
        var evaluator = new FakeEvaluator();
        var session = new PracticeSession(
            new AppPaths(dir.Path),
            evaluator,
            new FixedTimeProvider(FixedTimeProvider.Local(2026, 11, 6, 20)),
            new ApiKeyStore(_ => null),
            new Random(3));
        if (withCorpus)
        {
            session.ImportCorpus(dir.Write("tur.txt", Samples.TatoebaLines));
        }

        if (withKey)
        {
            session.SetApiKey(FakeKey);
        }

        return new Setup(dir, session, evaluator, new GeminiClient(new HttpClient(new FakeHttpHandler())));
    }

    /// <summary>Ek pencereleri ekranda açmadan kaydeden ana pencere.</summary>
    private static (MainWindow Window, List<string> Opened) CreateWindow(Setup s)
    {
        var opened = new List<string>();
        var window = new MainWindow(s.Session, s.Gemini);
        window.Practice.AnimateResults = false;
        window.ShowDialogHandler = dialog =>
        {
            opened.Add(dialog.Title);
            Assert.IsType<Border>(dialog.Content);
            Assert.NotNull(dialog.Host.Child);
            return true;
        };
        return (window, opened);
    }

    [Fact]
    public void MainWindowShowsLogoShortModeIconButtonsAndPracticeOnly()
    {
        using var s = Create();
        WpfHost.Run(() =>
        {
            var (window, _) = CreateWindow(s);
            WpfHost.Layout(window.Root);

            // Üst çubuk: logo, kısa mod, ilerleme (hedef) ve ayarlar; sekme yok.
            Assert.Equal(Visibility.Visible, window.Logo.Visibility);
            Assert.Equal("TR → EN", window.ModeTrEn.Content);
            Assert.Equal("EN → TR", window.ModeEnTr.Content);
            Assert.True(window.ModeTrEn.IsChecked);
            Assert.Equal("0/20", window.TodayText.Text);
            Assert.Equal("Ayarlar", window.SettingsButton.ToolTip);
            Assert.Contains("İlerleme", (string)window.ProgressButton.ToolTip);
            Assert.Same(window.Practice, window.PageHost.Content);

            // Üst çubuk ayrı bir zemin/çizgi kullanmıyor; tüm pencere aynı gradyan.
            var topBar = (Grid)window.Root.Children[0];
            Assert.Null(topBar.Background);
            Assert.IsType<DrawingBrush>(window.Root.Background);

            // Windows başlık çubuğu yok; pencere düğmeleri üst çubukta, aynı zeminde.
            var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(window);
            Assert.NotNull(chrome);
            Assert.Equal(68, chrome.CaptionHeight);
            Assert.Equal("Küçült", window.MinimizeButton.ToolTip);
            Assert.Equal("Büyüt", window.MaximizeButton.ToolTip);
            Assert.Equal("Kapat", window.CloseButton.ToolTip);
            Assert.True(System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(window.SettingsButton));
            Assert.True(System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(window.ModeTrEn));

            // Çalışma ekranı: cümle, şeffaf alan, Gönder.
            var practice = window.Practice;
            Assert.Equal(s.Session.Current!.Source, practice.SourceText.Text);
            Assert.Equal("TÜRKÇE   ·   #1", practice.SourceCaption.Text);
            Assert.Equal("İngilizce çevirini yaz…", practice.Placeholder.Text);
            Assert.Equal(Visibility.Visible, practice.Placeholder.Visibility);
            Assert.Equal(Colors.Transparent, ((SolidColorBrush)practice.InputBox.Background).Color);
            Assert.Equal("Gönder", practice.SendButton.Content);
            Assert.Equal(Visibility.Collapsed, practice.ResultPanel.Visibility);

            practice.InputBox.Text = "Tom";
            Assert.Equal(Visibility.Collapsed, practice.Placeholder.Visibility);
            practice.InputBox.Text = string.Empty;

            WpfHost.Screenshot(window.Root, "1-calisma");
        });
    }

    [Fact]
    public async Task SendShowsEvaluationThenEnterGoesToNextSentence()
    {
        using var s = Create();
        s.Evaluator.Handler = (_, _) => new Evaluation
        {
            Score = 72,
            IsCorrect = false,
            CorrectedTranslation = "Tom said he didn't know what Mary was supposed to do.",
            NaturalTranslation = "Tom said he had no idea what Mary was supposed to do.",
            Mistakes = ["must → was supposed to: dolaylı anlatımda zaman geçmişe kayar"],
            Tip = "Dolaylı anlatımda (reported speech) fiil bir zaman geriye gider.",
        };

        await WpfHost.RunAsync(async () =>
        {
            var (window, _) = CreateWindow(s);
            var practice = window.Practice;
            var first = s.Session.Current!;

            practice.InputBox.Text = "Tom said he didn't know what Mary must do.";
            await practice.SendAsync();

            Assert.Single(s.Evaluator.Calls);
            Assert.Equal(Visibility.Visible, practice.ResultPanel.Visibility);
            Assert.Equal("72 / 100   ·   Düzeltilmeli", practice.ResultHeadline.Text);
            Assert.Equal("Sonraki cümle", practice.SendButton.Content);
            Assert.Equal("1/20", window.TodayText.Text);
            Assert.True(practice.ResultBody.Children.Count >= 5);
            WpfHost.Screenshot(window.Root, "2-sonuc", height: 860);

            await practice.SendAsync();

            Assert.NotSame(first, s.Session.Current);
            Assert.Equal(string.Empty, practice.InputBox.Text);
            Assert.Equal(Visibility.Collapsed, practice.ResultPanel.Visibility);
            Assert.Equal("Gönder", practice.SendButton.Content);
            Assert.Single(s.Evaluator.Calls);
        });
    }

    [Fact]
    public async Task EmptyTranslationShowsWarningWithoutCallingGemini()
    {
        using var s = Create();
        await WpfHost.RunAsync(async () =>
        {
            var (window, _) = CreateWindow(s);
            window.Practice.InputBox.Text = "   ";

            await window.Practice.SendAsync();

            Assert.Equal("Çeviri boş", window.Practice.ResultHeadline.Text);
            Assert.Empty(s.Evaluator.Calls);
        });
    }

    [Fact]
    public async Task WithoutDataFileSendOpensDataSettingsWindow()
    {
        using var s = Create(withCorpus: false);
        await WpfHost.RunAsync(async () =>
        {
            var (window, opened) = CreateWindow(s);
            Assert.Equal("Henüz cümle dosyası yüklenmedi.", window.Practice.SourceText.Text);
            Assert.Equal(Visibility.Visible, window.Practice.LoadDataButton.Visibility);
            Assert.Equal("Başlarken", window.Practice.ResultHeadline.Text);
            WpfHost.Screenshot(window.Root, "0-ilk-acilis");

            await window.Practice.SendAsync();

            Assert.Equal(["Ayarlar"], opened);
            Assert.Equal(Visibility.Visible, window.Settings.DataPanel.Visibility);
            Assert.Equal(Visibility.Collapsed, window.Settings.ApiPanel.Visibility);
            Assert.Same(window.Practice, window.PageHost.Content);
            Assert.Null(window.LastDialog!.Host.Child);
        });
    }

    [Fact]
    public async Task WithoutApiKeySendOpensApiSettingsWindow()
    {
        using var s = Create(withKey: false);
        await WpfHost.RunAsync(async () =>
        {
            var (window, opened) = CreateWindow(s);
            window.Practice.InputBox.Text = "Run!";

            await window.Practice.SendAsync();

            Assert.Equal("API anahtarı yok", window.Practice.ResultHeadline.Text);
            Assert.Equal(["Ayarlar"], opened);
            Assert.Equal(Visibility.Visible, window.Settings.ApiPanel.Visibility);
            Assert.Empty(s.Evaluator.Calls);
        });
    }

    [Fact]
    public void ModeButtonSwitchesDirectionAndPersists()
    {
        using var s = Create();
        WpfHost.Run(() =>
        {
            var (window, _) = CreateWindow(s);

            window.ModeEnTr.IsChecked = true;

            Assert.Equal(TranslationDirection.EnglishToTurkish, s.Session.Settings.Direction);
            Assert.Equal("İNGİLİZCE   ·   #1", window.Practice.SourceCaption.Text);
            Assert.Equal("Türkçe çevirini yaz…", window.Practice.Placeholder.Text);
            Assert.Equal(s.Session.Current!.Source, window.Practice.SourceText.Text);
            Assert.Contains("\"EnglishToTurkish\"", File.ReadAllText(s.Dir.File("settings.json")));
            WpfHost.Screenshot(window.Root, "3-mod-en-tr");
        });
    }

    [Fact]
    public void IconButtonsOpenSmallSettingsAndProgressWindows()
    {
        using var s = Create();
        WpfHost.Run(() =>
        {
            var (window, opened) = CreateWindow(s);

            window.SettingsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal("Ayarlar", window.LastDialog!.Title);
            Assert.True(window.LastDialog.Width < 1000);
            Assert.Equal(Visibility.Visible, window.Settings.ApiPanel.Visibility);

            window.ProgressButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal("Hedef", window.LastDialog!.Title);
            Assert.True(window.LastDialog.Width < 700);

            Assert.Equal(["Ayarlar", "Hedef"], opened);
            Assert.Same(window.Practice, window.PageHost.Content);
        });
    }

    [Fact]
    public void FirstRunSettingsShowHelpForApiKeyAndDataFile()
    {
        using var s = Create(withCorpus: false, withKey: false);
        WpfHost.Run(() =>
        {
            var (window, _) = CreateWindow(s);
            Assert.Equal("Lingora", window.Title);
            var settings = window.Settings;

            // Anahtar ve dosya yokken ilgili yardım kendiliğinden açık gelir.
            window.OpenSettings(SettingsSection.ApiKey);
            Assert.Equal(Visibility.Visible, settings.ApiHelpPanel.Visibility);
            var apiText = AllText(settings.ApiHelpPanel);
            Assert.Contains("Create API key", apiText);
            Assert.Contains("aistudio.google.com/apikey", apiText);
            WpfHost.ScreenshotPage(settings, "14-yardim-api", 900, 900);

            window.OpenSettings(SettingsSection.DataFile);
            Assert.Equal(Visibility.Visible, settings.DataHelpPanel.Visibility);
            var dataText = AllText(settings.DataHelpPanel);
            Assert.Contains("tur-eng.zip", dataText);
            Assert.Contains("manythings.org/anki", dataText);
            WpfHost.ScreenshotPage(settings, "15-yardim-txt", 900, 900);

            // Düğme paneli açıp kapatır.
            settings.DataHelpButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(Visibility.Collapsed, settings.DataHelpPanel.Visibility);
            settings.DataHelpButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(Visibility.Visible, settings.DataHelpPanel.Visibility);
        });
    }

    [Fact]
    public void HelpStaysClosedWhenKeyAndDataAlreadyExist()
    {
        using var s = Create();
        WpfHost.Run(() =>
        {
            var (window, _) = CreateWindow(s);
            window.OpenSettings(SettingsSection.ApiKey);
            Assert.Equal(Visibility.Collapsed, window.Settings.ApiHelpPanel.Visibility);
            window.Settings.ApiHelpButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(Visibility.Visible, window.Settings.ApiHelpPanel.Visibility);
        });
    }

    private static string AllText(DependencyObject root)
    {
        var parts = new List<string>();
        void Walk(object node)
        {
            switch (node)
            {
                case TextBlock block:
                    parts.Add(new System.Windows.Documents.TextRange(block.ContentStart, block.ContentEnd).Text);
                    return;
                case DependencyObject d:
                    foreach (var child in LogicalTreeHelper.GetChildren(d))
                    {
                        Walk(child);
                    }

                    break;
            }
        }

        Walk(root);
        return string.Join("\n", parts);
    }

    [Fact]
    public void PageWindowHasOwnSubtleCaptionWithCloseButton()
    {
        using var s = Create();
        WpfHost.Run(() =>
        {
            var page = new Lingora.Desktop.Pages.ProgressPage(s.Session);
            var dialog = new PageWindow("Hedef", page, 660, 560);
            try
            {
                var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(dialog);
                Assert.NotNull(chrome);
                Assert.Same(dialog.Frame, dialog.Content);
                Assert.IsType<DrawingBrush>(dialog.Frame.Background);

                WpfHost.Layout(dialog.Frame, 660, 560);
                var buttons = FindAll<Button>(dialog.Frame).ToList();
                var close = Assert.Single(buttons, b => Equals(b.ToolTip, "Kapat"));
                Assert.True(System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(close));

                WpfHost.Screenshot(dialog.Frame, "13-hedef-pencere", 660, 560);
            }
            finally
            {
                dialog.Detach();
            }
        });
    }

    private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindAll<T>(child))
            {
                yield return nested;
            }
        }
    }

    [Fact]
    public async Task SettingsPagesSaveKeyImportFileAndGeneralSettings()
    {
        using var s = Create(withCorpus: false, withKey: false);
        var txt = s.Dir.Write("tur.txt", Samples.TatoebaLines);

        await WpfHost.RunAsync(async () =>
        {
            var (window, _) = CreateWindow(s);
            window.OpenSettings(SettingsSection.ApiKey);
            var settings = window.Settings;
            Assert.Equal("Kayıtlı anahtar yok.", settings.CurrentKeyText.Text);

            settings.SaveKey("  ");
            Assert.Contains("boş", settings.ApiStatus.Text);

            settings.SaveKey(FakeKey);
            Assert.Contains("AIza…0000", settings.CurrentKeyText.Text);
            Assert.Equal(FakeKey, s.Session.GetApiKey().Key);
            Assert.DoesNotContain(FakeKey, File.ReadAllText(s.Dir.File("settings.json")));
            WpfHost.ScreenshotPage(settings, "4-ayarlar-api", 900, 640);

            settings.ShowSection(SettingsSection.DataFile);
            await settings.ImportAsync(txt);
            Assert.Equal("11 cümle çifti", settings.CorpusInfo.Text);
            Assert.StartsWith("Yüklendi: 11 cümle çifti.", settings.ImportStatus.Text);
            Assert.NotNull(s.Session.Current);
            Assert.Equal(s.Session.Current!.Source, window.Practice.SourceText.Text);
            WpfHost.ScreenshotPage(settings, "5-ayarlar-veri", 900, 640);

            await settings.ImportAsync(s.Dir.File("olmayan.txt"));
            Assert.StartsWith("Yüklenemedi:", settings.ImportStatus.Text);

            settings.ShowSection(SettingsSection.General);
            settings.SaveGeneral("sıfır", Difficulty.All, "gemini-flash-latest");
            Assert.Contains("arasında bir sayı", settings.GeneralStatus.Text);

            settings.SaveGeneral("7", Difficulty.Easy, "models/gemini-3.5-flash-lite");
            Assert.Equal(7, s.Session.Settings.DailyGoal);
            Assert.Equal(Difficulty.Easy, s.Session.Settings.Difficulty);
            Assert.Equal("gemini-3.5-flash-lite", s.Session.Settings.Model);
            Assert.Equal("gemini-3.5-flash-lite", settings.ModelBox.Text);
            Assert.Equal("0/7", window.TodayText.Text);
            WpfHost.ScreenshotPage(settings, "6-ayarlar-genel", 900, 640);

            settings.ShowSection(SettingsSection.SystemCheck);
            WpfHost.ScreenshotPage(settings, "7-ayarlar-kontrol", 900, 640);
        });
    }

    [Fact]
    public async Task ProgressWindowShowsTotalsGoalAndHistory()
    {
        using var s = Create();
        await WpfHost.RunAsync(async () =>
        {
            var (window, opened) = CreateWindow(s);
            for (var i = 0; i < 3; i++)
            {
                window.Practice.InputBox.Text = "cevap " + i;
                await window.Practice.SendAsync();
                await window.Practice.SendAsync();
            }

            Assert.Equal("3/20", window.TodayText.Text);
            window.OpenProgress();

            var progress = window.Progress;
            Assert.Equal(["Hedef"], opened);
            Assert.Equal(3, progress.Summary!.TotalSentences);
            Assert.Equal("3", progress.TodayCount.Text);
            Assert.Equal("/ 20 cümle", progress.GoalText.Text);
            Assert.Equal("17 kaldı", progress.RemainingText.Text);
            Assert.Equal(0.15, progress.FillRatio, 3);
            Assert.Equal(4, progress.Stats.Children.Count);
            Assert.Equal(7, progress.Week.Children.Count);
            Assert.Contains("%100", progress.Footer.Text);
            WpfHost.ScreenshotPage(progress, "8-hedef", 660, 560);
        });
    }

    [Fact]
    public async Task GoalWindowMarksGoalReached()
    {
        using var s = Create();
        s.Session.UpdateGeneralSettings(1, Difficulty.All, "gemini-3.5-flash-lite");
        await WpfHost.RunAsync(async () =>
        {
            var (window, _) = CreateWindow(s);
            window.Practice.InputBox.Text = "Tom";
            await window.Practice.SendAsync();
            window.OpenProgress();

            Assert.Equal("Hedef tamam", window.Progress.RemainingText.Text);
            Assert.Equal(1.0, window.Progress.FillRatio);
            Assert.Equal("1/1", window.TodayText.Text);
        });
    }

    [Fact]
    public async Task WhileWaitingOnlyDotsSpinThenAnswerTypesOut()
    {
        using var s = Create();
        var gate = new TaskCompletionSource();
        s.Evaluator.Gate = gate.Task;
        s.Evaluator.Handler = (_, _) => new Evaluation
        {
            Score = 95,
            IsCorrect = true,
            NaturalTranslation = "Tom said he didn't know that Mary could speak French.",
            Tip = "\"could\" yetenek bildiren \"can\" fiilinin geçmiş halidir.",
        };

        await WpfHost.RunAsync(async () =>
        {
            var (window, _) = CreateWindow(s);
            var practice = window.Practice;
            practice.AnimateResults = true;
            practice.InputBox.Text = "Tom said he did not know Mary can speak French.";

            var sending = practice.SendAsync();

            // Beklerken: yalnızca noktalar, başka yazı yok.
            Assert.False(sending.IsCompleted);
            Assert.Equal(Visibility.Visible, practice.ThinkingText.Visibility);
            Assert.Equal(".", practice.ThinkingText.Text);
            Assert.Equal(Visibility.Collapsed, practice.ResultPanel.Visibility);
            Assert.False(practice.SendButton.IsEnabled);
            WpfHost.Screenshot(window.Root, "2a-bekleniyor");

            await Task.Delay(800);
            Assert.Contains(practice.ThinkingText.Text, new[] { "..", "..." , "." });
            Assert.NotEqual(Visibility.Collapsed, practice.ThinkingText.Visibility);

            gate.SetResult();
            await sending;

            // Cevap geldi: noktalar kalktı, metin harf harf yazılmaya başladı.
            Assert.Equal(Visibility.Collapsed, practice.ThinkingText.Visibility);
            Assert.True(practice.IsTyping);
            Assert.True(practice.ResultHeadline.Text.Length < "95 / 100   ·   Doğru".Length);

            await Task.Delay(120);
            var partway = practice.ResultHeadline.Text;
            Assert.True(partway.Length > 0, "Yazma animasyonu ilerlemedi.");

            var waited = 0;
            while (practice.IsTyping && waited < 5000)
            {
                await Task.Delay(20);
                waited += 20;
            }

            Assert.False(practice.IsTyping, "Yazma animasyonu bitmedi.");
            Assert.Equal("95 / 100   ·   Doğru", practice.ResultHeadline.Text);
            var fields = practice.ResultBody.Children.OfType<StackPanel>().ToList();
            Assert.All(fields, f => Assert.Equal(Visibility.Visible, f.Visibility));
            var natural = fields.Single(f => ((TextBlock)f.Children[0]).Text == "DOĞAL ÇEVİRİ");
            Assert.Equal("Tom said he didn't know that Mary could speak French.", ((TextBox)natural.Children[1]).Text);
            Assert.NotEmpty(((TextBox)fields[^1].Children[1]).Text);
        });
    }

    [Fact]
    public async Task EnterDuringTypingFinishesAnswerFirst()
    {
        using var s = Create();
        await WpfHost.RunAsync(async () =>
        {
            var (window, _) = CreateWindow(s);
            var practice = window.Practice;
            practice.AnimateResults = true;
            var item = s.Session.Current;
            practice.InputBox.Text = "Tom";

            await practice.SendAsync();
            Assert.True(practice.IsTyping);

            await practice.SendAsync();   // ilk Enter: yazmayı bitir
            Assert.False(practice.IsTyping);
            Assert.Equal("90 / 100   ·   Doğru", practice.ResultHeadline.Text);
            Assert.Same(item, s.Session.Current);

            await practice.SendAsync();   // ikinci Enter: sonraki cümle
            Assert.NotSame(item, s.Session.Current);
        });
    }

    [Fact]
    public async Task VocabularyAppearsInResultAndInDictionaryWindowWithoutDuplicates()
    {
        using var s = Create();
        s.Evaluator.Handler = (_, _) => new Evaluation
        {
            Score = 58,
            IsCorrect = false,
            CorrectedTranslation = "Tom said he didn't know that Mary could speak French.",
            Vocabulary =
            [
                new VocabularyItem("be supposed to", "-mesi gerekmek / beklenmek", "You are supposed to wait here. (Burada beklemen gerekiyor.)", "kalıp"),
                new VocabularyItem("speak French", "Fransızca konuşmak", "Can you speak French? (Fransızca konuşabilir misin?)", "tamlama"),
                new VocabularyItem("could", "-ebilirdi (can'in geçmişi)", "She could swim at five. (Beş yaşında yüzebiliyordu.)", "kelime"),
            ],
        };

        await WpfHost.RunAsync(async () =>
        {
            var (window, opened) = CreateWindow(s);
            var practice = window.Practice;

            practice.InputBox.Text = "Tom said he not know Mary can French speak.";
            await practice.SendAsync();

            var labels = practice.ResultBody.Children.OfType<StackPanel>()
                .Select(p => ((TextBlock)p.Children[0]).Text).ToList();
            Assert.Contains("ÖĞRENİLECEKLER", labels);
            var note = (TextBlock)practice.ResultBody.Children[^1];
            Assert.Equal("3 yeni terim sözlüğe eklendi.", note.Text);
            Assert.Equal("3", window.VocabularyCount.Text);
            WpfHost.Screenshot(window.Root, "9-sonuc-sozluk", height: 900);

            // Aynı kalıpta ikinci kez hata: yeni kayıt açılmaz, tekrar sayısı artar.
            await practice.SendAsync();
            s.Evaluator.Handler = (_, _) => new Evaluation
            {
                Score = 70,
                IsCorrect = false,
                Vocabulary = [new VocabularyItem("To be supposed to.", "-mesi gerekmek", "He was supposed to call. (Araması gerekiyordu.)", "kalıp")],
            };
            practice.InputBox.Text = "ikinci cevap";
            await practice.SendAsync();

            Assert.Equal("1 terim zaten sözlükteydi, tekrar sayısı arttı.", ((TextBlock)practice.ResultBody.Children[^1]).Text);
            Assert.Equal("3", window.VocabularyCount.Text);

            // Sözlük penceresi
            window.VocabularyButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal("Sözlük", opened[^1]);
            var page = window.Vocabulary;
            Assert.Equal(3, page.Shown.Count);
            Assert.Equal("3 kayıt", page.CountText.Text);
            Assert.Equal("be supposed to", page.Shown[0].Term);
            Assert.Equal(2, page.Shown[0].MistakeCount);
            Assert.Equal(2, page.Shown[0].Examples.Count);
            WpfHost.ScreenshotPage(page, "10-sozluk", 720, 640);

            page.SearchBox.Text = "fransızca";
            Assert.Equal(["speak French"], page.Shown.Select(e => e.Term));
            Assert.Equal(Visibility.Collapsed, page.SearchPlaceholder.Visibility);

            page.SearchBox.Text = "olmayan kelime";
            Assert.Empty(page.Shown);
            Assert.Equal("Aramana uyan kayıt yok.", page.EmptyText.Text);

            page.SearchBox.Text = string.Empty;
            page.SortAlpha.IsChecked = true;
            Assert.Equal(["be supposed to", "could", "speak French"], page.Shown.Select(e => e.Term));

            page.SortMistakes.IsChecked = true;
            Assert.Equal("be supposed to", page.Shown[0].Term);

            page.Delete(page.Shown[0]);
            Assert.Equal(2, page.Shown.Count);
            Assert.Equal("2", window.VocabularyCount.Text);
            Assert.Null(s.Session.Vocabulary.Find("be supposed to"));
        });
    }

    [Fact]
    public void EmptyDictionaryExplainsHowItFills()
    {
        using var s = Create();
        WpfHost.Run(() =>
        {
            var (window, _) = CreateWindow(s);
            Assert.Equal(Visibility.Collapsed, window.VocabularyCount.Visibility);

            window.OpenVocabulary();

            Assert.Empty(window.Vocabulary.Shown);
            Assert.StartsWith("Sözlüğün henüz boş.", window.Vocabulary.EmptyText.Text);
            Assert.Equal(Visibility.Visible, window.Vocabulary.EmptyText.Visibility);
        });
    }
}

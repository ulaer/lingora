using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Lingora.Core;
using Lingora.Core.Gemini;
using Lingora.Core.Journal;
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

    private static void UseMistakesAndTip(Setup s) =>
        s.Evaluator.Handler = (_, _) => new Evaluation
        {
            Score = 72,
            IsCorrect = false,
            CorrectedTranslation = "Tom said he didn't know what Mary was supposed to do.",
            NaturalTranslation = "Tom said he had no idea what Mary was supposed to do.",
            Mistakes = ["must → was supposed to: dolaylı anlatımda zaman geçmişe kayar", "know → knew: zaman uyumu"],
            MinorIssues = ["tom → Tom"],
            Tip = "Dolaylı anlatımda (reported speech) fiil bir zaman geriye gider.",
        };

    [Fact]
    public void JournalIconSitsBetweenGoalAndDictionary()
    {
        using var s = Create();
        WpfHost.Run(() =>
        {
            var (window, opened) = CreateWindow(s);
            var bar = (StackPanel)window.JournalButton.Parent;
            var goal = bar.Children.IndexOf(window.ProgressButton);
            var journal = bar.Children.IndexOf(window.JournalButton);
            var dictionary = bar.Children.IndexOf(window.VocabularyButton);
            Assert.Equal(goal + 1, journal);
            Assert.Equal(journal + 1, dictionary);
            Assert.Contains("Günlük", (string)window.JournalButton.ToolTip);

            window.JournalButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(["Günlük"], opened);
            Assert.Same(window.Practice, window.PageHost.Content);
        });
    }

    [Fact]
    public async Task EachMistakeAndTipCanBeExplainedInDetailOnce()
    {
        using var s = Create();
        UseMistakesAndTip(s);
        await WpfHost.RunAsync(async () =>
        {
            var (window, _) = CreateWindow(s);
            var practice = window.Practice;
            practice.InputBox.Text = "tom said he didn't know what Mary must do";
            await practice.SendAsync();

            // 2 hata + 1 ipucu; küçük hatalarda açıklama düğmesi yok.
            Assert.Equal(3, practice.ExplainSlots.Count);
            Assert.All(practice.ExplainSlots, slot =>
            {
                Assert.Equal(ExplainSlot.ShowLabel, slot.Button.Content);
                Assert.Equal(Visibility.Collapsed, slot.Panel.Visibility);
            });

            var second = practice.ExplainSlots[1];
            await practice.ToggleExplanationAsync(second);
            Assert.Equal(Visibility.Visible, second.Panel.Visibility);
            Assert.Contains("know → knew", second.Text.Text);
            Assert.Equal(ExplainSlot.HideLabel, second.Button.Content);
            Assert.Equal(FeedbackKind.Mistake, Assert.Single(s.Evaluator.ExplainCalls).Kind);

            var tip = practice.ExplainSlots[2];
            Assert.Equal(FeedbackKind.Tip, tip.Kind);
            await practice.ToggleExplanationAsync(tip);
            WpfHost.Screenshot(window.Root, "16-ayrintili-aciklama", height: 1200);

            // Gizle / tekrar göster: AI yeniden çağrılmaz.
            await practice.ToggleExplanationAsync(second);
            Assert.Equal(Visibility.Collapsed, second.Panel.Visibility);
            await practice.ToggleExplanationAsync(second);
            Assert.Equal(Visibility.Visible, second.Panel.Visibility);
            Assert.Equal(2, s.Evaluator.ExplainCalls.Count);

            var saved = Assert.Single(s.Session.Journal.LoadDay(s.Session.Today));
            Assert.Equal(2, saved.Explanations.Count);
        });
    }

    [Fact]
    public async Task JournalShowsTodayCreatesLessonOnceAndSavesPdf()
    {
        using var s = Create();
        UseMistakesAndTip(s);
        var pdfPath = s.Dir.File(Path.Combine("disari", "not.pdf"));
        await WpfHost.RunAsync(async () =>
        {
            var (window, _) = CreateWindow(s);
            var practice = window.Practice;
            practice.InputBox.Text = "tom said he didn't know what Mary must do";
            await practice.SendAsync();
            await practice.ToggleExplanationAsync(practice.ExplainSlots[0]);

            window.OpenJournal();
            var journal = window.Journal;
            Assert.Equal(s.Session.Today, journal.SelectedDay);
            var today = journal.Days[0];
            Assert.Equal(1, today.Entries);
            Assert.Equal(2, today.Mistakes);
            Assert.False(today.HasLesson);
            Assert.Equal(Visibility.Visible, journal.LessonEmpty.Visibility);
            Assert.True(journal.CreateLessonButton.IsEnabled);
            WpfHost.ScreenshotPage(journal, "17-gunluk-bos", 1040, 720);

            await journal.CreateLessonAsync();
            Assert.NotNull(journal.ShownLesson);
            Assert.Equal(Visibility.Collapsed, journal.LessonEmpty.Visibility);
            Assert.Equal(Visibility.Visible, journal.LessonContent.Visibility);
            Assert.True(journal.Days[0].HasLesson);
            Assert.Single(s.Evaluator.LessonCalls);
            WpfHost.ScreenshotPage(journal, "18-ders-notu", 1040, 900);

            // Tekrar açınca kayıttan gelir; AI yeniden çağrılmaz.
            window.OpenJournal();
            Assert.NotNull(journal.ShownLesson);
            await journal.CreateLessonAsync();
            Assert.Single(s.Evaluator.LessonCalls);

            journal.SavePathPicker = suggested =>
            {
                Assert.Equal("Lingora-ders-notu-2026-11-06.pdf", suggested);
                return pdfPath;
            };
            Assert.NotNull(journal.SavePdfButton);
            journal.SavePdfButton!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.True(File.Exists(pdfPath));
            Assert.Contains("PDF kaydedildi", journal.LessonStatus.Text);

            // Vazgeçilirse bir şey yapılmaz.
            journal.SavePathPicker = _ => null;
            Assert.Null(journal.SavePdf());

            journal.TabRecords.IsChecked = true;
            Assert.Equal(Visibility.Visible, journal.RecordsPanel.Visibility);
            var records = AllText(journal.RecordsPanel);
            Assert.Contains("tom said he didn't know", records);
            WpfHost.ScreenshotPage(journal, "19-gunluk-kayitlar", 1040, 720);
        });
    }

    [Fact]
    public async Task ExplanationIsPlainTextLikeTheAnswerAndTypesOut()
    {
        using var s = Create();
        UseMistakesAndTip(s);
        await WpfHost.RunAsync(async () =>
        {
            var (window, _) = CreateWindow(s);
            var practice = window.Practice;
            practice.InputBox.Text = "tom said he didn't know what Mary must do";
            await practice.SendAsync();

            var slot = practice.ExplainSlots[0];

            // Kutu/zemin yok: yalnızca şeffaf bir yazı alanı.
            Assert.IsType<StackPanel>(slot.Panel);
            Assert.Null(((StackPanel)slot.Panel).Background);
            Assert.Equal(Colors.Transparent, ((SolidColorBrush)slot.Text.Background).Color);

            // Animasyon açıkken metin harf harf gelir.
            practice.AnimateResults = true;
            await practice.ToggleExplanationAsync(slot);
            Assert.True(slot.Typing.IsRunning);
            Assert.True(slot.Text.Text.Length < s.Evaluator.ExplainHandler(s.Evaluator.ExplainCalls[0]).Length);
            while (slot.Typing.StepOnce())
            {
            }

            Assert.StartsWith("Ayrıntılı açıklama: must", slot.Text.Text);
            Assert.Equal(Visibility.Visible, slot.Panel.Visibility);
            WpfHost.Screenshot(window.Root, "20-aciklama-seffaf", height: 1000);
        });
    }

    [Fact]
    public async Task JournalOffersRegenerationAfterNewSentencesAndBuildsAudioLesson()
    {
        using var s = Create();
        UseMistakesAndTip(s);
        var mp3Copy = s.Dir.File(Path.Combine("disari", "ders.mp3"));
        await WpfHost.RunAsync(async () =>
        {
            var (window, _) = CreateWindow(s);
            var practice = window.Practice;
            practice.InputBox.Text = "tom said he didn't know what Mary must do";
            await practice.SendAsync();

            window.OpenJournal();
            var journal = window.Journal;
            await journal.CreateLessonAsync();
            Assert.Null(journal.RegenerateLessonButton);

            // Sesli ders: metin + gerçek MP3 (Media Foundation).
            journal.TabAudio.IsChecked = true;
            Assert.Equal(Visibility.Visible, journal.AudioPanel.Visibility);
            Assert.Equal(Visibility.Collapsed, journal.LessonPanel.Visibility);
            Assert.True(journal.CreateAudioButton.IsEnabled);
            await journal.CreateAudioAsync();
            Assert.NotNull(journal.ShownAudio);
            Assert.Equal(Visibility.Visible, journal.AudioContent.Visibility);
            Assert.Contains("Bugün 1 cümle", journal.AudioScriptText.Text);
            Assert.Equal(Visibility.Collapsed, journal.RegenerateAudioButton.Visibility);
            Assert.True(new FileInfo(journal.ShownAudioPath!).Length > 1000);
            Assert.True(journal.Days[0].HasAudio);
            WpfHost.ScreenshotPage(journal, "21-sesli-ders", 1040, 760);

            journal.SaveMp3PathPicker = name =>
            {
                Assert.Equal("Lingora-sesli-ders-2026-11-06.mp3", name);
                return mp3Copy;
            };
            journal.SaveMp3Button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.True(File.Exists(mp3Copy));
            Assert.Contains("MP3 kaydedildi", journal.AudioStatus.Text);

            // Aynı gün yeni çeviri → ders notu ve sesli ders yenilenebilir.
            await practice.SendAsync();
            practice.InputBox.Text = "Tom said he knew";
            await practice.SendAsync();
            window.OpenJournal();
            Assert.NotNull(journal.RegenerateLessonButton);
            Assert.Contains("1 yeni çeviri", (string)journal.RegenerateLessonButton!.Content);
            Assert.Equal(Visibility.Visible, journal.RegenerateAudioButton.Visibility);

            await journal.CreateLessonAsync(regenerate: true);
            await journal.CreateAudioAsync(regenerate: true);
            Assert.Equal(2, s.Evaluator.LessonCalls.Count);
            Assert.Equal(2, s.Evaluator.AudioScriptCalls.Count);
            Assert.Null(journal.RegenerateLessonButton);
            Assert.Equal(Visibility.Collapsed, journal.RegenerateAudioButton.Visibility);
            Assert.Equal(2, journal.ShownAudio!.EntryCount);
        });
    }

    [Fact]
    public void SvgSceneDrawsEveryAssetAndComposesTintedObjectsAtAnchor()
    {
        WpfHost.Run(() =>
        {
            var library = GameFixture.Library;
            var renderer = new SvgScene(library);
            // Sahne asset'leri tek başına dolu bir resimdir; overlay ve yüz parçaları (v2) küçük eklerdir, ayrıca bakılır.
            var sceneCategories = new HashSet<string> { "character", "object", "background" };
            foreach (var a in library.Assets.Values.Where(a => sceneCategories.Contains(a.Category)))
            {
                Assert.True(Opaque(Rasterize(renderer.RenderAsset(a.Id))) > 300, a.Id);
            }

            foreach (var a in library.Assets.Values.Where(a => !sceneCategories.Contains(a.Category)))
            {
                Assert.True(Opaque(Rasterize(renderer.RenderAsset(a.Id))) > 0, a.Id);
            }

            // fill grup üzerinde (SVG mirası): güneş siyah değil sarı çizilir.
            Assert.Equal(Color.FromRgb(0xE9, 0xC4, 0x6A), Pixel(Rasterize(renderer.RenderAsset("sun")), 128, 128));

            // Yeşil elma: alt-orta noktası (176,160) tutma noktasında, gövdesi ~ (176,145).
            var green = Rasterize(renderer.Render(GameFixture.Scene("tom_stand__apple__green")));
            Assert.Equal(Color.FromRgb(0x2A, 0x9D, 0x8F), Pixel(green, 176, 145));
            var red = Rasterize(renderer.Render(GameFixture.Scene("tom_stand__apple")));
            Assert.Equal(Color.FromRgb(0xE6, 0x39, 0x46), Pixel(red, 176, 145));

            // Arka planlı sahne kenara kadar dolu; grup sahnesinde iki karakter yan yana.
            var park = Rasterize(renderer.Render(GameFixture.Scene("tom_stand__apple__park")));
            Assert.Equal(Color.FromRgb(0xA8, 0xDA, 0xDC), Pixel(park, 128, 5));
            var kids = Rasterize(renderer.Render(GameFixture.Scene("kids__run__as_tom_and_mary")));
            Assert.True(Opaque(kids, 0, 128) > 300 && Opaque(kids, 128, 256) > 300);
        });
    }

    [Fact]
    public void ObjectsThatBlendIntoClothingAreDetectedAndSkipped()
    {
        using var s = Create();
        WpfHost.Run(() =>
        {
            var library = GameFixture.Library;
            var renderer = new SvgScene(library);
            var yellowOnYellow = library.Scenes.First(x => x.En == "She is holding a yellow apple in the park." && x.Character == "woman_stand");
            var redOnBlue = GameFixture.Scene("tom_stand__apple");

            Assert.True(renderer.ObjectVisibility(yellowOnYellow) < GamePage.MinObjectVisibility);
            Assert.True(renderer.ObjectVisibility(redOnBlue) > 0.9);
            Assert.Equal(1, renderer.ObjectVisibility(GameFixture.Scene("tom_run")));

            // Kayıt turları (v5): sarı elma, kadının sarı üstünde kaybolur; kırmızı elma mavi üstünde görünür.
            Lingora.Core.Game.GameRound RoundOf(string tint, string character) =>
                library.Records(Lingora.Core.Game.GameLevel.A1)
                    .Where(r => r.Actors.Count == 1 && r.Actors[0].Char == character && r.Actors[0].Hold is { Obj: "apple" } h
                                && string.Equals(h.Tint, tint, StringComparison.OrdinalIgnoreCase))
                    .Select(r => new Lingora.Core.Game.GameRound(r, r.Sentences[0])).First();
            var camouflaged = RoundOf("#E9C46A", "woman_stand");
            Assert.True(renderer.ObjectVisibility(camouflaged) < GamePage.MinObjectVisibility);
            Assert.True(renderer.ObjectVisibility(RoundOf("#E63946", "tom_stand")) > 0.9);

            // Oyun hiçbir seviyede gizlenen nesneli tur göstermez.
            var (window, _) = CreateWindow(s);
            window.GameRandom = new Random(2);
            window.OpenGame();
            var game = window.Game!;
            foreach (var level in new[] { Lingora.Core.Game.GameLevel.A1, Lingora.Core.Game.GameLevel.A2, Lingora.Core.Game.GameLevel.B1 })
            {
                game.SetLevel(level);
                for (var i = 0; i < 30; i++)
                {
                    Assert.True(renderer.ObjectVisibility(game.Round!) >= GamePage.MinObjectVisibility, game.Round!.Id);
                    game.NextScene();
                }
            }
        });
    }

    private static BitmapSource Rasterize(DrawingImage image)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(image, new Rect(0, 0, 256, 256));
        }

        var bitmap = new RenderTargetBitmap(256, 256, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static Color Pixel(BitmapSource bitmap, int x, int y)
    {
        var px = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
        return Color.FromRgb(px[2], px[1], px[0]);
    }

    private static int Opaque(BitmapSource bitmap, int fromX = 0, int toX = 256)
    {
        var pixels = new byte[256 * 256 * 4];
        bitmap.CopyPixels(pixels, 256 * 4, 0);
        var count = 0;
        for (var y = 0; y < 256; y++)
        {
            for (var x = fromX; x < toX; x++)
            {
                if (pixels[(((y * 256) + x) * 4) + 3] > 200)
                {
                    count++;
                }
            }
        }

        return count;
    }

    [Fact]
    public void GameIconSitsAfterDictionaryAndOpensGameWindow()
    {
        using var s = Create();
        WpfHost.Run(() =>
        {
            var (window, opened) = CreateWindow(s);
            var bar = (StackPanel)window.GameButton.Parent;
            Assert.Equal(bar.Children.IndexOf(window.VocabularyButton) + 1, bar.Children.IndexOf(window.GameButton));
            Assert.Equal(bar.Children.IndexOf(window.GameButton) + 1, bar.Children.IndexOf(window.SettingsButton));

            window.GameRandom = new Random(11);
            window.GameButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(["Oyun"], opened);
            Assert.NotNull(window.Game!.Round);
            Assert.NotNull(window.Game.SceneImage.Source);
            Assert.True(window.Game.LegendVisible);
            Assert.Contains("dün", window.Game.LegendText.Text);
            window.Game.SetLegendVisible(false);
            Assert.False(window.Game.LegendVisible);
        });
    }

    [Fact]
    public async Task DescribeModeSendsPictureToGeminiAndShowsResult()
    {
        using var s = Create();
        s.Evaluator.Handler = (item, text) => new Evaluation
        {
            Score = 74,
            IsCorrect = false,
            CorrectedTranslation = item.References[0],
            NaturalTranslation = item.References[0],
            Mistakes = ["red → yellow: resimdeki toplar sarı"],
            Tip = "Renk sıfatı isimden önce gelir: two yellow balls.",
        };
        await WpfHost.RunAsync(async () =>
        {
            var (window, _) = CreateWindow(s);
            window.GameRandom = new Random(11);
            window.OpenGame();
            var game = window.Game!;
            game.AnimateResults = false;
            Assert.Contains("İNGİLİZCE", game.DescribeCaption.Text);
            Assert.StartsWith("İpucu · özne: ", game.HintText.Text);
            WpfHost.ScreenshotPage(game, "22-oyun-anlat", 1080, 780);

            game.InputBox.Text = "There are some balls";
            await game.SendAsync();

            var call = Assert.Single(s.Evaluator.Calls);
            Assert.NotNull(call.Item.ImageFacts);
            Assert.Equal(game.Round!.Sentence.En, call.Item.References[0]);
            Assert.Equal(Visibility.Visible, game.ResultPanel.Visibility);
            Assert.Equal("74 / 100   ·   Düzeltilmeli", game.ResultHeadline.Text);
            Assert.Equal("Sonraki resim", game.SendButton.Content);
            Assert.Equal("1/20", window.TodayText.Text);
            Assert.Contains("1 resim", game.ScoreText.Text);
            WpfHost.ScreenshotPage(game, "23-oyun-sonuc", 1080, 1000);

            var first = game.Round;
            await game.SendAsync();
            Assert.NotSame(first, game.Round);
            Assert.Equal(Visibility.Collapsed, game.ResultPanel.Visibility);
            Assert.Equal(string.Empty, game.InputBox.Text);
        });
    }

    [Fact]
    public async Task GameLanguageSelectorSwitchesToTurkishEverywhere()
    {
        using var s = Create();
        s.Evaluator.Handler = (item, _) => new Evaluation
        {
            Score = 80,
            IsCorrect = true,
            CorrectedTranslation = item.References[0],
            NaturalTranslation = item.References[0],
        };
        await WpfHost.RunAsync(async () =>
        {
            var (window, _) = CreateWindow(s);
            window.GameRandom = new Random(7);
            window.OpenGame();
            var game = window.Game!;
            game.AnimateResults = false;
            Assert.True(game.LangEnglish.IsChecked);
            Assert.False(game.LangTurkish.IsChecked);

            game.LangTurkish.IsChecked = true;

            Assert.Equal(TranslationDirection.EnglishToTurkish, s.Session.Settings.GameDirection);
            Assert.Equal(TranslationDirection.TurkishToEnglish, s.Session.Settings.Direction);
            Assert.Equal("RESMİ TÜRKÇE BİR CÜMLEYLE ANLAT", game.DescribeCaption.Text);
            Assert.Contains("Türkçe yaz", game.Placeholder.Text);
            Assert.Equal(game.Round!.Sentence.Tr, game.Question!.Correct);
            Assert.Contains(game.Library!.SubjectIn(game.Round!, TranslationDirection.EnglishToTurkish), game.HintText.Text);
            WpfHost.ScreenshotPage(game, "26-oyun-turkce", 1080, 780);

            game.InputBox.Text = "Resimdeki kişi bir şey tutuyor";
            await game.SendAsync();
            var call = Assert.Single(s.Evaluator.Calls);
            Assert.Equal(TranslationDirection.EnglishToTurkish, call.Item.Direction);
            Assert.Equal(game.Round!.Sentence.Tr, call.Item.References[0]);
            Assert.Contains(game.Round!.Sentence.Tr, AllText(game.ResultBody));

            game.ModeChoose.IsChecked = true;
            Assert.Contains("DOĞRU TÜRKÇE CÜMLE", game.ChooseCaption.Text);
            Assert.All(game.Question!.Options, o => Assert.Contains(o, game.Library.Scenes.Select(x => x.Tr)));

            // Geri İngilizceye.
            game.LangEnglish.IsChecked = true;
            Assert.Equal(TranslationDirection.TurkishToEnglish, s.Session.Settings.GameDirection);
            Assert.Equal(game.Round!.Sentence.En, game.Question!.Correct);
        });
    }

    [Fact]
    public void ChooseModeMarksAnswerRecordsItAndMovesOn()
    {
        using var s = Create();
        WpfHost.Run(() =>
        {
            var (window, _) = CreateWindow(s);
            window.GameRandom = new Random(4);
            window.OpenGame();
            var game = window.Game!;
            game.ModeChoose.IsChecked = true;
            Assert.Equal(Visibility.Visible, game.ChoosePanel.Visibility);
            Assert.Equal(Visibility.Collapsed, game.DescribePanel.Visibility);
            Assert.Equal(4, game.OptionButtons.Count);
            WpfHost.ScreenshotPage(game, "24-oyun-sec", 1080, 780);

            var question = game.Question!;
            var wrong = Enumerable.Range(0, 4).First(i => i != question.CorrectIndex);
            game.OptionButtons[wrong].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.All(game.OptionButtons, b => Assert.False(b.IsEnabled));
            Assert.Equal(Ui.Brush("SuccessBrush"), game.OptionButtons[question.CorrectIndex].BorderBrush);
            Assert.Equal(Ui.Brush("ErrorBrush"), game.OptionButtons[wrong].BorderBrush);
            Assert.Contains(question.Correct, game.ChoiceFeedback.Text);
            Assert.Equal(Visibility.Visible, game.NextChoiceButton.Visibility);
            Assert.Equal("1/20", window.TodayText.Text);
            Assert.Empty(s.Evaluator.Calls);
            WpfHost.ScreenshotPage(game, "25-oyun-sec-sonuc", 1080, 780);

            game.NextChoiceButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.NotSame(question, game.Question);
            Assert.All(game.OptionButtons, b => Assert.True(b.IsEnabled));
        });
    }

    [Fact]
    public void GameWithoutAssetsExplainsWhatIsMissing()
    {
        using var s = Create();
        WpfHost.Run(() =>
        {
            var (window, _) = CreateWindow(s);
            window.GameAssetsDirectory = s.Dir.File("yok");
            window.OpenGame();
            Assert.Equal(Visibility.Visible, window.Game!.MissingText.Visibility);
        });
    }

    [Fact]
    public void Mp3EncoderWritesPlayableMp3From24kHzSpeech()
    {
        using var dir = new TempDir();
        var path = dir.File("ses.mp3");

        new Mp3Encoder().EncodeMp3(FakeEvaluator.Tone(1.5), path);

        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 4000);
        var isId3 = bytes[0] == 'I' && bytes[1] == 'D' && bytes[2] == '3';
        var isFrame = bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0;
        Assert.True(isId3 || isFrame, "MP3 başlığı bekleniyordu.");
    }

    [Fact]
    public void JournalWithoutEntriesCannotCreateLesson()
    {
        using var s = Create();
        WpfHost.Run(() =>
        {
            var (window, _) = CreateWindow(s);
            window.OpenJournal();
            Assert.False(window.Journal.CreateLessonButton.IsEnabled);
            Assert.Contains("kayıt yok", window.Journal.DayStats.Text);
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
                case TextBox box:
                    parts.Add(box.Text);
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

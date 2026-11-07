using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lingora.Core;
using Lingora.Core.Gemini;
using Lingora.Core.Models;
using Lingora.Core.Storage;
using Lingora.Desktop;
using Lingora.Desktop.Pages;

namespace Lingora.Tests;

/// <summary>
/// README ve tanıtım için ekran görüntüleri ve demo animasyonu kareleri üretir.
/// Yalnızca LINGORA_MEDIA ortam değişkeni bir klasör gösteriyorsa çalışır; aksi halde hiçbir şey yapmaz.
/// Kareler sonra ffmpeg ile GIF/MP4'e dönüştürülür (bkz. docs/media/README.md).
/// </summary>
[Collection("WPF")]
public class MediaCapture
{
    private const double Scale = 2;

    private static readonly string Corpus = string.Join('\n', new[]
    {
        "We need to set off early tomorrow morning.\tYarın sabah erkenden yola çıkmamız gerekiyor.",
        "I have been looking forward to this trip.\tBu geziyi dört gözle bekliyordum.",
        "She gave up smoking last year.\tGeçen yıl sigarayı bıraktı.",
        "Could you keep an eye on my bag?\tÇantama göz kulak olur musun?",
        "It took me a while to get used to the noise.\tGürültüye alışmam biraz zaman aldı.",
        "Let's put off the meeting until Friday.\tToplantıyı cumaya erteleyelim.",
        "He came up with a brilliant idea.\tAklına harika bir fikir geldi.",
        "I ran into an old friend at the station.\tİstasyonda eski bir arkadaşıma rastladım.",
        "We ran out of milk this morning.\tBu sabah sütümüz bitti.",
        "Make sure you lock the door.\tKapıyı kilitlediğinden emin ol.",
    });

    private static readonly VocabularyItem[] Words =
    [
        new("set off", "yola çıkmak", "We set off at dawn. (Şafakta yola çıktık.)", "kalıp"),
        new("look forward to", "dört gözle beklemek", "I look forward to hearing from you. (Haberini dört gözle bekliyorum.)", "kalıp"),
        new("give up", "bırakmak, vazgeçmek", "Don't give up now. (Şimdi vazgeçme.)", "kalıp"),
        new("keep an eye on", "göz kulak olmak", "Keep an eye on the kids. (Çocuklara göz kulak ol.)", "kalıp"),
        new("get used to", "alışmak", "You'll get used to it. (Alışacaksın.)", "kalıp"),
        new("put off", "ertelemek", "Never put off till tomorrow. (Yarına bırakma.)", "kalıp"),
        new("come up with", "(fikir) bulmak, ortaya atmak", "She came up with a plan. (Bir plan buldu.)", "kalıp"),
        new("run into", "rastlamak, tesadüfen karşılaşmak", "I ran into Tom yesterday. (Dün Tom'a rastladım.)", "kalıp"),
        new("early in the morning", "sabah erkenden", "He gets up early in the morning. (Sabah erkenden kalkar.)", "tamlama"),
        new("brilliant", "parlak, harika", "What a brilliant idea! (Ne harika bir fikir!)", "kelime"),
    ];

    [Fact]
    public async Task CaptureReadmeMediaAndDemoFrames()
    {
        var output = Environment.GetEnvironmentVariable("LINGORA_MEDIA");
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        var frames = Path.Combine(output, "frames");
        if (Directory.Exists(frames))
        {
            Directory.Delete(frames, recursive: true);
        }

        Directory.CreateDirectory(frames);

        using var dir = new TempDir();
        var clock = new FixedTimeProvider(FixedTimeProvider.Local(2026, 11, 1, 19));
        var evaluator = new FakeEvaluator();
        var session = new PracticeSession(new AppPaths(dir.Path), evaluator, clock, new ApiKeyStore(_ => null), new Random(7));
        session.ImportCorpus(dir.Write("ornek.txt", Corpus));
        session.SetApiKey("AIzaTEST-media-key-0000");

        // Geçmiş: son 6 gün, çoğunda günlük hedef (20) tutturulmuş; sözlük dolu.
        var perDay = new[] { 21, 24, 13, 20, 22, 20 };
        var counter = 0;
        evaluator.Handler = (_, _) =>
        {
            counter++;
            var word = Words[counter % Words.Length];
            return new Evaluation
            {
                Score = 70 + (counter * 7 % 30),
                IsCorrect = true,
                CorrectedTranslation = "-",
                NaturalTranslation = "-",
                Vocabulary = counter % 3 == 0 ? [word] : [],
            };
        };
        for (var day = 0; day < perDay.Length; day++)
        {
            for (var i = 0; i < perDay[day]; i++)
            {
                clock.Now = FixedTimeProvider.Local(2026, 11, 1 + day, 9 + (i % 12), i % 60);
                await session.SubmitAsync("practice");
                session.NextSentence();
            }
        }

        // Bugün: 12 cümle yapılmış, şimdi vitrin cümlesi ekranda.
        for (var i = 0; i < 12; i++)
        {
            clock.Now = FixedTimeProvider.Local(2026, 11, 7, 9 + i % 10, i * 4);
            await session.SubmitAsync("practice");
            session.NextSentence();
        }

        clock.Now = FixedTimeProvider.Local(2026, 11, 7, 21, 15);
        session.StartFromLine(1);

        const string typed = "we have to go out early tomorrow morning";
        evaluator.Handler = (_, _) => new Evaluation
        {
            Score = 78,
            IsCorrect = false,
            CorrectedTranslation = "We have to set off early tomorrow morning.",
            NaturalTranslation = "We need to head off first thing tomorrow morning.",
            Mistakes = ["go out → set off: \"go out\" dışarı çıkmak demek; yolculuğa başlamak için \"set off\" ya da \"leave\" kullanılır."],
            MinorIssues = ["Cümle küçük harfle başlamış: we → We", "Sonda nokta eksik."],
            Tip = "Yolculuğa başlamayı anlatırken \"set off / set out\" kalıplarını kullan.",
            Vocabulary =
            [
                Words[0],
                new("first thing in the morning", "sabah ilk iş olarak", "I'll call you first thing in the morning. (Sabah ilk iş seni ararım.)", "tamlama"),
            ],
        };

        await WpfHost.RunAsync(async () =>
        {
            var window = new MainWindow(session, new GeminiClient(new HttpClient(new FakeHttpHandler())))
            {
                ShowDialogHandler = _ => true,
            };
            var practice = window.Practice;
            practice.AnimateResults = true;
            var frame = 0;

            void Shot(int repeat = 1)
            {
                for (var r = 0; r < repeat; r++)
                {
                    Save(window.Frame, Path.Combine(frames, $"f{frame++:D4}.png"), 1000, 720);
                }
            }

            // 1) Boş ekran
            Shot(repeat: 12);
            Save(window.Frame, Path.Combine(output, "01-calisma.png"), 1000, 720);

            // 2) Çeviri yazılıyor
            for (var i = 2; i <= typed.Length; i += 2)
            {
                practice.InputBox.Text = typed[..i];
                Shot();
            }

            practice.InputBox.Text = typed;
            Shot(repeat: 8);

            // 3) Gemini bekleniyor: yalnızca dönen noktalar
            var gate = new TaskCompletionSource();
            evaluator.Gate = gate.Task;
            var sending = practice.SendAsync();
            foreach (var dots in new[] { ".", "..", "...", ".", "..", "...", ".", "..", "..." })
            {
                practice.ThinkingText.Text = dots;
                Shot(repeat: 3);
            }

            // 4) Cevap harf harf yazılıyor
            gate.SetResult();
            await sending;
            var steps = 0;
            while (practice.StepTyping())
            {
                if (++steps % 3 == 0)
                {
                    Shot();
                }
            }

            Shot(repeat: 40);
            Save(window.Frame, Path.Combine(output, "02-sonuc.png"), 1000, 960);

            // Ek pencereler (sabit görüntüler)
            window.OpenVocabulary();
            SavePage(window.Vocabulary, "Sözlük", Path.Combine(output, "03-sozluk.png"), 720, 640);
            window.OpenProgress();
            SavePage(window.Progress, "Hedef", Path.Combine(output, "04-hedef.png"), 660, 560);
            window.OpenSettings(SettingsSection.DataFile);
            window.Settings.ToggleHelp(SettingsSection.DataFile, show: true);
            SavePage(window.Settings, "Ayarlar", Path.Combine(output, "05-yardim.png"), 900, 760);
        });
    }

    private static void SavePage(UserControl page, string title, string path, double width, double height)
    {
        var dialog = new PageWindow(title, page, width, height);
        try
        {
            Save(dialog.Frame, path, width, height);
        }
        finally
        {
            dialog.Detach();
        }
    }

    private static void Save(FrameworkElement root, string path, double width, double height)
    {
        WpfHost.Layout(root, width, height);
        var bitmap = new RenderTargetBitmap((int)(width * Scale), (int)(height * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}

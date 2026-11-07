using System.Net;
using System.Text.Json;
using System.Windows;
using Lingora.Core;
using Lingora.Core.Corpus;
using Lingora.Core.Gemini;
using Lingora.Core.Models;
using Lingora.Core.Storage;
using Lingora.Desktop;
using Lingora.Desktop.Pages;

namespace Lingora.Tests;

public class SequentialOrderTests
{
    private static PracticeSession Open(TempDir dir, FakeEvaluator? evaluator = null) =>
        new(new AppPaths(dir.Path), evaluator ?? new FakeEvaluator(),
            new FixedTimeProvider(FixedTimeProvider.Local(2026, 11, 7, 10)), new ApiKeyStore(_ => null), new Random(5));

    private static PracticeSession OpenWithCorpus(TempDir dir)
    {
        var session = Open(dir);
        session.ImportCorpus(dir.Write("tur.txt", Samples.TatoebaLines));
        session.SetApiKey("AIzaTEST-order");
        return session;
    }

    [Fact]
    public void DefaultIsSequentialFromFirstLine()
    {
        using var dir = new TempDir();
        var session = OpenWithCorpus(dir);

        Assert.Equal(SentenceOrder.Sequential, session.Settings.Order);
        Assert.Equal(0, session.Current!.CorpusIndex);
        Assert.Equal(1, session.CurrentLine);
        Assert.Equal("Selam.", session.Current.Source);

        Assert.Equal(1, session.NextSentence()!.CorpusIndex);
        Assert.Equal(2, session.NextSentence()!.CorpusIndex);
        Assert.Equal(2, session.Settings.Position);
    }

    [Fact]
    public async Task PositionSurvivesRestart()
    {
        using var dir = new TempDir();
        var session = OpenWithCorpus(dir);
        session.NextSentence();
        session.NextSentence();
        await session.SubmitAsync("cevap");
        session.NextSentence();
        var shown = session.Current!.Source;

        var reopened = Open(dir);

        Assert.Equal(3, reopened.Current!.CorpusIndex);
        Assert.Equal(shown, reopened.Current.Source);
        Assert.Equal(4, reopened.CurrentLine);
    }

    [Fact]
    public void OldSettingsFileWithoutOrderStartsSequentialAtTop()
    {
        using var dir = new TempDir();
        dir.Write("settings.json", "{\"direction\":\"TurkishToEnglish\",\"dailyGoal\":20}");
        File.WriteAllText(dir.File("corpus.tsv"), "Hi.\tSelam.\nRun!\tKoş!\n");

        var session = Open(dir);

        Assert.Equal(SentenceOrder.Sequential, session.Settings.Order);
        Assert.Equal(0, session.Current!.CorpusIndex);
    }

    [Fact]
    public void StartFromLineSkipsAheadAndValidates()
    {
        using var dir = new TempDir();
        var session = OpenWithCorpus(dir);

        session.StartFromLine(8);
        Assert.Equal(7, session.Current!.CorpusIndex);
        Assert.Equal(7, Open(dir).Current!.CorpusIndex);

        Assert.Throws<ArgumentOutOfRangeException>(() => session.StartFromLine(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.StartFromLine(12));
        Assert.Equal(7, session.Current!.CorpusIndex);
    }

    [Fact]
    public void StartFromLineWithoutCorpusFails()
    {
        using var dir = new TempDir();
        Assert.Throws<InvalidOperationException>(() => Open(dir).StartFromLine(1));
    }

    [Fact]
    public void WrapsToTopAtEndOfFile()
    {
        using var dir = new TempDir();
        var session = OpenWithCorpus(dir);
        session.StartFromLine(11);

        session.NextSentence();

        Assert.Equal(0, session.Current!.CorpusIndex);
        Assert.Contains("baştan", session.Notice);
    }

    [Fact]
    public void DirectionChangeKeepsSameLine()
    {
        using var dir = new TempDir();
        var session = OpenWithCorpus(dir);
        session.StartFromLine(4);

        session.SetDirection(TranslationDirection.EnglishToTurkish);

        Assert.Equal(3, session.Current!.CorpusIndex);
        Assert.Equal("Run!", session.Current.Source);
    }

    [Fact]
    public void DifficultySkipsForwardToMatchingSentence()
    {
        using var dir = new TempDir();
        var session = OpenWithCorpus(dir);

        session.UpdateGeneralSettings(20, Difficulty.Hard, "gemini-3.5-flash-lite");
        Assert.Equal(6, session.Current!.CorpusIndex);

        session.NextSentence();
        Assert.Equal(7, session.Current!.CorpusIndex);

        session.UpdateGeneralSettings(20, Difficulty.Easy, "gemini-3.5-flash-lite");
        Assert.Equal(0, session.Current!.CorpusIndex);   // 7'den sonra kolay yok → başa döner
    }

    [Fact]
    public void RandomModeDoesNotLoseSequentialPosition()
    {
        using var dir = new TempDir();
        var session = OpenWithCorpus(dir);
        session.StartFromLine(5);

        session.SetOrder(SentenceOrder.Random);
        for (var i = 0; i < 5; i++)
        {
            session.NextSentence();
        }

        Assert.Equal(4, session.Settings.Position);
        session.SetOrder(SentenceOrder.Sequential);
        Assert.Equal(4, session.Current!.CorpusIndex);
    }

    [Fact]
    public void ImportingNewFileStartsFromTop()
    {
        using var dir = new TempDir();
        var session = OpenWithCorpus(dir);
        session.StartFromLine(9);

        session.ImportCorpus(dir.Write("yeni.txt", "Go.\tGit.\nCome.\tGel.\n"));

        Assert.Equal(0, session.Current!.CorpusIndex);
        Assert.Equal("Git.", session.Current.Source);
    }

    [Fact]
    public void PickerNextInOrderHonoursDifficultyAndWraps()
    {
        var corpus = SentenceCorpus.FromPairs(
        [
            new SentencePair("Hi.", "Selam."),
            new SentencePair("I really want to go there tomorrow morning early.", "Uzun."),
            new SentencePair("Go.", "Git."),
        ]);
        var picker = new SentencePicker(corpus);

        Assert.Equal(1, picker.NextInOrder(TranslationDirection.TurkishToEnglish, Difficulty.Hard, 0)!.Item.CorpusIndex);
        var wrapped = picker.NextInOrder(TranslationDirection.TurkishToEnglish, Difficulty.Hard, 2)!;
        Assert.Equal(1, wrapped.Item.CorpusIndex);
        Assert.True(wrapped.Wrapped);
        var fallback = picker.NextInOrder(TranslationDirection.TurkishToEnglish, Difficulty.Medium, 2)!;
        Assert.True(fallback.UsedDifficultyFallback);
        Assert.Equal(2, fallback.Item.CorpusIndex);
    }
}

public class MinorIssueTests
{
    [Fact]
    public void PromptSaysMinorIssuesDoNotReduceScore()
    {
        var instruction = PromptBuilder.SystemInstruction;

        Assert.Contains("KÜÇÜK HATALAR PUANI DÜŞÜRMEZ", instruction);
        Assert.Contains("gitmak", instruction);
        Assert.Contains("küçük harfle başlaması", instruction);
        Assert.Contains("minorIssues", instruction);
        Assert.Contains("isCorrect'i false yapmaz", instruction);
        Assert.Contains("minorIssues", PromptBuilder.ResponseFields);

        using var schema = JsonDocument.Parse(PromptBuilder.BuildResponseSchema().ToJsonString());
        Assert.Equal("ARRAY", schema.RootElement.GetProperty("properties").GetProperty("minorIssues").GetProperty("type").GetString());
    }

    [Fact]
    public void ParsesMinorIssuesSeparatelyFromMistakes()
    {
        var evaluation = GeminiClient.ParseEvaluationJson(
            "{\"score\":96,\"isCorrect\":true,\"mistakes\":[],\"minorIssues\":[\"gitmak → gitmek\",\"tom → Tom\"]}");

        Assert.Equal(96, evaluation.Score);
        Assert.Empty(evaluation.Mistakes);
        Assert.Equal(["gitmak → gitmek", "tom → Tom"], evaluation.MinorIssues);
    }

    [Fact]
    public void FormatterShowsMinorIssuesAsNotAffectingScore()
    {
        var item = new PracticeItem(TranslationDirection.EnglishToTurkish, "I want to go.", ["Gitmek istiyorum."], 0);
        var text = EvaluationFormatter.Format(item, "gitmak istiyorum", new Evaluation
        {
            Score = 95,
            IsCorrect = true,
            MinorIssues = ["gitmak → gitmek"],
        });

        Assert.Contains("Küçük hatalar (puanı etkilemez):", text);
        Assert.Contains("gitmak → gitmek", text);
        Assert.Contains("Hata bulunmadı.", text);
    }
}

[Collection("WPF")]
public class OrderAndMinorIssueUiTests
{
    private static (TempDir Dir, PracticeSession Session, FakeEvaluator Evaluator) Setup()
    {
        var dir = new TempDir();
        var evaluator = new FakeEvaluator();
        var session = new PracticeSession(new AppPaths(dir.Path), evaluator,
            new FixedTimeProvider(FixedTimeProvider.Local(2026, 11, 7, 10)), new ApiKeyStore(_ => null));
        session.ImportCorpus(dir.Write("tur.txt", Samples.TatoebaLines));
        session.SetApiKey("AIzaTEST-ui-order");
        return (dir, session, evaluator);
    }

    private static MainWindow Window(PracticeSession session)
    {
        var window = new MainWindow(session, new GeminiClient(new HttpClient(new FakeHttpHandler())));
        window.Practice.AnimateResults = false;
        window.ShowDialogHandler = _ => true;
        return window;
    }

    [Fact]
    public async Task MinorIssuesAreShownSeparatelyAndScoreStaysHigh()
    {
        var (dir, session, evaluator) = Setup();
        using var _ = dir;
        session.SetDirection(TranslationDirection.EnglishToTurkish);
        evaluator.Handler = (_, _) => new Evaluation
        {
            Score = 97,
            IsCorrect = true,
            CorrectedTranslation = "Selam.",
            MinorIssues = ["selam → Selam: cümle büyük harfle başlar", "nokta eksik"],
        };

        await WpfHost.RunAsync(async () =>
        {
            var window = Window(session);
            window.Practice.InputBox.Text = "selam";
            await window.Practice.SendAsync();

            Assert.Equal("97 / 100   ·   Doğru", window.Practice.ResultHeadline.Text);
            var fields = window.Practice.ResultBody.Children.OfType<System.Windows.Controls.StackPanel>()
                .ToDictionary(p => ((System.Windows.Controls.TextBlock)p.Children[0]).Text, p => ((System.Windows.Controls.TextBox)p.Children[1]).Text);
            Assert.Equal("Hata bulunmadı.", fields["HATALAR"]);
            Assert.Contains("selam → Selam", fields["KÜÇÜK HATALAR (PUANI ETKİLEMEZ)"]);
            WpfHost.Screenshot(window.Root, "11-kucuk-hatalar", height: 820);
        });
    }

    [Fact]
    public void SettingsStartLineAndOrderAndReopenKeepPlaceAndDictionary()
    {
        var (dir, session, _) = Setup();
        using var __ = dir;
        session.Vocabulary.Merge([new VocabularyItem("be supposed to", "-mesi gerekmek", "", "kalıp")], "x", DateTimeOffset.Now);

        WpfHost.Run(() =>
        {
            var window = Window(session);
            var settings = window.Settings;
            window.OpenSettings(SettingsSection.General);
            Assert.True(settings.OrderSequential.IsChecked);
            Assert.Contains("1. satırdasınız (toplam 11)", settings.PositionInfo.Text);

            settings.StartFromLine("abc");
            Assert.Contains("satır numarası", settings.StartLineStatus.Text);

            settings.StartFromLine("999");
            Assert.Contains("1 ile 11 arasında", settings.StartLineStatus.Text);

            settings.StartFromLine("8");
            Assert.Equal("8. satırdan devam ediliyor.", settings.StartLineStatus.Text);
            Assert.Equal(7, session.Current!.CorpusIndex);
            Assert.Equal("TÜRKÇE   ·   #8", window.Practice.SourceCaption.Text);
            Assert.Equal(session.Current.Source, window.Practice.SourceText.Text);
            WpfHost.ScreenshotPage(settings, "12-ayarlar-sira", 900, 760);

            settings.SaveGeneral("20", Difficulty.All, "gemini-3.5-flash-lite", SentenceOrder.Random);
            Assert.Equal(SentenceOrder.Random, session.Settings.Order);
            settings.SaveGeneral("20", Difficulty.All, "gemini-3.5-flash-lite", SentenceOrder.Sequential);
            Assert.Equal(7, session.Current!.CorpusIndex);
        });

        // Uygulamayı kapatıp açmak: yeni oturum aynı yerden ve aynı sözlükle başlar.
        var reopened = new PracticeSession(new AppPaths(dir.Path), new FakeEvaluator(), apiKeyStore: new ApiKeyStore(_ => null));
        WpfHost.Run(() =>
        {
            var window = Window(reopened);
            Assert.Equal("TÜRKÇE   ·   #8", window.Practice.SourceCaption.Text);
            Assert.Equal("1", window.VocabularyCount.Text);
            Assert.Equal(1, reopened.Vocabulary.Count);
        });
    }
}

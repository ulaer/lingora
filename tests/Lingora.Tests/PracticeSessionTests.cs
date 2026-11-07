using System.Net;
using Lingora.Core;
using Lingora.Core.Gemini;
using Lingora.Core.Models;
using Lingora.Core.Storage;

namespace Lingora.Tests;

public class PracticeSessionTests
{
    private const string FakeKey = "AIzaTEST-session-key-000";

    private static PracticeSession Create(TempDir dir, FakeEvaluator evaluator, FixedTimeProvider? time = null, Func<string, string?>? env = null) =>
        new(new AppPaths(dir.Path), evaluator, time ?? new FixedTimeProvider(FixedTimeProvider.Local(2026, 11, 6, 10)), new ApiKeyStore(env ?? (_ => null)), new Random(42));

    private static PracticeSession CreateReady(TempDir dir, FakeEvaluator evaluator, FixedTimeProvider? time = null)
    {
        var session = Create(dir, evaluator, time);
        session.ImportCorpus(dir.Write("tur.txt", Samples.TatoebaLines));
        session.SetApiKey(FakeKey);
        return session;
    }

    [Fact]
    public async Task WithoutCorpusAsksUserToLoadFile()
    {
        using var dir = new TempDir();
        var evaluator = new FakeEvaluator();
        var session = Create(dir, evaluator);

        Assert.Equal(SessionState.NoCorpus, session.State);
        Assert.Null(session.Current);

        var outcome = await session.SubmitAsync("Hello");

        Assert.False(outcome.Success);
        Assert.Contains("Veri Dosyası", outcome.Text);
        Assert.Empty(evaluator.Calls);
    }

    [Fact]
    public void ImportSelectsFirstSentence()
    {
        using var dir = new TempDir();
        var session = Create(dir, new FakeEvaluator());

        var result = session.ImportCorpus(dir.Write("tur.txt", Samples.TatoebaLines));

        Assert.Equal(11, result.Imported);
        Assert.Equal(11, session.Corpus.Count);
        Assert.Equal(SessionState.AwaitingAnswer, session.State);
        Assert.NotNull(session.Current);
        Assert.Equal(TranslationDirection.TurkishToEnglish, session.Current!.Direction);
        Assert.NotEmpty(session.Current.References);
    }

    [Fact]
    public async Task EmptyTranslationIsRejected()
    {
        using var dir = new TempDir();
        var evaluator = new FakeEvaluator();
        var session = CreateReady(dir, evaluator);

        var outcome = await session.SubmitAsync("   ");

        Assert.False(outcome.Success);
        Assert.Contains("çevirinizi yazın", outcome.Text);
        Assert.Empty(evaluator.Calls);
    }

    [Fact]
    public async Task MissingApiKeyIsReported()
    {
        using var dir = new TempDir();
        var evaluator = new FakeEvaluator();
        var session = Create(dir, evaluator);
        session.ImportCorpus(dir.Write("tur.txt", Samples.TatoebaLines));

        var outcome = await session.SubmitAsync("Run!");

        Assert.False(outcome.Success);
        Assert.Contains("API Anahtarı", outcome.Text);
        Assert.Empty(evaluator.Calls);
    }

    [Fact]
    public async Task SuccessfulSubmitIsEvaluatedRecordedAndCounted()
    {
        using var dir = new TempDir();
        var evaluator = new FakeEvaluator();
        var session = CreateReady(dir, evaluator);
        var item = session.Current!;

        var outcome = await session.SubmitAsync("  My translation.  ");

        Assert.True(outcome.Success);
        Assert.Contains("Puan: 90/100", outcome.Text);
        var call = Assert.Single(evaluator.Calls);
        Assert.Equal(item, call.Item);
        Assert.Equal("My translation.", call.Text);
        Assert.Equal(FakeKey, call.ApiKey);
        Assert.Equal("gemini-flash-latest", call.Model);

        Assert.Equal(SessionState.ShowingResult, session.State);
        Assert.Equal(1, session.TodayCount);
        Assert.Contains("Bugün 1/20", session.StatusLine);
        var line = Assert.Single(File.ReadAllLines(dir.File("attempts.jsonl")));
        Assert.Contains("My translation.", line);
        Assert.Contains("\"dailyGoal\":20", line);
    }

    [Fact]
    public async Task GeminiErrorIsShownAndNotCounted()
    {
        using var dir = new TempDir();
        var evaluator = new FakeEvaluator { Handler = (_, _) => throw new GeminiException("API anahtarı geçersiz.") };
        var session = CreateReady(dir, evaluator);

        var outcome = await session.SubmitAsync("Run!");

        Assert.False(outcome.Success);
        Assert.Equal("API anahtarı geçersiz.", outcome.Text);
        Assert.Equal(SessionState.AwaitingAnswer, session.State);
        Assert.Equal(0, session.TodayCount);
        Assert.False(File.Exists(dir.File("attempts.jsonl")));
    }

    [Fact]
    public async Task EndToEndWithRealGeminiClientAndFakeHttp()
    {
        using var dir = new TempDir();
        var handler = new FakeHttpHandler().Respond(HttpStatusCode.OK, Samples.GeminiSuccess(Samples.EvaluationJson));
        var client = new GeminiClient(new HttpClient(handler));
        var session = new PracticeSession(new AppPaths(dir.Path), client, new FixedTimeProvider(FixedTimeProvider.Local(2026, 11, 6)), new ApiKeyStore(_ => null));
        session.ImportCorpus(dir.Write("tur.txt", Samples.TatoebaLines));
        session.SetApiKey(FakeKey);

        var outcome = await session.SubmitAsync("Some answer");

        Assert.True(outcome.Success, outcome.Text);
        Assert.Contains("Puan: 85/100", outcome.Text);
        Assert.Contains("İpucu: Dolaylı anlatımda zaman bir geri kayar.", outcome.Text);
        var request = Assert.Single(handler.Requests);
        Assert.StartsWith("https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-latest:generateContent", request.Request.RequestUri!.ToString());
        Assert.Contains(session.Current!.Source, request.Body);
        Assert.Contains("Some answer", request.Body);
    }

    [Fact]
    public async Task NextSentenceAfterResultReturnsToAwaitingAnswer()
    {
        using var dir = new TempDir();
        var session = CreateReady(dir, new FakeEvaluator());
        var first = session.Current!;
        await session.SubmitAsync("x");

        session.NextSentence();

        Assert.Equal(SessionState.AwaitingAnswer, session.State);
        Assert.NotEqual(first.CorpusIndex, session.Current!.CorpusIndex);
    }

    [Fact]
    public void SettingsChangesArePersisted()
    {
        using var dir = new TempDir();
        var session = CreateReady(dir, new FakeEvaluator());

        session.SetDirection(TranslationDirection.EnglishToTurkish);
        session.UpdateGeneralSettings(7, Difficulty.Easy, "models/gemini-2.5-flash");

        Assert.Equal(TranslationDirection.EnglishToTurkish, session.Current!.Direction);
        Assert.InRange(session.Corpus.GetWordCount(session.Current.CorpusIndex), 1, 4);

        var reopened = Create(dir, new FakeEvaluator());
        Assert.Equal(TranslationDirection.EnglishToTurkish, reopened.Settings.Direction);
        Assert.Equal(7, reopened.Settings.DailyGoal);
        Assert.Equal(Difficulty.Easy, reopened.Settings.Difficulty);
        Assert.Equal("gemini-2.5-flash", reopened.Settings.Model);
        Assert.Equal(FakeKey, reopened.GetApiKey().Key);
        Assert.Equal(11, reopened.Corpus.Count);
    }

    [Fact]
    public void MediumDifficultyWithoutSentencesShowsNotice()
    {
        using var dir = new TempDir();
        var session = CreateReady(dir, new FakeEvaluator());

        session.UpdateGeneralSettings(20, Difficulty.Medium, "gemini-flash-latest");

        Assert.NotNull(session.Current);
        Assert.Contains("Orta", session.Notice);
    }

    [Fact]
    public async Task ProgressSurvivesRestartAndDayChange()
    {
        using var dir = new TempDir();
        var time = new FixedTimeProvider(FixedTimeProvider.Local(2026, 11, 5, 21));
        var session = CreateReady(dir, new FakeEvaluator(), time);
        for (var i = 0; i < 3; i++)
        {
            await session.SubmitAsync("cevap " + i);
            session.NextSentence();
        }

        time.Now = FixedTimeProvider.Local(2026, 11, 6, 9);
        var reopened = Create(dir, new FakeEvaluator(), time);
        var progress = reopened.GetProgress();

        Assert.Equal(3, progress.TotalSentences);
        Assert.Equal(0, progress.TodayCount);
        Assert.Equal(3, progress.RecentDays[1].Count);
        Assert.Equal(0, reopened.TodayCount);
    }

    [Fact]
    public void EnvironmentKeyIsUsedWhenNoneSaved()
    {
        using var dir = new TempDir();
        var session = Create(dir, new FakeEvaluator(), env: name => name == "GEMINI_API_KEY" ? "env-key" : null);

        Assert.Equal(ApiKeySource.Environment, session.GetApiKey().Source);
    }
}

public class DiagnosticsTests
{
    [Fact]
    public async Task FreshInstallHasOnlyWarnings()
    {
        using var dir = new TempDir();
        var client = new GeminiClient(new HttpClient(new FakeHttpHandler()));

        var lines = await Diagnostics.RunAsync(new AppPaths(dir.Path), client, live: false, new ApiKeyStore(_ => null));

        Assert.DoesNotContain(lines, l => l.Level == CheckLevel.Error);
        Assert.Contains(lines, l => l.Level == CheckLevel.Warning && l.Message.Contains("Cümle dosyası"));
        Assert.Contains(lines, l => l.Level == CheckLevel.Warning && l.Message.Contains("API anahtarı girilmemiş"));
    }

    [Fact]
    public async Task LiveCheckUsesKeyWithoutPrintingIt()
    {
        using var dir = new TempDir();
        var paths = new AppPaths(dir.Path);
        var keyStore = new ApiKeyStore(_ => null);
        var session = new PracticeSession(paths, new FakeEvaluator(), apiKeyStore: keyStore);
        session.ImportCorpus(dir.Write("tur.txt", Samples.TatoebaLines));
        session.SetApiKey("AIzaTEST-diagnostics-key-1234");

        var handler = new FakeHttpHandler()
            .Respond(HttpStatusCode.OK, "{\"models\":[{\"name\":\"models/gemini-flash-latest\",\"supportedGenerationMethods\":[\"generateContent\"]}]}")
            .Respond(HttpStatusCode.OK, Samples.GeminiSuccess(Samples.EvaluationJson));
        var lines = await Diagnostics.RunAsync(paths, new GeminiClient(new HttpClient(handler)), live: true, keyStore);
        var report = Diagnostics.Format(lines);

        Assert.DoesNotContain(lines, l => l.Level == CheckLevel.Error);
        Assert.Contains("11 cümle çifti", report);
        Assert.Contains("Gemini bağlantısı başarılı", report);
        Assert.Contains("puan 85/100", report);
        Assert.DoesNotContain("AIzaTEST-diagnostics-key-1234", report);
        Assert.Contains("AIza…1234", report);
    }

    [Fact]
    public async Task LiveCheckReportsInvalidKeyAsError()
    {
        using var dir = new TempDir();
        var handler = new FakeHttpHandler().Respond(HttpStatusCode.BadRequest, "{\"error\":{\"code\":400,\"message\":\"API key not valid.\",\"status\":\"INVALID_ARGUMENT\"}}");

        var lines = await Diagnostics.RunAsync(new AppPaths(dir.Path), new GeminiClient(new HttpClient(handler)), live: true, new ApiKeyStore(_ => "bad-key"));

        Assert.Contains(lines, l => l.Level == CheckLevel.Error && l.Message.Contains("API anahtarı geçersiz"));
    }
}

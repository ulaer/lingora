using System.Net;
using System.Text.Json;
using Lingora.Core;
using Lingora.Core.Gemini;
using Lingora.Core.Journal;
using Lingora.Core.Models;
using Lingora.Core.Storage;

namespace Lingora.Tests;

public class JournalStoreTests
{
    private static JournalStore Create(TempDir dir) =>
        new(dir.File("journal"), dir.File("lessons"), FixedTimeProvider.Istanbul);

    private static JournalEntry Entry(DateTimeOffset at, params string[] mistakes) => new()
    {
        Timestamp = at,
        Source = "Selam.",
        UserTranslation = "Hi",
        Mistakes = mistakes.ToList(),
        Tip = "İpucu",
    };

    [Fact]
    public void EntriesAreGroupedByLocalDay()
    {
        using var dir = new TempDir();
        var store = Create(dir);

        // 6 Kasım 23:30 İstanbul ve 7 Kasım 00:10 İstanbul (UTC'de ikisi de 6 Kasım).
        store.Append(Entry(FixedTimeProvider.Local(2026, 11, 6, 23, 30), "a → b: neden"));
        store.Append(Entry(FixedTimeProvider.Local(2026, 11, 7, 0, 10)));
        store.Append(Entry(FixedTimeProvider.Local(2026, 11, 7, 9, 0), "x → y: neden", "p → q: neden"));

        Assert.Single(store.LoadDay(new DateOnly(2026, 11, 6)));
        Assert.Equal(2, store.LoadDay(new DateOnly(2026, 11, 7)).Count);
        Assert.True(File.Exists(dir.File(Path.Combine("journal", "2026-11-07.json"))));

        var days = store.Days();
        Assert.Equal([new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 6)], days.Select(d => d.Date));
        Assert.Equal(2, days[0].Entries);
        Assert.Equal(2, days[0].Mistakes);
        Assert.Equal(2, days[0].Tips);
        Assert.False(days[0].HasLesson);
    }

    [Fact]
    public void UpdateReplacesEntryAndKeepsExplanations()
    {
        using var dir = new TempDir();
        var store = Create(dir);
        var entry = Entry(FixedTimeProvider.Local(2026, 11, 7, 10), "go out → set off: anlam");
        store.Append(entry);

        entry.Explanations.Add(new ItemExplanation { Kind = FeedbackKind.Mistake, Index = 0, Item = entry.Mistakes[0], Text = "Detay" });
        store.Update(entry);

        var loaded = Assert.Single(store.LoadDay(new DateOnly(2026, 11, 7)));
        Assert.Equal(entry.Id, loaded.Id);
        Assert.Equal("Detay", loaded.FindExplanation(FeedbackKind.Mistake, 0)!.Text);
        Assert.Equal(1, store.Summarize(new DateOnly(2026, 11, 7)).Explanations);
    }

    [Fact]
    public void CorruptDayFileIsBackedUp()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File("journal"));
        dir.Write(Path.Combine("journal", "2026-11-07.json"), "{ bozuk");
        var store = Create(dir);

        Assert.Empty(store.LoadDay(new DateOnly(2026, 11, 7)));
        Assert.Single(store.Warnings);
        Assert.Single(Directory.GetFiles(dir.File("journal"), "2026-11-07.json.bozuk-*"));
    }

    [Fact]
    public void LessonStoreRoundTripsAndMarksDay()
    {
        using var dir = new TempDir();
        var lessons = new LessonStore(dir.File("lessons"));
        var date = new DateOnly(2026, 11, 5);
        lessons.Save(new LessonNote { Date = date, Title = "Başlık", Topics = [new() { Title = "Konu", Explanation = "Açıklama", Examples = ["Ex."] }] });

        var loaded = lessons.Load(date)!;
        Assert.Equal("Başlık", loaded.Title);
        Assert.Equal("Konu", Assert.Single(loaded.Topics).Title);

        // Yalnızca ders notu olan gün de listede görünür.
        var day = Assert.Single(Create(dir).Days());
        Assert.Equal(date, day.Date);
        Assert.True(day.HasLesson);
        Assert.Equal(0, day.Entries);
    }
}

public class StudyAssistantSessionTests
{
    private static PracticeSession Create(TempDir dir, FakeEvaluator evaluator, FixedTimeProvider? clock = null, bool withKey = true)
    {
        var session = new PracticeSession(
            new AppPaths(dir.Path),
            evaluator,
            clock ?? new FixedTimeProvider(FixedTimeProvider.Local(2026, 11, 7, 20)),
            new ApiKeyStore(_ => null),
            new Random(1));
        if (!File.Exists(dir.File("corpus.tsv")))
        {
            session.ImportCorpus(dir.Write("tur.txt", Samples.TatoebaLines));
        }

        if (withKey)
        {
            session.SetApiKey("AIzaTEST-study-0000");
        }

        return session;
    }

    private static FakeEvaluator EvaluatorWithMistakes() => new()
    {
        Handler = (_, _) => new Evaluation
        {
            Score = 72,
            IsCorrect = false,
            CorrectedTranslation = "Tom said he didn't know what Mary was supposed to do.",
            NaturalTranslation = "Tom said he had no idea what Mary was supposed to do.",
            Mistakes = ["must → was supposed to: dolaylı anlatımda zaman geçmişe kayar", "know → knew: zaman uyumu"],
            MinorIssues = ["tom → Tom"],
            Tip = "Dolaylı anlatımda fiil bir zaman geri gider.",
        },
    };

    [Fact]
    public async Task SubmitWritesMistakesTipsAndMinorIssuesToTodaysJournal()
    {
        using var dir = new TempDir();
        var session = Create(dir, EvaluatorWithMistakes());

        var outcome = await session.SubmitAsync("tom said he didn't know what Mary must do");

        Assert.NotNull(outcome.Entry);
        var entry = Assert.Single(session.Journal.LoadDay(session.Today));
        Assert.Equal(outcome.Entry!.Id, entry.Id);
        Assert.Equal(2, entry.Mistakes.Count);
        Assert.Equal("tom → Tom", Assert.Single(entry.MinorIssues));
        Assert.StartsWith("Dolaylı", entry.Tip);
        Assert.Equal(72, entry.Score);
    }

    [Fact]
    public async Task UnstructuredAnswerIsNotJournaledAndFailedCallsAreNot()
    {
        using var dir = new TempDir();
        var evaluator = new FakeEvaluator { Handler = (_, _) => new Evaluation { RawText = "serbest metin" } };
        var session = Create(dir, evaluator);

        var outcome = await session.SubmitAsync("Hello");

        Assert.True(outcome.Success);
        Assert.Null(outcome.Entry);
        Assert.Empty(session.Journal.LoadDay(session.Today));
    }

    [Fact]
    public async Task ExplainCallsAiOnceThenServesSavedExplanation()
    {
        using var dir = new TempDir();
        var evaluator = EvaluatorWithMistakes();
        var session = Create(dir, evaluator);
        var entry = (await session.SubmitAsync("Tom said he didn't know what Mary must do")).Entry!;

        var first = await session.ExplainAsync(entry, FeedbackKind.Mistake, 1);
        var again = await session.ExplainAsync(entry, FeedbackKind.Mistake, 1);
        var tip = await session.ExplainAsync(entry, FeedbackKind.Tip, 0);

        Assert.True(first.Success);
        Assert.False(first.FromCache);
        Assert.Contains("know → knew", first.Text);
        Assert.True(again.FromCache);
        Assert.Equal(first.Text, again.Text);
        Assert.True(tip.Success);
        Assert.Equal(2, evaluator.ExplainCalls.Count);

        var request = evaluator.ExplainCalls[0];
        Assert.Equal(FeedbackKind.Mistake, request.Kind);
        Assert.Equal("Tom said he didn't know what Mary must do", request.UserTranslation);
        Assert.Equal(FeedbackKind.Tip, evaluator.ExplainCalls[1].Kind);

        // Açıklamalar günlükte kalıcı: yeniden açılışta da AI çağrılmadan gelir.
        var reopened = Create(dir, new FakeEvaluator());
        var saved = Assert.Single(reopened.Journal.LoadDay(reopened.Today));
        Assert.Equal(2, saved.Explanations.Count);
        var cached = await reopened.ExplainAsync(saved, FeedbackKind.Mistake, 1);
        Assert.True(cached.FromCache);
    }

    [Fact]
    public async Task ExplainReportsInvalidItemAndMissingKeyWithoutCallingAi()
    {
        using var dir = new TempDir();
        var evaluator = EvaluatorWithMistakes();
        var session = Create(dir, evaluator);
        var entry = (await session.SubmitAsync("Tom said")).Entry!;

        Assert.False((await session.ExplainAsync(entry, FeedbackKind.Mistake, 5)).Success);

        session.ClearApiKey();
        var noKey = await session.ExplainAsync(entry, FeedbackKind.Mistake, 0);
        Assert.False(noKey.Success);
        Assert.Contains("API anahtarı", noKey.Text);
        Assert.Empty(evaluator.ExplainCalls);
    }

    [Fact]
    public async Task LessonIsGeneratedOnceAndThenLoadedFromDisk()
    {
        using var dir = new TempDir();
        var evaluator = EvaluatorWithMistakes();
        var session = Create(dir, evaluator);
        await session.SubmitAsync("Tom said he didn't know what Mary must do");
        session.NextSentence();
        await session.SubmitAsync("Tom said he knows");

        var first = await session.GetOrCreateLessonAsync(session.Today);
        var second = await session.GetOrCreateLessonAsync(session.Today);

        Assert.True(first.Success);
        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Single(evaluator.LessonCalls);
        Assert.Equal(2, evaluator.LessonCalls[0].Entries);
        Assert.Equal(2, first.Note!.EntryCount);
        Assert.Equal(session.Today, first.Note.Date);
        Assert.True(session.Journal.Summarize(session.Today).HasLesson);

        // Kapat-aç: not dosyadan gelir, AI yeniden çağrılmaz.
        var reopenedEvaluator = new FakeEvaluator();
        var reopened = Create(dir, reopenedEvaluator);
        var loaded = await reopened.GetOrCreateLessonAsync(reopened.Today);
        Assert.True(loaded.FromCache);
        Assert.Equal(first.Note.Title, loaded.Note!.Title);
        Assert.Empty(reopenedEvaluator.LessonCalls);
    }

    [Fact]
    public async Task LessonForEmptyDayOrFailedAiIsNotSaved()
    {
        using var dir = new TempDir();
        var evaluator = EvaluatorWithMistakes();
        var session = Create(dir, evaluator);

        var empty = await session.GetOrCreateLessonAsync(new DateOnly(2026, 11, 1));
        Assert.False(empty.Success);
        Assert.Contains("kaydı yok", empty.Error);

        await session.SubmitAsync("Tom said");
        evaluator.LessonHandler = (_, _) => throw new GeminiException("Kota doldu (429).");
        var failed = await session.GetOrCreateLessonAsync(session.Today);
        Assert.False(failed.Success);
        Assert.Contains("429", failed.Error);
        Assert.Null(session.Lessons.Load(session.Today));
        Assert.Single(evaluator.LessonCalls);
    }
}

public class StudyAssistantGeminiTests
{
    private static (GeminiClient Client, FakeHttpHandler Handler) Create()
    {
        var handler = new FakeHttpHandler();
        return (new GeminiClient(new HttpClient(handler), "https://example.test/v1beta/"), handler);
    }

    private static readonly ExplainRequest Request = new(
        TranslationDirection.TurkishToEnglish,
        "Yarın sabah erkenden yola çıkmamız gerekiyor.",
        "we have to go out early tomorrow morning",
        "We have to set off early tomorrow morning.",
        FeedbackKind.Mistake,
        "go out → set off: anlam");

    [Fact]
    public async Task ExplainSendsPlainTextRequestWithKeyInHeader()
    {
        var (client, handler) = Create();
        handler.Respond(HttpStatusCode.OK, Samples.GeminiSuccess("\"Set off\" yolculuğa başlamak demektir.\n• We set off at dawn. (Şafakta yola çıktık.)"));

        var text = await client.ExplainAsync(Request, "AIzaTEST-explain", "gemini-flash-latest");

        Assert.StartsWith("\"Set off\"", text);
        var (request, body) = handler.Requests[0];
        Assert.DoesNotContain("AIzaTEST", request.RequestUri!.ToString());
        Assert.Equal("AIzaTEST-explain", request.Headers.GetValues("x-goog-api-key").Single());
        using var doc = JsonDocument.Parse(body!);
        Assert.Equal("text/plain", doc.RootElement.GetProperty("generationConfig").GetProperty("responseMimeType").GetString());
        var prompt = doc.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;
        Assert.Contains("Açıklanacak hata: go out → set off", prompt);
        Assert.Contains("<<<\nwe have to go out", prompt.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task LessonParsesStructuredJson()
    {
        var (client, handler) = Create();
        const string json = """
            {"title":"Yola çıkmak: set off","summary":"İyi gün.","topics":[{"title":"Phrasal verbs","explanation":"Açıklama","examples":["We set off. (Yola çıktık.)"]}],
             "mistakes":[{"wrong":"go out","right":"set off","why":"Anlam farkı"}],"practice":["Yarın yola çıkacağız."]}
            """;
        handler.Respond(HttpStatusCode.OK, Samples.GeminiSuccess(json));
        var entries = new List<JournalEntry> { new() { Source = "S", UserTranslation = "U", Mistakes = ["go out → set off: anlam"] } };

        var note = await client.CreateLessonAsync(new DateOnly(2026, 11, 7), entries, "AIzaTEST-lesson", "models/gemini-flash-latest");

        Assert.Equal("Yola çıkmak: set off", note.Title);
        Assert.Equal("Phrasal verbs", Assert.Single(note.Topics).Title);
        Assert.Equal("set off", Assert.Single(note.Mistakes).Right);
        Assert.Single(note.Practice);
        Assert.Equal(1, note.EntryCount);
        Assert.Equal("gemini-flash-latest", note.Model);

        using var doc = JsonDocument.Parse(handler.Requests[0].Body!);
        var config = doc.RootElement.GetProperty("generationConfig");
        Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString());
        var required = config.GetProperty("responseSchema").GetProperty("required").EnumerateArray().Select(e => e.GetString());
        Assert.Equal(StudyPrompts.LessonFields, required);
        var prompt = doc.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;
        Assert.Contains("Hata: go out → set off", prompt);
        Assert.Contains("7 Kasım 2026", prompt);
    }

    [Fact]
    public void LessonWithBadJsonGivesTurkishError()
    {
        var ex = Assert.Throws<GeminiException>(() => GeminiClient.ParseLesson(Samples.GeminiSuccess("bu json değil")));
        Assert.Contains("ders notu", ex.Message);
    }

    [Fact]
    public void LessonPromptIsCappedForVeryBusyDays()
    {
        var entries = Enumerable.Range(0, StudyPrompts.MaxLessonEntries + 20)
            .Select(i => new JournalEntry { Source = "S" + i, UserTranslation = "U" + i })
            .ToList();
        var prompt = StudyPrompts.BuildLessonPrompt(new DateOnly(2026, 11, 7), entries);
        Assert.Contains("#" + StudyPrompts.MaxLessonEntries + " ", prompt);
        Assert.DoesNotContain("#" + (StudyPrompts.MaxLessonEntries + 1) + " ", prompt);
    }
}

public class LessonPdfTests
{
    [Fact]
    public void ExportsReadableA4Pdf()
    {
        using var dir = new TempDir();
        var note = new FakeEvaluator().LessonHandler(new DateOnly(2026, 11, 7), []);
        note.Date = new DateOnly(2026, 11, 7);
        note.EntryCount = 12;
        note.Title = "Dolaylı anlatım: şimdi, geçmiş ve ğüşıöç";
        var path = dir.File(Path.Combine("pdf", LessonPdfExporter.SuggestedFileName(note)));

        LessonPdfExporter.Export(note, path);
        LessonPdfExporter.Export(note, path); // ikinci kez de çalışır (yazı tipi ayarı bir kez yapılır)

        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 2000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.EndsWith("Lingora-ders-notu-2026-11-07.pdf", path);
    }
}

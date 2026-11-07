using System.Text.Json;
using Lingora.Core;
using Lingora.Core.Gemini;
using Lingora.Core.Lexicon;
using Lingora.Core.Models;
using Lingora.Core.Storage;

namespace Lingora.Tests;

public class VocabularyBookTests
{
    private static readonly DateTimeOffset Day1 = FixedTimeProvider.Local(2026, 11, 6, 10);
    private static readonly DateTimeOffset Day2 = FixedTimeProvider.Local(2026, 11, 7, 10);

    private static VocabularyItem Word(string term, string meaning = "anlam", string example = "", string kind = "kalıp") =>
        new(term, meaning, example, kind);

    [Theory]
    [InlineData("be supposed to", "be supposed to")]
    [InlineData("To Be Supposed To!", "be supposed to")]
    [InlineData("  be   supposed to. ", "be supposed to")]
    [InlineData("the weather", "weather")]
    [InlineData("an apple", "apple")]
    [InlineData("Speak French", "speak french")]
    [InlineData("don’t mind", "don't mind")]
    [InlineData("well-known", "well-known")]
    [InlineData("to", "to")]
    [InlineData("", "")]
    public void NormalizeKeyMakesSimilarTermsEqual(string term, string expected)
    {
        Assert.Equal(expected, VocabularyBook.NormalizeKey(term));
    }

    [Fact]
    public void SimilarTermsAreStoredOnceAndCounted()
    {
        using var dir = new TempDir();
        var book = new VocabularyBook(dir.File("vocabulary.json"));

        var first = book.Merge([Word("be supposed to", "-mesi gerekmek", "You are supposed to wait. (Beklemen gerekiyor.)")], "cümle 1", Day1);
        var second = book.Merge([Word("To be supposed to.", "-mesi beklenmek", "He was supposed to call. (Aramalıydı.)")], "cümle 2", Day2);

        var entry = Assert.Single(book.Entries);
        Assert.Single(first.Added);
        Assert.Empty(first.Repeated);
        Assert.Empty(second.Added);
        Assert.Same(entry, Assert.Single(second.Repeated));

        Assert.Equal("be supposed to", entry.Term);
        Assert.Equal("-mesi gerekmek", entry.Meaning);
        Assert.Equal(2, entry.MistakeCount);
        Assert.Equal(Day1, entry.FirstSeen);
        Assert.Equal(Day2, entry.LastSeen);
        Assert.Equal("cümle 2", entry.SourceSentence);
        Assert.Equal(2, entry.Examples.Count);
        Assert.StartsWith("He was supposed to call.", entry.Examples[0]);
    }

    [Fact]
    public void DuplicatesInsideOneAnswerCountOnce()
    {
        using var dir = new TempDir();
        var book = new VocabularyBook(dir.File("vocabulary.json"));

        var result = book.Merge([Word("speak French"), Word("Speak french!"), Word("weather", kind: "kelime")], "x", Day1);

        Assert.Equal(2, book.Count);
        Assert.Equal(2, result.Added.Count);
        Assert.Equal(1, book.Find("SPEAK FRENCH")!.MistakeCount);
    }

    [Fact]
    public void ExamplesAreDeduplicatedAndCapped()
    {
        using var dir = new TempDir();
        var book = new VocabularyBook(dir.File("vocabulary.json"));
        for (var i = 0; i < 5; i++)
        {
            book.Merge([Word("rely on", example: $"Örnek {i}")], "x", Day1.AddMinutes(i));
        }

        book.Merge([Word("rely on", example: "örnek 4")], "x", Day2);

        var entry = book.Find("rely on")!;
        Assert.Equal(6, entry.MistakeCount);
        Assert.Equal(VocabularyBook.MaxExamples, entry.Examples.Count);
        Assert.Equal("Örnek 4", entry.Examples[0]);
    }

    [Fact]
    public void SurvivesRestart()
    {
        using var dir = new TempDir();
        var path = dir.File("vocabulary.json");
        new VocabularyBook(path).Merge([Word("get used to", "alışmak", "I got used to it. (Alıştım.)")], "x", Day1);

        var reopened = new VocabularyBook(path);
        reopened.Merge([Word("Get used to")], "y", Day2);

        var entry = Assert.Single(reopened.Entries);
        Assert.Equal("alışmak", entry.Meaning);
        Assert.Equal(2, entry.MistakeCount);
        Assert.Contains("alışmak", File.ReadAllText(path));
    }

    [Fact]
    public void HandEditedDuplicateEntriesAreMergedOnLoad()
    {
        using var dir = new TempDir();
        var path = dir.Write("vocabulary.json",
            "[{\"term\":\"look after\",\"meaning\":\"bakmak\",\"mistakeCount\":2,\"examples\":[\"a\"]},"
            + "{\"term\":\"Look after.\",\"meaning\":\"ilgilenmek\",\"mistakeCount\":3,\"examples\":[\"b\"]}]");

        var book = new VocabularyBook(path);

        var entry = Assert.Single(book.Entries);
        Assert.Equal(5, entry.MistakeCount);
        Assert.Equal(2, entry.Examples.Count);
    }

    [Fact]
    public void CorruptFileStartsEmptyWithBackup()
    {
        using var dir = new TempDir();
        var path = dir.Write("vocabulary.json", "{ bozuk");

        var book = new VocabularyBook(path);

        Assert.Equal(0, book.Count);
        Assert.NotNull(book.LoadWarning);
        Assert.True(File.Exists(book.LoadWarning));
    }

    [Fact]
    public void QuerySearchesTermMeaningAndExamplesAndSorts()
    {
        using var dir = new TempDir();
        var book = new VocabularyBook(dir.File("vocabulary.json"));
        book.Merge([Word("be supposed to", "-mesi gerekmek")], "x", Day1);
        book.Merge([Word("apologize", "özür dilemek", "She apologized to me.", "kelime")], "x", Day1.AddHours(1));
        book.Merge([Word("be supposed to")], "x", Day2);
        book.Merge([Word("cup of tea", "bir fincan çay", kind: "tamlama")], "x", Day1.AddHours(2));

        Assert.Equal(["be supposed to"], book.Query("GEREK", VocabularySort.Recent).Select(e => e.Term));
        Assert.Equal(["apologize"], book.Query("apologized to", VocabularySort.Recent).Select(e => e.Term));
        Assert.Equal(["cup of tea"], book.Query("çay", VocabularySort.Recent).Select(e => e.Term));
        Assert.Empty(book.Query("yok böyle", VocabularySort.Recent));

        Assert.Equal("be supposed to", book.Query(null, VocabularySort.Recent)[0].Term);
        Assert.Equal("be supposed to", book.Query(null, VocabularySort.MostMistakes)[0].Term);
        Assert.Equal(["apologize", "be supposed to", "cup of tea"], book.Query("", VocabularySort.Alphabetical).Select(e => e.Term));
    }

    [Fact]
    public void RemoveDeletesAndAllowsReAdding()
    {
        using var dir = new TempDir();
        var path = dir.File("vocabulary.json");
        var book = new VocabularyBook(path);
        book.Merge([Word("look forward to")], "x", Day1);

        Assert.True(book.Remove(book.Entries[0]));
        Assert.Equal(0, new VocabularyBook(path).Count);

        var again = book.Merge([Word("look forward to")], "x", Day2);
        Assert.Single(again.Added);
    }
}

public class VocabularyGeminiTests
{
    [Fact]
    public void SchemaAsksForVocabularyWithTermMeaningExampleType()
    {
        using var doc = JsonDocument.Parse(PromptBuilder.BuildResponseSchema().ToJsonString());
        var vocabulary = doc.RootElement.GetProperty("properties").GetProperty("vocabulary");

        Assert.Equal("ARRAY", vocabulary.GetProperty("type").GetString());
        var items = vocabulary.GetProperty("items");
        Assert.Equal(["term", "meaning", "example", "type"], items.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["kelime", "kalıp", "tamlama"], items.GetProperty("properties").GetProperty("type").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("vocabulary", PromptBuilder.SystemInstruction);
        Assert.Contains("kalıp", PromptBuilder.SystemInstruction);
    }

    [Fact]
    public void ParsesVocabularyAndNormalizesKinds()
    {
        var evaluation = GeminiClient.ParseEvaluationJson("""
            {"score":60,"isCorrect":false,"vocabulary":[
              {"term":"be supposed to","meaning":"-mesi gerekmek","example":"You are supposed to wait.","type":"kalıp"},
              {"term":"cup of tea","meaning":"bir fincan çay","example":"","type":"collocation"},
              {"term":"weather","meaning":"hava durumu","example":"","type":"noun"},
              {"term":"","meaning":"boş terim atlanır","type":"kelime"},
              {"term":"eksik anlam","meaning":"","type":"kelime"},
              "bozuk öğe"
            ]}
            """);

        Assert.Equal(3, evaluation.Vocabulary.Count);
        Assert.Equal(new VocabularyItem("be supposed to", "-mesi gerekmek", "You are supposed to wait.", "kalıp"), evaluation.Vocabulary[0]);
        Assert.Equal("tamlama", evaluation.Vocabulary[1].Kind);
        Assert.Equal("kelime", evaluation.Vocabulary[2].Kind);
    }

    [Fact]
    public void MissingVocabularyGivesEmptyList()
    {
        Assert.Empty(GeminiClient.ParseEvaluationJson("{\"score\":100,\"isCorrect\":true}").Vocabulary);
    }

    [Fact]
    public void FormatterListsVocabulary()
    {
        var item = new PracticeItem(TranslationDirection.TurkishToEnglish, "Kaç!", ["Run!"], 0);
        var text = EvaluationFormatter.Format(item, "Escape", new Evaluation
        {
            Score = 50,
            IsCorrect = false,
            Vocabulary = [new VocabularyItem("run away", "kaçmak", "", "kalıp")],
        });

        Assert.Contains("Öğrenilecekler:", text);
        Assert.Contains("run away (kalıp): kaçmak", text);
    }
}

public class VocabularySessionTests
{
    [Fact]
    public async Task EvaluationsFillDictionaryAndRepeatedMistakesStayOneEntry()
    {
        using var dir = new TempDir();
        var evaluator = new FakeEvaluator();
        var time = new FixedTimeProvider(FixedTimeProvider.Local(2026, 11, 7, 9));
        var session = new PracticeSession(new AppPaths(dir.Path), evaluator, time, new ApiKeyStore(_ => null), new Random(1));
        session.ImportCorpus(dir.Write("tur.txt", Samples.TatoebaLines));
        session.SetApiKey("AIzaTEST-vocab");

        evaluator.Handler = (_, _) => new Evaluation
        {
            Score = 60,
            IsCorrect = false,
            Vocabulary =
            [
                new VocabularyItem("be supposed to", "-mesi gerekmek", "You are supposed to wait.", "kalıp"),
                new VocabularyItem("speak French", "Fransızca konuşmak", "", "tamlama"),
            ],
        };
        var first = await session.SubmitAsync("cevap 1");
        Assert.Equal(2, first.Words!.Added.Count);

        session.NextSentence();
        time.Now = time.Now.AddHours(2);
        evaluator.Handler = (_, _) => new Evaluation
        {
            Score = 65,
            IsCorrect = false,
            Vocabulary = [new VocabularyItem("To be supposed to", "-mesi beklenmek", "He was supposed to come.", "kalıp")],
        };
        var second = await session.SubmitAsync("cevap 2");

        Assert.Empty(second.Words!.Added);
        Assert.Single(second.Words.Repeated);
        Assert.Equal(2, session.Vocabulary.Count);
        Assert.Equal(2, session.Vocabulary.Find("be supposed to")!.MistakeCount);
        Assert.True(File.Exists(dir.File("vocabulary.json")));

        var reopened = new PracticeSession(new AppPaths(dir.Path), new FakeEvaluator(), time, new ApiKeyStore(_ => null));
        Assert.Equal(2, reopened.Vocabulary.Count);
        Assert.Equal(2, reopened.Vocabulary.Find("be supposed to")!.MistakeCount);
    }

    [Fact]
    public async Task FailedEvaluationAddsNothing()
    {
        using var dir = new TempDir();
        var evaluator = new FakeEvaluator { Handler = (_, _) => throw new GeminiException("hata") };
        var session = new PracticeSession(new AppPaths(dir.Path), evaluator, apiKeyStore: new ApiKeyStore(_ => null));
        session.ImportCorpus(dir.Write("tur.txt", Samples.TatoebaLines));
        session.SetApiKey("AIzaTEST-vocab");

        await session.SubmitAsync("x");

        Assert.Equal(0, session.Vocabulary.Count);
        Assert.False(File.Exists(dir.File("vocabulary.json")));
    }
}

using System.Text.Json;
using Lingora.Core;
using Lingora.Core.Game;
using Lingora.Core.Gemini;
using Lingora.Core.Models;
using Lingora.Core.Storage;

namespace Lingora.Tests;

internal static class GameFixture
{
    private static readonly Lazy<GameLibrary> Shared = new(() =>
        GameLibrary.Load(GameLibrary.FindDirectory() ?? throw new InvalidOperationException("game-assets bulunamadı")));

    public static GameLibrary Library => Shared.Value;

    public static GameScene Scene(string id) => Library.Scenes.Single(s => s.Id == id);
}

public class GameLibraryTests
{
    [Fact]
    public void LoadsManifestAndAllScenesWithExistingFiles()
    {
        var library = GameFixture.Library;

        Assert.Equal(54, library.Assets.Count);
        Assert.Equal(4948, library.Scenes.Count);
        Assert.All(library.Assets.Values, a => Assert.True(File.Exists(library.AssetPath(a.Id)), a.File));
        Assert.All(library.Scenes, s =>
        {
            Assert.True(library.Assets.ContainsKey(s.Character), s.Id);
            Assert.True(s.Object is null || library.Assets.ContainsKey(s.Object), s.Id);
            Assert.True(s.Background is null || library.Assets.ContainsKey(s.Background), s.Id);
            Assert.False(string.IsNullOrWhiteSpace(s.En));
            Assert.False(string.IsNullOrWhiteSpace(s.Tr));
        });
    }

    [Fact]
    public void FactsDescribeEverythingVisibleInThePicture()
    {
        var library = GameFixture.Library;
        var scene = GameFixture.Scene("mary_stand__ball__yellow__x2__school");

        var facts = library.DescribeFacts(scene);

        Assert.Contains("Mary (Mary, kız çocuk)", facts);
        Assert.Contains("\"The girl\"", facts);
        Assert.Contains("\"A girl\"", facts);
        Assert.Contains("holding", facts);
        Assert.Contains("iki sarı top (two yellow balls); adet: 2; renk: sarı (yellow)", facts);
        Assert.Contains("okul (at school / okulda)", facts);
    }

    [Fact]
    public void FactsForDrinkWithoutBackgroundWarnAgainstInventedPlace()
    {
        var library = GameFixture.Library;
        var scene = GameFixture.Scene("tom_stand__glass__milk");

        var facts = library.DescribeFacts(scene);

        Assert.Contains("bir bardak süt (a glass of milk)", facts);
        Assert.Contains("arka plan yok", facts);
        Assert.Contains("yer uydurmak hatadır", facts);
    }

    [Fact]
    public void GroupScenesListAllMembers()
    {
        var library = GameFixture.Library;
        var scene = GameFixture.Scene("kids__run__as_tom_and_mary");
        var facts = library.DescribeFacts(scene);
        Assert.Contains("Tom (Tom, erkek çocuk) ve Mary (Mary, kız çocuk)", facts);
        Assert.Contains("\"They\"", facts);
        Assert.Equal("Tom ve Mary", library.SubjectIn(scene, TranslationDirection.EnglishToTurkish));
        Assert.Equal("Tom and Mary", library.SubjectIn(scene, TranslationDirection.TurkishToEnglish));
    }

    [Fact]
    public void SubjectHintUsesTargetLanguage()
    {
        var library = GameFixture.Library;
        var scene = library.Scenes.First(s => s.Subject == "My mother");
        Assert.Equal("Annem", library.SubjectIn(scene, TranslationDirection.EnglishToTurkish));
        Assert.Equal("My mother", library.SubjectIn(scene, TranslationDirection.TurkishToEnglish));
    }
}

public class ChoiceBuilderTests
{
    [Theory]
    [InlineData(TranslationDirection.TurkishToEnglish)]
    [InlineData(TranslationDirection.EnglishToTurkish)]
    public void BuildsFourDistinctOptionsWithOneCorrect(TranslationDirection direction)
    {
        var library = GameFixture.Library;
        var random = new Random(5);
        for (var i = 0; i < 40; i++)
        {
            var scene = library.Pick(random);
            var question = ChoiceBuilder.Build(library, scene, direction, random);

            Assert.Equal(4, question.Options.Count);
            Assert.Equal(4, question.Options.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(scene.Sentence(direction), question.Correct);
            Assert.Single(question.Options, o => o == scene.Sentence(direction));
        }
    }

    [Fact]
    public void LessSpecificSentencesThatAreAlsoTrueAreNeverDistractors()
    {
        var library = GameFixture.Library;
        var yellowApplePark = library.Scenes.First(s => s.En == "She is holding a yellow apple in the park.");
        for (var seed = 0; seed < 30; seed++)
        {
            var question = ChoiceBuilder.Build(library, yellowApplePark, TranslationDirection.TurkishToEnglish, new Random(seed));
            Assert.DoesNotContain("She is holding an apple in the park.", question.Options);
            Assert.DoesNotContain("She is holding a yellow apple.", question.Options);
            Assert.DoesNotContain("She is holding an apple.", question.Options);
        }

        // Tersi: resimde yer yokken yer söyleyen cümle yanlıştır, çeldirici olabilir.
        var plain = library.Scenes.First(s => s.En == "She is holding a yellow apple.");
        var colors = library.Colors.Select(c => c.Key).ToHashSet();
        Assert.False(ChoiceBuilder.AlsoTrue(plain, yellowApplePark, colors));
        Assert.True(ChoiceBuilder.AlsoTrue(yellowApplePark, plain, colors));
    }

    [Fact]
    public void DistractorsShowADifferentPictureAndPreferOneDifference()
    {
        var library = GameFixture.Library;
        var scene = GameFixture.Scene("mary_stand__ball__yellow__x2__school");
        var question = ChoiceBuilder.Build(library, scene, TranslationDirection.TurkishToEnglish, new Random(1));
        var key = ChoiceBuilder.Visual(scene);

        foreach (var option in question.Options.Where(o => o != question.Correct))
        {
            var other = library.Scenes.First(s => s.En == option);
            Assert.Equal(1, ChoiceBuilder.Distance(key, ChoiceBuilder.Visual(other)));
            Assert.Equal(scene.Subject, other.Subject);
        }
    }
}

public class ScenePromptTests
{
    [Fact]
    public void ImagePromptCarriesFactsCanonicalSentenceAndImageRules()
    {
        using var dir = new TempDir();
        var session = new PracticeSession(new AppPaths(dir.Path), new FakeEvaluator(), new FixedTimeProvider(FixedTimeProvider.Local(2026, 11, 8, 10)), new ApiKeyStore(_ => null));
        var library = GameFixture.Library;
        var scene = GameFixture.Scene("mary_stand__ball__yellow__x2__school");

        var item = session.SceneItem(library, scene);
        var body = PromptBuilder.BuildRequestBody(item, "Mary holding two balls");
        var system = body["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>();
        var prompt = body["contents"]![0]!["parts"]![0]!["text"]!.GetValue<string>();

        Assert.Equal(TranslationDirection.TurkishToEnglish, item.Direction);
        Assert.Equal("Resim: Mary okulda iki sarı top tutuyor.", item.Source);
        Assert.Equal("Mary is holding two yellow balls at school.", Assert.Single(item.References));
        Assert.Contains("RESİM MODU", system);
        Assert.Contains("resimde gerçekte ne olduğunu açıkça", system);
        Assert.Contains("Görev: resmi anlat. Öğrenci resmi İngilizce", prompt);
        Assert.Contains("iki sarı top (two yellow balls)", prompt);
        Assert.Contains("Kanonik İngilizce cümle", prompt);
        Assert.Contains("<<<\nMary holding two balls\n>>>", prompt.Replace("\r\n", "\n"));

        // Normal çeviri isteminde resim kuralları yok.
        var plain = PromptBuilder.BuildRequestBody(new PracticeItem(TranslationDirection.TurkishToEnglish, "Selam.", ["Hi."], 0), "Hi");
        Assert.DoesNotContain("RESİM MODU", plain["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>());
    }
}

public class TurkishScenePromptTests
{
    [Fact]
    public void TurkishGameLanguageMakesGeminiEvaluateAndAnswerInTurkish()
    {
        using var dir = new TempDir();
        var session = new PracticeSession(new AppPaths(dir.Path), new FakeEvaluator(), new FixedTimeProvider(FixedTimeProvider.Local(2026, 11, 8, 10)), new ApiKeyStore(_ => null));
        session.SetGameDirection(TranslationDirection.EnglishToTurkish);
        var scene = GameFixture.Scene("mary_stand__ball__yellow__x2__school");

        var item = session.SceneItem(GameFixture.Library, scene);
        var body = PromptBuilder.BuildRequestBody(item, "Kız okulda iki sarı top tutuyor");
        var system = body["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>();
        var prompt = body["contents"]![0]!["parts"]![0]!["text"]!.GetValue<string>();

        Assert.Equal(TranslationDirection.EnglishToTurkish, item.Direction);
        Assert.Equal("Mary okulda iki sarı top tutuyor.", Assert.Single(item.References));
        Assert.Equal("Resim: Mary is holding two yellow balls at school.", item.Source);
        Assert.Contains("Öğrenci resmi Türkçe tek cümleyle anlattı", prompt);
        Assert.Contains("HEDEF DİL: TÜRKÇE", prompt);
        Assert.Contains("MUTLAKA TÜRKÇE", prompt);
        Assert.Contains("asla İngilizce cümle önerme", prompt);
        Assert.Contains("Kanonik Türkçe cümle", prompt);
        Assert.Contains("\"Kız\"", prompt);
        Assert.Contains("\"O\"", prompt);
        Assert.Contains("özne düşürülebilir", prompt);
        Assert.DoesNotContain("\"The girl\"", prompt);
        Assert.Contains("Değerlendirmeyi daima HEDEF DİLDE yap", system);

        // Seçim kalıcı; çalışma ekranının modu değişmez.
        var reopened = new PracticeSession(new AppPaths(dir.Path), new FakeEvaluator(), new FixedTimeProvider(FixedTimeProvider.Local(2026, 11, 8, 10)), new ApiKeyStore(_ => null));
        Assert.Equal(TranslationDirection.EnglishToTurkish, reopened.Settings.GameDirection);
        Assert.Equal(TranslationDirection.TurkishToEnglish, reopened.Settings.Direction);
    }

    [Fact]
    public void ChoiceRecordsUseTheQuestionsLanguage()
    {
        using var dir = new TempDir();
        var session = new PracticeSession(new AppPaths(dir.Path), new FakeEvaluator(), new FixedTimeProvider(FixedTimeProvider.Local(2026, 11, 8, 10)), new ApiKeyStore(_ => null));
        var question = ChoiceBuilder.Build(GameFixture.Library, GameFixture.Scene("tom_stand__apple__park"), TranslationDirection.EnglishToTurkish, new Random(1));

        Assert.Equal("Tom parkta bir elma tutuyor.", question.Correct);
        var entry = session.RecordChoice(question, question.CorrectIndex);
        Assert.Equal(TranslationDirection.EnglishToTurkish, entry.Direction);
        Assert.Equal("Resim (seçmeli): Tom is holding an apple in the park.", entry.Source);
    }
}

public class GameSessionTests
{
    private static PracticeSession Create(TempDir dir, FakeEvaluator evaluator)
    {
        var session = new PracticeSession(
            new AppPaths(dir.Path),
            evaluator,
            new FixedTimeProvider(FixedTimeProvider.Local(2026, 11, 8, 10)),
            new ApiKeyStore(_ => null),
            new Random(1));
        session.ImportCorpus(dir.Write("tur.txt", Samples.TatoebaLines));
        session.SetApiKey("AIzaTEST-game-0000");
        return session;
    }

    [Fact]
    public async Task DescribingAPictureCountsLikeATranslationButKeepsPracticeScreen()
    {
        using var dir = new TempDir();
        var evaluator = new FakeEvaluator
        {
            Handler = (_, _) => new Evaluation
            {
                Score = 70,
                IsCorrect = false,
                CorrectedTranslation = "Mary is holding two yellow balls.",
                Mistakes = ["red → yellow: resimdeki toplar sarı"],
                Tip = "Renk sıfatı isimden önce gelir.",
                Vocabulary = [new VocabularyItem("yellow", "sarı", "a yellow ball (sarı bir top)", "kelime")],
            },
        };
        var session = Create(dir, evaluator);
        var current = session.Current;
        var state = session.State;

        var outcome = await session.SubmitSceneAsync(GameFixture.Library, GameFixture.Scene("mary_stand__ball__yellow__x2__school"), "Mary is holding two red balls");

        Assert.True(outcome.Success);
        Assert.NotNull(Assert.Single(evaluator.Calls).Item.ImageFacts);
        Assert.Equal(1, session.TodayCount);
        var entry = Assert.Single(session.Journal.LoadDay(session.Today));
        Assert.StartsWith("Resim: ", entry.Source);
        Assert.Equal("red → yellow: resimdeki toplar sarı", Assert.Single(entry.Mistakes));
        Assert.NotNull(session.Vocabulary.Find("yellow"));
        Assert.Same(current, session.Current);
        Assert.Equal(state, session.State);
    }

    [Fact]
    public async Task EmptySentenceOrMissingKeyIsNotSent()
    {
        using var dir = new TempDir();
        var evaluator = new FakeEvaluator();
        var session = Create(dir, evaluator);
        var scene = GameFixture.Scene("tom_stand__apple");

        Assert.False((await session.SubmitSceneAsync(GameFixture.Library, scene, "  ")).Success);
        session.ClearApiKey();
        Assert.False((await session.SubmitSceneAsync(GameFixture.Library, scene, "Tom holds an apple")).Success);
        Assert.Empty(evaluator.Calls);
        Assert.Equal(0, session.TodayCount);
    }

    [Fact]
    public void ChoiceResultsCountAndWrongAnswersGoToJournal()
    {
        using var dir = new TempDir();
        var session = Create(dir, new FakeEvaluator());
        var library = GameFixture.Library;
        var question = ChoiceBuilder.Build(library, GameFixture.Scene("tom_stand__apple__park"), TranslationDirection.TurkishToEnglish, new Random(3));
        var wrong = Enumerable.Range(0, 4).First(i => i != question.CorrectIndex);

        var right = session.RecordChoice(question, question.CorrectIndex);
        var miss = session.RecordChoice(question, wrong);

        Assert.Equal(2, session.TodayCount);
        Assert.Empty(right.Mistakes);
        Assert.Equal(100, right.Score);
        Assert.Equal(0, miss.Score);
        Assert.Contains("→ Tom is holding an apple in the park.", Assert.Single(miss.Mistakes));
        Assert.Equal(2, session.Journal.LoadDay(session.Today).Count);
        Assert.Equal(2, session.Attempts.Count);
    }
}

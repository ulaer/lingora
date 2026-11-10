using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lingora.Core;
using Lingora.Core.Game;
using Lingora.Core.Models;
using Lingora.Core.Storage;
using Lingora.Desktop;

namespace Lingora.Tests;

/// <summary>Küçük, elle yazılmış bir game-assets klasörü (gerçek manifest + verilen jsonl satırları).</summary>
internal sealed class MiniGameDir : IDisposable
{
    private readonly TempDir _dir = new();

    public MiniGameDir(string level, IEnumerable<object> records)
    {
        File.Copy(Path.Combine(GameLibrary.FindDirectory()!, "manifest.json"), _dir.File("manifest.json"));
        _dir.Write("scenes.json", "{\"version\":1,\"count\":0,\"scenes\":[]}");
        Directory.CreateDirectory(_dir.File("scenes"));
        _dir.Write(Path.Combine("scenes", level + ".jsonl"), string.Join("\n", records.Select(r => JsonSerializer.Serialize(r))));
    }

    public GameLibrary Load() => GameLibrary.Load(_dir.Path);

    public void Dispose() => _dir.Dispose();

    public static object Sentence(string subject, string en, string tr, string[]? mentions, string[]? subjectOk, string form = "prog", string[]? badges = null) =>
        new { form, subject, en, tr, badges = badges ?? [], mentions, subject_ok = subjectOk };

    public static object Record(string id, string ch, string? bg, object facts, params object[] sentences) => new
    {
        id,
        level = "A2",
        layout = "single",
        bg,
        actors = new[] { new { @char = ch, x = 128, s = 1.0 } },
        facts,
        sentences,
    };

    public static object Facts(string subject, string verb, string? place) =>
        new { subject, verb, @object = (string?)null, variant = (string?)null, count = 0, place, time = (string?)null, weather = (string?)null, state = (string?)null, other = (string?)null };
}

public class GameLevelCoreTests
{
    [Theory]
    [InlineData(GameLevel.A1)]
    [InlineData(GameLevel.A2)]
    [InlineData(GameLevel.B1)]
    public void JsonlRecordsLoadWithSentencesAndKnownAssets(GameLevel level)
    {
        var library = GameFixture.Library;
        var records = library.Records(level);

        Assert.NotEmpty(records);
        Assert.All(records.Take(400), r =>
        {
            Assert.Equal(level, r.Level);
            Assert.NotEmpty(r.Sentences);
            Assert.All(r.Sentences, s =>
            {
                Assert.False(string.IsNullOrWhiteSpace(s.En), r.Id);
                Assert.False(string.IsNullOrWhiteSpace(s.Tr), r.Id);
            });
            Assert.All(r.Actors.Concat(r.Panels.SelectMany(p => p.Actors)), a => Assert.True(library.Assets.ContainsKey(a.Char), a.Char));
        });
    }

    [Fact]
    public void A2AndB1CarryTheirSpecialFields()
    {
        var library = GameFixture.Library;
        Assert.Contains(library.Records(GameLevel.A2), r => r.Sentences.Any(s => s.Badges.Count > 0));
        Assert.Contains(library.Records(GameLevel.B1), r => r.Bubble is not null || r.Prop is not null || r.Panels.Count > 1);
        Assert.NotEmpty(library.Emotions);
        Assert.NotEmpty(library.Weathers);
        Assert.Equal(-34, library.Layouts.Marker.Y);
        Assert.Equal(0.2, library.Layouts.Badge.S, 1);
    }

    [Theory]
    [InlineData(GameLevel.A1)]
    [InlineData(GameLevel.A2)]
    [InlineData(GameLevel.B1)]
    public void PickRoundComesFromTheSelectedLevelAndChoicesStayDistinct(GameLevel level)
    {
        var library = GameFixture.Library;
        var random = new Random(3);
        for (var i = 0; i < 15; i++)
        {
            var round = library.PickRound(level, random);
            Assert.Equal(level, round.Level);
            Assert.Contains(round.Sentence, round.Record.Sentences);
            Assert.Null(round.Legacy); // v5: A1 de kayıt yolundan

            var question = ChoiceBuilder.Build(library, round, TranslationDirection.TurkishToEnglish, random);
            Assert.Equal(4, question.Options.Count);
            Assert.Equal(4, question.Options.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(round.Sentence.Answer?.En ?? round.Sentence.En, question.Correct);
        }
    }

    [Fact]
    public void WhAnswerDistractorsAreWrongValuesEvenWhenTheirQuestionIsTheSame()
    {
        var library = GameFixture.Library;
        var random = new Random(4);
        var whRounds = library.Records(GameLevel.B1)
            .SelectMany(r => r.Sentences.Where(s => s.Answer is not null).Select(s => new GameRound(r, s)))
            .ToList();
        Assert.NotEmpty(whRounds);
        foreach (var round in whRounds.OrderBy(_ => random.Next()).Take(60))
        {
            var question = ChoiceBuilder.Build(library, round, TranslationDirection.TurkishToEnglish, random);
            Assert.Equal(4, question.Options.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(round.Sentence.Answer!.En, question.Correct);
            foreach (var (text, source) in question.Options.Zip(question.OptionSources!))
            {
                if (source is null)
                {
                    continue;
                }

                var other = library.Records(GameLevel.B1).Single(r => r.Id == source);
                Assert.False(ChoiceBuilder.AnswerIsTrueFor(round.Record, other, round.Sentence.Form, library.PlaceParents), $"{round.Record.Id}: {text}");
            }
        }
    }

    [Fact]
    public void WeightedPickDilutesCrowdedPosesInRecordLevels()
    {
        var records = Enumerable.Range(0, 90).Select(i => MiniGameDir.Record($"run{i}", "tom_run", "park", MiniGameDir.Facts("tom", "run", "park"),
                MiniGameDir.Sentence("Tom", $"Tom runs {i}.", $"Tom koşar {i}.", null, null)))
            .Concat(Enumerable.Range(0, 10).Select(i => MiniGameDir.Record($"swim{i}", "tom_swim", "beach", MiniGameDir.Facts("tom", "swim", "beach"),
                MiniGameDir.Sentence("Tom", $"Tom swims {i}.", $"Tom yüzer {i}.", null, null))));
        using var mini = new MiniGameDir("A2", records);
        var library = mini.Load();
        var random = new Random(9);

        var swim = Enumerable.Range(0, 4000).Count(_ => library.PickRound(GameLevel.A2, random).Record.Id.StartsWith("swim", StringComparison.Ordinal));

        // Tekdüzede %10; 1/√n ağırlığıyla ~%25.
        Assert.InRange(swim / 4000.0, 0.18, 0.32);
        Assert.True(library.HasLevel(GameLevel.A2));
        Assert.False(library.HasLevel(GameLevel.B1));
    }

    [Fact]
    public void LegacyScenesJsonPickStillWorks()
    {
        var library = GameFixture.Library;
        var scene = library.Pick(new Random(7));
        var round = GameRound.FromScene(scene);
        Assert.Equal(scene.En, round.Sentence.En);
        Assert.NotNull(round.Legacy);
    }

    [Fact]
    public void TrueButDifferentSentencesAreNeverDistractorsButFalseOnesAre()
    {
        var picture = MiniGameDir.Record("tom_run_park", "tom_run", "park", MiniGameDir.Facts("tom", "run", "park"),
            MiniGameDir.Sentence("Tom", "Tom is running in the park.", "Tom parkta koşuyor.", null, null));
        var all = new[] { "subject", "verb", "place" };
        var records = new[]
        {
            picture,
            // "He" Tom'a da uyar (subject_ok) → doğru, çeldirici olamaz.
            MiniGameDir.Record("man_run_park", "man_run", "park", MiniGameDir.Facts("man", "run", "park"),
                MiniGameDir.Sentence("He", "He is running in the park.", "O parkta koşuyor.", all, ["tom", "man"]),
                MiniGameDir.Sentence("The man", "The man is running in the park.", "Adam parkta koşuyor.", all, ["man"])),
            // Yer söylenmiyor → Tom parkta koşarken de doğru.
            MiniGameDir.Record("tom_run_none", "tom_run", null, MiniGameDir.Facts("tom", "run", null),
                MiniGameDir.Sentence("Tom", "Tom is running.", "Tom koşuyor.", ["subject", "verb"], ["tom"])),
            // Yanlış olanlar: farklı fiil, farklı özne, farklı yer.
            MiniGameDir.Record("tom_swim_park", "tom_swim", "park", MiniGameDir.Facts("tom", "swim", "park"),
                MiniGameDir.Sentence("Tom", "Tom is swimming in the park.", "Tom parkta yüzüyor.", all, ["tom"])),
            MiniGameDir.Record("mary_run_park", "mary_run", "park", MiniGameDir.Facts("mary", "run", "park"),
                MiniGameDir.Sentence("Mary", "Mary is running in the park.", "Mary parkta koşuyor.", all, ["mary"])),
            MiniGameDir.Record("tom_run_beach", "tom_run", "beach", MiniGameDir.Facts("tom", "run", "beach"),
                MiniGameDir.Sentence("Tom", "Tom is running on the beach.", "Tom sahilde koşuyor.", all, ["tom"])),
        };
        using var mini = new MiniGameDir("A2", records);
        var library = mini.Load();
        var round = new GameRound(library.Records(GameLevel.A2)[0], library.Records(GameLevel.A2)[0].Sentences[0]);

        var seen = new HashSet<string>();
        for (var seed = 0; seed < 40; seed++)
        {
            var question = ChoiceBuilder.Build(library, round, TranslationDirection.TurkishToEnglish, new Random(seed));
            Assert.Equal("Tom is running in the park.", question.Correct);
            Assert.DoesNotContain("He is running in the park.", question.Options);
            Assert.DoesNotContain("Tom is running.", question.Options);
            seen.UnionWith(question.Options);
        }

        Assert.Contains("The man is running in the park.", seen);
        Assert.Contains("Tom is swimming in the park.", seen);
        Assert.Contains("Mary is running in the park.", seen);
        Assert.Contains("Tom is running on the beach.", seen);
    }

    [Fact]
    public void OnlySameFormSentencesAreOfferedAsDistractors()
    {
        var all = new[] { "subject", "verb" };
        var records = new[]
        {
            MiniGameDir.Record("a", "tom_run", null, MiniGameDir.Facts("tom", "run", null),
                MiniGameDir.Sentence("Tom", "Did Tom run yesterday?", "Tom dün koştu mu?", all, ["tom"], "past_q", ["cal_yesterday", "badge_question"])),
            MiniGameDir.Record("b", "mary_run", null, MiniGameDir.Facts("mary", "run", null),
                MiniGameDir.Sentence("Mary", "Did Mary run yesterday?", "Mary dün koştu mu?", all, ["mary"], "past_q", ["cal_yesterday", "badge_question"]),
                MiniGameDir.Sentence("Mary", "Mary is running.", "Mary koşuyor.", all, ["mary"])),
            MiniGameDir.Record("c", "tom_swim", null, MiniGameDir.Facts("tom", "swim", null),
                MiniGameDir.Sentence("Tom", "Did Tom swim yesterday?", "Tom dün yüzdü mü?", all, ["tom"], "past_q", ["cal_yesterday", "badge_question"])),
        };
        using var mini = new MiniGameDir("A2", records);
        var library = mini.Load();
        var first = library.Records(GameLevel.A2)[0];
        var question = ChoiceBuilder.Build(library, new GameRound(first, first.Sentences[0]), TranslationDirection.TurkishToEnglish, new Random(1));

        Assert.Equal(3, question.Options.Count);
        Assert.DoesNotContain("Mary is running.", question.Options);
    }

    [Fact]
    public void RecordsWithoutFactsFallBackToTheirPicture()
    {
        var a = new GameRecord("a", GameLevel.A2, "single", "park", null, null, null, null, [new GameActor("tom_run", 128, 1, null, null, null)], null, null, [], null, []);
        var same = a with { Id = "a2" };
        var other = a with { Id = "b", Bg = "beach" };
        var sentence = new GameSentence("prog", "Tom", "x", "y", [], [], [], null);

        Assert.True(ChoiceBuilder.IsTrueFor(a, sentence, same));
        Assert.False(ChoiceBuilder.IsTrueFor(a, sentence, other));
    }

    [Fact]
    public void LevelIsPersistedInSettings()
    {
        using var dir = new TempDir();
        var time = new FixedTimeProvider(FixedTimeProvider.Local(2026, 11, 8, 10));
        var session = new PracticeSession(new AppPaths(dir.Path), new FakeEvaluator(), time, new ApiKeyStore(_ => null));
        Assert.Equal(GameLevel.A1, session.Settings.GameLevel);

        session.SetGameLevel(GameLevel.B1);

        var reopened = new PracticeSession(new AppPaths(dir.Path), new FakeEvaluator(), time, new ApiKeyStore(_ => null));
        Assert.Equal(GameLevel.B1, reopened.Settings.GameLevel);
    }

    [Fact]
    public void DescribingARoundUsesFactsAndBadgesOfTheAskedSentence()
    {
        using var dir = new TempDir();
        var session = new PracticeSession(new AppPaths(dir.Path), new FakeEvaluator(), new FixedTimeProvider(FixedTimeProvider.Local(2026, 11, 8, 10)), new ApiKeyStore(_ => null));
        var library = GameFixture.Library;
        var record = library.Records(GameLevel.A2).First(r => r.Sentences.Any(s => s.Badges.Contains("cal_yesterday") && s.Badges.Contains("badge_question")));
        var sentence = record.Sentences.First(s => s.Badges.Contains("cal_yesterday") && s.Badges.Contains("badge_question"));

        var item = session.SceneItem(library, new GameRound(record, sentence));

        Assert.Equal(sentence.En, Assert.Single(item.References));
        Assert.Contains("geçmiş zaman (dün) + soru cümlesi", item.ImageFacts);
        Assert.Contains("Kim:", item.ImageFacts);
    }
}


public class GameV5TruthRuleTests
{
    private static GameRecord Rec(string id, Dictionary<string, string?> facts, string? bg = null) =>
        new(id, GameLevel.A2, "single", bg, null, null, null, null, [], null, null, [], new GameFacts(facts), []);

    private static GameSentence Sent(string[] mentions, string[]? ok = null, string? person = null, string form = "prog", string[]? badges = null) =>
        new(form, "X", "x", "y", badges ?? [], mentions, ok ?? [], person);

    [Fact]
    public void HoldingAnObjectIsTrueForPicturesWhereItIsOnlyHeld()
    {
        var give = Rec("give", new() { ["subject"] = "mary", ["verb"] = "give", ["object"] = "ball", ["holds"] = "ball", ["count"] = "1" });
        var holdBall = Rec("hold", new() { ["subject"] = "mary", ["verb"] = "hold", ["object"] = "ball", ["count"] = "1" });
        var holdApple = Rec("hold2", new() { ["subject"] = "mary", ["verb"] = "hold", ["object"] = "apple", ["count"] = "1" });
        var sentence = Sent(["subject", "verb", "object", "count"], ["mary", "woman"]);

        Assert.True(ChoiceBuilder.IsTrueFor(give, sentence, holdBall));
        Assert.False(ChoiceBuilder.IsTrueFor(give, sentence, holdApple));
    }

    [Fact]
    public void PersonSeparatesIFromHe()
    {
        var iPicture = Rec("i", new() { ["subject"] = "tom", ["person"] = "1s", ["verb"] = "run" });
        var hePicture = Rec("he", new() { ["subject"] = "tom", ["person"] = "3s", ["verb"] = "run" });
        var heSentence = Sent(["subject", "person", "verb"], ["tom"], "3s");

        Assert.False(ChoiceBuilder.IsTrueFor(iPicture, heSentence, hePicture));
        Assert.True(ChoiceBuilder.IsTrueFor(hePicture, heSentence, hePicture));
    }

    [Fact]
    public void AtHomeIsTrueForKitchenButNotTheOtherWayAround()
    {
        var parents = new Dictionary<string, string> { ["kitchen"] = "room" };
        var kitchen = Rec("k", new() { ["subject"] = "tom", ["verb"] = "run", ["place"] = "kitchen" });
        var room = Rec("r", new() { ["subject"] = "tom", ["verb"] = "run", ["place"] = "room" });
        var atHome = Sent(["subject", "verb", "place"], ["tom"]);

        Assert.True(ChoiceBuilder.IsTrueFor(kitchen, atHome, room, parents));
        Assert.False(ChoiceBuilder.IsTrueFor(kitchen, atHome, room));
        Assert.False(ChoiceBuilder.IsTrueFor(room, Sent(["subject", "verb", "place"], ["tom"]), kitchen, parents));
    }

    [Fact]
    public void NegationIsNotInvertedAndDistractorsKeepTheAskedPolarity()
    {
        object Snt(string subject, string en, string form, string[] badges, string[] ok) =>
            MiniGameDir.Sentence(subject, en, en, ["subject", "verb"], ok, form, badges);
        var records = new[]
        {
            MiniGameDir.Record("a", "tom_run", null, MiniGameDir.Facts("tom", "run", null),
                Snt("Tom", "Tom isn't running.", "prog_neg", ["badge_not"], ["tom"]),
                Snt("Tom", "Tom is running.", "prog", [], ["tom"])),
            MiniGameDir.Record("b", "man_run", null, MiniGameDir.Facts("man", "run", null),
                Snt("He", "He isn't running.", "prog_neg", ["badge_not"], ["tom", "man"]),
                Snt("The man", "The man isn't running.", "prog_neg", ["badge_not"], ["man"]),
                Snt("The man", "The man is running.", "prog", [], ["man"])),
            MiniGameDir.Record("c", "mary_run", null, MiniGameDir.Facts("mary", "run", null),
                Snt("Mary", "Mary isn't running.", "prog_neg", ["badge_not"], ["mary"]),
                Snt("Mary", "Mary is running.", "prog", [], ["mary"])),
            MiniGameDir.Record("d", "tom_swim", null, MiniGameDir.Facts("tom", "swim", null),
                Snt("Tom", "Tom isn't swimming.", "prog_neg", ["badge_not"], ["tom"])),
        };
        using var mini = new MiniGameDir("A2", records);
        var library = mini.Load();
        var first = library.Records(GameLevel.A2)[0];
        var seen = new HashSet<string>();
        for (var seed = 0; seed < 30; seed++)
        {
            var q = ChoiceBuilder.Build(library, new GameRound(first, first.Sentences[0]), TranslationDirection.TurkishToEnglish, new Random(seed));
            Assert.All(q.Options, o => Assert.Contains("n't", o));
            Assert.DoesNotContain("He isn't running.", q.Options);
            seen.UnionWith(q.Options);
        }

        Assert.Contains("The man isn't running.", seen);
        Assert.Contains("Mary isn't running.", seen);
        Assert.Contains("Tom isn't swimming.", seen);
    }

    [Fact]
    public void ListeningToIsTrueOnlyForTheMatchingTalkToPicture()
    {
        var tomTalksToMary = Rec("t", new() { ["subject"] = "tom", ["verb"] = "talk_to", ["other"] = "mary" });
        var maryTalksToTom = Rec("m", new() { ["subject"] = "mary", ["verb"] = "talk_to", ["other"] = "tom" });
        var tomWaves = Rec("w", new() { ["subject"] = "tom", ["verb"] = "wave_at", ["other"] = "mary" });
        var tomRuns = Rec("r", new() { ["subject"] = "tom", ["verb"] = "run" });
        var listening = Sent(["subject", "verb", "other"], ["mary"], "3s") with { Role = "other" };

        Assert.True(ChoiceBuilder.IsTrueFor(tomTalksToMary, listening, tomTalksToMary));
        Assert.False(ChoiceBuilder.IsTrueFor(maryTalksToTom, listening, tomTalksToMary));
        Assert.False(ChoiceBuilder.IsTrueFor(tomWaves, listening, tomTalksToMary));
        Assert.False(ChoiceBuilder.IsTrueFor(tomRuns, listening, tomTalksToMary));
    }

    [Fact]
    public void LegendHasAllTurkishLabels()
    {
        var labels = GameLibrary.BadgeLegend.Select(b => b.Label).ToList();
        Assert.Equal(["dün", "yarın", "değil", "soru", "her gün", "-ebilir", "ben", "sen"], labels);
    }

    [Fact]
    public void A1RoundsComeFromRecordsAndGoThroughTheFactsRule()
    {
        var library = GameFixture.Library;
        var round = library.PickRound(GameLevel.A1, new Random(4));
        Assert.Null(round.Legacy);
        Assert.Contains(round.Sentence, round.Record.Sentences);
    }
}


public class GameV6Tests
{
    private static GameRound RoundOf(string id, GameLevel level = GameLevel.A2) =>
        new(new GameRecord(id, level, "single", null, null, null, null, null, [], null, null, [], null, []),
            new GameSentence("prog", "Tom", "x", "y", [], [], [], null));

    private static GameProgress Create(TempDir dir) =>
        new(new AppPaths(dir.Path), new FixedTimeProvider(FixedTimeProvider.Local(2026, 11, 8, 10)));

    [Fact]
    public void EveryAnswerWritesOneLogLine()
    {
        using var dir = new TempDir();
        var progress = Create(dir);

        progress.Record(RoundOf("tom_run"), "choose", false, "mary_run", new Random(1));
        progress.Record(RoundOf("tom_run"), "describe", true, null, new Random(1));

        var lines = File.ReadAllLines(new AppPaths(dir.Path).GameLogFile);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"recordId\":\"tom_run\"", lines[0]);
        Assert.Contains("\"mode\":\"choose\"", lines[0]);
        Assert.Contains("\"distractorId\":\"mary_run\"", lines[0]);
        Assert.Equal(2, Create(dir).Log().Count);
    }

    [Fact]
    public void MissedPictureReturnsAfterThreeToFiveRoundsAndLeavesAfterTwoCorrect()
    {
        using var dir = new TempDir();
        for (var seed = 0; seed < 10; seed++)
        {
            var progress = Create(dir);
            progress.State.Items.Clear();
            var random = new Random(seed);
            var round = RoundOf("tom_run");
            progress.Record(round, "choose", false, null, random);

            var waited = 0;
            while (progress.DueRecordId(GameLevel.A2) is null)
            {
                progress.Advance();
                waited++;
            }

            Assert.InRange(waited, 3, 5);
            progress.Record(round, "choose", true, null, random);
            Assert.Single(progress.State.Items); // bir doğru yetmez, tekrar gelir
            Assert.Null(progress.DueRecordId(GameLevel.A2));

            while (progress.DueRecordId(GameLevel.A2) is null)
            {
                progress.Advance();
            }

            progress.Record(round, "choose", true, null, random);
            Assert.Empty(progress.State.Items);
        }
    }

    [Fact]
    public void ReviewQueueSurvivesRestart()
    {
        using var dir = new TempDir();
        Create(dir).Record(RoundOf("tom_run"), "choose", false, null, new Random(1));

        var reopened = Create(dir);

        Assert.Equal("tom_run", Assert.Single(reopened.State.Items).Id);
    }

    [Fact]
    public void NextLevelIsSuggestedWhenEightyPercentOfLastTwentyAreCorrect()
    {
        using var dir = new TempDir();
        var progress = Create(dir);
        var random = new Random(1);
        for (var i = 0; i < 20; i++)
        {
            progress.Record(RoundOf("r" + i, GameLevel.A1), "choose", i >= 4, null, random); // 16/20
        }

        Assert.Equal(GameLevel.A2, progress.SuggestedLevel(GameLevel.A1));
        Assert.Contains("A2 seviyesini deneyebilirsin", progress.Summary(GameLevel.A1));
        Assert.Equal((16, 20), progress.Accuracy(GameLevel.A1));
        Assert.Null(progress.SuggestedLevel(GameLevel.A2));

        for (var i = 0; i < 5; i++)
        {
            progress.Record(RoundOf("x" + i, GameLevel.A1), "choose", false, null, random); // son 20: 15 doğru
        }

        Assert.Null(progress.SuggestedLevel(GameLevel.A1));
    }

    [Fact]
    public void WhChooseRoundOffersAnswersWithOneCorrect()
    {
        object Wh(string subject, string q, string qTr, string a, string aTr) =>
            MiniGameDir.Sentence(subject, q, qTr, ["subject", "verb"], null, "wh_what") is var s
                ? new { form = "wh_what", subject, en = q, tr = qTr, badges = new[] { "badge_question" }, mentions = new[] { "subject", "verb" }, subject_ok = new[] { subject.ToLowerInvariant() }, answer = new { en = a, tr = aTr } }
                : s;
        var records = new[]
        {
            MiniGameDir.Record("tom_eat", "tom_eat", null, MiniGameDir.Facts("tom", "eat", null), Wh("Tom", "What is Tom eating?", "Tom ne yiyor?", "An apple.", "Bir elma.")),
            MiniGameDir.Record("mary_eat", "mary_eat", null, MiniGameDir.Facts("mary", "eat", null), Wh("Mary", "What is Mary eating?", "Mary ne yiyor?", "A banana.", "Bir muz.")),
            MiniGameDir.Record("man_eat", "man_eat", null, MiniGameDir.Facts("man", "eat", null), Wh("Man", "What is the man eating?", "Adam ne yiyor?", "Bread.", "Ekmek.")),
            MiniGameDir.Record("woman_eat", "woman_eat", null, MiniGameDir.Facts("woman", "eat", null), Wh("Woman", "What is the woman eating?", "Kadın ne yiyor?", "Soup.", "Çorba.")),
        };
        using var mini = new MiniGameDir("B1", records.Select(r => r));
        var library = mini.Load();
        var first = library.Records(GameLevel.B1)[0];
        var round = new GameRound(first, first.Sentences[0]);

        var q = ChoiceBuilder.Build(library, round, TranslationDirection.TurkishToEnglish, new Random(2));

        Assert.Equal("What is Tom eating?", q.Prompt);
        Assert.Equal("An apple.", q.Correct);
        Assert.Equal(4, q.Options.Count);
        Assert.Single(q.Options, o => o == "An apple.");
        Assert.DoesNotContain(q.Options, o => o.Contains('?'));
        Assert.Contains("Bread.", q.Options);
        Assert.Equal(q.Options.Count, q.OptionSources!.Count);
        Assert.Null(q.OptionSources[q.CorrectIndex]);
    }

    [Fact]
    public void DescribeItemUsesAnswerForWhAndAddsPronounlessTurkishForms()
    {
        using var dir = new TempDir();
        var session = new PracticeSession(new AppPaths(dir.Path), new FakeEvaluator(), new FixedTimeProvider(FixedTimeProvider.Local(2026, 11, 8, 10)), new ApiKeyStore(_ => null));
        var library = GameFixture.Library;
        var record = library.Records(GameLevel.A1)[0];
        var wh = new GameSentence("wh_what", "Tom", "What is Tom eating?", "Tom ne yiyor?", ["badge_question"], [], [], null) { Answer = new GameAnswer("An apple.", "Bir elma.") };
        var item = session.SceneItem(library, new GameRound(record, wh));
        Assert.Equal("An apple.", Assert.Single(item.References));
        Assert.Contains("What is Tom eating?", item.Source);
        Assert.Contains("doğru cevap: An apple.", item.ImageFacts);

        session.SetGameDirection(TranslationDirection.EnglishToTurkish);
        var pronoun = new GameSentence("prog", "I", "I am running.", "Ben koşuyorum.", [], [], [], "1s") { TrAlt = ["Koşuyorum."] };
        var turkish = session.SceneItem(library, new GameRound(record, pronoun));
        Assert.Equal(["Ben koşuyorum.", "Koşuyorum."], turkish.References);
        Assert.Contains("Koşuyorum.", turkish.ImageFacts);
    }
}

public class GameV4RenderingTests
{
    private static BitmapSource Rasterize(DrawingImage image)
    {
        var width = (int)Math.Round(image.Width);
        var height = (int)Math.Round(image.Height);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(image, new Rect(0, 0, width, height));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static byte[] Pixels(BitmapSource bitmap)
    {
        var px = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(px, bitmap.PixelWidth * 4, 0);
        return px;
    }

    private static int Opaque(BitmapSource bitmap) => Pixels(bitmap).Where((_, i) => i % 4 == 3).Count(a => a > 200);

    private static int Differing(BitmapSource a, BitmapSource b, int maxX, int maxY)
    {
        var pa = Pixels(a);
        var pb = Pixels(b);
        var n = 0;
        for (var y = 0; y < maxY; y++)
        {
            for (var x = 0; x < maxX; x++)
            {
                var i = ((y * a.PixelWidth) + x) * 4;
                if (pa[i] != pb[i] || pa[i + 1] != pb[i + 1] || pa[i + 2] != pb[i + 2])
                {
                    n++;
                }
            }
        }

        return n;
    }

    [Fact]
    public void A2EmotionSceneRendersWithFaceAndBadgesOnTopLeft()
    {
        WpfHost.Run(() =>
        {
            var library = GameFixture.Library;
            var renderer = new SvgScene(library);
            var record = library.Records(GameLevel.A2).First(r => r.Actors.Any(a => a.Expr is not null) && r.Sentences.Any(s => s.Badges.Count > 0));
            var sentence = record.Sentences.First(s => s.Badges.Count > 0);

            var image = renderer.Render(record, sentence);
            var plain = renderer.Render(record, null);

            Assert.Equal(256, image.Width);
            Assert.Equal(256, image.Height);
            var withBadge = Rasterize(image);
            Assert.True(Opaque(withBadge) > 1500);
            // Rozet sol üstte: 4..55 px bölgesi rozetsiz çizimden farklı.
            Assert.True(Differing(withBadge, Rasterize(plain), 60, 60) > 100);
        });
    }

    [Fact]
    public void ExpressionChangesTheFaceComparedToNeutral()
    {
        WpfHost.Run(() =>
        {
            var library = GameFixture.Library;
            var renderer = new SvgScene(library);
            var record = library.Records(GameLevel.A2).First(r => r.Actors.Any(a => a.Expr is not null && library.Emotions.ContainsKey(a.Expr)));
            var neutral = record with { Actors = record.Actors.Select(a => a with { Expr = null }).ToList() };

            Assert.True(Differing(Rasterize(renderer.Render(record)), Rasterize(renderer.Render(neutral)), 256, 256) > 50);
        });
    }

    [Fact]
    public void B1BubbleSceneRendersBubbleWithContent()
    {
        WpfHost.Run(() =>
        {
            var library = GameFixture.Library;
            var renderer = new SvgScene(library);
            var record = library.Records(GameLevel.B1).First(r => r.Bubble is not null);
            var bare = record with { Bubble = null };

            var bubble = Rasterize(renderer.Render(record));
            Assert.Equal(256, renderer.Render(record).Width);
            Assert.True(Opaque(bubble) > 1500);
            Assert.True(Differing(bubble, Rasterize(renderer.Render(bare)), 256, 256) > 200);
        });
    }

    [Fact]
    public void PanelsAreRenderedSideBySide()
    {
        WpfHost.Run(() =>
        {
            var library = GameFixture.Library;
            var renderer = new SvgScene(library);
            var record = library.Records(GameLevel.B1).First(r => r.Layout == "panels" && r.Panels.Count >= 2);

            var image = renderer.Render(record, record.Sentences[0]);
            var expected = (record.Panels.Count * 256) + ((record.Panels.Count - 1) * SvgScene.PanelGap);

            Assert.Equal(expected, image.Width);
            Assert.Equal(256, image.Height);
            var bitmap = Rasterize(image);
            var px = Pixels(bitmap);
            var w = bitmap.PixelWidth;
            int Count(int from, int to)
            {
                var n = 0;
                for (var y = 0; y < 256; y++)
                {
                    for (var x = from; x < to; x++)
                    {
                        if (px[(((y * w) + x) * 4) + 3] > 200)
                        {
                            n++;
                        }
                    }
                }

                return n;
            }

            Assert.True(Count(0, 256) > 1500);
            Assert.True(Count(264, w) > 1500);
        });
    }

    [Fact]
    public void FramesAnimateCharactersAndPropStates()
    {
        WpfHost.Run(() =>
        {
            var library = GameFixture.Library;
            var renderer = new SvgScene(library);
            var prop = library.Records(GameLevel.B1).First(r => r.Prop is { States.Count: > 1 });

            var f0 = Rasterize(renderer.Render(prop, null, 0));
            var f3 = Rasterize(renderer.Render(prop, null, 3));
            Assert.True(Differing(f0, f3, 256, 256) > 20, "durum değişimi görünmeli");

            // Statik çizim = 0. kare ve tekrarlanabilir.
            Assert.Equal(0, Differing(f0, Rasterize(renderer.Render(prop, null, 0)), 256, 256));

            var framed = Directory.EnumerateFiles(Path.Combine(library.Directory, "assets", "characters"), "*.svg")
                .Where(f => File.ReadAllText(f).Contains("data-frame=\"2\"", StringComparison.Ordinal))
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .ToHashSet();
            var running = library.Records(GameLevel.A2).First(r => r.Actors.Count == 1 && framed.Contains(r.Actors[0].Char) && r.Actors[0].Expr is null && r.Actors[0].Hold is null);
            Assert.True(Differing(Rasterize(renderer.Render(running, null, 0)), Rasterize(renderer.Render(running, null, 1)), 256, 256) > 20);
        });
    }

    [Fact]
    public void MarkerMovesBesideTheHeadWhenItWouldLeaveTheCanvas()
    {
        var marker = new GamePlacement(0, -34, 0.22);
        var tall = SvgScene.MarkerCenter(new System.Windows.Point(146, 44), marker, 1);
        var low = SvgScene.MarkerCenter(new System.Windows.Point(128, 120), marker, 1);
        var smallActor = SvgScene.MarkerCenter(new System.Windows.Point(128, 120), marker, 0.7);

        Assert.Equal(new System.Windows.Point(146 + 52, 44 + 24), tall);
        Assert.Equal(new System.Windows.Point(128, 120 - 34), low);
        Assert.Equal(new System.Windows.Point(128, 86), smallActor);
    }

    [Fact]
    public void NegatedSentenceDrawsACrossOnTheHeldObject()
    {
        WpfHost.Run(() =>
        {
            var library = GameFixture.Library;
            var renderer = new SvgScene(library);
            var record = library.Records(GameLevel.A1).First(r => r.Actors.Count == 1 && r.Actors[0].Hold is { Obj: "apple" } && r.Actors[0].Char == "tom_stand");
            var hold = record.Actors[0].Hold!;
            var plain = new GameSentence("prog", "Tom", "x", "y", [], [], [], null);
            var negative = plain with { Badges = ["badge_not"] };

            var a = Rasterize(renderer.Render(record, plain));
            var b = Rasterize(renderer.Render(record, negative));
            var left = (int)hold.AnchorX - 20;
            var top = (int)hold.AnchorY - 20;
            var pa = Pixels(a);
            var pb = Pixels(b);
            var diff = 0;
            for (var y = top; y < top + 40; y++)
            {
                for (var x = left; x < left + 40; x++)
                {
                    var i = ((y * 256) + x) * 4;
                    if (pa[i] != pb[i] || pa[i + 1] != pb[i + 1] || pa[i + 2] != pb[i + 2])
                    {
                        diff++;
                    }
                }
            }

            Assert.True(diff > 20, "çarpı tutma noktasında görünmeli");
        });
    }
}

using System.Text;
using Lingora.Core.Corpus;
using Lingora.Core.Models;

namespace Lingora.Tests;

public class CorpusParserTests
{
    [Theory]
    [InlineData("Hi.\tMerhaba.\tCC-BY 2.0 (France) Attribution: tatoeba.org #1", "Hi.", "Merhaba.")]
    [InlineData("Hi.\tMerhaba.", "Hi.", "Merhaba.")]
    [InlineData("﻿Run!\tKoş!\tx", "Run!", "Koş!")]
    [InlineData("  Run!  \t  Kaç!  \r", "Run!", "Kaç!")]
    public void ParsesValidLines(string line, string english, string turkish)
    {
        Assert.True(CorpusParser.TryParseLine(line, out var pair));
        Assert.Equal(english, pair.English);
        Assert.Equal(turkish, pair.Turkish);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Sadece tek sütun")]
    [InlineData("\tMerhaba.")]
    [InlineData("Hi.\t   \tx")]
    public void RejectsInvalidLines(string? line)
    {
        Assert.False(CorpusParser.TryParseLine(line, out _));
    }
}

public class CorpusImporterTests
{
    [Fact]
    public void ImportsUserSampleFile()
    {
        using var dir = new TempDir();
        var source = dir.Write("tur.txt", Samples.TatoebaLines);
        var destination = dir.File("corpus.tsv");

        var result = CorpusImporter.Import(source, destination);

        Assert.Equal(11, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Duplicates);
        var lines = File.ReadAllLines(destination, Encoding.UTF8);
        Assert.Equal(11, lines.Length);
        Assert.Equal("Hi.\tSelam.", lines[0]);
        Assert.DoesNotContain(lines, l => l.Contains("CC-BY", StringComparison.Ordinal));
        Assert.False(File.Exists(destination + ".tmp"));
    }

    [Fact]
    public void SkipsInvalidAndDuplicateLines()
    {
        using var dir = new TempDir();
        var source = dir.Write("tur.txt", "Hi.\tSelam.\tx\r\nbozuk satır\r\n\r\nHi.\tSelam.\ty\r\nRun!\tKoş!\r\n");

        var result = CorpusImporter.Import(source, dir.File("corpus.tsv"));

        Assert.Equal(2, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(1, result.Duplicates);
    }

    [Fact]
    public void FileWithoutValidLinesThrowsAndKeepsExistingCorpus()
    {
        using var dir = new TempDir();
        var destination = dir.Write("corpus.tsv", "Hi.\tSelam.\n");
        var source = dir.Write("bad.txt", "bu dosya\nsekmesiz\n");

        Assert.Throws<InvalidDataException>(() => CorpusImporter.Import(source, destination));

        Assert.Equal("Hi.\tSelam.\n", File.ReadAllText(destination));
        Assert.False(File.Exists(destination + ".tmp"));
    }

    [Fact]
    public void MissingFileThrowsFileNotFound()
    {
        using var dir = new TempDir();
        Assert.Throws<FileNotFoundException>(() => CorpusImporter.Import(dir.File("yok.txt"), dir.File("corpus.tsv")));
    }

    [Fact]
    public void AcceptsQuotedPathFromExplorer()
    {
        using var dir = new TempDir();
        var source = dir.Write("tur.txt", "Hi.\tMerhaba.\n");

        var result = CorpusImporter.Import($"  \"{source}\"  ", dir.File("corpus.tsv"));

        Assert.Equal(1, result.Imported);
    }

    [Fact]
    public void FallsBackToTurkishWindowsEncoding()
    {
        using var dir = new TempDir();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var source = dir.Write("ansi.txt", "Run!\tKoş!\nI see.\tAnlıyorum, çok güzel.\n", Encoding.GetEncoding(1254));
        var destination = dir.File("corpus.tsv");

        var result = CorpusImporter.Import(source, destination);

        Assert.Equal(2, result.Imported);
        Assert.Equal("windows-1254", result.EncodingName);
        var corpus = SentenceCorpus.Load(destination);
        Assert.Equal("Koş!", corpus.GetTurkish(0));
        Assert.Equal("Anlıyorum, çok güzel.", corpus.GetTurkish(1));
    }
}

public class SentenceCorpusTests
{
    private static SentenceCorpus LoadSample()
    {
        using var dir = new TempDir();
        var destination = dir.File("corpus.tsv");
        CorpusImporter.Import(dir.Write("tur.txt", Samples.TatoebaLines), destination);
        return SentenceCorpus.Load(destination);
    }

    [Fact]
    public void LoadsPairsWithTurkishCharacters()
    {
        var corpus = LoadSample();

        Assert.Equal(11, corpus.Count);
        Assert.Equal(new SentencePair("Hi.", "Selam."), corpus.GetPair(0));
        Assert.Equal("Tom gerçekten çok iyi sörf yapmayı bilmediğini söyledi.", corpus.GetTurkish(10));
    }

    [Fact]
    public void EnglishToTurkishReferencesIncludeAllTranslations()
    {
        var corpus = LoadSample();

        var references = corpus.GetReferences(0, TranslationDirection.EnglishToTurkish);

        Assert.Equal(new[] { "Merhaba.", "Selam." }, references.OrderBy(r => r, StringComparer.Ordinal));
    }

    [Fact]
    public void TurkishToEnglishReferencesIncludeAllTranslations()
    {
        var corpus = LoadSample();
        var kac = Enumerable.Range(0, corpus.Count).First(i => corpus.GetTurkish(i) == "Kaç!");

        var item = corpus.CreateItem(kac, TranslationDirection.TurkishToEnglish);

        Assert.Equal("Kaç!", item.Source);
        Assert.Equal(new[] { "Run!", "Run." }, item.References.OrderBy(r => r, StringComparer.Ordinal));
    }

    [Fact]
    public void DifficultyUsesEnglishWordCount()
    {
        var corpus = LoadSample();

        Assert.Equal(6, corpus.GetIndices(Difficulty.Easy).Count);
        Assert.Empty(corpus.GetIndices(Difficulty.Medium));
        Assert.Equal(5, corpus.GetIndices(Difficulty.Hard).Count);
        Assert.Equal(11, corpus.GetIndices(Difficulty.All).Count);
        Assert.Equal(11, corpus.GetWordCount(6));
        Assert.Equal(10, corpus.GetWordCount(7));
    }

    [Fact]
    public void HandlesCrLfBomAndMissingFinalNewLine()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Hi.\tSelam.\r\nRun!\tKoş!")).ToArray();
        var corpus = new SentenceCorpus(bytes);

        Assert.Equal(2, corpus.Count);
        Assert.Equal("Hi.", corpus.GetEnglish(0));
        Assert.Equal("Selam.", corpus.GetTurkish(0));
        Assert.Equal("Koş!", corpus.GetTurkish(1));
    }

    [Fact]
    public void IgnoresAttributionColumnIfPresent()
    {
        var corpus = new SentenceCorpus(Encoding.UTF8.GetBytes("Hi.\tSelam.\tCC-BY\n"));

        Assert.Equal("Selam.", corpus.GetTurkish(0));
    }

    [Fact]
    public void MissingFileGivesEmptyCorpus()
    {
        Assert.Equal(0, SentenceCorpus.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tsv")).Count);
    }

    [Theory]
    [InlineData("Hi.", 1)]
    [InlineData("Tom said  he   knew.", 4)]
    [InlineData("", 0)]
    public void CountsWords(string text, int expected)
    {
        Assert.Equal(expected, SentenceCorpus.CountWords(Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void LargeCorpusLoadsQuickly()
    {
        var pairs = Enumerable.Range(0, 200_000).Select(i => new SentencePair($"Sentence number {i}.", $"Cümle numarası {i}."));
        var corpus = SentenceCorpus.FromPairs(pairs);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var references = corpus.GetReferences(123_456, TranslationDirection.TurkishToEnglish);
        watch.Stop();

        Assert.Equal(200_000, corpus.Count);
        Assert.Equal(new[] { "Sentence number 123456." }, references);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Referans indeksi çok yavaş: {watch.Elapsed}");
    }
}

public class SentencePickerTests
{
    private static SentenceCorpus Corpus(params (string En, string Tr)[] pairs) =>
        SentenceCorpus.FromPairs(pairs.Select(p => new SentencePair(p.En, p.Tr)));

    [Fact]
    public void EmptyCorpusReturnsNull()
    {
        Assert.Null(new SentencePicker(SentenceCorpus.Empty).Next(TranslationDirection.TurkishToEnglish, Difficulty.All));
    }

    [Fact]
    public void FallsBackToAllWhenDifficultyHasNoSentences()
    {
        var picker = new SentencePicker(Corpus(("Hi.", "Selam.")), new Random(1));

        var result = picker.Next(TranslationDirection.EnglishToTurkish, Difficulty.Hard);

        Assert.NotNull(result);
        Assert.True(result!.UsedDifficultyFallback);
        Assert.Equal("Hi.", result.Item.Source);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(1234)]
    public void PrefersUnseenSentences(int seed)
    {
        var picker = new SentencePicker(Corpus(("Hi.", "Selam."), ("Run!", "Koş!"), ("Go.", "Git.")), new Random(seed));
        picker.MarkSeen(TranslationDirection.TurkishToEnglish, "Selam.");
        picker.MarkSeen(TranslationDirection.TurkishToEnglish, "Koş!");

        Assert.Equal("Git.", picker.Next(TranslationDirection.TurkishToEnglish, Difficulty.All)!.Item.Source);
    }

    [Fact]
    public void AvoidingImmediateRepeatWinsOverUnseen()
    {
        // "Git." atlandıysa (görülmedi ama az önce gösterildi) hemen tekrar gelmemeli.
        var picker = new SentencePicker(Corpus(("Hi.", "Selam."), ("Go.", "Git.")), new Random(5));
        picker.MarkSeen(TranslationDirection.TurkishToEnglish, "Selam.");
        Assert.Equal("Git.", picker.Next(TranslationDirection.TurkishToEnglish, Difficulty.All)!.Item.Source);

        Assert.Equal("Selam.", picker.Next(TranslationDirection.TurkishToEnglish, Difficulty.All)!.Item.Source);
    }

    [Fact]
    public void SeenIsTrackedPerDirection()
    {
        var picker = new SentencePicker(Corpus(("Hi.", "Selam.")));
        picker.MarkSeen(TranslationDirection.TurkishToEnglish, "Selam.");

        Assert.True(picker.IsSeen(TranslationDirection.TurkishToEnglish, "Selam."));
        Assert.False(picker.IsSeen(TranslationDirection.EnglishToTurkish, "Selam."));
    }

    [Fact]
    public void DoesNotRepeatPreviousSentenceWhenAlternativesExist()
    {
        var picker = new SentencePicker(Corpus(("Hi.", "Selam."), ("Run!", "Koş!")), new Random(3));
        var previous = picker.Next(TranslationDirection.TurkishToEnglish, Difficulty.All)!.Item.CorpusIndex;

        for (var i = 0; i < 20; i++)
        {
            var next = picker.Next(TranslationDirection.TurkishToEnglish, Difficulty.All)!.Item.CorpusIndex;
            Assert.NotEqual(previous, next);
            previous = next;
        }
    }
}

using Lingora.Core.Models;

namespace Lingora.Core.Corpus;

/// <summary>Seçim sonucu.</summary>
/// <param name="Item">Seçilen cümle.</param>
/// <param name="UsedDifficultyFallback">Seçilen zorlukta cümle olmadığı için tüm havuzdan seçildi.</param>
/// <param name="Wrapped">Sıralı modda dosyanın sonuna gelinip başa dönüldü.</param>
public sealed record PickResult(PracticeItem Item, bool UsedDifficultyFallback, bool Wrapped = false);

/// <summary>
/// Rastgele cümle seçer; daha önce çalışılan cümleleri ve bir önceki cümleyi mümkün olduğunca tekrar etmez.
/// </summary>
public sealed class SentencePicker
{
    private const int MaxTries = 40;

    private readonly SentenceCorpus _corpus;
    private readonly Random _random;
    private readonly HashSet<ulong> _seen = new();
    private int _lastIndex = -1;

    public SentencePicker(SentenceCorpus corpus, Random? random = null)
    {
        _corpus = corpus;
        _random = random ?? Random.Shared;
    }

    public void MarkSeen(TranslationDirection direction, string source) => _seen.Add(Key(direction, source));

    public bool IsSeen(TranslationDirection direction, string source) => _seen.Contains(Key(direction, source));

    public PickResult? Next(TranslationDirection direction, Difficulty difficulty)
    {
        if (_corpus.Count == 0)
        {
            return null;
        }

        var pool = _corpus.GetIndices(difficulty);
        var fallback = false;
        if (pool.Count == 0)
        {
            pool = _corpus.GetIndices(Difficulty.All);
            fallback = difficulty != Difficulty.All;
        }

        var chosen = pool[_random.Next(pool.Count)];
        for (var attempt = 0; attempt < MaxTries; attempt++)
        {
            var candidate = pool[_random.Next(pool.Count)];
            var repeatsLast = candidate == _lastIndex && pool.Count > 1;
            if (repeatsLast)
            {
                continue;
            }

            chosen = candidate;
            if (!IsSeen(direction, _corpus.GetSource(candidate, direction)))
            {
                break;
            }
        }

        _lastIndex = chosen;
        return new PickResult(_corpus.CreateItem(chosen, direction), fallback);
    }

    /// <summary>
    /// Sıralı mod: <paramref name="start"/> indeksinden (dahil) itibaren zorluğa uyan ilk cümle.
    /// Sona gelinirse başa döner; zorluğa uyan hiç cümle yoksa zorluk yok sayılır.
    /// </summary>
    public PickResult? NextInOrder(TranslationDirection direction, Difficulty difficulty, int start)
    {
        var count = _corpus.Count;
        if (count == 0)
        {
            return null;
        }

        var wrapped = false;
        if (start < 0 || start >= count)
        {
            wrapped = start >= count;
            start = 0;
        }

        for (var step = 0; step < count; step++)
        {
            var index = (start + step) % count;
            if (difficulty.Contains(_corpus.GetWordCount(index)))
            {
                _lastIndex = index;
                return new PickResult(_corpus.CreateItem(index, direction), false, wrapped || start + step >= count);
            }
        }

        _lastIndex = start;
        return new PickResult(_corpus.CreateItem(start, direction), difficulty != Difficulty.All, wrapped);
    }

    private static ulong Key(TranslationDirection direction, string source) =>
        Fnv1a.Hash((int)direction + "|" + source);
}

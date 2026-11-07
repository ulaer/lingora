using System.Text;
using Lingora.Core.Models;

namespace Lingora.Core.Corpus;

/// <summary>
/// Bellekte sıkıştırılmış cümle havuzu. Yüz binlerce satırlık dosyalar için string listesi
/// yerine tek bir UTF-8 bayt dizisi ve satır indeksleri tutulur.
/// Satır biçimi: <c>İngilizce&lt;TAB&gt;Türkçe</c>.
/// </summary>
public sealed class SentenceCorpus
{
    private readonly byte[] _data;
    private readonly int[] _start;
    private readonly int[] _tab;
    private readonly int[] _end;
    private readonly byte[] _englishWordCount;
    private readonly Dictionary<Difficulty, int[]> _difficultyCache = new();
    private readonly object _indexLock = new();

    private HashIndex? _englishIndex;
    private HashIndex? _turkishIndex;

    internal SentenceCorpus(byte[] data)
    {
        _data = data;
        var starts = new List<int>();
        var tabs = new List<int>();
        var ends = new List<int>();

        var span = data.AsSpan();
        var position = span.StartsWith("﻿"u8) ? 3 : 0;
        while (position < data.Length)
        {
            var newLine = span[position..].IndexOf((byte)'\n');
            var lineEnd = newLine < 0 ? data.Length : position + newLine;
            var contentEnd = lineEnd > position && data[lineEnd - 1] == (byte)'\r' ? lineEnd - 1 : lineEnd;

            var tabOffset = span[position..contentEnd].IndexOf((byte)'\t');
            if (tabOffset > 0)
            {
                var tab = position + tabOffset;
                var turkishEnd = span[(tab + 1)..contentEnd].IndexOf((byte)'\t');
                var end = turkishEnd < 0 ? contentEnd : tab + 1 + turkishEnd;
                if (end > tab + 1)
                {
                    starts.Add(position);
                    tabs.Add(tab);
                    ends.Add(end);
                }
            }

            position = lineEnd + 1;
        }

        _start = starts.ToArray();
        _tab = tabs.ToArray();
        _end = ends.ToArray();
        _englishWordCount = new byte[_start.Length];
        for (var i = 0; i < _start.Length; i++)
        {
            _englishWordCount[i] = (byte)Math.Min(CountWords(span[_start[i].._tab[i]]), byte.MaxValue);
        }
    }

    public static SentenceCorpus Empty { get; } = new(Array.Empty<byte>());

    public int Count => _start.Length;

    public static SentenceCorpus Load(string path) =>
        File.Exists(path) ? new SentenceCorpus(File.ReadAllBytes(path)) : Empty;

    public static SentenceCorpus FromPairs(IEnumerable<SentencePair> pairs)
    {
        var builder = new StringBuilder();
        foreach (var pair in pairs)
        {
            builder.Append(pair.English).Append('\t').Append(pair.Turkish).Append('\n');
        }

        return new SentenceCorpus(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    public string GetEnglish(int index) => Encoding.UTF8.GetString(EnglishSpan(index));

    public string GetTurkish(int index) => Encoding.UTF8.GetString(TurkishSpan(index));

    public SentencePair GetPair(int index) => new(GetEnglish(index), GetTurkish(index));

    public int GetWordCount(int index) => _englishWordCount[index];

    public string GetSource(int index, TranslationDirection direction) =>
        direction == TranslationDirection.TurkishToEnglish ? GetTurkish(index) : GetEnglish(index);

    /// <summary>
    /// Kaynak cümlesi aynı olan tüm satırların hedef dildeki çevirileri
    /// (ör. "Hi." → "Selam.", "Merhaba.").
    /// </summary>
    public IReadOnlyList<string> GetReferences(int index, TranslationDirection direction)
    {
        var sourceIsTurkish = direction == TranslationDirection.TurkishToEnglish;
        var hashIndex = GetIndex(sourceIsTurkish);
        var source = sourceIsTurkish ? TurkishSpan(index) : EnglishSpan(index);
        var hash = Fnv1a.Hash(source);

        var references = new List<string>();
        for (var k = hashIndex.LowerBound(hash); k < hashIndex.Hashes.Length && hashIndex.Hashes[k] == hash; k++)
        {
            var other = hashIndex.Order[k];
            var otherSource = sourceIsTurkish ? TurkishSpan(other) : EnglishSpan(other);
            if (!otherSource.SequenceEqual(source))
            {
                continue;
            }

            var target = sourceIsTurkish ? GetEnglish(other) : GetTurkish(other);
            if (!references.Contains(target, StringComparer.Ordinal))
            {
                references.Add(target);
            }
        }

        return references;
    }

    public PracticeItem CreateItem(int index, TranslationDirection direction) =>
        new(direction, GetSource(index, direction), GetReferences(index, direction), index);

    /// <summary>Zorluk aralığına uyan satır indeksleri (önbellekli).</summary>
    public IReadOnlyList<int> GetIndices(Difficulty difficulty)
    {
        lock (_difficultyCache)
        {
            if (!_difficultyCache.TryGetValue(difficulty, out var indices))
            {
                var list = new List<int>();
                for (var i = 0; i < Count; i++)
                {
                    if (difficulty.Contains(_englishWordCount[i]))
                    {
                        list.Add(i);
                    }
                }

                indices = list.ToArray();
                _difficultyCache[difficulty] = indices;
            }

            return indices;
        }
    }

    internal static int CountWords(ReadOnlySpan<byte> text)
    {
        var count = 0;
        var inWord = false;
        foreach (var b in text)
        {
            var isSpace = b is (byte)' ' or (byte)'\t';
            if (!isSpace && !inWord)
            {
                count++;
            }

            inWord = !isSpace;
        }

        return count;
    }

    private ReadOnlySpan<byte> EnglishSpan(int index) => _data.AsSpan(_start[index], _tab[index] - _start[index]);

    private ReadOnlySpan<byte> TurkishSpan(int index) => _data.AsSpan(_tab[index] + 1, _end[index] - _tab[index] - 1);

    private HashIndex GetIndex(bool turkish)
    {
        lock (_indexLock)
        {
            if (turkish)
            {
                return _turkishIndex ??= BuildIndex(TurkishSpanFor);
            }

            return _englishIndex ??= BuildIndex(EnglishSpanFor);
        }
    }

    private ReadOnlySpan<byte> EnglishSpanFor(int i) => EnglishSpan(i);

    private ReadOnlySpan<byte> TurkishSpanFor(int i) => TurkishSpan(i);

    private delegate ReadOnlySpan<byte> SpanSelector(int index);

    private HashIndex BuildIndex(SpanSelector selector)
    {
        var hashes = new ulong[Count];
        var order = new int[Count];
        for (var i = 0; i < Count; i++)
        {
            hashes[i] = Fnv1a.Hash(selector(i));
            order[i] = i;
        }

        Array.Sort(hashes, order);
        return new HashIndex(hashes, order);
    }

    private sealed record HashIndex(ulong[] Hashes, int[] Order)
    {
        public int LowerBound(ulong hash)
        {
            int low = 0, high = Hashes.Length;
            while (low < high)
            {
                var mid = low + ((high - low) / 2);
                if (Hashes[mid] < hash)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            return low;
        }
    }
}

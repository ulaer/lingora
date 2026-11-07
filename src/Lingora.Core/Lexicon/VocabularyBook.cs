using System.Globalization;
using System.Text;
using System.Text.Json;
using Lingora.Core.Models;
using Lingora.Core.Storage;

namespace Lingora.Core.Lexicon;

/// <summary>Sözlükteki tek kayıt. Aynı (veya çok benzer) terim tekrar gelirse yeni kayıt açılmaz, sayaç artar.</summary>
public sealed class VocabularyEntry
{
    public string Term { get; set; } = string.Empty;

    public string Meaning { get; set; } = string.Empty;

    /// <summary>"kelime", "kalıp" veya "tamlama".</summary>
    public string Kind { get; set; } = "kelime";

    /// <summary>Farklı örnek cümleler (en fazla <see cref="VocabularyBook.MaxExamples"/>).</summary>
    public List<string> Examples { get; set; } = new();

    /// <summary>Bu terimde kaç kez eksik/hata görüldü.</summary>
    public int MistakeCount { get; set; } = 1;

    public DateTimeOffset FirstSeen { get; set; }

    public DateTimeOffset LastSeen { get; set; }

    /// <summary>Terimin geçtiği son kaynak cümle.</summary>
    public string SourceSentence { get; set; } = string.Empty;
}

public enum VocabularySort
{
    /// <summary>En son hata yapılan en üstte.</summary>
    Recent,

    /// <summary>En çok hata yapılan en üstte.</summary>
    MostMistakes,

    /// <summary>Alfabetik.</summary>
    Alphabetical,
}

/// <summary>Bir değerlendirmeden sonra sözlüğe ne eklendiği.</summary>
/// <param name="Added">Yeni açılan kayıtlar.</param>
/// <param name="Repeated">Zaten sözlükte olan ve sayacı artan kayıtlar.</param>
public sealed record VocabularyMergeResult(IReadOnlyList<VocabularyEntry> Added, IReadOnlyList<VocabularyEntry> Repeated)
{
    public static VocabularyMergeResult Empty { get; } = new([], []);

    public int Total => Added.Count + Repeated.Count;
}

/// <summary>
/// Uygulama içi sözlük (vocabulary.json). Gemini'nin her değerlendirmede döndürdüğü eksik kelime,
/// kalıp ve tamlamaları birleştirerek saklar; benzer terimler tek kayıtta toplanır.
/// </summary>
public sealed class VocabularyBook
{
    public const int MaxExamples = 3;

    private static readonly string[] LeadingFillers = ["to ", "a ", "an ", "the "];
    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;

    private readonly string _path;
    private readonly List<VocabularyEntry> _entries;
    private readonly Dictionary<string, VocabularyEntry> _byKey = new(StringComparer.Ordinal);

    public VocabularyBook(string path)
    {
        _path = path;
        _entries = LoadFile(path, out var corrupt);
        LoadWarning = corrupt;
        foreach (var entry in _entries.ToList())
        {
            var key = NormalizeKey(entry.Term);
            if (key.Length == 0 || !_byKey.TryAdd(key, entry))
            {
                // Elle düzenlenmiş dosyada yinelenen kayıt: birleştir.
                if (key.Length > 0)
                {
                    MergeInto(_byKey[key], entry);
                }

                _entries.Remove(entry);
            }
        }
    }

    /// <summary>Dosya bozuksa yedeğin yolu (kayıtlar boş başlar).</summary>
    public string? LoadWarning { get; }

    public int Count => _entries.Count;

    public IReadOnlyList<VocabularyEntry> Entries => _entries;

    /// <summary>
    /// "To Be Supposed To!" ve "be supposed to" gibi benzer yazımları aynı anahtara indirger:
    /// küçük harf, baştaki/sondaki noktalama, fazla boşluklar ve baştaki to/a/an/the atılır.
    /// </summary>
    public static string NormalizeKey(string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return string.Empty;
        }

        var lowered = term.Trim().ToLowerInvariant().Replace('’', '\'').Replace('‘', '\'');
        var builder = new StringBuilder(lowered.Length);
        var lastWasSpace = false;
        foreach (var c in lowered)
        {
            if (char.IsLetterOrDigit(c) || c is '\'' or '-')
            {
                builder.Append(c);
                lastWasSpace = false;
            }
            else if (!lastWasSpace && builder.Length > 0)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        var key = builder.ToString().Trim(' ', '-', '\'');
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var filler in LeadingFillers)
            {
                if (key.StartsWith(filler, StringComparison.Ordinal) && key.Length > filler.Length)
                {
                    key = key[filler.Length..];
                    changed = true;
                }
            }
        }

        return key;
    }

    public VocabularyEntry? Find(string term) =>
        _byKey.TryGetValue(NormalizeKey(term), out var entry) ? entry : null;

    /// <summary>Yeni terimleri ekler, bilinenlerin sayacını artırır ve dosyaya kaydeder.</summary>
    public VocabularyMergeResult Merge(IEnumerable<VocabularyItem> items, string sourceSentence, DateTimeOffset now)
    {
        var added = new List<VocabularyEntry>();
        var repeated = new List<VocabularyEntry>();
        var seenInBatch = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            var key = NormalizeKey(item.Term);
            if (key.Length == 0 || !seenInBatch.Add(key))
            {
                continue;
            }

            if (_byKey.TryGetValue(key, out var existing))
            {
                existing.MistakeCount++;
                existing.LastSeen = now;
                existing.SourceSentence = sourceSentence;
                if (existing.Meaning.Length == 0)
                {
                    existing.Meaning = item.Meaning.Trim();
                }

                AddExample(existing, item.Example);
                repeated.Add(existing);
                continue;
            }

            var entry = new VocabularyEntry
            {
                Term = item.Term.Trim(),
                Meaning = item.Meaning.Trim(),
                Kind = string.IsNullOrWhiteSpace(item.Kind) ? "kelime" : item.Kind.Trim(),
                MistakeCount = 1,
                FirstSeen = now,
                LastSeen = now,
                SourceSentence = sourceSentence,
            };
            AddExample(entry, item.Example);
            _entries.Add(entry);
            _byKey[key] = entry;
            added.Add(entry);
        }

        if (added.Count + repeated.Count > 0)
        {
            Save();
        }

        return new VocabularyMergeResult(added, repeated);
    }

    public bool Remove(VocabularyEntry entry)
    {
        if (!_entries.Remove(entry))
        {
            return false;
        }

        _byKey.Remove(NormalizeKey(entry.Term));
        Save();
        return true;
    }

    /// <summary>Arama (terim, anlam veya örnekte; büyük/küçük harf duyarsız) ve sıralama.</summary>
    public IReadOnlyList<VocabularyEntry> Query(string? search, VocabularySort sort)
    {
        IEnumerable<VocabularyEntry> query = _entries;
        var text = search?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            query = query.Where(e =>
                Contains(e.Term, text) || Contains(e.Meaning, text) || e.Examples.Any(x => Contains(x, text)));
        }

        query = sort switch
        {
            VocabularySort.MostMistakes => query.OrderByDescending(e => e.MistakeCount).ThenByDescending(e => e.LastSeen),
            VocabularySort.Alphabetical => query.OrderBy(e => NormalizeKey(e.Term), StringComparer.Ordinal),
            _ => query.OrderByDescending(e => e.LastSeen),
        };

        return query.ToList();
    }

    private static bool Contains(string source, string value) =>
        Compare.IndexOf(source, value, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    private static void AddExample(VocabularyEntry entry, string? example)
    {
        var text = example?.Trim();
        if (string.IsNullOrEmpty(text) || entry.Examples.Contains(text, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        entry.Examples.Insert(0, text);
        if (entry.Examples.Count > MaxExamples)
        {
            entry.Examples.RemoveRange(MaxExamples, entry.Examples.Count - MaxExamples);
        }
    }

    private static void MergeInto(VocabularyEntry target, VocabularyEntry duplicate)
    {
        target.MistakeCount += duplicate.MistakeCount;
        if (duplicate.LastSeen > target.LastSeen)
        {
            target.LastSeen = duplicate.LastSeen;
        }

        foreach (var example in duplicate.Examples)
        {
            AddExample(target, example);
        }
    }

    private static List<VocabularyEntry> LoadFile(string path, out string? corruptBackup)
    {
        corruptBackup = null;
        if (!File.Exists(path))
        {
            return new List<VocabularyEntry>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<VocabularyEntry>>(File.ReadAllText(path, Encoding.UTF8), JsonDefaults.Indented)
                   ?? new List<VocabularyEntry>();
        }
        catch (JsonException)
        {
            corruptBackup = $"{path}.bozuk-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(path, corruptBackup, overwrite: true);
            return new List<VocabularyEntry>();
        }
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_entries, JsonDefaults.Indented), new UTF8Encoding(false));
        File.Move(temp, _path, overwrite: true);
    }
}

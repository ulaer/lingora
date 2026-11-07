using System.Globalization;
using System.Text.Json;
using Lingora.Core.Storage;

namespace Lingora.Core.Journal;

/// <summary>
/// Günlük: her gün için ayrı dosya (journal/yyyy-MM-dd.json). O günün değerlendirilmiş çevirileri,
/// hataları, ipuçları ve istenen ayrıntılı açıklamalar burada tutulur.
/// </summary>
public sealed class JournalStore
{
    private const string DateFormat = "yyyy-MM-dd";
    private readonly string _directory;
    private readonly string _lessonDirectory;
    private readonly TimeZoneInfo _zone;

    public JournalStore(string directory, string lessonDirectory, TimeZoneInfo zone)
    {
        _directory = directory;
        _lessonDirectory = lessonDirectory;
        _zone = zone;
    }

    /// <summary>Okunamayan (bozuk) gün dosyaları; yedeklenip boş kabul edilir.</summary>
    public List<string> Warnings { get; } = new();

    public DateOnly DayOf(DateTimeOffset timestamp) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timestamp, _zone).DateTime);

    public void Append(JournalEntry entry)
    {
        var day = DayOf(entry.Timestamp);
        var entries = LoadDay(day);
        entries.Add(entry);
        WriteDay(day, entries);
    }

    /// <summary>Kayıt değişti (ör. açıklama eklendi); aynı kimlikli kaydı günceller.</summary>
    public void Update(JournalEntry entry)
    {
        var day = DayOf(entry.Timestamp);
        var entries = LoadDay(day);
        var index = entries.FindIndex(e => e.Id == entry.Id);
        if (index < 0)
        {
            entries.Add(entry);
        }
        else
        {
            entries[index] = entry;
        }

        WriteDay(day, entries);
    }

    public List<JournalEntry> LoadDay(DateOnly day)
    {
        var path = DayFile(day);
        if (!File.Exists(path))
        {
            return new List<JournalEntry>();
        }

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<List<JournalEntry>>(stream, JsonDefaults.Indented) ?? new List<JournalEntry>();
        }
        catch (JsonException)
        {
            var backup = path + ".bozuk-" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Move(path, backup, overwrite: true);
            Warnings.Add($"Günlük dosyası bozuktu, yedeklendi: {backup}");
            return new List<JournalEntry>();
        }
    }

    /// <summary>Kaydı olan veya ders notu olan günler, yeniden eskiye.</summary>
    public IReadOnlyList<JournalDay> Days()
    {
        var dates = new SortedSet<DateOnly>();
        foreach (var directory in new[] { _directory, _lessonDirectory })
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                if (DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(file), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    dates.Add(date);
                }
            }
        }

        return dates.Reverse().Select(Summarize).ToList();
    }

    public JournalDay Summarize(DateOnly date)
    {
        var entries = LoadDay(date);
        return new JournalDay(
            date,
            entries.Count,
            entries.Sum(e => e.Mistakes.Count),
            entries.Count(e => e.Tip.Length > 0),
            entries.Sum(e => e.Explanations.Count),
            File.Exists(LessonStore.FileFor(_lessonDirectory, date)),
            new AudioLessonStore(_lessonDirectory).Load(date) is not null);
    }

    internal static string FileName(DateOnly day) => day.ToString(DateFormat, CultureInfo.InvariantCulture) + ".json";

    private string DayFile(DateOnly day) => Path.Combine(_directory, FileName(day));

    private void WriteDay(DateOnly day, List<JournalEntry> entries)
    {
        Directory.CreateDirectory(_directory);
        AtomicFile.WriteJson(DayFile(day), entries);
    }
}

/// <summary>Üretilmiş ders notları: lessons/yyyy-MM-dd.json. Bir kez yazılır, sonra yalnızca okunur.</summary>
public sealed class LessonStore
{
    private readonly string _directory;

    public LessonStore(string directory) => _directory = directory;

    public static string FileFor(string directory, DateOnly day) => Path.Combine(directory, JournalStore.FileName(day));

    public LessonNote? Load(DateOnly day)
    {
        var path = FileFor(_directory, day);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<LessonNote>(stream, JsonDefaults.Indented);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(LessonNote note)
    {
        Directory.CreateDirectory(_directory);
        AtomicFile.WriteJson(FileFor(_directory, note.Date), note);
    }
}

internal static class AtomicFile
{
    /// <summary>Önce geçici dosyaya yazar, sonra yerine taşır (yarım dosya kalmaz).</summary>
    public static void WriteJson<T>(string path, T value)
    {
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        {
            JsonSerializer.Serialize(stream, value, JsonDefaults.Indented);
        }

        File.Move(temp, path, overwrite: true);
    }
}

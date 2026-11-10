using System.Globalization;
using System.Text.Json;
using Lingora.Core.Storage;

namespace Lingora.Core.Game;

/// <summary>game-log.jsonl satırı: bir cevap.</summary>
public sealed record GameLogEntry(
    DateTimeOffset Time,
    string RecordId,
    string Form,
    GameLevel Level,
    string Mode,
    bool Correct,
    string? DistractorId);

/// <summary>Yeniden sorulacak (kaçırılan) resim.</summary>
public sealed class ReviewItem
{
    public string Id { get; set; } = string.Empty;

    public GameLevel Level { get; set; }

    /// <summary>Bu tur numarasında ya da sonrasında tekrar gelir.</summary>
    public int Due { get; set; }

    /// <summary>Art arda doğru cevap sayısı; 2'de kuyruktan çıkar.</summary>
    public int Streak { get; set; }
}

public sealed class ReviewState
{
    public int Round { get; set; }

    public List<ReviewItem> Items { get; set; } = [];
}

/// <summary>
/// Oyun ilerlemesi: cevap kaydı (game-log.jsonl), kaçırılanı yeniden sorma kuyruğu (game-review.json, oturumlar arası
/// kalıcı) ve seviye başına doğruluk / üst seviye önerisi.
/// </summary>
public sealed class GameProgress
{
    /// <summary>Son kaç cevaba bakılır ve hangi oranda üst seviye önerilir.</summary>
    public const int Window = 20;

    public const double SuggestRatio = 0.8;

    private readonly string _logFile;
    private readonly string _reviewFile;
    private readonly TimeProvider _time;

    public GameProgress(AppPaths paths, TimeProvider time)
    {
        _logFile = paths.GameLogFile;
        _reviewFile = paths.GameReviewFile;
        _time = time;
        State = LoadState();
    }

    public ReviewState State { get; private set; }

    /// <summary>Tüm cevaplar (eskiden yeniye).</summary>
    public IReadOnlyList<GameLogEntry> Log()
    {
        var list = new List<GameLogEntry>();
        if (!File.Exists(_logFile))
        {
            return list;
        }

        foreach (var line in File.ReadLines(_logFile))
        {
            try
            {
                if (JsonSerializer.Deserialize<GameLogEntry>(line, JsonDefaults.Compact) is { } e)
                {
                    list.Add(e);
                }
            }
            catch (JsonException)
            {
                // bozuk satır atlanır
            }
        }

        return list;
    }

    /// <summary>Yeni bir tur başladı (kuyruk sayacı).</summary>
    public void Advance()
    {
        State.Round++;
        SaveState();
    }

    /// <summary>Vakti gelmiş, kaçırılan resmin kimliği (yoksa null).</summary>
    public string? DueRecordId(GameLevel level) =>
        State.Items.Where(i => i.Level == level && i.Due <= State.Round).OrderBy(i => i.Due).FirstOrDefault()?.Id;

    /// <summary>Cevabı günlüğe yazar; yanlışsa 3–5 tur sonrasına kuyruğa koyar, kuyruktakini doğruysa ilerletir.</summary>
    public void Record(GameRound round, string mode, bool correct, string? distractorId, Random random)
    {
        var entry = new GameLogEntry(_time.GetLocalNow(), round.Record.Id, round.Sentence.Form, round.Level, mode, correct, distractorId);
        try
        {
            File.AppendAllText(_logFile, JsonSerializer.Serialize(entry, JsonDefaults.Compact) + Environment.NewLine);
        }
        catch (IOException)
        {
            // günlük yazılamazsa oyun devam eder
        }

        var item = State.Items.FirstOrDefault(i => i.Id == entry.RecordId && i.Level == entry.Level);
        if (!correct)
        {
            if (item is null)
            {
                State.Items.Add(item = new ReviewItem { Id = entry.RecordId, Level = entry.Level });
            }

            item.Streak = 0;
            item.Due = State.Round + random.Next(3, 6);
        }
        else if (item is not null)
        {
            item.Streak++;
            if (item.Streak >= 2)
            {
                State.Items.Remove(item);
            }
            else
            {
                item.Due = State.Round + random.Next(3, 6);
            }
        }

        SaveState();
    }

    /// <summary>Seviyedeki toplam doğru / toplam cevap.</summary>
    public (int Correct, int Total) Accuracy(GameLevel level)
    {
        var all = Log().Where(e => e.Level == level).ToList();
        return (all.Count(e => e.Correct), all.Count);
    }

    /// <summary>Son 20 cevabın %80'i doğruysa bir üst seviye (kilit yok, öneri); yoksa null.</summary>
    public GameLevel? SuggestedLevel(GameLevel level)
    {
        if (level == GameLevel.B1)
        {
            return null;
        }

        var last = Log().Where(e => e.Level == level).TakeLast(Window).ToList();
        return last.Count == Window && last.Count(e => e.Correct) >= Window * SuggestRatio ? level + 1 : null;
    }

    /// <summary>Ekranda gösterilen özet: seviye başına doğruluk ve öneri.</summary>
    public string Summary(GameLevel current)
    {
        var parts = new List<string>();
        foreach (var level in Enum.GetValues<GameLevel>())
        {
            var (c, t) = Accuracy(level);
            if (t > 0)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{level} %{100 * c / t} ({c}/{t})"));
            }
        }

        var text = parts.Count == 0 ? string.Empty : "Doğruluk: " + string.Join(" · ", parts);
        if (SuggestedLevel(current) is { } next)
        {
            text += (text.Length == 0 ? string.Empty : "  ") + $"{current} seviyesinde son {Window} cevabın %80'i doğru, {next} seviyesini deneyebilirsin.";
        }

        return text;
    }

    private ReviewState LoadState()
    {
        try
        {
            if (File.Exists(_reviewFile))
            {
                return JsonSerializer.Deserialize<ReviewState>(File.ReadAllText(_reviewFile), JsonDefaults.Indented) ?? new ReviewState();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // bozuk dosya: temiz başla
        }

        return new ReviewState();
    }

    private void SaveState()
    {
        try
        {
            File.WriteAllText(_reviewFile, JsonSerializer.Serialize(State, JsonDefaults.Indented));
        }
        catch (IOException)
        {
            // kuyruk yazılamazsa oyun devam eder
        }
    }
}

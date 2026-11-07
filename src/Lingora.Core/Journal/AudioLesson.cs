using System.Globalization;
using System.Text;
using System.Text.Json;
using Lingora.Core.Storage;

namespace Lingora.Core.Journal;

/// <summary>Bir günün kayıtlarından üretilen, yalnızca metin olan ve MP3'e seslendirilen ders.</summary>
public sealed class AudioLesson
{
    public DateOnly Date { get; set; }

    /// <summary>Seslendirilen anlatım metni (yalnızca düz yazı).</summary>
    public string Script { get; set; } = string.Empty;

    /// <summary>Üretildiği andaki kayıt sayısı (yeni çeviri yapıldıysa yenilenebilir).</summary>
    public int EntryCount { get; set; }

    public string Voice { get; set; } = string.Empty;

    public string TtsModel { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public double DurationSeconds { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Seslendirilmiş ham ses: 16 bit, tek kanal PCM.</summary>
public sealed record SpeechAudio(byte[] Pcm, int SampleRate)
{
    public double DurationSeconds => SampleRate <= 0 ? 0 : Pcm.Length / 2.0 / SampleRate;
}

/// <summary>PCM sesi MP3 dosyasına çevirir (Windows'ta Media Foundation; testlerde sahtesi).</summary>
public interface IAudioEncoder
{
    void EncodeMp3(SpeechAudio audio, string path);
}

/// <summary>
/// Sesli dersler: lessons/yyyy-MM-dd.audio.json (anlatım metni ve bilgiler) ve lessons/yyyy-MM-dd.mp3.
/// </summary>
public sealed class AudioLessonStore
{
    private readonly string _directory;

    public AudioLessonStore(string directory) => _directory = directory;

    public string AudioFile(DateOnly date) => Path.Combine(_directory, Stem(date) + ".mp3");

    public string ScriptFile(DateOnly date) => Path.Combine(_directory, Stem(date) + ".audio.json");

    /// <summary>Hem metin hem MP3 varsa sesli dersi döner.</summary>
    public AudioLesson? Load(DateOnly date)
    {
        var script = ScriptFile(date);
        if (!File.Exists(script) || !File.Exists(AudioFile(date)))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(script);
            return JsonSerializer.Deserialize<AudioLesson>(stream, JsonDefaults.Indented);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>MP3'ü geçici dosyadan yerine taşır, sonra metni yazar (yarım kayıt kalmaz).</summary>
    public void Save(AudioLesson lesson, string encodedMp3TempPath)
    {
        Directory.CreateDirectory(_directory);
        File.Move(encodedMp3TempPath, AudioFile(lesson.Date), overwrite: true);
        AtomicFile.WriteJson(ScriptFile(lesson.Date), lesson);
    }

    public string NewTempMp3Path(DateOnly date)
    {
        Directory.CreateDirectory(_directory);
        return Path.Combine(_directory, Stem(date) + ".tmp-" + Guid.NewGuid().ToString("N")[..8] + ".mp3");
    }

    private static string Stem(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>Anlatım metnini seslendirme isteklerine uygun parçalara böler.</summary>
public static class SpeechChunker
{
    /// <summary>Bir seslendirme isteğine giden en fazla karakter (uzun metinler parça parça seslendirilir).</summary>
    public const int MaxChunkLength = 1400;

    public static IReadOnlyList<string> Split(string script, int maxLength = MaxChunkLength)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();

        void Flush()
        {
            if (current.Length > 0)
            {
                chunks.Add(current.ToString().Trim());
                current.Clear();
            }
        }

        var paragraphs = (script ?? string.Empty)
            .Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var paragraph in paragraphs)
        {
            foreach (var piece in SplitLong(paragraph, maxLength))
            {
                if (current.Length > 0 && current.Length + 2 + piece.Length > maxLength)
                {
                    Flush();
                }

                if (current.Length > 0)
                {
                    current.Append("\n\n");
                }

                current.Append(piece);
            }
        }

        Flush();
        return chunks;
    }

    private static IEnumerable<string> SplitLong(string paragraph, int maxLength)
    {
        if (paragraph.Length <= maxLength)
        {
            yield return paragraph;
            yield break;
        }

        var sentence = new StringBuilder();
        foreach (var part in System.Text.RegularExpressions.Regex.Split(paragraph, @"(?<=[.!?…])\s+"))
        {
            if (sentence.Length > 0 && sentence.Length + 1 + part.Length > maxLength)
            {
                yield return sentence.ToString();
                sentence.Clear();
            }

            if (sentence.Length > 0)
            {
                sentence.Append(' ');
            }

            sentence.Append(part);
        }

        if (sentence.Length > 0)
        {
            yield return sentence.ToString();
        }
    }
}

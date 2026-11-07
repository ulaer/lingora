using System.Text;
using System.Text.Json;
using Lingora.Core.Models;
using Lingora.Core.Storage;

namespace Lingora.Core.Progress;

/// <summary>
/// Denemeleri JSON Lines biçiminde (her satır bir kayıt) ekleme-yalnız dosyada saklar.
/// Bozuk satırlar atlanır, diğer kayıtlar kaybolmaz.
/// </summary>
public sealed class ProgressStore
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private readonly string _path;

    public ProgressStore(string path) => _path = path;

    /// <summary>Son okumada atlanan bozuk satır sayısı.</summary>
    public int LastCorruptLineCount { get; private set; }

    public void Append(AttemptRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var line = JsonSerializer.Serialize(record, JsonDefaults.Compact) + "\n";
        File.AppendAllText(_path, line, Utf8NoBom);
    }

    public List<AttemptRecord> LoadAll()
    {
        LastCorruptLineCount = 0;
        var records = new List<AttemptRecord>();
        if (!File.Exists(_path))
        {
            return records;
        }

        foreach (var line in File.ReadLines(_path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var record = JsonSerializer.Deserialize<AttemptRecord>(line, JsonDefaults.Compact);
                if (record is not null)
                {
                    records.Add(record);
                }
            }
            catch (JsonException)
            {
                LastCorruptLineCount++;
            }
        }

        return records;
    }
}

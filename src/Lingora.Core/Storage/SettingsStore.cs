using System.Text;
using System.Text.Json;
using Lingora.Core.Models;

namespace Lingora.Core.Storage;

/// <summary>settings.json okur/yazar. Bozuk dosyada varsayılanlara döner ve bozuk dosyayı yedekler.</summary>
public sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore(string path) => _path = path;

    /// <summary>Son yüklemede bozuk dosya yedeklendiyse yedeğin yolu.</summary>
    public string? LastBackupPath { get; private set; }

    public AppSettings Load()
    {
        LastBackupPath = null;
        if (!File.Exists(_path))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_path, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonDefaults.Indented) ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch (JsonException)
        {
            var backup = $"{_path}.bozuk-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(_path, backup, overwrite: true);
            LastBackupPath = backup;
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonDefaults.Indented), new UTF8Encoding(false));
        File.Move(temp, _path, overwrite: true);
    }
}

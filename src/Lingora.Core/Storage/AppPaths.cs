namespace Lingora.Core.Storage;

/// <summary>Uygulamanın veri dosyalarının konumları.</summary>
public sealed class AppPaths
{
    /// <summary>Veri klasörünü değiştirmek için ortam değişkeni (testler ve taşınabilir kullanım).</summary>
    public const string HomeEnvironmentVariable = "LINGORA_HOME";

    public AppPaths(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            throw new ArgumentException("Veri klasörü boş olamaz.", nameof(dataDirectory));
        }

        DataDirectory = Path.GetFullPath(dataDirectory);
    }

    public string DataDirectory { get; }

    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public string CorpusFile => Path.Combine(DataDirectory, "corpus.tsv");

    public string AttemptsFile => Path.Combine(DataDirectory, "attempts.jsonl");

    public string VocabularyFile => Path.Combine(DataDirectory, "vocabulary.json");

    /// <summary>Uygulamanın eski adı; bu adla oluşmuş veri klasörü ilk açılışta taşınır.</summary>
    public const string LegacyFolderName = "CeviriPratik";

    /// <summary>%APPDATA%\Lingora veya LINGORA_HOME.</summary>
    public static AppPaths Default()
    {
        var overridden = Environment.GetEnvironmentVariable(HomeEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return new AppPaths(overridden);
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var paths = new AppPaths(Path.Combine(appData, "Lingora"));
        paths.MigrateFrom(Path.Combine(appData, LegacyFolderName));
        return paths;
    }

    /// <summary>
    /// Yeni klasör henüz yoksa eski klasördeki verileri (ayarlar, şifreli anahtar, cümleler,
    /// ilerleme, sözlük) buraya taşır. Taşıma başarısız olursa eski klasör yerinde kalır.
    /// </summary>
    public bool MigrateFrom(string legacyDirectory)
    {
        if (Directory.Exists(DataDirectory) || !Directory.Exists(legacyDirectory))
        {
            return false;
        }

        try
        {
            Directory.Move(legacyDirectory, DataDirectory);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void EnsureCreated() => Directory.CreateDirectory(DataDirectory);
}

using Lingora.Core;
using Lingora.Core.Storage;

namespace Lingora.Tests;

public class LegacyDataMigrationTests
{
    [Fact]
    public void MovesOldCeviriPratikFolderWhenNewFolderMissing()
    {
        using var dir = new TempDir();
        var legacy = dir.File("CeviriPratik");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "{\"position\": 1234}");
        File.WriteAllText(Path.Combine(legacy, "vocabulary.json"), "[]");

        var paths = new AppPaths(dir.File("Lingora"));
        Assert.True(paths.MigrateFrom(legacy));

        Assert.False(Directory.Exists(legacy));
        Assert.Equal("{\"position\": 1234}", File.ReadAllText(paths.SettingsFile));
        Assert.True(File.Exists(paths.VocabularyFile));
    }

    [Fact]
    public void DoesNotTouchAnythingWhenNewFolderAlreadyExists()
    {
        using var dir = new TempDir();
        var legacy = dir.File("CeviriPratik");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "old");
        var paths = new AppPaths(dir.File("Lingora"));
        paths.EnsureCreated();
        File.WriteAllText(paths.SettingsFile, "new");

        Assert.False(paths.MigrateFrom(legacy));
        Assert.Equal("new", File.ReadAllText(paths.SettingsFile));
        Assert.True(Directory.Exists(legacy));
    }

    [Fact]
    public void NoLegacyFolderIsFine()
    {
        using var dir = new TempDir();
        var paths = new AppPaths(dir.File("Lingora"));
        Assert.False(paths.MigrateFrom(dir.File("CeviriPratik")));
    }

    [Fact]
    public void KeySavedByOldVersionStillDecrypts()
    {
        // DPAPI entropy değeri eski adla kaldı; eski sürümde kaydedilen anahtar okunmaya devam eder.
        var encrypted = System.Security.Cryptography.ProtectedData.Protect(
            "AIzaTEST-legacy-key-000"u8.ToArray(),
            "CeviriPratik.ApiKey.v1"u8.ToArray(),
            System.Security.Cryptography.DataProtectionScope.CurrentUser);
        var settings = new Lingora.Core.Models.AppSettings { ProtectedApiKey = "dpapi:" + Convert.ToBase64String(encrypted) };

        var lookup = new ApiKeyStore(_ => null).Get(settings);

        Assert.Equal("AIzaTEST-legacy-key-000", lookup.Key);
    }
}

public class HelpTopicsTests
{
    [Fact]
    public void ApiKeyHelpExplainsWhereToGetKeyWithoutContainingOne()
    {
        var topic = HelpTopics.ApiKey;
        Assert.StartsWith("https://aistudio.google.com", topic.LinkUrl);
        Assert.True(topic.Steps.Count >= 4);
        Assert.Contains(topic.Steps, s => s.Contains("Kaydet"));
        Assert.Contains(topic.Notes, n => n.Contains("DPAPI"));
    }

    [Fact]
    public void CorpusHelpPointsToTatoebaTurkishFile()
    {
        var topic = HelpTopics.CorpusFile;
        Assert.StartsWith("https://www.manythings.org/anki", topic.LinkUrl);
        Assert.Contains(topic.Steps, s => s.Contains("tur-eng.zip"));
        Assert.Contains(topic.Steps, s => s.Contains("Gözat"));
        Assert.Contains(topic.Notes, n => n.Contains("<TAB>"));
    }
}

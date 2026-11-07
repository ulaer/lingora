using Lingora.Core.Models;
using Lingora.Core.Storage;

namespace Lingora.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void MissingFileGivesDefaults()
    {
        using var dir = new TempDir();
        var settings = new SettingsStore(dir.File("settings.json")).Load();

        Assert.Equal(TranslationDirection.TurkishToEnglish, settings.Direction);
        Assert.Equal(AppSettings.DefaultDailyGoal, settings.DailyGoal);
        Assert.Equal(Difficulty.All, settings.Difficulty);
        Assert.Equal("gemini-flash-latest", settings.Model);
        Assert.Null(settings.ProtectedApiKey);
    }

    [Fact]
    public void RoundTripsAllValues()
    {
        using var dir = new TempDir();
        var store = new SettingsStore(dir.File("sub/settings.json"));
        store.Save(new AppSettings
        {
            Direction = TranslationDirection.EnglishToTurkish,
            DailyGoal = 35,
            Difficulty = Difficulty.Medium,
            Model = "gemini-2.5-flash",
            ProtectedApiKey = "dpapi:abc",
        });

        var loaded = store.Load();

        Assert.Equal(TranslationDirection.EnglishToTurkish, loaded.Direction);
        Assert.Equal(35, loaded.DailyGoal);
        Assert.Equal(Difficulty.Medium, loaded.Difficulty);
        Assert.Equal("gemini-2.5-flash", loaded.Model);
        Assert.Equal("dpapi:abc", loaded.ProtectedApiKey);
        Assert.Contains("\"EnglishToTurkish\"", File.ReadAllText(dir.File("sub/settings.json")));
    }

    [Fact]
    public void CorruptFileIsBackedUpAndDefaultsReturned()
    {
        using var dir = new TempDir();
        var path = dir.Write("settings.json", "{ bozuk json");
        var store = new SettingsStore(path);

        var settings = store.Load();

        Assert.Equal(AppSettings.DefaultDailyGoal, settings.DailyGoal);
        Assert.NotNull(store.LastBackupPath);
        Assert.Equal("{ bozuk json", File.ReadAllText(store.LastBackupPath!));
    }

    [Fact]
    public void HandEditedValuesAreNormalized()
    {
        using var dir = new TempDir();
        var path = dir.Write("settings.json", "{\"dailyGoal\":-5,\"model\":\" models/gemini-x \",\"difficulty\":\"Hard\"}");

        var settings = new SettingsStore(path).Load();

        Assert.Equal(1, settings.DailyGoal);
        Assert.Equal("gemini-x", settings.Model);
        Assert.Equal(Difficulty.Hard, settings.Difficulty);
    }

    [Theory]
    [InlineData(null, "gemini-flash-latest")]
    [InlineData("   ", "gemini-flash-latest")]
    [InlineData("models/gemini-2.5-flash", "gemini-2.5-flash")]
    [InlineData(" gemini-1.5-flash ", "gemini-1.5-flash")]
    public void NormalizesModelNames(string? input, string expected)
    {
        Assert.Equal(expected, AppSettings.NormalizeModelName(input));
    }
}

public class ApiKeyStoreTests
{
    private const string Key = "AIzaTEST-1234567890-abcdefXYZ";

    [Fact]
    public void StoresKeyEncryptedAndReadsItBack()
    {
        var store = new ApiKeyStore(_ => null);
        var settings = new AppSettings();

        store.Set(settings, $"  {Key}  ");
        var lookup = store.Get(settings);

        Assert.NotNull(settings.ProtectedApiKey);
        Assert.DoesNotContain(Key, settings.ProtectedApiKey);
        Assert.StartsWith(OperatingSystem.IsWindows() ? "dpapi:" : "plain:", settings.ProtectedApiKey);
        Assert.Equal(Key, lookup.Key);
        Assert.Equal(ApiKeySource.Settings, lookup.Source);
        Assert.Null(lookup.Error);
    }

    [Fact]
    public void SavedSettingsFileDoesNotContainPlainKey()
    {
        using var dir = new TempDir();
        var settings = new AppSettings();
        new ApiKeyStore(_ => null).Set(settings, Key);
        new SettingsStore(dir.File("settings.json")).Save(settings);

        Assert.DoesNotContain(Key, File.ReadAllText(dir.File("settings.json")));
    }

    [Fact]
    public void FallsBackToEnvironmentVariable()
    {
        var store = new ApiKeyStore(name => name == ApiKeyStore.EnvironmentVariable ? " env-key " : null);

        var lookup = store.Get(new AppSettings());

        Assert.Equal("env-key", lookup.Key);
        Assert.Equal(ApiKeySource.Environment, lookup.Source);
    }

    [Fact]
    public void NoKeyAnywhere()
    {
        var lookup = new ApiKeyStore(_ => null).Get(new AppSettings());

        Assert.False(lookup.HasKey);
        Assert.Equal(ApiKeySource.None, lookup.Source);
    }

    [Fact]
    public void ClearRemovesKey()
    {
        var store = new ApiKeyStore(_ => null);
        var settings = new AppSettings();
        store.Set(settings, Key);

        store.Clear(settings);

        Assert.False(store.Get(settings).HasKey);
    }

    [Fact]
    public void UndecryptableKeyReportsErrorAndUsesEnvironment()
    {
        var settings = new AppSettings { ProtectedApiKey = "dpapi:AAAAbozukveri" };
        var lookup = new ApiKeyStore(_ => "env-key").Get(settings);

        Assert.Equal("env-key", lookup.Key);
        Assert.NotNull(lookup.Error);
    }

    [Theory]
    [InlineData("", "boş")]
    [InlineData("   ", "boş")]
    [InlineData("abc def", "boşluk")]
    public void ValidateRejectsBadKeys(string key, string expected)
    {
        Assert.Contains(expected, ApiKeyStore.Validate(key));
        Assert.Throws<ArgumentException>(() => new ApiKeyStore(_ => null).Set(new AppSettings(), key));
    }

    [Theory]
    [InlineData(null, "(yok)")]
    [InlineData("short", "*****")]
    [InlineData("AIzaTEST-1234567890-x9Kq", "AIza…x9Kq")]
    public void MasksKeys(string? key, string expected)
    {
        Assert.Equal(expected, ApiKeyStore.Mask(key));
    }
}

public class AppPathsTests
{
    [Fact]
    public void FilesLiveInDataDirectory()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "cp-test"));

        Assert.Equal(Path.Combine(paths.DataDirectory, "settings.json"), paths.SettingsFile);
        Assert.Equal(Path.Combine(paths.DataDirectory, "corpus.tsv"), paths.CorpusFile);
        Assert.Equal(Path.Combine(paths.DataDirectory, "attempts.jsonl"), paths.AttemptsFile);
    }

    [Fact]
    public void RejectsEmptyDirectory()
    {
        Assert.Throws<ArgumentException>(() => new AppPaths(" "));
    }
}

using System.Runtime.InteropServices;
using System.Text;
using Lingora.Core.Corpus;
using Lingora.Core.Gemini;
using Lingora.Core.Models;
using Lingora.Core.Progress;
using Lingora.Core.Storage;

namespace Lingora.Core;

public enum CheckLevel
{
    Ok,
    Warning,
    Error,
}

public sealed record CheckLine(CheckLevel Level, string Message)
{
    public override string ToString() => Level switch
    {
        CheckLevel.Ok => "[OK]    ",
        CheckLevel.Warning => "[UYARI] ",
        _ => "[HATA]  ",
    } + Message;
}

/// <summary>
/// <c>--kontrol</c> komutu: arayüzü açmadan kurulumun bu bilgisayarda çalıştığını doğrular.
/// API anahtarı hiçbir zaman açık yazdırılmaz.
/// </summary>
public static class Diagnostics
{
    public static async Task<IReadOnlyList<CheckLine>> RunAsync(
        AppPaths paths,
        GeminiClient client,
        bool live,
        ApiKeyStore? apiKeyStore = null,
        TimeProvider? time = null,
        CancellationToken cancellationToken = default)
    {
        var lines = new List<CheckLine>();
        time ??= TimeProvider.System;
        apiKeyStore ??= new ApiKeyStore();

        lines.Add(new(CheckLevel.Ok, $".NET {Environment.Version} · {RuntimeInformation.OSDescription.Trim()}"));

        try
        {
            paths.EnsureCreated();
            var probe = Path.Combine(paths.DataDirectory, $".yazma-testi-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            lines.Add(new(CheckLevel.Ok, $"Veri klasörü yazılabilir: {paths.DataDirectory}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lines.Add(new(CheckLevel.Error, $"Veri klasörüne yazılamıyor ({paths.DataDirectory}): {ex.Message}"));
            return lines;
        }

        var settingsStore = new SettingsStore(paths.SettingsFile);
        var settings = settingsStore.Load();
        lines.Add(settingsStore.LastBackupPath is null
            ? new(CheckLevel.Ok, $"Ayarlar: mod={settings.Direction.DisplayName()}, hedef={settings.DailyGoal}/gün, zorluk={settings.Difficulty.DisplayName()}, model={settings.Model}")
            : new(CheckLevel.Warning, $"Ayar dosyası bozuktu, varsayılanlar kullanılıyor. Yedek: {settingsStore.LastBackupPath}"));

        var corpus = SentenceCorpus.Load(paths.CorpusFile);
        PracticeItem? sample = null;
        if (corpus.Count == 0)
        {
            lines.Add(new(CheckLevel.Warning, "Cümle dosyası yüklenmemiş. Uygulamada Ayarlar > Veri Dosyası (TXT) menüsünden yükleyin."));
        }
        else
        {
            lines.Add(new(CheckLevel.Ok, $"Cümle havuzu: {corpus.Count:N0} cümle çifti"));
            sample = new SentencePicker(corpus).Next(settings.Direction, settings.Difficulty)?.Item;
            if (sample is not null)
            {
                var prompt = PromptBuilder.BuildRequestBody(sample, "test").ToJsonString();
                lines.Add(new(CheckLevel.Ok, $"Örnek cümle ve istem hazırlandı ({prompt.Length:N0} karakter): \"{sample.Source}\""));
            }
        }

        var progressStore = new ProgressStore(paths.AttemptsFile);
        var attempts = progressStore.LoadAll();
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var summary = ProgressCalculator.Calculate(attempts, settings.DailyGoal, today, time.LocalTimeZone);
        lines.Add(progressStore.LastCorruptLineCount == 0
            ? new(CheckLevel.Ok, $"İlerleme kaydı: toplam {summary.TotalSentences} cümle, bugün {summary.TodayCount}/{summary.DailyGoal}")
            : new(CheckLevel.Warning, $"İlerleme kaydında {progressStore.LastCorruptLineCount} bozuk satır atlandı (toplam {summary.TotalSentences})."));

        var key = apiKeyStore.Get(settings);
        if (key.Error is not null)
        {
            lines.Add(new(CheckLevel.Warning, key.Error));
        }

        if (!key.HasKey)
        {
            lines.Add(new(CheckLevel.Warning, "API anahtarı girilmemiş. Uygulamada Ayarlar > API Anahtarı menüsünden ekleyin."));
        }
        else
        {
            var source = key.Source == ApiKeySource.Settings ? "ayarlar (DPAPI ile şifreli)" : $"{ApiKeyStore.EnvironmentVariable} ortam değişkeni";
            lines.Add(new(CheckLevel.Ok, $"API anahtarı bulundu: {ApiKeyStore.Mask(key.Key)} · kaynak: {source}"));
        }

        if (!live)
        {
            return lines;
        }

        if (!key.HasKey)
        {
            lines.Add(new(CheckLevel.Warning, "Canlı Gemini testi atlandı (API anahtarı yok)."));
            return lines;
        }

        try
        {
            var models = await client.ListModelsAsync(key.Key!, cancellationToken).ConfigureAwait(false);
            lines.Add(new(CheckLevel.Ok, $"Gemini bağlantısı başarılı: {models.Count} model kullanılabilir."));
            if (!models.Contains(settings.Model, StringComparer.OrdinalIgnoreCase))
            {
                var flash = string.Join(", ", models.Where(m => m.Contains("flash", StringComparison.OrdinalIgnoreCase)).Take(6));
                lines.Add(new(CheckLevel.Warning, $"'{settings.Model}' model listesinde görünmüyor (takma ad olabilir). Flash modeller: {flash}"));
            }

            var item = sample ?? new PracticeItem(TranslationDirection.TurkishToEnglish, "Merhaba.", ["Hello.", "Hi."], -1);
            var userText = item.References.FirstOrDefault() ?? "Hello.";
            var evaluation = await client.EvaluateAsync(item, userText, key.Key!, settings.Model, cancellationToken).ConfigureAwait(false);
            lines.Add(evaluation.RawText is null
                ? new(CheckLevel.Ok, $"Örnek değerlendirme alındı ({settings.Model}): puan {evaluation.Score?.ToString() ?? "-"}/100")
                : new(CheckLevel.Warning, "Gemini yanıt verdi ama yapılandırılmış JSON döndürmedi; ham metin gösterilecek."));
        }
        catch (GeminiException ex)
        {
            lines.Add(new(CheckLevel.Error, $"Gemini testi başarısız: {ex.Message}"));
        }

        return lines;
    }

    public static string Format(IEnumerable<CheckLine> lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.AppendLine(line.ToString());
        }

        return builder.ToString();
    }
}

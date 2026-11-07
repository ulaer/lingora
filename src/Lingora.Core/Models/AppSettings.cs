namespace Lingora.Core.Models;

/// <summary>Kalıcı kullanıcı ayarları (settings.json).</summary>
public sealed class AppSettings
{
    /// <summary>
    /// Varsayılan model. İstenen "gemini-1.5-flash" Google tarafından kaldırıldı;
    /// "gemini-flash-latest" takma adı her zaman güncel Flash modeline yönlenir.
    /// </summary>
    public const string DefaultModel = "gemini-flash-latest";

    public const int DefaultDailyGoal = 20;
    public const int MinDailyGoal = 1;
    public const int MaxDailyGoal = 1000;

    public TranslationDirection Direction { get; set; } = TranslationDirection.TurkishToEnglish;

    public int DailyGoal { get; set; } = DefaultDailyGoal;

    public Difficulty Difficulty { get; set; } = Difficulty.All;

    public string Model { get; set; } = DefaultModel;

    /// <summary>Cümle sırası. Varsayılan: dosyadaki sırayla (kolaydan zora).</summary>
    public SentenceOrder Order { get; set; } = SentenceOrder.Sequential;

    /// <summary>Sıralı modda kalınan yer: ekrandaki cümlenin korpustaki indeksi (0 tabanlı).</summary>
    public int Position { get; set; }

    /// <summary>DPAPI ile şifrelenmiş API anahtarı. Düz metin anahtar asla burada tutulmaz.</summary>
    public string? ProtectedApiKey { get; set; }

    /// <summary>Elle düzenlenmiş veya bozuk değerleri güvenli aralıklara çeker.</summary>
    public void Normalize()
    {
        DailyGoal = Math.Clamp(DailyGoal, MinDailyGoal, MaxDailyGoal);

        if (!Enum.IsDefined(Direction))
        {
            Direction = TranslationDirection.TurkishToEnglish;
        }

        if (!Enum.IsDefined(Difficulty))
        {
            Difficulty = Difficulty.All;
        }

        Model = NormalizeModelName(Model);

        if (!Enum.IsDefined(Order))
        {
            Order = SentenceOrder.Sequential;
        }

        Position = Math.Max(0, Position);

        if (string.IsNullOrWhiteSpace(ProtectedApiKey))
        {
            ProtectedApiKey = null;
        }
    }

    /// <summary>"models/gemini-x" → "gemini-x"; boşsa varsayılan model.</summary>
    public static string NormalizeModelName(string? model)
    {
        var name = (model ?? string.Empty).Trim();
        if (name.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
        {
            name = name["models/".Length..];
        }

        return name.Length == 0 ? DefaultModel : name;
    }
}

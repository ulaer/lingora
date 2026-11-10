namespace Lingora.Core.Models;

/// <summary>Kalıcı kullanıcı ayarları (settings.json).</summary>
public sealed class AppSettings
{
    /// <summary>
    /// Varsayılan model. İstenen "gemini-1.5-flash" Google tarafından kaldırıldı;
    /// "gemini-flash-latest" takma adı her zaman güncel Flash modeline yönlenir.
    /// </summary>
    public const string DefaultModel = "gemini-flash-latest";

    /// <summary>Sesli ders için Gemini metin okuma (TTS) modeli.</summary>
    public const string DefaultTtsModel = "gemini-3.1-flash-tts-preview";

    /// <summary>Varsayılan ses: sıcak tonlu "Sulafat".</summary>
    public const string DefaultVoice = "Sulafat";

    /// <summary>Ayarlar'da sunulan Gemini hazır sesleri (ad, Türkçe tanım).</summary>
    public static readonly IReadOnlyList<(string Name, string Description)> Voices =
    [
        ("Sulafat", "Sıcak, sakin öğretmen"),
        ("Achird", "Samimi, arkadaşça"),
        ("Charon", "Bilgilendirici, net"),
        ("Vindemiatrix", "Yumuşak, nazik"),
        ("Kore", "Kararlı, güçlü"),
        ("Puck", "Enerjik, neşeli"),
    ];

    public const int DefaultDailyGoal = 20;
    public const int MinDailyGoal = 1;
    public const int MaxDailyGoal = 1000;

    public TranslationDirection Direction { get; set; } = TranslationDirection.TurkishToEnglish;

    public int DailyGoal { get; set; } = DefaultDailyGoal;

    public Difficulty Difficulty { get; set; } = Difficulty.All;

    public string Model { get; set; } = DefaultModel;

    /// <summary>Oyunda resmin hangi dilde anlatılacağı (hedef dil). Çalışma ekranındaki moddan bağımsızdır.</summary>
    public TranslationDirection GameDirection { get; set; } = TranslationDirection.TurkishToEnglish;

    /// <summary>Oyunun seviyesi (A1, A2, B1). Kalıcıdır.</summary>
    public Lingora.Core.Game.GameLevel GameLevel { get; set; } = Lingora.Core.Game.GameLevel.A1;

    /// <summary>Sesli ders seslendirme modeli.</summary>
    public string TtsModel { get; set; } = DefaultTtsModel;

    /// <summary>Sesli ders sesi (Gemini hazır ses adı).</summary>
    public string Voice { get; set; } = DefaultVoice;

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

        if (!Enum.IsDefined(GameDirection))
        {
            GameDirection = TranslationDirection.TurkishToEnglish;
        }

        if (!Enum.IsDefined(GameLevel))
        {
            GameLevel = Lingora.Core.Game.GameLevel.A1;
        }

        if (!Enum.IsDefined(Difficulty))
        {
            Difficulty = Difficulty.All;
        }

        Model = NormalizeModelName(Model);
        TtsModel = string.IsNullOrWhiteSpace(TtsModel) ? DefaultTtsModel : NormalizeModelName(TtsModel);
        Voice = string.IsNullOrWhiteSpace(Voice) ? DefaultVoice : Voice.Trim();

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

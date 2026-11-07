namespace Lingora.Core.Models;

/// <summary>Cümlelerin hangi sırayla geleceği.</summary>
public enum SentenceOrder
{
    /// <summary>Dosyadaki sırayla, kalınan yerden devam (Tatoeba dosyaları kolaydan zora sıralıdır).</summary>
    Sequential = 0,

    /// <summary>Rastgele, daha önce çalışılanları mümkün olduğunca tekrar etmeden.</summary>
    Random = 1,
}

/// <summary>Cümle zorluğu; İngilizce cümlenin kelime sayısına göre belirlenir.</summary>
public enum Difficulty
{
    All = 0,
    Easy = 1,
    Medium = 2,
    Hard = 3,
}

public static class DifficultyExtensions
{
    public static (int Min, int Max) WordRange(this Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => (1, 4),
        Difficulty.Medium => (5, 8),
        Difficulty.Hard => (9, int.MaxValue),
        _ => (0, int.MaxValue),
    };

    public static bool Contains(this Difficulty difficulty, int wordCount)
    {
        var (min, max) = difficulty.WordRange();
        return wordCount >= min && wordCount <= max;
    }

    public static string DisplayName(this Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => "Kolay (1-4 kelime)",
        Difficulty.Medium => "Orta (5-8 kelime)",
        Difficulty.Hard => "Zor (9+ kelime)",
        _ => "Hepsi",
    };
}

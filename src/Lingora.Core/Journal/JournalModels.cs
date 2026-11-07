using Lingora.Core.Models;

namespace Lingora.Core.Journal;

/// <summary>Değerlendirmedeki geri bildirim satırının türü.</summary>
public enum FeedbackKind
{
    Mistake,
    Tip,
}

/// <summary>Bir hata veya ipucu satırı için AI'dan istenen ayrıntılı açıklama.</summary>
public sealed class ItemExplanation
{
    public FeedbackKind Kind { get; set; }

    /// <summary>Hatalar listesindeki sıra (ipucu için 0).</summary>
    public int Index { get; set; }

    /// <summary>Açıklanan satırın metni.</summary>
    public string Item { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Günlüğe yazılan tek bir değerlendirilmiş çeviri: hatalar, ipuçları ve istenen açıklamalar.</summary>
public sealed class JournalEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset Timestamp { get; set; }

    public TranslationDirection Direction { get; set; }

    public string Source { get; set; } = string.Empty;

    public string UserTranslation { get; set; } = string.Empty;

    public int? Score { get; set; }

    public bool? IsCorrect { get; set; }

    public string CorrectedTranslation { get; set; } = string.Empty;

    public string NaturalTranslation { get; set; } = string.Empty;

    public List<string> Mistakes { get; set; } = new();

    public List<string> MinorIssues { get; set; } = new();

    public string Tip { get; set; } = string.Empty;

    public List<ItemExplanation> Explanations { get; set; } = new();

    public ItemExplanation? FindExplanation(FeedbackKind kind, int index) =>
        Explanations.FirstOrDefault(e => e.Kind == kind && e.Index == index);

    /// <summary>Açıklanacak satırın metni; geçersiz sırada null.</summary>
    public string? GetItem(FeedbackKind kind, int index) => kind switch
    {
        FeedbackKind.Mistake when index >= 0 && index < Mistakes.Count => Mistakes[index],
        FeedbackKind.Tip when index == 0 && Tip.Length > 0 => Tip,
        _ => null,
    };

    public static JournalEntry From(PracticeItem item, string userTranslation, Evaluation evaluation, DateTimeOffset timestamp) => new()
    {
        Timestamp = timestamp,
        Direction = item.Direction,
        Source = item.Source,
        UserTranslation = userTranslation,
        Score = evaluation.Score,
        IsCorrect = evaluation.IsCorrect,
        CorrectedTranslation = evaluation.CorrectedTranslation,
        NaturalTranslation = evaluation.NaturalTranslation,
        Mistakes = evaluation.Mistakes.ToList(),
        MinorIssues = evaluation.MinorIssues.ToList(),
        Tip = evaluation.Tip,
    };
}

/// <summary>Günlük listesindeki bir günün özeti.</summary>
public sealed record JournalDay(DateOnly Date, int Entries, int Mistakes, int Tips, int Explanations, bool HasLesson);

public sealed class LessonTopic
{
    public string Title { get; set; } = string.Empty;

    public string Explanation { get; set; } = string.Empty;

    public List<string> Examples { get; set; } = new();
}

public sealed class LessonMistake
{
    public string Wrong { get; set; } = string.Empty;

    public string Right { get; set; } = string.Empty;

    public string Why { get; set; } = string.Empty;
}

/// <summary>Bir günün kayıtlarından AI ile üretilen ders notu. Bir kez üretilir, sonra dosyadan okunur.</summary>
public sealed class LessonNote
{
    public DateOnly Date { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public List<LessonTopic> Topics { get; set; } = new();

    public List<LessonMistake> Mistakes { get; set; } = new();

    public List<string> Practice { get; set; } = new();

    /// <summary>Notun üretildiği andaki kayıt sayısı (sonradan eklenenleri göstermek için).</summary>
    public int EntryCount { get; set; }

    public string Model { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

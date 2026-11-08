namespace Lingora.Core.Models;

/// <summary>Veri dosyasındaki bir İngilizce–Türkçe cümle çifti.</summary>
public readonly record struct SentencePair(string English, string Turkish);

/// <summary>Kullanıcıya gösterilen çevrilecek cümle.</summary>
/// <param name="Direction">Çeviri yönü.</param>
/// <param name="Source">Çevrilecek cümle (kaynak dilde).</param>
/// <param name="References">Veri dosyasındaki hedef dil çevirileri (referans).</param>
/// <param name="CorpusIndex">Cümlenin korpustaki satır indeksi.</param>
/// <param name="ImageFacts">Oyun (resmi anlat) modunda resimde kesin görünenler; doluysa istem resim moduna geçer.</param>
public sealed record PracticeItem(
    TranslationDirection Direction,
    string Source,
    IReadOnlyList<string> References,
    int CorpusIndex,
    string? ImageFacts = null);

/// <summary>Gemini'nin önerdiği tek bir kelime/kalıp/tamlama.</summary>
/// <param name="Term">İngilizce terim (sözlük biçiminde).</param>
/// <param name="Meaning">Türkçe anlamı.</param>
/// <param name="Example">Örnek kullanım.</param>
/// <param name="Kind">"kelime", "kalıp" veya "tamlama".</param>
public sealed record VocabularyItem(string Term, string Meaning, string Example, string Kind);

/// <summary>Gemini'nin değerlendirme sonucu.</summary>
public sealed class Evaluation
{
    public int? Score { get; init; }

    public bool? IsCorrect { get; init; }

    public string CorrectedTranslation { get; init; } = string.Empty;

    public string NaturalTranslation { get; init; } = string.Empty;

    public IReadOnlyList<string> Mistakes { get; init; } = Array.Empty<string>();

    /// <summary>Puanı etkilemeyen küçük hatalar (yazım, büyük/küçük harf, noktalama).</summary>
    public IReadOnlyList<string> MinorIssues { get; init; } = Array.Empty<string>();

    public string Tip { get; init; } = string.Empty;

    /// <summary>Öğrencinin bu cümlede eksik/yanlış kullandığı kelime, kalıp ve tamlamalar.</summary>
    public IReadOnlyList<VocabularyItem> Vocabulary { get; init; } = Array.Empty<VocabularyItem>();

    /// <summary>Model yapılandırılmış JSON döndüremediyse ham metin.</summary>
    public string? RawText { get; init; }
}

/// <summary>attempts.jsonl dosyasındaki tek satır: değerlendirilmiş bir çeviri denemesi.</summary>
public sealed class AttemptRecord
{
    public DateTimeOffset Timestamp { get; set; }

    public TranslationDirection Direction { get; set; }

    public string Source { get; set; } = string.Empty;

    public string UserTranslation { get; set; } = string.Empty;

    public int? Score { get; set; }

    public bool? IsCorrect { get; set; }

    /// <summary>Deneme anındaki günlük hedef (geçmiş günler kendi hedefiyle değerlendirilir).</summary>
    public int DailyGoal { get; set; }
}

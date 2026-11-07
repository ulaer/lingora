namespace Lingora.Core.Models;

/// <summary>Alıştırma yönü.</summary>
public enum TranslationDirection
{
    /// <summary>Türkçe cümle verilir, kullanıcı İngilizceye çevirir (yazma pratiği).</summary>
    TurkishToEnglish = 0,

    /// <summary>İngilizce cümle verilir, kullanıcı Türkçeye çevirir (okuma pratiği).</summary>
    EnglishToTurkish = 1,
}

public static class TranslationDirectionExtensions
{
    public static string DisplayName(this TranslationDirection direction) =>
        $"{direction.SourceLanguage()} → {direction.TargetLanguage()}";

    public static string SourceLanguage(this TranslationDirection direction) =>
        direction == TranslationDirection.TurkishToEnglish ? "Türkçe" : "İngilizce";

    public static string TargetLanguage(this TranslationDirection direction) =>
        direction == TranslationDirection.TurkishToEnglish ? "İngilizce" : "Türkçe";
}

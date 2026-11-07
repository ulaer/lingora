namespace Lingora.Core;

/// <summary>Ayarlar sayfasındaki "Nasıl yapılır?" yardımının içeriği.</summary>
public sealed record HelpTopic(string Title, string LinkText, string LinkUrl, IReadOnlyList<string> Steps, IReadOnlyList<string> Notes);

/// <summary>
/// Depo API anahtarı veya cümle dosyası içermez; kullanıcı ikisini de kendisi edinir.
/// Bu sınıf ilgili sayfalardaki adım adım anlatımı tutar.
/// </summary>
public static class HelpTopics
{
    public const string ApiKeyUrl = "https://aistudio.google.com/apikey";
    public const string CorpusUrl = "https://www.manythings.org/anki/";

    public static HelpTopic ApiKey { get; } = new(
        "Gemini API anahtarı nasıl alınır?",
        "aistudio.google.com/apikey",
        ApiKeyUrl,
        [
            "Aşağıdaki bağlantıdan Google AI Studio'yu aç ve Google hesabınla giriş yap.",
            "\"Create API key\" (API anahtarı oluştur) düğmesine bas; istenirse bir proje seç ya da yeni proje oluştur.",
            "Oluşan anahtarı kopyala (genellikle \"AIza\" ile başlar).",
            "Bu sayfadaki \"Yeni anahtar\" alanına yapıştır ve Kaydet'e bas.",
            "\"Bağlantıyı test et\" ile doğrula; hesabında kullanılabilen modeller listelenir.",
        ],
        [
            "Anahtar ücretsizdir; ücretsiz kota modele göre değişir. Hızlı ve kotası geniş bir model için Genel sekmesinde gemini-3.5-flash-lite yazabilirsin.",
            "Anahtar koda veya depoya girmez. Bu bilgisayarda Windows DPAPI ile şifrelenir; yalnızca senin Windows kullanıcın çözebilir.",
            "Anahtarını kimseyle paylaşma. Sızdığını düşünürsen AI Studio'dan silip yenisini oluştur.",
        ]);

    public static HelpTopic CorpusFile { get; } = new(
        "Cümle dosyası (TXT) nereden bulunur?",
        "manythings.org/anki",
        CorpusUrl,
        [
            "Aşağıdaki bağlantıdan manythings.org/anki sayfasını aç.",
            "Listeden \"Turkish - English\" satırındaki tur-eng.zip dosyasını indir.",
            "Zip'i bir klasöre çıkar; içinden tur.txt çıkar.",
            "Bu sayfada \"Gözat…\" ile tur.txt dosyasını seç ve Yükle'ye bas.",
            "Cümleler dosyadaki sırayla, kolaydan zora gelir. İstersen Genel > Başlangıç satırı ile ileri atlayabilirsin.",
        ],
        [
            "Kendi dosyanı da kullanabilirsin: her satırda İngilizce<TAB>Türkçe ve isteğe bağlı üçüncü sütunda kaynak bilgisi olmalı (UTF-8).",
            "Dosya bu bilgisayardaki veri klasörüne kopyalanır; asıl dosyayı sonradan silebilirsin. Yeni dosya yüklemek ilerlemeni silmez.",
            "Cümleler Tatoeba projesindendir (CC-BY 2.0 FR); paylaşırken atıf satırlarını koru.",
        ]);
}

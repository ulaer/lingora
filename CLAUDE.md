# CLAUDE.md – Geliştirici / Asistan Kontrol Dosyası

Bu dosya, projede çalışan geliştirici veya yapay zekâ asistanı için kısa yol haritasıdır.
Kullanıcı dokümantasyonu `README.md`, gereksinim takibi `docs/GEREKSINIMLER.md` içindedir.

## Proje

Lingora: Türkçe↔İngilizce cümle çevirisi alıştırması yapan, çeviriyi Gemini API ile
değerlendiren, günlük ilerlemeyi kaydeden **.NET 8 WPF masaüstü** uygulaması (Windows).
Kullanıcı terminal arayüzü istemiyor; ilk sürümdeki Terminal.Gui arayüzü kaldırıldı.
Eski adı **CeviriPratik**. Geriye uyum için iki yerde eski ad bilerek kalır: `ApiKeyStore` DPAPI
entropy değeri ve `AppPaths.LegacyFolderName` (`%APPDATA%\CeviriPratik` ilk açılışta `Lingora`'ya taşınır).
Depo herkese açık (GitHub): API anahtarı, TXT/TSV cümle dosyası, kullanıcı verisi asla eklenmez
(`.gitignore`); kullanıcı bunları Ayarlar'daki "Nasıl yapılır?" yardımıyla (`Core/HelpTopics.cs`) kendisi ekler.

## Komutlar (depo kökünden)

```powershell
dotnet build Lingora.sln                       # derle
dotnet test Lingora.sln                        # tüm testler (çekirdek + WPF arayüz)
dotnet run --project src/Lingora.Desktop       # pencereyi aç
dotnet publish src/Lingora.Desktop -c Release -r win-x64 --self-contained false -o uygulama
```

Arayüz testlerinin ekran görüntülerini görmek için:

```powershell
$env:LINGORA_SCREENSHOTS = "C:\bir\klasor"; dotnet test Lingora.sln
```

README görselleri ve demo (GIF/MP4) için: `$env:LINGORA_MEDIA = (Resolve-Path docs\media).Path;
dotnet test --filter MediaCapture`, sonra `docs\media\make-media.ps1` (ffmpeg gerekir).

Testler gerçek kullanıcı verisine dokunmaz (her test geçici klasörde `AppPaths` kullanır).
Elle deneme için `LINGORA_HOME` ile ayrı bir veri klasörü verilebilir.

## Yapı

```
src/Lingora.Core/      İş mantığı (arayüzden bağımsız, test edilen kısım)
  Models/                   AppSettings, TranslationDirection, Difficulty, PracticeItem, Evaluation, AttemptRecord
  Corpus/                   CorpusParser, CorpusImporter, SentenceCorpus, SentencePicker
  Gemini/                   PromptBuilder, GeminiClient, GeminiException, ITranslationEvaluator
  Progress/                 ProgressStore (attempts.jsonl), ProgressCalculator, ProgressFormatter
  Storage/                  AppPaths, SettingsStore, ApiKeyStore (+ DPAPI)
  Lexicon/                  VocabularyBook (vocabulary.json): sözlük, benzer terim birleştirme (NormalizeKey)
  PracticeSession.cs        Çalışma akışı (cümle seç → gönder → değerlendir → kaydet)
  EvaluationFormatter.cs    Sonucu düz metne çevirir
  Diagnostics.cs            Ayarlar > Sistem Kontrolü çıktısı
  HelpTopics.cs             Ayarlar'daki "Nasıl yapılır?" adımları (API anahtarı, TXT dosyası)
src/Lingora.Desktop/   WPF arayüzü (exe adı: Lingora.exe)
  Theme.xaml                Lacivert palet ve tüm stiller (renkler yalnızca burada)
  MainWindow.xaml           Çalışma ekranı hep açık; üst çubukta logo, kısa mod (TR → EN / EN → TR),
                            İlerleme (hedef ikonu + bugün x/hedef) ve Ayarlar (dişli) butonları
  PageWindow.cs             Hedef/Ayarlar/Sözlük ek penceresi + WindowFrame (Windows başlık çubuğu yerine
                            WindowChrome; küçült/büyüt/kapat uygulamanın üst çubuğunda)
  Typewriter.cs             Cevabın harf harf yazılma animasyonu (testlerde AnimateResults=false)
  Pages/                    PracticePage, ProgressPage (Hedef), SettingsPage, VocabularyPage (Sözlük)
  Ui.cs                     Kodla oluşturulan yazı öğeleri için yardımcılar
tests/Lingora.Tests/   xUnit; DesktopUiTests WPF'i tek STA iş parçacığında çalıştırır
uygulama/                   Yayınlanmış exe (git dışı)
```

## Kurallar

- İş mantığı Core'a; Desktop yalnızca görünüm + olay bağlama. Sayfa kodunda test edilmesi gereken
  mantık biriktiyse Core'a taşı.
- API anahtarı **asla** koda, loga, URL'ye veya test verisine gerçek değer olarak yazılmaz.
  Header: `x-goog-api-key`.
- Kullanıcıya görünen metinler Türkçe. Kod tanımlayıcıları İngilizce.
- Arayüz sade ve yazı ağırlıklı. Kullanıcının istediği istisnalar: küçük logo, hedef ve dişli ikonları
  (vektör/font, resim dosyası yok). Başka görsel ekleme. Zemin gradyan, üst çubuk ayrı şerit değil,
  çalışma ekranındaki yazı alanı çerçevesiz ve şeffaf. Renkler `Theme.xaml` içinde.
- WPF projelerinde `System.IO` örtük değil; gerekiyorsa `using System.IO;` ekle.
- Zaman için `TimeProvider` enjekte edilir (testlerde sabit saat).
- Yeni gereksinim → önce `docs/GEREKSINIMLER.md` tablosuna satır, sonra kod + test.

## Bitti tanımı

1. `dotnet build` uyarısız/hatasız.
2. `dotnet test` tamamı yeşil.
3. Ekran görüntüleri (`LINGORA_SCREENSHOTS`) gözle kontrol edildi.
4. `uygulama\Lingora.exe` açılıyor, pencere başlığı "Lingora" ve yanıt veriyor.

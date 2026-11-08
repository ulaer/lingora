# Gereksinim Analizi ve Kabul Listesi

Bu dosya, uygulamanın neyi yapması gerektiğini, bunun kodda nerede karşılandığını ve
hangi testle doğrulandığını izlemek için tutulur. Yeni bir özellik eklerken önce
buraya bir satır ekleyin.

## 1. Amaç

Kullanıcının İngilizce **okuma** (EN→TR) ve **yazma** (TR→EN) becerisini, cümleden
cümleye çeviri alıştırmasıyla geliştirmek. Kullanıcının çevirisi Google AI Studio
(Gemini) API'sine hazır bir istemle gönderilir; puan, düzeltme ve Türkçe açıklama
geri gelir. İlerleme günlük olarak kaydedilir.

## 2. Gereksinimler

| No  | Gereksinim | Karşılandığı yer | Doğrulama |
|-----|------------|------------------|-----------|
| R1  | İki mod: Türkçe→İngilizce, İngilizce→Türkçe; üst navigasyondan değiştirilebilir, seçim kalıcı | `Core/Models/TranslationDirection.cs`, `Desktop/MainWindow.xaml` (Mod seçici), `SettingsStore` | `PracticeSessionTests`, `DesktopUiTests.ModeButtonSwitchesDirectionAndPersists` |
| R2  | Sade, koyu mavi tonlarında arayüz; yazı dışında görsel yok | `Desktop/Theme.xaml` (lacivert palet, ikon/resim yok) | Ekran görüntüleri gözle kontrol |
| R3  | Üst navigasyon: Ayarlar ve Mod (ek olarak Çalışma, İlerleme) | `Desktop/MainWindow.xaml` | `DesktopUiTests.MainWindowShowsNavigation...` |
| R4  | Ana ekranda yalnızca: çevrilecek cümle, çeviri alanı, Gönder butonu (+ sonuç metni) | `Desktop/Pages/PracticePage.xaml` | `DesktopUiTests` |
| R5  | Gönder → Gemini'ye hazır istem (kaynak cümle, yön, referans çeviri, kullanıcı çevirisi) → sonuç gösterilir | `Core/Gemini/PromptBuilder.cs`, `Core/Gemini/GeminiClient.cs`, `PracticePage.ShowEvaluation` | `PromptBuilderTests`, `GeminiClientTests`, `DesktopUiTests.SendShowsEvaluation...` |
| R6  | API anahtarı ayarlardaki ayrı sayfadan girilir, **koda gömülü değildir**; diskte Windows DPAPI ile şifreli tutulur | `Desktop/Pages/SettingsPage` (API Anahtarı), `Core/Storage/ApiKeyStore.cs` | `ApiKeyStoreTests`, `DesktopUiTests.SettingsPages...` |
| R7  | Cümle TXT dosyası (Tatoeba/manythings: `EN<TAB>TR<TAB>Kaynak`) ayarlardaki ayrı sayfadan yüklenir | `Desktop/Pages/SettingsPage` (Veri Dosyası), `Core/Corpus/CorpusImporter.cs` | `CorpusImporterTests`, `SentenceCorpusTests`, `DesktopUiTests.SettingsPages...` |
| R8  | İlerleme sayfası: toplam cümle, bugün yapılan, günlük hedef ve ulaşıldı mı, gün gün geçmiş | `Desktop/Pages/ProgressPage`, `Core/Progress/ProgressCalculator.cs` | `ProgressCalculatorTests`, `DesktopUiTests.ProgressPage...` |
| R9  | İlerleme kalıcı olarak kaydedilir ve takip edilir | `Core/Progress/ProgressStore.cs` (`attempts.jsonl`) | `ProgressStoreTests` |
| R10 | Web değil; **pencereli masaüstü uygulaması** (07.11.2026'da kullanıcı netleştirdi: "terminalde değil uygulama olarak") | `src/Lingora.Desktop` (net8.0-windows, WPF) | `dotnet build`, `uygulama\Lingora.exe` açılıyor |
| R11 | Otomatik testler | `tests/Lingora.Tests` | `dotnet test` |
| R13 | İstem ayrıca öğrencinin eksik gördüğü kelime/kalıp/tamlamaları anlam ve örnekle döndürür; bunlar uygulama içi sözlüğe kaydedilir; sözlük üst çubukta ikon butonla açılır; benzer terimde tekrar hata tek kayıtta sayılır | `PromptBuilder` (vocabulary şeması), `GeminiClient.ReadVocabulary`, `Core/Lexicon/VocabularyBook.cs`, `PracticeSession`, `Desktop/Pages/VocabularyPage` | `VocabularyBookTests`, `VocabularyGeminiTests`, `VocabularySessionTests`, `DesktopUiTests.Vocabulary...` |
| R14 | Yazım hatası, küçük harfle başlama, büyük/küçük harf ve noktalama puanı düşürmez; "küçük hatalar" olarak ayrı gösterilir | `PromptBuilder` (kural + `minorIssues`), `Evaluation.MinorIssues`, `PracticePage` | `MinorIssueTests`, `OrderAndMinorIssueUiTests` |
| R15 | Cümleler dosya sırasıyla (kolaydan zora) gelir; kullanıcı istediği satırdan başlayabilir (ör. ilk 10.000'i atla); rastgele mod isteğe bağlı | `SentencePicker.NextInOrder`, `PracticeSession.StartFromLine/SetOrder`, `SettingsPage` (Sıra, Başlangıç satırı) | `SequentialOrderTests` |
| R16 | Kaldığı yer ve sözlük uygulama içinde kalıcı; kapat-aç sonrası sıfırlanmaz | `AppSettings.Position` (settings.json), `VocabularyBook` (vocabulary.json) | `SequentialOrderTests.PositionSurvivesRestart`, `OrderAndMinorIssueUiTests.SettingsStartLine...`, `VocabularyBookTests.SurvivesRestart` |
| R17 | Zemin daha şık/modern (gece mavisi + yumuşak mavi/mor ışık); Windows'un göze batan başlık çubuğu yok, küçült/büyüt/kapat üst çubukta sade ikonlar; masaüstü kısayolu | `Theme.xaml` (`BgGradient` DrawingBrush, `CaptionButton`), `PageWindow.cs` (`WindowFrame`), `MainWindow.xaml` | `DesktopUiTests.MainWindowShows...`, `DesktopUiTests.PageWindowHasOwnSubtleCaption...` |
| R18 | GitHub'a hazır: kurumsal ad **Lingora** (exe, pencere, ad alanları, ürün bilgileri), depoda API anahtarı ve TXT yok; API ve Veri Dosyası sayfalarında "Nasıl yapılır?" yardımı (ilk kurulumda açık gelir); eski veri klasörü taşınır; README, demo GIF/MP4, LinkedIn metni | `Directory.Build.props`, `.gitignore`, `Core/HelpTopics.cs`, `SettingsPage`, `AppPaths.MigrateFrom`, `README.md`, `docs/media/`, `docs/LINKEDIN.md` | `HelpTopicsTests`, `LegacyDataMigrationTests`, `DesktopUiTests.FirstRunSettingsShowHelp...`, `MediaCapture` |
| R19 | AI destek: (a) sonuçtaki her hata ve ipucu satırı için istenirse ayrıntılı açıklama; (b) her günün hataları, ipuçları ve açıklamaları günlüğe kaydedilir; (c) her gün için AI ders notu bir kez üretilir, sonra kayıttan açılır, PDF olarak kaydedilebilir; (d) Günlük, üst çubukta Hedef ile Sözlük arasında ikon butonla açılır | `Core/Journal/*` (JournalStore, LessonStore, LessonPdfExporter), `PromptBuilder` (Explain/Lesson), `GeminiClient : IStudyAssistant`, `PracticeSession.ExplainAsync/GetOrCreateLessonAsync`, `PracticePage`, `Pages/JournalPage`, `MainWindow` | `JournalStoreTests`, `StudyAssistantSessionTests`, `StudyAssistantGeminiTests`, `LessonPdfTests`, `JournalUiTests` |
| R20 | (a) Ayrıntılı açıklama ilk cevap gibi kutusuz/şeffaf ve harf harf yazılarak gelir; (b) ders notu oluşturulduktan sonra o gün yeni çeviri yapıldıysa yeniden oluşturulabilir; (c) aynı günlük kayıtlardan yalnızca metin bir sesli ders anlatımı üretilir, uygun öğretmen üslubuyla seslendirilir ve MP3 olarak kaydedilir | `PracticePage` (ExplainSlot + Typewriter), `PracticeSession.GetOrCreateLessonAsync(regenerate)`, `GetOrCreateAudioLessonAsync`, `StudyPrompts` (AudioScript, SpeechStyle, TTS gövdesi), `GeminiClient.SynthesizeAsync`, `AudioLessonStore`, `SpeechChunker`, `Desktop/Mp3Encoder`, `JournalPage` (Sesli ders sekmesi), `SettingsPage` (ses, TTS modeli) | `AudioLessonSessionTests`, `SpeechChunkerTests`, `SpeechGeminiTests`, `DesktopUiTests.Explanation...`, `...BuildsAudioLesson`, `Mp3EncoderWritesPlayableMp3...` |
| R21 | Dil öğrenme oyunu için flat SVG asset kütüphanesi: tek palet ve stil kilidi, 256×256 saydam zemin, ortak taban çizgisi; az sayıda modüler asset + uygulama içi dönüşüm (renk, adet, konum, grup) ile en çok cümleyi kapsar; sahneler ve kanonik EN/TR cümleler model kullanmadan yerel script ile üretilir. Arayüze henüz bağlı değil | `game-assets/` (style-lock.md, manifest.json, scenes.json, tools/combine.py, preview.html) | `tools/validate.py` (stil kilidi denetimi), `preview.html` ile gözle kontrol, coverage.md |
| R22 | `game-assets` uygulamaya eklenir: üst çubukta Oyun ikonu, iki mod — **Resmi anlat** (sahne gösterilir, kullanıcı hedef dilde cümleyle anlatır; her zaman Gemini değerlendirir; istem resmi kesin olgularla tarif eder) ve **Doğru cümleyi seç** (4 seçenek, çeldiriciler tek görsel farkla, yerel). Oyundaki cümleler günlük hedefe, günlüğe ve (AI değerlendirmesinde) sözlüğe sayılır. Oyun penceresinde İngilizce/Türkçe seçici (AppSettings.GameDirection, kalıcı); Türkçe seçiliyse istem Türkçe dil bilgisiyle değerlendirir, düzeltilmiş/doğal cümleler Türkçe olur | `Core/Game/*` (GameLibrary, SceneFacts, ChoiceBuilder), `PracticeItem.ImageContext`, `PromptBuilder` (resim modu), `PracticeSession.SubmitSceneAsync/RecordChoice`, `Desktop/SvgScene`, `Pages/GamePage`, `MainWindow` | `GameLibraryTests`, `ScenePromptTests`, `GameSessionTests`, `SvgSceneTests`, `GameUiTests` |
| R12 | Model: istek Gemini 1.5 Flash idi; bu model Google tarafından kaldırıldı. Model adı ayarlanabilir, varsayılan `gemini-flash-latest` | `Core/Models/AppSettings.cs`, `SettingsPage` (Genel) | `SettingsStoreTests`, `GeminiClientTests` |

## 3. Tasarım kararları

- **Dil/çatı:** C# / .NET 8 (makinede kurulu LTS), arayüz **WPF** (Windows masaüstü penceresi).
  İlk sürüm Terminal.Gui ile terminal arayüzüydü; kullanıcı pencereli uygulama istediği için kaldırıldı.
- **Katmanlar:** Tüm iş mantığı `Lingora.Core` içinde ve arayüzden bağımsız;
  `Lingora.Desktop` yalnızca ekranları çizer. Testler hem Core'u hem WPF sayfalarını hedefler.
- **Veri klasörü:** `%APPDATA%\Lingora` (ya da `LINGORA_HOME` ortam değişkeni).
  - `settings.json` – mod, hedef, zorluk, model, şifreli API anahtarı
  - `corpus.tsv` – içe aktarılmış, sadeleştirilmiş cümle çiftleri (`EN<TAB>TR`)
  - `attempts.jsonl` – her değerlendirilmiş deneme bir satır (ekleme-yalnız)
- **Büyük TXT dosyaları:** manythings `tur.txt` yüz binlerce satırdır. Korpus bellekte
  tek bir UTF-8 bayt dizisi + satır indeksleri olarak tutulur (string listesi değil).
  Aynı cümlenin birden fazla çevirisi hash-sıralı indeksle bulunur ve referans olarak gönderilir.
- **API anahtarı:** Koda gömülü değil. Ayarlardan girilir, `settings.json` içinde DPAPI
  (CurrentUser) ile şifreli saklanır. Ayarlı değilse `GEMINI_API_KEY` ortam değişkeni denenir.
  Anahtar URL'de değil `x-goog-api-key` başlığında gönderilir.
- **Gemini çıktısı:** `responseMimeType: application/json` + `responseSchema` ile yapılandırılmış
  JSON istenir (puan, doğru mu, düzeltilmiş çeviri, doğal çeviri, hatalar, ipucu).
  JSON bozuk gelirse ham metin gösterilir.
- **Sayım kuralı:** Yalnızca başarıyla değerlendirilen gönderimler "yapılan cümle" sayılır.
  API hatası sayılmaz.
- **Günlük hedef geçmişi:** Her deneme kaydı o andaki hedefi de saklar; geçmiş günler kendi
  hedefleriyle değerlendirilir.
- **Zorluk:** İngilizce cümlenin kelime sayısına göre: Kolay 1–4, Orta 5–8, Zor 9+, Hepsi.

## 4. Doğrulama kaydı

**06.11.2026 – Windows 10 Pro 19045, .NET SDK 9.0.306 / runtime 8.0.17**

| Kontrol | Sonuç |
|---------|-------|
| `dotnet build Lingora.sln` | 0 uyarı, 0 hata |
| `dotnet test Lingora.sln` | 109/109 başarılı (korpus, Gemini istemcisi, anahtar şifreleme, ayarlar, ilerleme, oturum akışı, tanılama, Terminal.Gui ekran çizimi) |
| `Lingora --kontrol` (yalıtılmış klasör + örnek veri) | Tüm satırlar `[OK]`, yalnızca "API anahtarı yok" uyarısı |
| `Lingora --kontrol --canli` + geçersiz test anahtarı | Gerçek Google API'ye ulaşıldı, "API key not valid" → Türkçe "API anahtarı geçersiz" mesajı |
| Arayüz gerçek terminalde (Claude masaüstü terminal paneli) | Menü, durum satırı, cümle, çeviri alanı ve Gönder butonu çizildi; Ctrl+C ile kapanmadı |
| `publish\Lingora.exe --kontrol` (gerçek `%APPDATA%\Lingora`) | Çalışıyor; TXT ve API anahtarı henüz girilmediği için 2 beklenen uyarı |

**Doğrulanamayanlar:** Gerçek bir API anahtarıyla Gemini değerlendirmesi (anahtar kullanıcıda),
gerçek terminalde klavyeyle menü/pencere gezintisi (pencereler sahte sürücüyle test edildi),
24-bit renklerin görsel kontrolü.

**07.11.2026 – Masaüstü (WPF) sürümü**

| Kontrol | Sonuç |
|---------|-------|
| `dotnet build Lingora.sln` | 0 uyarı, 0 hata |
| `dotnet test Lingora.sln` | 114/114 başarılı (106 çekirdek + 8 WPF arayüz testi: gezinme, gönder → sonuç → sonraki, boş çeviri, dosya/anahtar yokken yönlendirme, mod değişimi, ayar sayfaları, ilerleme) |
| Ekran görüntüleri (9 sayfa durumu) | Gözle kontrol edildi: lacivert tema, yalnızca yazı, tüm sayfalar dolu çiziliyor |
| `uygulama\Lingora.exe` | Pencere açıldı, başlık "Lingora", yanıt veriyor |

**07.11.2026 – Arayüz sadeleştirme (kullanıcı isteği)**

Değişiklikler: üst çubuk ana ekranla aynı gradyan zeminde (ayrı şerit yok); "Lingora" yazısı
yerine küçük logo; Çalışma/İlerleme/Ayarlar sekmeleri kaldırıldı, çalışma ekranı hep açık;
İlerleme (hedef ikonu + bugün x/hedef) ve Ayarlar (dişli) küçük ek pencerede açılıyor; mod
"TR → EN / EN → TR" olarak kısaltıldı, "Mod" yazısı kalktı; yazı alanı çerçevesiz ve şeffaf;
durum satırı, alt başlıklar ve "Atla" bağlantısı kaldırıldı (F5 hâlâ cümle atlar).

| Kontrol | Sonuç |
|---------|-------|
| `dotnet build` | 0 uyarı, 0 hata |
| `dotnet test` | 115/115 başarılı (9 WPF arayüz testi; ek pencereler ekranda açılmadan doğrulanıyor) |
| Ekran görüntüleri | Gözle kontrol edildi |
| `uygulama\Lingora.exe` | Yeniden yayınlandı, pencere açık ve yanıt veriyor |

**07.11.2026 – Hedef penceresi, yazı tipi, bekleme ve yazma animasyonu**

Hedef penceresi sadeleşti (büyük "bugün x/hedef", ince çubuk, 4 özet, son 7 gün sütunları);
çalışma ekranında ince (Light) yazı tipi, sonuç zeminsiz ve üstte etiket/altta değer düzeninde;
beklerken yalnızca `. .. ...` döner; cevap `Typewriter` ile hızlıca yazılır, Enter anında tamamlar.
`dotnet test`: 118/118 başarılı (noktalar, animasyonun kendi kendine bitmesi ve Enter ile tamamlama test ediliyor).

**07.11.2026 – Sözlük (R13)**

`dotnet test`: 144/144 başarılı. Benzer terim birleştirme (büyük/küçük harf, noktalama, baştaki
to/a/an/the, ’ ve '), örnek tekrarını önleme (en fazla 3), yeniden açılışta kalıcılık, bozuk dosya
yedeği, arama/sıralama/silme ve arayüz akışı test edildi. Gerçek Gemini'nin vocabulary alanını
doldurup doldurmadığı gerçek anahtarla denenmedi.

**07.11.2026 – Küçük hatalar, sıralı ilerleme, kalıcı konum (R14–R16)**

`dotnet test`: 160/160 başarılı. Yayınlama bir sonraki turda (R17 ile birlikte) yapıldı.

**07.11.2026 – Modern zemin, özel başlık çubuğu, masaüstü kısayolu (R17)**

`dotnet test`: 161/161 başarılı, 0 uyarı. Ekran görüntüleri (1-calisma, 2-sonuc, 13-hedef-pencere) kontrol
edildi. `uygulama\` yayınlandı; `Masaüstü\Lingora.lnk` ile açıldı, başlık "Lingora", yanıt veriyor.

**07.11.2026 – AI destek: ayrıntılı açıklama, günlük, ders notu ve PDF (R19)**

`dotnet test`: 189/189 başarılı, 0 uyarı. Ekran görüntüleri (16-ayrintili-aciklama, 17/18/19 günlük, docs/media
06–08) ve örnek PDF (Windows.Data.Pdf ile görüntüye çevrilerek; Türkçe karakterler doğru) gözle kontrol edildi.
Ders notunun bir kez üretilip sonra dosyadan geldiği, açıklamaların ikinci kez AI çağırmadığı testlerle doğrulandı.

**Doğrulanamayanlar:** Gerçek API anahtarıyla değerlendirme, açıklama ve ders notu üretimi (istek gövdeleri ve
yanıt ayrıştırma sahte HTTP ile test edildi); fareyle/klavyeyle elle gezinme (akışlar testte sayfa
metodlarıyla çalıştırıldı); gerçek ekranda büyütülmüş pencerenin kenar payı.

**07.11.2026 – Şeffaf açıklama, notu yenileme, sesli ders (R20)**

`dotnet test`: 203/203 başarılı, 0 uyarı. Gerçek Media Foundation ile 24 kHz PCM'den MP3 üretildi (test). Ekran
görüntüleri (20-aciklama-seffaf, 21-sesli-ders, docs/media/09) kontrol edildi. TTS modeli adı Google belgelerinden
(`gemini-3.1-flash-tts-preview`, ham 24 kHz PCM). **Doğrulanamayan:** gerçek anahtarla anlatım/seslendirme ve
uygulama içinde sesin gerçekten çalınması (testte oynatıcı açılmadı).

**08.11.2026 – Resimli oyun (R22)**

`dotnet test`: 222/222 başarılı, 0 uyarı. 54 SVG'nin hepsi kendi çizicimizle (SvgScene) çiziliyor; renk/tutma
noktası kompozisyonu piksel testiyle doğrulandı. Ekran görüntüleri (22–25, docs/media 10–11) kontrol edildi.
Nesnesi arkasındaki renge karışan 241 sahne (ör. sarı tişört önünde sarı elma, gökyüzü önünde su bardağı)
oyunda gösterilmez (`SvgScene.ObjectVisibility` < 0,6). Rengi/yeri söylemeyen ve resim için de doğru olan cümleler
çeldirici yapılmaz. **Doğrulanamayan:** gerçek Gemini'nin resim modu yanıt kalitesi.

Gelecek çalışmalar (kullanıcı notu): ders notu üretiminde iyileştirmeler ve düzeltmeler eklenecek
(ör. yeniden üretme, gün aralığı için not).

## 5. Kapsam dışı (şimdilik)

- Çoklu kullanıcı, bulut senkronizasyonu.
- Uygulama arayüzünde görsel/grafik (kullanıcı yazı ağırlıklı arayüz istiyor). `game-assets/` ayrı bir oyun için hazırlanır, arayüze bağlı değildir.

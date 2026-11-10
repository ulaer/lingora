# v6 sözleşmesi — REVIEW-v4 P1 + P2

v4/v5 sözleşmeleri geçerli. **Yeni SVG çizilmez**; yeni içerik mevcut asset'ler + kompozisyon kurallarıyla kurulur.
Akışlar ve dosyaları v5 ile aynı (Veri: `game-assets/**` dışında `assets/` yalnızca script düzenlemesi; Uygulama: `src/**`, `tests/**`).

## P1 — Veri

1. **Rozet okunurluğu**: rozet/işaret overlay'lerine (`badge_*`, `cal_*`, `marker_*`) arka disk eklenir — grubun ilk
   çocuğu `<circle cx="128" cy="128" r="74" fill="#F1FAEE" stroke="#2B2D42" stroke-width="6"/>`. Script ile; şekiller değişmez.
2. **Geniş zaman yalnızca alışkanlık fiillerinde**: eat, drink, read, run, swim, walk, sleep, cook, write, play, watch, give.
   (sit, think, smile, cry… için "her gün" üretilmez.)
3. **Zamirsiz Türkçe kabul**: kişi zamirli Türkçe cümleye `tr_alt` (zamirsiz biçim): "Ben koşuyorum." → `tr_alt: ["Koşuyorum."]`.
   Uygulama Türkçe cevapta `tr_alt`'ı da doğru sayar ve Gemini olgularına ekler.

## P2 — Veri: yeni içerik

| Özellik | Resim (mevcut asset'lerle) | Örnek | Seviye |
|---|---|---|---|
| **this / that / these / those** | `show_near`: nesne tek başına, x=128, s=0.6; `show_far`: nesne s=0.22, (196, 150) — uzakta, arka plan üstünde | This is a ball. / Bu bir top. · That is a car. / Şu bir araba. · These are apples. / Bunlar elma. | A1 |
| **İyelik** | Aktör nesneyi tutar; sahip `marker_me` (my), `marker_you` (your) ya da karakter (Tom's) | This is my book. / Bu benim kitabım. · This is Tom's ball. / Bu Tom'un topu. | A2 |
| **Nesne zamirleri** | `give` / `talk_to` çiftinde alıcıya `marker_me` / `marker_you` | Tom is giving me an apple. / Tom bana bir elma veriyor. · Mary is talking to you. / Mary seninle konuşuyor. | A2 |
| **Emir kipi** | Eylem resmi + aktörde `marker_you` + `mark_exclaim` rozeti; olumsuzda + `badge_not` | Read a book! / Bir kitap oku! · Don't run! / Koşma! | A2 |
| **wh-soruları** | Normal resim + `badge_question`; cümlede `answer` | What is Tom eating? / Tom ne yiyor? → An apple. / Bir elma. · Where is Mary reading? / Mary nerede okuyor? → At school. / Okulda. · Who is swimming? / Kim yüzüyor? → Tom. | B1 |

- Biçimler: `this`, `that`, `poss`, `imp`, `imp_neg`, `wh_what`, `wh_where`, `wh_who`.
- Türkçe iyelik ekleri nesne başına açık tablo (`tr_poss`: kitabım/kitabın/kitabı …), sahip için `tr_gen` (Tom'un, Mary'nin, adamın…).
- `facts`: `distance` (near/far), `owner` (karakter ya da 1s/2s), `receiver_person`; kendi-cümlesi kapısı yeni biçimleri de sınar.
- wh-cümlesinde `"answer": {"en": "An apple.", "tr": "Bir elma."}` ve `mentions` sorulan boyut hariç.
- Toplam `scenes/*.jsonl` ≤ ~10 MB.

## P1 — Uygulama

1. **Cevap kaydı**: her cevap için bir satır (`game-log.jsonl`, AppPaths altında): zaman, kayıt id, biçim, seviye, mod, doğru mu,
   seçilen çeldiricinin kaynak kaydı.
2. **Kaçırılanı yeniden sor**: yanlış cevaplanan resim 3–5 tur sonra tekrar gelir (kalıcı, oturumlar arası); 2 kez doğru
   cevaplanınca kuyruktan çıkar.
3. **İlerleme**: seviye başına doğruluk oranı gösterilir; bir seviyede son 20 cevabın %80'i doğruysa üst seviye önerilir
   (kilit yok, öneri).
4. **Erişilebilirlik**: resme `AutomationProperties.Name` (olgulardan Türkçe tarif; cevabı ele vermeyen biçimde), sistemde
   animasyon kapalıysa (`SystemParameters.ClientAreaAnimation == false`) animasyon yok; zamanlayıcı yalnızca karesi/durumu
   olan sahnede çalışır.
5. **Arka planda yükleme**: oyun verisi UI iş parçacığını bloklamadan yüklenir ("Yükleniyor…").
6. **Rozet açıklaması simgeli**: metin yerine küçük simge + Türkçe etiket.

## P2 — Uygulama

- wh-soruları: "Doğru cümleyi seç" modunda soru gösterilir, seçenekler **cevaplardır** (çeldiriciler aynı soru tipinin
  resim için yanlış cevapları); "Resmi anlat" modunda öğrenciden soruyu cevaplaması istenir (Gemini'ye soru + cevap + olgular).
- Emir, iyelik, this/that sahneleri mevcut kayıt çizimiyle gösterilir (`show_near`/`show_far` yerleşimleri eklenir).

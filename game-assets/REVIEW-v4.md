# v4 inceleme ve "ne eksik" tartışması

v4'ü, v2'yi de test eden üç alt model inceledi (A: dil/pedagoji — Sonnet, B: görsel/uygulama — Sonnet,
C: veri/oyun QA — Haiku). Önce her biri kendi açısından bağımsız rapor yazdı, sonra diğer ikisinin raporunu
okuyup eleştirdi ve tek bir öncelik listesi çıkardı.

## 1. tur — ölçümler

| | Bulgu |
|---|---|
| A — dil | Kişi ekleri, geniş zaman, -ebil ve "vermek" biçim olarak temiz. 9 hata; en büyüğü büyük harfli "ile" nesnesi: "Tom **Adam** ile konuşuyor" (304 cümle). Gerçek korpusun resimlenebilir payı **%1,2 → %3,26** (ilk 10 bin satırda %6,5). |
| B — görsel/uygulama | v3 düzeltmeleri tuttu (balon/rozet başa binme 0). Yeni: ben/sen işareti 382 sahnede tuvalden taşıyor (kadında %100); A1'in eski çizim yolu işaret çizmiyor (366 cümle); "vermek"te nesne 456/456 sahnede alıcıya biniyor; 1.725 nesne ve 1.311 rozet zemine karışıyor. |
| C — oyun QA | Doğruluk kuralıyla benzetilen sorularda **gerçekte doğru olan çeldirici**: A1 %20 (eski yol), A2 %41, B1 %33. A2'deki kaçakların %88'i olumsuz cümle: kural olumsuzu ters çevirmiyor. C, v2'deki "%0" sonucunun dar ölçüldüğünü kendisi düzeltti. |

## 2. tur — tartışma

**Uzlaşılan:** oyunun doğruluğu yeni içerikten önce gelir. Korpus kapsamını artırmak (emir kipi, iyelik,
modal, wh-soruları, this/that) üçünün listesinde de P0'dan sonra.

**Ayrışılan — olumsuz cümle resimde nasıl gösterilir?**

| Ajan | Görüş |
|---|---|
| A (dil) | Rozeti koru: "değil" rozeti resimdeki önermenin **tam olarak** inkârıdır; kuralı ters çevirme, sözleşmeyi yaz, çeldiricileri kutupluluk/zamana göre seç; inkâr edilen nesneye çarpı + açıklama paneli. C'nin %41'i olumsuzu gerçek dünya anlamıyla okuduğu için şişkin. |
| B (görsel) | Olumlu resmin üstündeki "değil" rozeti **okunmuyor** — resim doğru cevapla çelişiyor. En küçük çözüm: olumsuz biçimleri resimli modlardan çıkar, metin alıştırması yap; sonra karşıt resim (nesnenin hayaleti + çarpı). Evet/hayır soruları sorun değil. |
| C (QA) | Olumsuzda kuralı ters çevir; her olumsuz cümleyi olumlu iddianın **yanlış olduğu** bir karşıt resme bağla; kendi-cümlesi-doğru denetimini derleme kapısına ekle. |

## Ortak öncelik listesi

| # | Öncelik | Eksik | Neden | En küçük çözüm | Emek | Sahibi |
|---|---|---|---|---|---|---|
| 1 | P0 | Olumsuz cümle sözleşmesi | A2 sorularının %41'inde doğru çeldirici; 5.316 doğru cevap kendi resmiyle çelişiyor | Karar ver (yukarıdaki üç seçenek); kısa vadede olumsuzları resimli modlardan çıkar + kendi-cümlesi denetimi | S–M | veri + uygulama |
| 2 | P0 | Ben/sen işaretleri görünür | 382 sahnede taşıyor, A1 hiç çizmiyor → "I", "you", "he" aynı resim | İşaret y ≥ 18 ya da baş yanında; A1 `actors` okusun | S | asset + uygulama |
| 3 | P0 | A1 de olgu kuralından geçsin | A1'de %20 doğru çeldirici (takma ad, "vermek" içinde "tutmak") | scenes.json yerine kayıt yolu / ChoiceBuilder facts | S–M | uygulama |
| 4 | P0 | "Vermek" okunur olsun | 456/456 sahnede nesne alıcıda | `pair_give` yerleşimi (x 64/192, s 0.7) + lint "nesne başka aktöre biniyor" | S | veri |
| 5 | P0 | Büyük harfli "ile" nesnesi | 304 görünür Türkçe hata | Nesne rolünde küçük harf ya da -la/-le (adamla, doktorla) | S | veri |
| 6 | P0/P1 | Kişi ve yer olgusu | ~3.200 A2 kaçağı I/you'dan; "at home" mutfağı da kapsıyor (~4.300) | `person` olgu boyutu; yer hiyerarşisi | S–M | veri + uygulama |
| 7 | P1 | Rozet/nesne okunurluğu | 1.311 rozet, 1.725 nesne zemine karışıyor | Rozetlere mürekkep kenar; A2/B1'e görünürlük süzgeci; ilk açılışta rozet açıklaması | S–M | asset + uygulama |
| 8 | P1 | Dil düzeltmeleri | Kendine soru ("Koşuyor muyum?", 360), hep açık "Ben/Sen", "açtık" eşsesli, alışkanlık olmayan geniş zaman | 1. kişi sorularını çıkar; zamirsiz biçimi de kabul et; geniş zamanı alışkanlık fiillerine sınırla | S–M | veri |
| 9 | P1 | İlerleme ve hata kaydı | Kaçırılan resim tekrar gelmiyor, hangi çeldiricinin şaşırttığı bilinmiyor | Cevap başına bir satır log; kaçırılanı yeniden sıraya koy; seviye açma eşiği | M | uygulama |
| 10 | P1/P2 | Erişilebilirlik ve performans | Resmin ekran okuyucu adı yok; sürekli 300 ms çizim; 14 MB veri UI iş parçacığında | facts'ten AutomationProperties; azaltılmış hareket; arka planda yükleme | M | uygulama |
| 11 | P2 | Sonraki içerik dalgası | Korpus kapsamı %3,26 | Önce this/that ve nesne zamirleri (işaretlerle), sonra iyelik, emir kipi, modal, wh-soruları | M–L | veri + asset |

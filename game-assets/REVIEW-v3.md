# v3 inceleme kaydı

v2'yi üç alt model bağımsız test etti, sonra birbirlerinin raporunu okuyup tüm önerileri puanladı.
v3, üçünün ortak en yüksek puanlı paketidir.

## 1. tur — test

| Testçi | Model | Bakış | Öne çıkan bulgu |
|---|---|---|---|
| A | Sonnet | dil ve pedagoji | Türkçe biçim bilgisi (ünlü uyumu, ekler) 24.534 cümlede temiz; 11 anlam hatası: "balık tutuyor" (= balık avlıyor, 166 cümle), kanıtsal "sıkılmış/şaşırmış", sıralı panelde aynı eylem. Korpusun yalnızca ~%1,2'si resimlenebilir; "hold" A1'in %69'u. |
| B | Sonnet | görsel kompozisyon + uygulama | 188 balonlu sahnede aktör yerleşimi uygulanmamış (balon başa biniyor), 578 sahnede ikinci rozet saça biniyor, gece gökyüzü mürekkep saçla aynı renk, doktor/polis yüz koordinatları 12 px kayık, 3'lü tutmada nesneler havada, WPF grup üzerindeki `fill`'i görmüyor. |
| C | Haiku | veri bütünlüğü + oyun QA | 16.946 benzetilmiş soruda doğru çıkan çeldirici 0 — ama güvenlik sıralamaya bağlı; 5 hava çifti aynı resim, farklı "doğru" cümle; tekdüze seçimde tutma sahnesi %69. |

## 2. tur — çapraz puanlama (etki + uygulanabilirlik + (10 − risk), en çok 30)

| Öneri | A'nın puanı | B'nin puanı | C'nin puanı |
|---|---|---|---|
| **C3** derleme anında belirsizlik denetimi | **23** | **23** | **22** |
| C2 ağırlıklı örnekleme / kota | 22 | 21 | 21 |
| A3 kabul kümesi + dil denetimi | 22 | 20 | 20 |
| B-P1 kompozisyon denetimi (kaynağında düzelt) | — | 22 | 20 |
| A1 I/you/we + durum paketi | 19 | 18 | 16 |
| B-P2 WPF v2 çizici (A2/B1 sahneleri) | — | 18 | 16 |

Üç ajan da aynı paketi önerdi: **tek denetim kapısı (C3 + A3 + B-P1) + ağırlıklı örnekleme (C2) + kaynağında
veri düzeltmeleri**; WPF'te A2/B1 gösterimi ve yeni içerik paketleri v4'e.

## v3'te yapılanlar

- `tools/lint.py` — `combine.py` her çalıştığında: belirsizlik (AMB), dil (LANG), yerleşim (LAY) kuralları;
  `combine.py --strict` hata varsa derlemeyi durdurur. Sonuç `coverage.md`'de.
- Aynı resmi veren kayıtlar birleşir; cümleleri tek **kabul kümesi** olur (`merged_ids`).
- `scenes.json` sahnelerine `weight`; uygulama `GameLibrary.Pick` ağırlıklı seçer (tutma payı %68 → %39).
- Dil: balık elde tutulmaz; "canı sıkkın", "şaşkın"; "want" geçmişi "istedi" + olumsuz/soru; "on the beach";
  sıralı panelde yer cümle başında, aynı eylem çifti ve "think" yok.
- Görsel: balonlu/çift sahnelerde yerleşim aktörleri; rozetler dikey ve küçük; gece gökyüzü koyu mavi;
  yeme/içme tutma noktası gözlerden uzak; 3'lü nesneler avuçta; yüzme/el sallama çiftleri aralıklı;
  tren küçültüldü; hava overlay'i aktörlerin arkasında; doktor/polis yüz koordinatları düzeltildi.
- Uygulama: `SvgScene` grup üzerindeki `fill`/`stroke`'u miras alır; çoklu nesne aralığı 0.3.

## v4'e kalanlar

- WPF'te A2/B1 (rozet, balon, yüz ifadesi, animasyon) — B-P2.
- I/you/we ve durum sıfatları paketi — A1; "hold" yerine sık fiiller, geniş zaman, "can" — A2.
- Çeldiriciler için olgu tabanlı doğruluk denetimi — C1.

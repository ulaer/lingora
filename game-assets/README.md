# game-assets

Dil öğrenme oyunu için flat/geometrik SVG kütüphanesi. Amaç: az sayıda modüler asset ile
en çok cümleyi kapsamak. Her cümleye ayrı resim çizilmez; karakter-poz, nesne, arka plan ve
overlay'ler uygulama içinde birleştirilir (renk, adet, konum, grup, yüz ifadesi, balon, rozet,
gökyüzü rengi, durum ve animasyon dönüşümleri).

| Dosya | Ne |
|---|---|
| `style-lock.md` | Stil şartnamesi (v1 + v2): palet, viewBox, taban çizgisi, tint, anchor, yüz parçaları, kareler, durumlar |
| `assets/characters/` | özne × poz (insanlar, meslekler, hayvanlar); bazı pozlar 2 kareli animasyon |
| `assets/objects/` | elde tutulan nesneler + sahne nesneleri (TV, lamba, kapı, araba, tren, piyano…; bazıları durumlu) |
| `assets/backgrounds/` | mekânlar; gökyüzü/pencere `data-tint="sky"` ile gece/akşam olur |
| `assets/overlays/` | konuşma/düşünce balonu, dün/yarın/olumsuz/soru rozetleri, güneş/ay/saat, hava (yağmur, kar…), duygu işaretleri |
| `assets/overlays/face/` | yüz parçaları (ağız/göz/kaş) — duygu ifadeleri için değiştirilir |
| `manifest.json` | asset → kelime/anlam; fiil çekimleri (EN/TR), yer kuralları, takma adlar, gruplar, duygular, hava, etkileşimler, yerleşimler |
| `scenes.json` | Uygulamanın okuduğu biçim (Core/Game/GameLibrary): A1 kayıtları cümle başına bir sahne, v1 kimlikleriyle |
| `scenes/A1.jsonl` | şimdiki zaman: özne + fiil + nesne + yer, gruplar |
| `scenes/A2.jsonl` | dün/yarın, olumsuz, soru (rozetli), duygular, günün vakti, hava durumu |
| `scenes/B1.jsonl` | etkileşim (konuşmak/dinlemek, el sallamak, aramak), düşünce balonu (istemek, birini düşünmek), ışık/TV/kapı durum değişimi, sıralı iki panel |
| `docs/v4-contract.md` | v4 sözleşmesi: yeni asset kimlikleri, `actors[].marker`, `facts`/`mentions`/`person` |
| `coverage.md` | Kapsam özeti |
| `tools/combine.py` | `manifest.json` + SVG işaretleri → `scenes/*.jsonl` + `coverage.md` (kural tabanlı, model kullanmaz) |
| `tools/grammar.py` | EN/TR çekim: zaman, olumsuz, soru, ünlü uyumu, -ydı/-lar ekleri; v4: kişi ekleri (koşuyorum…), geniş zaman (`aor*`), yetenek (`can*`) |
| `tools/lint.py` | kalite kapısı: belirsizlik, dil, yerleşim (v3) + olgu (`facts`/`mentions`/`person`), kişi eki ↔ özne, geniş zaman ve `can` rozetleri (v4); `combine.py --strict` hata varsa durur |
| `REVIEW-v3.md` | v3'ü belirleyen üç ajanlı test ve çapraz puanlama kaydı |
| `tools/validate.py` | Tüm SVG'leri stil kilidine göre denetler (kareler, durumlar, yüz parçaları dahil) |
| `preview.html` | Kompozisyonun referans uygulaması ve önizleme |

## Komutlar (bu klasörden)

```powershell
python tools/validate.py      # stil denetimi
python tools/combine.py       # sahneleri, coverage.md'yi üret + kalite kapısı (--strict: hata varsa dur)
python -m http.server 8765    # sonra: http://localhost:8765/preview.html?level=B1&n=12
```

Önizleme parametreleri: `?mode=assets[&cat=character|object|background|overlay|face]`,
`?level=A1|A2|B1&n=12`, `&ids=id1,id2`, `&filter=metin`, `&form=past_q` ya da `&sent=2` (hangi cümle).

## Sahne kaydı (her satır bir görsel)

```json
{"id": "...", "level": "A2", "layout": "single|pair|bubble|pair_bubble|prop|panels",
 "bg": "park", "sky": "#2B2D42", "sky_overlay": "moon", "weather": "rain", "time": "night",
 "actors": [{"char": "tom_read", "x": 128, "s": 1, "expr": "tired",
             "hold": {"obj": "book", "tint": "#457B9D", "n": 1, "anchor": [128, 160], "s": 0.4}}],
 "bubble": {"type": "thought", "content": {"obj": "car"}},
 "prop": {"obj": "lamp", "states": ["on", "off"]},
 "panels": [{"layout": "single", "actors": [...]}, ...],
 "sentences": [{"form": "past_q", "subject": "Tom", "en": "...", "tr": "...", "badges": ["cal_yesterday", "badge_question"]}]}
```

Aynı görsel birden çok cümle taşır (takma ad özneler, zaman/olumsuz/soru biçimleri). v3: aynı resmi
veren kayıtlar birleşir (`merged_ids`); `sentences` o resmin **kabul kümesidir** — hepsi doğrudur.
`scenes.json` sahnelerindeki `weight`, oyunun ağırlıklı seçimi içindir. `badges`
cümleye özeldir: oyun hangi cümleyi soruyorsa onun rozetlerini gösterir.

### v4 alanları

- Her kayıtta `facts` (`subject, verb, object, variant, count, place, time, weather, state, other`); `scenes.json` tüm anahtarları
  yazar, `scenes/*.jsonl` boyut için `null`/`0` boyutları atar (eksik anahtar = null/0).
- Her cümlede `mentions` (cümlenin söylediği boyutlar), `person` (`1s|2s|1p|2p|3s|3p`) ve zamir/kişi özneleri için `subject_ok`
  (ör. "He" → tom, man, police). Cümle, kaynağı dışındaki resim için `mentions` boyutlarının hepsi o resmin olgusuyla eşitse doğrudur.
  Öznesi resmin öznesinden farklı olan cümleler (ör. "Mary listens to Tom" tom'un konuştuğu resimde) boş `mentions` taşır:
  hiçbir resim için çeldirici olmaz.
- `actors[].marker`: `marker_me` / `marker_you`. Kişi sahneleri: "I/You" tek aktör + marker, "We/You (çoğul)" pair + iki marker.
- Formlar: `aor`, `aor_neg`, `aor_q` (`cal_everyday` rozeti, "every day"/"her gün") ve `can`, `can_neg`, `can_q` (`badge_can`).
- `facts` ve `mentions` türetimi `combine.py` içinde (`F`, `M`, `S`); kişi eki kuralları `grammar.py` (`past_person`, `fut_person`…).
- Boyut sınırları `combine.py` başında: kişi ve geniş zaman/yetenek sahneleri yalnızca yersiz (`None`) kayıtlar, A2'de kişi + tüm
  zamanlar yalnızca `tom`/`woman`; `give` A2'de elma/kitap × (yersiz, park, okul). `scenes/*.jsonl` toplamı ≈ 8,8 MB.
- `bored` ("canı sıkkın") kişi eki alamadığı için kişi cümlelerinden dışlanır (`no_person`).

## Kompozisyon kuralları

- Aktör: `translate(x - 128*s, 230 - 230*s) scale(s)`; elindeki nesne aktörün içinde
  `translate(ax - 128*hs, ay - 230*hs) scale(hs)` (anchor SVG'deki `data-hand`'den gelir).
- `data-tint="primary"` şekiller `hold.tint` ile, `data-tint="sky"` şekiller sahnenin `sky` rengiyle boyanır.
- Duygu: `data-part="mouth"/"eyes"` gizlenir, `overlays/face/` parçası (`data-cx`,`data-cy`) noktasına konur;
  işaret (ter, gözyaşı…) `data-head` yanına (`layouts.mark`).
- `data-frame` grupları döngüde tek tek gösterilir; `prop.states` sırayla oynatılır (ör. ışık açık → kapalı).
- Balon, rozet, güneş/ay konumları `manifest.json` → `layouts`; marker: `layouts.marker` (`dx` yok, merkez `(head.x, head.y + dy)`).

## Yeni asset eklemek

1. `manifest.json`'a satırı ekle (yeni poz ise `poses` + `verbs`, yeni özne ise `characters`).
2. SVG'yi `style-lock.md`'ye uyarak çiz; mevcut bir asset'i referans al.
3. `python tools/validate.py` ve `python tools/combine.py` çalıştır, `preview.html` ile gözle kontrol et.

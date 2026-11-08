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
| `scenes/A1.jsonl` | şimdiki zaman: özne + fiil + nesne + yer, gruplar |
| `scenes/A2.jsonl` | dün/yarın, olumsuz, soru (rozetli), duygular, günün vakti, hava durumu |
| `scenes/B1.jsonl` | etkileşim (konuşmak/dinlemek, el sallamak, aramak), düşünce balonu (istemek, birini düşünmek), ışık/TV/kapı durum değişimi, sıralı iki panel |
| `coverage.md` | Kapsam özeti |
| `tools/combine.py` | `manifest.json` + SVG işaretleri → `scenes/*.jsonl` + `coverage.md` (kural tabanlı, model kullanmaz) |
| `tools/grammar.py` | EN/TR çekim: zaman, olumsuz, soru, ünlü uyumu, -ydı/-lar ekleri |
| `tools/validate.py` | Tüm SVG'leri stil kilidine göre denetler (kareler, durumlar, yüz parçaları dahil) |
| `preview.html` | Kompozisyonun referans uygulaması ve önizleme |

## Komutlar (bu klasörden)

```powershell
python tools/validate.py      # stil denetimi
python tools/combine.py       # sahneleri ve coverage.md'yi yeniden üret
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

Aynı görsel birden çok cümle taşır (takma ad özneler, zaman/olumsuz/soru biçimleri). `badges`
cümleye özeldir: oyun hangi cümleyi soruyorsa onun rozetlerini gösterir.

## Kompozisyon kuralları

- Aktör: `translate(x - 128*s, 230 - 230*s) scale(s)`; elindeki nesne aktörün içinde
  `translate(ax - 128*hs, ay - 230*hs) scale(hs)` (anchor SVG'deki `data-hand`'den gelir).
- `data-tint="primary"` şekiller `hold.tint` ile, `data-tint="sky"` şekiller sahnenin `sky` rengiyle boyanır.
- Duygu: `data-part="mouth"/"eyes"` gizlenir, `overlays/face/` parçası (`data-cx`,`data-cy`) noktasına konur;
  işaret (ter, gözyaşı…) `data-head` yanına (`layouts.mark`).
- `data-frame` grupları döngüde tek tek gösterilir; `prop.states` sırayla oynatılır (ör. ışık açık → kapalı).
- Balon, rozet, güneş/ay konumları `manifest.json` → `layouts`.

## Yeni asset eklemek

1. `manifest.json`'a satırı ekle (yeni poz ise `poses` + `verbs`, yeni özne ise `characters`).
2. SVG'yi `style-lock.md`'ye uyarak çiz; mevcut bir asset'i referans al.
3. `python tools/validate.py` ve `python tools/combine.py` çalıştır, `preview.html` ile gözle kontrol et.

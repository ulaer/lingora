# game-assets

Dil öğrenme oyunu için flat/geometrik SVG kütüphanesi. Amaç: az sayıda modüler asset ile
en çok cümleyi kapsamak. Her cümleye ayrı resim çizilmez; karakter-poz, nesne ve arka plan
uygulama içinde birleştirilir (renk, adet, konum, grup dönüşümleri).

| Dosya | Ne |
|---|---|
| `style-lock.md` | Stil şartnamesi: palet, viewBox, taban çizgisi, tint ve anchor sözleşmesi |
| `assets/` | `characters/` (özne × poz), `objects/`, `backgrounds/` |
| `manifest.json` | Asset → kelime/anlam, pozların fiili ve yer kuralları, takma adlar, gruplar |
| `scenes.json` | Üretilen sahneler: yalnızca asset id'leri + dönüşüm + kanonik EN/TR cümle (sahne başına bir satır) |
| `coverage.md` | Kapsam özeti |
| `tools/combine.py` | `manifest.json` → `scenes.json` + `coverage.md` (kural tabanlı, model kullanmaz) |
| `tools/validate.py` | Tüm SVG'leri stil kilidine göre denetler |
| `preview.html` | Kompozisyonun referans uygulaması ve önizleme |

## Komutlar (bu klasörden)

```powershell
python tools/validate.py      # stil denetimi
python tools/combine.py       # sahneleri ve coverage.md'yi yeniden üret
python -m http.server 8765    # sonra: http://localhost:8765/preview.html?mode=scenes&n=12
```

Önizleme parametreleri: `?mode=assets[&cat=character|object|background]`,
`?mode=scenes&n=12`, `?mode=scenes&ids=tom_swim__pool,kids__run__park__as_tom_and_mary`.

## Kompozisyon kuralı

Nesne, pozun `anchor` noktasına alt-orta noktasıyla oturur:
`translate(ax - 128*s, ay - 230*s) scale(s)`. `data-tint="primary"` taşıyan şekillerin
`fill` değeri sahnenin `transform.tint` değeriyle değiştirilir. Grup sahnelerinde her karakter
`translate(x - 128*scale, 230 - 230*scale) scale(scale)` ile yan yana konur.

## Yeni asset eklemek

1. `manifest.json`'a satırı ekle (yeni poz ise `poses`, yeni özne ise `characters`).
2. SVG'yi `style-lock.md`'ye uyarak çiz; mevcut bir asset'i referans al.
3. `python tools/validate.py` ve `python tools/combine.py` çalıştır, `preview.html` ile gözle kontrol et.

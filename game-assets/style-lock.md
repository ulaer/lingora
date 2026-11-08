# Lingora Stil Kilidi (v1)

Bu şartname her SVG üretiminin başına aynen konur. Sapma = yeniden üretim.

## Biçim
- Flat / geometrik SVG. Yalnızca düz renk dolgu (`fill`).
- YASAK: gradyan, filtre, gölge, blur, doku, desen, `opacity` < 1, `<image>`, `<text>`, `<style>`, CSS sınıfı ile renk, dış referans.
- `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256">`
- Saydam zemin: arka plan dışındaki assetlerde zemin dikdörtgeni YOK.
- Kök altında tek grup: `<g id="<asset_id>">` (ör. `<g id="tom_eat">`). Tüm şekiller bunun içinde.
- Perspektif yok, yandan/önden düz görünüm. Basit şekiller: rect (rx serbest), circle, ellipse, path, polygon.
- Çizgi: yalnızca ince detaylar için (ağız, kitap satırı, kapak çizgisi) `stroke="#2B2D42" stroke-width="4" stroke-linecap="round" fill="none"`. Dış kontur çizgisi YOK.
- Koordinatlar tam sayı veya .5; dosya başına hedef < 4 KB, en fazla 6 KB.

## Palet (tek palet, başka hex YOK)
| ad     | hex       | kullanım |
|--------|-----------|----------|
| ink    | `#2B2D42` | saç (Tom), göz, detay çizgisi, pantolon |
| paper  | `#F1FAEE` | beyaz, süt, kâğıt sayfa |
| skin   | `#F2C6A0` | ten |
| brown  | `#8D5A3B` | saç (Mary), ekmek kabuğu, ahşap, çay/kahve |
| red    | `#E63946` | elma, Mary elbise |
| orange | `#F4A261` | portakal suyu, ekmek içi, çatı |
| yellow | `#E9C46A` | güneş, sarı varyant |
| green  | `#2A9D8F` | yaprak, çim, ağaç |
| blue   | `#457B9D` | Tom tişört, su kenarı, mavi varyant |
| sky    | `#A8DADC` | gökyüzü, su, cam |

## Hizalama
- Taban çizgisi y=230: karakter ayakları ve nesnelerin alt kenarı y=230'a değer.
- Karakter: yatay merkez x=128, kafa üstü ≈ y=40, gövde yüksekliği ≈ 190 px.
- Nesne (tek başına gösterim): x=128 merkezli, alt kenar y=230, en geniş ölçü 120–160 px.
- Arka plan: 256×256 tam dolu; zemin şeridi y=230'dan aşağı; ortadaki x=64–192, y=40–230 alanı sade (karakter oraya gelir).

## Renklendirilebilir (tint) katman
- Uygulama içinde rengi değişebilen şekillere `data-tint="primary"` eklenir; varsayılan `fill` manifestteki `default_tint` olur.
- Bir assette en fazla iki tint kanalı: `primary`, `secondary`.

## Tutma noktası (anchor) — kompozisyon sözleşmesi
Nesne sahnede karakterin üstüne `translate(ax - 128*s, ay - 230*s) scale(s)` ile konur
(nesnenin alt-orta noktası anchor'a oturur). Pozlar el/ağız konumunu buna göre çizer:

| poz   | anchor (ax, ay) | ölçek s | not |
|-------|-----------------|---------|-----|
| stand | (176, 160)      | 0.30    | sağ el yanda, avuç yukarı (nesne avuçta durur) |
| eat   | (150, 100)      | 0.26    | sağ el ağız hizasında |
| drink | (150, 100)      | 0.26    | bardak ağza dayalı, kafa hafif geride |
| read  | (128, 160)      | 0.40    | iki el göğüs önünde, nesne açık tutulur |

Karakter pozu içinde nesne ÇİZİLMEZ; yalnızca boş eller.

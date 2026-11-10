# v4 sözleşmesi (üç iş akışının ortak arayüzü)

v4 üç paralel iş akışıyla yapılır; her akış YALNIZCA kendi dosyalarına dokunur ve bu sözleşmeye uyar.

| Akış | Dokunduğu dosyalar | Dokunmadığı |
|---|---|---|
| **SVG** | `game-assets/assets/**` (yeni dosyalar) | manifest, tools, src, tests |
| **İçerik** | `game-assets/manifest.json`, `game-assets/tools/*.py`, `game-assets/style-lock.md`, `game-assets/README.md` | assets (yalnızca okur), src, tests |
| **Uygulama** | `src/**`, `tests/**` | game-assets (yalnızca okur) |

## 1. Yeni asset kimlikleri (SVG akışı çizer, içerik akışı manifest'e ekler)

Karakter pozları (`assets/characters/`), insanlar: `tom`, `mary`, `man`, `woman`:

| poz | dosya | kare | not |
|---|---|---|---|
| `give` | `<c>_give.svg` | 1 | sağ kol öne uzanmış, avuç yukarı; `data-hand` uzanan avuçta (≈ x 196, y 150). Alıcı sağda durur (pair yerleşimi). |
| `write` | `<c>_write.svg` | 1 | küçük kahverengi masada oturur (masa asset'in içinde), sağ el masada yazıyor; `data-hand` kalem ucu (≈ 150, 170). |
| `cook` | `<c>_cook.svg` | 2 | ayakta, önünde ocak + tencere (asset'in içinde), sağ el kaşıkla karıştırır (kareler: kaşık sol/sağ); tencereden 2 kareli buhar. |

Overlay'ler (`assets/overlays/`), (128,128) merkezli, ≈ 120 px:

| kimlik | anlam |
|---|---|
| `marker_me` | "ben/biz": sarı (#E9C46A) aşağı bakan kalın ok + küçük yıldız — konuşan kişi |
| `marker_you` | "sen/siz": mavi (#457B9D) aşağı bakan kalın ok + el işareti şekli — karşıdaki kişi |
| `cal_everyday` | geniş zaman / alışkanlık: takvim sayfası + döngü oku (ink) |
| `badge_can` | yetenek ("can"): yeşil (#2A9D8F) daire + kâğıt renkli onay işareti |
| `mark_food` | açlık: küçük tabak + çatal (düşünce işareti gibi baş yanında) |
| `mark_thermometer` | hastalık: kırmızı uçlu termometre |

Tüm v1–v3 stil kuralları geçerli (palet, tek kök `<g id>`, yasaklar, yüz parçaları `data-part`, `data-head`, kareler `data-frame`).

## 2. Sahne kaydına eklenen alanlar (içerik akışı üretir, uygulama okur)

`scenes/*.jsonl` kayıtları ve `scenes.json` sahneleri:

```json
"actors": [{"char": "tom_run", "x": 128, "s": 1, "marker": "marker_me"}],
"facts": {"subject": "tom", "verb": "run", "object": null, "variant": null, "count": 0,
          "place": "park", "time": null, "weather": null, "state": null, "other": null}
```

- `actors[].marker`: `marker_me` / `marker_you` — `data-head` noktasının üstüne çizilir (`layouts.marker`: `{"dy": -34, "s": 0.22}`;
  overlay merkezi `(head.x, head.y + dy)`).
- `facts`: resmin olguları. `subject` karakter ya da grup kimliği (`kids`, `parents`…), `other` etkileşimde ikinci kişi,
  `state` duygu/durum (`tired`, `hungry`…).
- Her cümleye `mentions`: cümlenin söylediği olgu boyutları, ör. `["subject","verb","object","count","place"]`.
  Zamir/takma ad özne `subject` söyler ama değeri `subject_ok` listesiyle verilir: `"subject_ok": ["tom", "man"]`
  ("He" iki resme de uyar).
- Kişi: cümleye `"person": "1s" | "2s" | "1p" | "2p" | "3s" | "3p"`.

**Doğruluk kuralı (uygulama çeldiriciyi buna göre eler):** Cümle C, resim R için **doğrudur** ⇔ C'nin `mentions`
listesindeki her boyutta C'nin kaynak sahnesinin olgusu R'nin olgusuna eşittir (`subject` için: R.subject ∈ C.subject_ok
ya da eşit). Çeldirici yalnızca R için doğru OLMAYAN cümlelerden seçilir. `facts` yoksa eski `AlsoTrue` kuralı geçerli.

## 3. Yeni içerik (içerik akışı)

- **Kişi zamirleri** (A1 şimdiki zaman; A2'de tüm biçimler): `I` / `Ben` (`marker_me` tek kişide), `You` / `Sen`
  (`marker_you`), `We` / `Biz` (pair, iki aktörde `marker_me`), `You` (çoğul) / `Siz` (pair, `marker_you`).
  Türkçe kişi ekleri ünlü uyumuyla: koşuyorum/koşuyorsun/koşuyoruz/koşuyorsunuz; koştum/koştun/koştuk/koştunuz;
  koşacağım/koşacaksın/koşacağız/koşacaksınız; olumsuz ve soru (koşuyor muyum?, koştun mu?, koşacak mıyız?);
  sıfat yüklem: yorgunum/yorgunsun/yorgunuz/yorgunsunuz, yorgundum… EN: I am / you are / we are; I'm not…
- **Durum sıfatları** (A2, yüz + işaret): `hungry`/`aç` (mark_food), `sick`/`hasta` (mark_thermometer + eyes_half),
  `sleepy`/`uykulu` (mark_zzz + eyes_half).
- **İfade fiilleri** (A1, yeni SVG yok): `crying`/`ağlıyor` (sad yüz + mark_tears), `laughing`/`gülüyor` (mouth_open +
  mark_sparkle), `smiling`/`gülümsüyor` (mouth_smile).
- **Geniş zaman** (A2, `cal_everyday` rozeti): "Tom reads a book every day." / "Tom her gün bir kitap okur." —
  Türkçe geniş zaman biçimleri fiil başına açıkça yazılır (okur, yer, içer, koşar, yüzer, oturur, uyur, oynar, uçar,
  yürür, konuşur, düşünür, ister, izler, çalar, açar, kapatır, bekler, tutar, verir, yazar, pişirir…) + olumsuz
  (okumaz…) + soru (okur mu?). EN: reads / doesn't read / Does Tom read…?
- **"can"** (A2, `badge_can`): "Tom can swim." / "Tom yüzebilir." — olumsuz "can't swim" / "yüzemez", soru
  "Can Tom swim?" / "Tom yüzebilir mi?". Nesnesiz pozlar (swim, run, walk, fly, wave, cook) için.
- **Yeni fiiller** (A1 + A2 biçimleri): `give` (pair: veren `give` pozu elinde nesne, alıcı `stand`):
  "Tom is giving Mary an apple." / "Tom Mary'ye bir elma veriyor."; `write` (+ pen/letter yok, yazı masada):
  "Mary is writing a letter." / "Mary bir mektup yazıyor."; `cook`: "The woman is cooking." / "Kadın yemek pişiriyor."
  (yalnızca mutfak).
- Kalite kapısı (lint) yeni kurallar: kişi eki ↔ özne uyumu, geniş zaman rozeti, `facts`/`mentions` her kayıtta.

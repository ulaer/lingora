# v5 sözleşmesi — REVIEW-v4 P0 düzeltmeleri

v4 sözleşmesi (`v4-contract.md`) geçerli; bu belge onu genişletir. İki iş akışı:

| Akış | Dosyalar |
|---|---|
| **Veri** | `game-assets/manifest.json`, `game-assets/tools/*.py`, `game-assets/preview.html`, `game-assets/README.md`, `style-lock.md` |
| **Uygulama** | `src/**`, `tests/**` |

## 1. Olumsuz cümle sözleşmesi (kullanıcı kararı: seçenek A — rozet kalır)

- `badge_not` rozeti **resimde gösterilen önermenin tam olarak inkârıdır**. Resim "Tom elma yiyor"u gösterir;
  rozetli doğru cevap "Tom isn't eating an apple." / "Tom elma yemiyor." olur.
- Doğruluk kuralı olumsuzda **ters çevrilmez**. Bir cümlenin bir resim için "doğru cevap" olup olmadığı,
  cümlenin **olumlu çekirdeğinin** (kutupluluk ve soru eki hariç olgular) resimle eşleşmesine bakılarak belirlenir.
- **Çeldiriciler sorulan cümleyle aynı biçimdendir** (`form` ve `badges` aynı). Çekirdeği resim için doğru olan cümle
  çeldirici olamaz; olumlu ve olumsuz cümleler aynı soruda karışmaz.
- Görsel destek: sorulan cümlede `badge_not` varsa, inkâr edilen şeyin üstüne küçük çarpı çizilir: elde nesne varsa
  tutma noktasına (`hold.anchor`), yoksa aktörün göğsüne (`data-head` + (0, 70)). Çarpı = `badge_not` asset'i, ölçek 0.12.
- Oyunda bir **rozet açıklaması** gösterilir: dün, yarın, değil, soru, her gün, -ebilir, ben, sen (Türkçe etiketler).

## 2. Olgu (facts) genişletmesi

- `facts.person`: `1s | 2s | 1p | 2p | 3s | 3p`; kişi zamirli cümlelerin `mentions` listesinde `person` bulunur.
  Doğruluk: `person` söyleyen cümle yalnızca resmin kişisi aynıysa doğrudur (I-resmi ≠ he-resmi).
- `manifest.place_parents`: `{"kitchen": "room", "bedroom": "room", "bathroom": "room"}`. `place` söyleyen
  "at home" (`room`) cümlesi, resmin yeri `room` ya da çocuklarından biriyse doğrudur.
- `facts.holds`: resimde öznenin elinde görünen nesneler (ör. `give`, `eat`, `read`, `drink` resimlerinde de nesne).
  Fiili `hold` olan cümle için doğruluk: cümlenin nesnesi `facts.holds` içinde mi (fiil eşitliği aranmaz).
  ("She is holding a ball." vermek resmi için doğrudur → çeldirici olamaz.)

## 3. Kendi-cümlesi kapısı (lint)

Her kaydın her cümlesi, yukarıdaki kuralla **kendi resmi için doğru** olmalı (`lint.py`, ERROR). Python kuralı ile
C# `ChoiceBuilder` kuralı aynı tabloyla yazılır; `tests/` aynı örneklerle doğrular.

## 4. Görsel düzeltmeler

- **`pair_give` yerleşimi**: veren x=64 s=0.7, alan x=192 s=0.7. Lint: verilen nesnenin tuval x'i alanın gövdesine
  (x ≥ alan.x − 30) girmemeli.
- **Ben/sen işareti konumu**: önce başın üstü (`data-head` + (0, dy)); işaretin tuvaldeki üst kenarı < 4 ise başın
  yanına (`data-head` + (52, 24)) konur. Kural hem `preview.html` hem `SvgScene`'de aynı.
- **A1 de kayıt yolundan** (`scenes/A1.jsonl`): oyun tüm seviyelerde kayıtları çizer (işaret, ifade dahil) ve
  `facts` kuralını kullanır; `scenes.json` geriye uyum için kalır.

## 5. Dil düzeltmeleri

- "ile" nesnesi: küçük harf ve ek biçimi — `tr_ins` (Tom'la, Mary'yle, adamla, kadınla, babamla, annemle,
  öğretmenle, doktorla, polisle, köpekle, kediyle): "Tom adamla konuşuyor."
- 1. kişi soruları (1s/1p `_q`) üretilmez ("Koşuyor muyum?").
- "aç" (hungry) 1p geçmişi "açtık" (= açmak) ile karışır → bu biçim üretilmez.

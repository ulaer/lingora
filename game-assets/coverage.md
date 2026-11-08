# Coverage raporu (v2)

SVG: **141** — background 14, character 69, face 10, object 27, overlay 21

| seviye | sahne (görsel) | cümle | farklı EN kelime | farklı TR kelime | özne |
|---|---|---|---|---|---|
| A1 | 2,834 | 8,307 | 100 | 101 | 26 |  <!-- 1708 KB -->
| A2 | 2,489 | 12,126 | 125 | 179 | 16 |  <!-- 2575 KB -->
| B1 | 1,040 | 3,978 | 109 | 124 | 13 |  <!-- 953 KB -->
| **toplam** | **6,363** | **24,411** | 195 | 279 | |

## A2/B1 özellik başına sahne

- zaman/olumsuz/soru: 1,115
- günün vakti: 1,085
- sıralı panel: 544
- etkileşim: 328
- duygu: 245
- nesne durumu / prop: 120
- istemek (balon): 48
- hava: 44

## Örnek cümleler

- A1 `tom_stand__apple` — He is holding an apple. / O bir elma tutuyor.
- A1 `mary_stand__ball__orange__x3` — She is holding three orange balls. / O üç turuncu top tutuyor.
- A1 `cat_play__ball__blue__park` — The cat is playing with a blue ball in the park. / Kedi parkta mavi bir topla oynuyor.
- A1 `woman_stand__glass__milk` — She is holding a glass of milk. / O bir bardak süt tutuyor.
- A1 `tom_call` — He is talking on the phone. / O telefonla konuşuyor.
- A2 `tom_stand__apple__tense` — Is Tom holding an apple? / Tom bir elma tutuyor mu?
- A2 `man_stand__umbrella__kitchen__tense` — Is the man holding an umbrella in the kitchen? / Adam mutfakta bir şemsiye tutuyor mu?
- A2 `woman_walk__school__tense` — Is the woman walking at school? / Kadın okulda yürüyor mu?
- A2 `mary_read__letter__room__evening` — Mary is reading a letter at home in the evening. / Mary akşam evde bir mektup okuyor.
- A2 `woman_read__letter__shop__morning` — The woman is reading a letter at the shop in the morning. / Kadın sabah mağazada bir mektup okuyor.
- B1 `talk_to__tom__mary` — Tom talked to Mary yesterday. / Tom dün Mary ile konuştu.
- B1 `call__mary__tom__park` — Mary called Tom in the park yesterday. / Mary dün parkta Tom'u aradı.
- B1 `open_door__tom__room` — Tom isn't opening the door at home. / Tom evde kapıyı açmıyor.
- B1 `seq__tom_call__street__then__read` — On the street, Tom first talked on the phone, then he read a book. / Tom sokakta önce telefonla konuştu, sonra bir kitap okudu.
- B1 `seq__man_eat__apple__office__then__talk` — At the office, the man first ate an apple, then he talked. / Adam ofiste önce bir elma yedi, sonra konuştu.

## Kalite kapısı (tools/lint.py)

Birleştirilen aynı-resim kaydı: A1 211, A2 0, B1 0 (cümleleri tek kabul kümesinde).

Oyun örneklemesi (scenes.json `weight`): tutma pozu payı tekdüze %68 → ağırlıklı %39.

| seviye | kural | sayı | örnek |
|---|---|---|---|
| — | sorun yok | 0 | |

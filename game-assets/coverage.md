# Coverage raporu (v2)

SVG: **159** — background 14, character 81, face 10, object 27, overlay 27

| seviye | sahne (görsel) | cümle | farklı EN kelime | farklı TR kelime | özne |
|---|---|---|---|---|---|
| A1 | 3,577 | 9,682 | 127 | 170 | 31 |  <!-- 3081 KB -->
| A2 | 3,171 | 16,851 | 200 | 685 | 20 |  <!-- 4676 KB -->
| B1 | 1,376 | 4,490 | 125 | 145 | 14 |  <!-- 1444 KB -->
| **toplam** | **8,124** | **31,023** | 283 | 793 | |

## A2/B1 özellik başına sahne

- zaman/olumsuz/soru: 1,432
- günün vakti: 1,085
- etkileşim: 816
- sıralı panel: 544
- duygu: 458
- nesne durumu / prop: 120
- istemek (balon): 48
- hava: 44

## Örnek cümleler

- A1 `tom_stand__apple` — He is holding an apple. / O bir elma tutuyor.
- A1 `mary_stand__ball__x2__school` — She is holding two balls at school. / O okulda iki top tutuyor.
- A1 `man_stand__glass__milk__office` — He is holding a glass of milk at the office. / O ofiste bir bardak süt tutuyor.
- A1 `woman_drink__cup__tea__room` — She is drinking tea at home. / O evde çay içiyor.
- A1 `give__man__tom__apple__room` — He is giving Tom an apple at home. / O evde Tom'a bir elma veriyor.
- A2 `tom_stand__apple__tense` — Is Tom holding an apple? / Tom bir elma tutuyor mu?
- A2 `man_read__letter__office__tense` — Is the man reading a letter at the office? / Adam ofiste bir mektup okuyor mu?
- A2 `tom_eat__cake__p1s__tense` — I'm not eating cake. / Ben pasta yemiyorum.
- A2 `mary_drink__cup__coffee__beach__morning` — Mary is drinking coffee on the beach in the morning. / Mary sabah sahilde kahve içiyor.
- A2 `mary_call__room__night` — Mary is talking on the phone at home at night. / Mary gece evde telefonla konuşuyor.
- B1 `talk_to__tom__mary` — Tom talked to Mary yesterday. / Tom dün Mary'yle konuştu.
- B1 `think_about__tom__woman__school` — Tom thought about the woman at school yesterday. / Tom dün okulda kadını düşündü.
- B1 `seq__tom_drink__glass__milk__kitchen__then__read` — In the kitchen, Tom first drank milk, then he read a book. / Tom mutfakta önce süt içti, sonra bir kitap okudu.
- B1 `seq__man_drink__glass__milk__kitchen__then__call` — In the kitchen, the man first drank milk, then he talked on the phone. / Adam mutfakta önce süt içti, sonra telefonla konuştu.
- B1 `mary_stand__apple__wh` — What is Mary holding? / Mary ne tutuyor?

## Kalite kapısı (tools/lint.py)

Birleştirilen aynı-resim kaydı: A1 211, A2 0, B1 0 (cümleleri tek kabul kümesinde).

Oyun örneklemesi (scenes.json `weight`): tutma pozu payı tekdüze %59 → ağırlıklı %37.

| seviye | kural | sayı | örnek |
|---|---|---|---|
| — | sorun yok | 0 | |

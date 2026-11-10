# Coverage raporu (v2)

SVG: **159** — background 14, character 81, face 10, object 27, overlay 27

| seviye | sahne (görsel) | cümle | farklı EN kelime | farklı TR kelime | özne |
|---|---|---|---|---|---|
| A1 | 3,517 | 9,622 | 110 | 164 | 29 |  <!-- 2921 KB -->
| A2 | 3,019 | 16,753 | 179 | 662 | 19 |  <!-- 4458 KB -->
| B1 | 1,040 | 3,978 | 109 | 124 | 13 |  <!-- 1182 KB -->
| **toplam** | **7,576** | **30,353** | 246 | 759 | |

## A2/B1 özellik başına sahne

- zaman/olumsuz/soru: 1,432
- günün vakti: 1,085
- sıralı panel: 544
- duygu: 458
- etkileşim: 328
- nesne durumu / prop: 120
- istemek (balon): 48
- hava: 44

## Örnek cümleler

- A1 `tom_stand__apple` — He is holding an apple. / O bir elma tutuyor.
- A1 `mary_stand__book__x3__school` — She is holding three books at school. / O okulda üç kitap tutuyor.
- A1 `man_stand__apple__bedroom` — He is holding an apple in the bedroom. / O yatak odasında bir elma tutuyor.
- A1 `woman_eat__fish__room` — She is eating a fish at home. / O evde bir balık yiyor.
- A1 `give__mary__man__apple` — She is giving the man an apple. / O adama bir elma veriyor.
- A2 `tom_stand__apple__tense` — Is Tom holding an apple? / Tom bir elma tutuyor mu?
- A2 `man_drink__cup__coffee__beach__tense` — Is the man drinking coffee on the beach? / Adam sahilde kahve içiyor mu?
- A2 `give__woman__man__book__school__tense` — Is the woman giving the man a book at school? / Kadın okulda adama bir kitap veriyor mu?
- A2 `mary_read__letter__room__night` — Mary is reading a letter at home at night. / Mary gece evde bir mektup okuyor.
- A2 `man_walk__office__evening` — The man is walking at the office in the evening. / Adam akşam ofiste yürüyor.
- B1 `talk_to__tom__mary` — Tom talked to Mary yesterday. / Tom dün Mary ile konuştu.
- B1 `call__mary__tom__park` — Mary called Tom in the park yesterday. / Mary dün parkta Tom'u aradı.
- B1 `open_door__tom__room` — Tom isn't opening the door at home. / Tom evde kapıyı açmıyor.
- B1 `seq__tom_call__street__then__read` — On the street, Tom first talked on the phone, then he read a book. / Tom sokakta önce telefonla konuştu, sonra bir kitap okudu.
- B1 `seq__man_eat__apple__office__then__talk` — At the office, the man first ate an apple, then he talked. / Adam ofiste önce bir elma yedi, sonra konuştu.

## Kalite kapısı (tools/lint.py)

Birleştirilen aynı-resim kaydı: A1 211, A2 0, B1 0 (cümleleri tek kabul kümesinde).

Oyun örneklemesi (scenes.json `weight`): tutma pozu payı tekdüze %59 → ağırlıklı %37.

| seviye | kural | sayı | örnek |
|---|---|---|---|
| — | sorun yok | 0 | |

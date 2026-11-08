"""v3 kalite kapısı: belirsizlik + dil + yerleşim denetimi (combine.py her çalıştığında çağırır).

Üç test ajanının (dil, görsel, veri/oyun) ortak önerisi: hataları oyunda değil, üretim anında yakala.
Seviyeler: ERROR (combine --strict ile derlemeyi durdurur) ve WARN (sayılır, rapora yazılır).

  AMB  belirsizlik  — aynı resim farklı cümle setleri; farklı resimler aynı cümle (zamir dışı);
                      resimde olan vakit/hava cümlede yok
  LANG dil          — yanlış eşdeğer ("balık tutuyor"), kanıtsal -mIş durum sıfatı, biçim (boşluk,
                      büyük harf, noktalama), sıralı panelde aynı eylem
  LAY  yerleşim     — balonlu sahnede aktör yerleşimi, çift yerleşimde çakışma, rozet yığını, geniş prop
"""
import json
from collections import defaultdict

PRONOUNS_EN = {"He", "She", "They", "It"}
PRONOUNS_TR = {"O", "Onlar"}
FALSE_FRIENDS_TR = ["balık tut"]                       # balık tutmak = balık avlamak
EVIDENTIAL_TR = ["sıkılmış", "şaşırmış"]               # durum sıfatı değil, kanıtsal kip
TIME_WORDS = {"morning": "in the morning", "evening": "in the evening", "night": "at night"}
WEATHER_WORDS = {"rain": "rain", "snow": "snow", "sunny": "sun", "cloudy": "cloud", "windy": "wind"}

VISUAL_KEYS = ("layout", "bg", "sky", "sky_overlay", "weather", "actors", "bubble", "prop", "panels")


def visual_key(r):
    return json.dumps({k: r.get(k) for k in VISUAL_KEYS}, sort_keys=True, ensure_ascii=False)


def merge_identical(rows):
    """Aynı resmi veren kayıtları birleştirir: cümleleri tek kabul kümesinde toplar.
    ('an apple' / 'a red apple' — varsayılan renk kırmızı olduğundan resim aynı.)"""
    by_key, out, merged = {}, [], 0
    for r in rows:
        k = visual_key(r)
        if k not in by_key:
            r = dict(r)
            r["sentences"] = list(r["sentences"])
            by_key[k] = r
            out.append(r)
            continue
        base = by_key[k]
        seen = {(s["form"], s["en"]) for s in base["sentences"]}
        base["sentences"] += [s for s in r["sentences"] if (s["form"], s["en"]) not in seen]
        base.setdefault("merged_ids", []).append(r["id"])
        merged += 1
    return out, merged


def run(levels, manifest):
    issues = defaultdict(list)   # (sev, code) -> örnekler

    def add(sev, code, example):
        issues[(sev, code)].append(example)

    lay = manifest["layouts"]
    for name, rows in levels.items():
        # --- AMB-1: birleştirmeden sonra aynı resim iki kayıtta kalmamalı
        keys = defaultdict(list)
        for r in rows:
            keys[visual_key(r)].append(r["id"])
        for ids in keys.values():
            if len(ids) > 1:
                add("ERROR", "AMB-1 aynı resim, ayrı kayıtlar", f"{name}: {ids[:3]}")
        for r in rows:
            for s in r["sentences"]:
                en, tr = s["en"], s["tr"]
                # --- AMB-3: resimde olan vakit / hava cümlede de olmalı
                if r.get("time") and TIME_WORDS[r["time"]] not in en:
                    add("ERROR", "AMB-3 vakit cümlede yok", f"{r['id']}: {en}")
                if r.get("weather") and WEATHER_WORDS[r["weather"]] not in en.lower():
                    add("ERROR", "AMB-3 hava cümlede yok", f"{r['id']}: {en}")
                # --- LANG
                low = tr.lower()
                for ff in FALSE_FRIENDS_TR:
                    if ff in low:
                        add("ERROR", "LANG-1 yanlış eşdeğer", f"{r['id']}: {tr}")
                for ev in EVIDENTIAL_TR:
                    if ev in low:
                        add("ERROR", "LANG-2 kanıtsal -mIş", f"{r['id']}: {tr}")
                for text in (en, tr):
                    if "  " in text or not text[:1].isupper() or text[-1] not in ".?":
                        add("ERROR", "LANG-3 biçim", f"{r['id']}: {text}")
            # --- LANG-4: sıralı panelde aynı eylem iki kez
            if r["layout"] == "panels":
                p1, p2 = (p["actors"][0]["char"].split("_", 1)[1] for p in r["panels"])
                if p1 == p2 or {p1, p2} == {"talk", "call"}:
                    add("ERROR", "LANG-4 sıralı aynı eylem", r["id"])
            # --- LAY
            if r["layout"] in ("bubble", "pair_bubble", "pair_wave"):
                want = lay[r["layout"]]["actors"]
                got = [{"x": a["x"], "s": a["s"]} for a in r["actors"]]
                if got != [{"x": w["x"], "s": w["s"]} for w in want[: len(got)]]:
                    add("ERROR", "LAY-1 yerleşim aktörü uyuşmuyor", r["id"])
            if r["layout"] == "pair" and len(r["actors"]) == 2:
                a, b = r["actors"]
                pose = a["char"].split("_", 1)[1]
                if pose in ("swim", "wave") and abs(b["x"] - a["x"]) < 110:
                    add("ERROR", "LAY-2 çift çakışması", r["id"])
            for s in r["sentences"]:
                if len(s.get("badges", [])) > 2:
                    add("ERROR", "LAY-3 rozet yığını", r["id"])
            if r.get("prop"):
                obj = next(a for a in manifest["assets"] if a["id"] == r["prop"]["obj"])
                if obj.get("prop_scale") and r["prop"].get("s") != obj["prop_scale"]:
                    add("ERROR", "LAY-4 geniş prop ölçeği", r["id"])

    # --- AMB-2: farklı resimler aynı cümle (zamir/çoğul zamir hariç — onlar bilinçli olarak çok resme uyar)
    for lang, allow in (("en", PRONOUNS_EN), ("tr", PRONOUNS_TR)):
        text_keys = defaultdict(set)
        for rows in levels.values():
            for r in rows:
                k = visual_key(r)
                for s in r["sentences"]:
                    if s["form"].startswith("prog") and not s.get("badges"):
                        first = s[lang].split()[0]
                        if first not in allow:
                            text_keys[s[lang]].add(k)
        for text, ks in text_keys.items():
            if len(ks) > 1:
                add("WARN", f"AMB-2 farklı resimler aynı cümle ({lang})", text)
    if lay["badge"].get("dx"):
        add("ERROR", "LAY-3 rozetler yatay (başa biner)", "layouts.badge")
    return issues


def summary_lines(issues):
    lines = ["| seviye | kural | sayı | örnek |", "|---|---|---|---|"]
    for (sev, code), ex in sorted(issues.items()):
        lines.append(f"| {sev} | {code} | {len(ex)} | {ex[0][:90].replace('|', '/')} |")
    if len(lines) == 2:
        lines.append("| — | sorun yok | 0 | |")
    return lines

"""v3 kalite kapısı: belirsizlik + dil + yerleşim denetimi (combine.py her çalıştığında çağırır).

Üç test ajanının (dil, görsel, veri/oyun) ortak önerisi: hataları oyunda değil, üretim anında yakala.
Seviyeler: ERROR (combine --strict ile derlemeyi durdurur) ve WARN (sayılır, rapora yazılır).

  AMB  belirsizlik  — aynı resim farklı cümle setleri; farklı resimler aynı cümle (zamir dışı);
                      resimde olan vakit/hava cümlede yok
  LANG dil          — yanlış eşdeğer ("balık tutuyor"), kanıtsal -mIş durum sıfatı, biçim (boşluk,
                      büyük harf, noktalama), sıralı panelde aynı eylem
  LAY  yerleşim     — balonlu sahnede aktör yerleşimi, çift yerleşimde çakışma, rozet yığını, geniş prop
  FACT v4 olgular   — her kayıtta `facts`; her cümlede `mentions` (boş olmayan boyutlar) ve `person`;
                      aynı resim birleşirken olgu çelişmesi
  PERS v4 kişi      — cümlenin kişisi ↔ zamir ↔ Türkçe kişi eki ↔ resimdeki marker (ben/sen/biz/siz)
  HAB  v4 geniş z.  — aor* biçimi `cal_everyday` taşır ("every day"/"her gün" yalnızca onda)
  CAN  v4 yetenek   — can* biçimi `badge_can` taşır ("can"/"can't" yalnızca onda)
"""
import json
import re
from collections import defaultdict

PRONOUNS_EN = {"He", "She", "They", "It", "I", "You", "We"}
PRONOUNS_TR = {"O", "Onlar", "Ben", "Sen", "Biz", "Siz"}
FACT_KEYS = ("subject", "verb", "object", "variant", "count", "place", "time", "weather", "state", "other")
PERSONS = {"1s", "2s", "1p", "2p", "3s", "3p"}
PERSON_PRON = {"1s": ("i", "Ben", "marker_me"), "2s": ("you", "Sen", "marker_you"),
               "1p": ("we", "Biz", "marker_me"), "2p": ("you", "Siz", "marker_you")}
TR_2P = re.compile(r"(s[ıiuü]n[ıiuü]z|[dt][ıiuü]n[ıiuü]z)$")
FACT_CONFLICTS = []   # merge_identical doldurur: aynı resim, farklı olgu
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
        if r.get("facts") != base.get("facts"):
            FACT_CONFLICTS.append(f"{base['id']} <- {r['id']}")
        seen = {(s["form"], s["en"]) for s in base["sentences"]}
        base["sentences"] += [s for s in r["sentences"] if (s["form"], s["en"]) not in seen]
        base.setdefault("merged_ids", []).append(r["id"])
        merged += 1
    return out, merged


def tr_person_ok(person, word):
    w = word.rstrip(".?").lower()
    if person == "1s":
        return w.endswith("m")
    if person == "2s":
        return w.endswith("n")
    if person == "2p":
        return bool(TR_2P.search(w))
    return w.endswith(("z", "k")) and not TR_2P.search(w)       # 1p


def check_v4(name, rows, add):
    for r in rows:
        f = r.get("facts")
        if not isinstance(f, dict) or set(f) != set(FACT_KEYS):
            add("ERROR", "FACT-1 kayıtta facts yok/eksik", r["id"])
            f = f or {}
        marks = {a.get("marker") for a in r["actors"]}
        for s in r["sentences"]:
            en, tr, pers = s["en"], s["tr"], s.get("person")
            ms = s.get("mentions")
            if not isinstance(ms, list) or pers not in PERSONS:
                add("ERROR", "FACT-2 cümlede mentions/person yok", f"{r['id']}: {en}")
                continue
            for d in ms:
                if d not in FACT_KEYS or (not f.get(d) and d != "count") or (d == "count" and not f.get("count")):
                    add("ERROR", "FACT-2 mentions olgusuz boyutu söylüyor", f"{r['id']}: {d} / {en}")
            if "subject_ok" in s and not (isinstance(s["subject_ok"], list) and s["subject_ok"]):
                add("ERROR", "FACT-2 subject_ok boş", f"{r['id']}: {en}")
            # --- PERS
            toks = {w.strip(".,?").lower() for w in en.split()}
            if pers in PERSON_PRON:
                p_en, p_tr, marker = PERSON_PRON[pers]
                en_ok = bool({"i", "i'm"} & toks) if pers == "1s" else p_en in toks
                if not en_ok or tr.split()[0] != p_tr:
                    add("ERROR", "PERS-1 zamir kişiyle uyuşmuyor", f"{r['id']}: {en} / {tr}")
                words = tr.rstrip(".?").split()
                if len(words) > 1 and words[-1] in ("mı", "mi", "mu", "mü"):   # "koştum mu": kişi parçacıktan önce
                    words = words[:-1]
                if not tr_person_ok(pers, words[-1]):
                    add("ERROR", "PERS-2 kişi eki özneyle uyuşmuyor", f"{r['id']}: {tr}")
                if marks - {None} != {marker} or r["actors"][0].get("marker") != marker:
                    add("ERROR", "PERS-3 resimde marker uyuşmuyor", f"{r['id']}: {pers} {sorted(map(str, marks))}")
            else:
                if tr.split()[0] in ("Ben", "Sen", "Biz", "Siz") or toks & {"i", "we", "i'm", "you"} or marks - {None}:
                    add("ERROR", "PERS-4 üçüncü kişi cümlesinde kişi zamiri/marker", f"{r['id']}: {en}")
            # --- HAB / CAN
            form, bd = s["form"], s.get("badges", [])
            hab = form.startswith("aor")
            if hab != ("cal_everyday" in bd) or hab != ("every day" in en and "her gün" in tr):
                add("ERROR", "HAB-1 geniş zaman rozeti/ifadesi", f"{r['id']}: {en} / {tr}")
            can = form.startswith("can")
            if can != ("badge_can" in bd):
                add("ERROR", "CAN-1 yetenek rozeti", f"{r['id']}: {en} / {tr}")
            if can != bool(re.search(r"\bcan(?:'t)?\b", en.lower())):
                add("ERROR", "CAN-2 'can' yalnızca yetenek biçiminde", f"{r['id']}: {en}")


def run(levels, manifest):
    issues = defaultdict(list)   # (sev, code) -> örnekler

    def add(sev, code, example):
        issues[(sev, code)].append(example)

    lay = manifest["layouts"]
    for c in FACT_CONFLICTS:
        add("ERROR", "FACT-3 aynı resim, farklı olgu", c)
    for name, rows in levels.items():
        check_v4(name, rows, add)
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

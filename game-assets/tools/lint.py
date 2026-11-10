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
  v5 OWN  kendi-cümlesi kapısı — her cümle, kendi resmi için doğruluk kuralıyla (`sentence_true`) doğru olmalı
  v6 WH / IMP / POSS / TR-ALT — wh cevabı resimle uyumlu, sorulan boyut mentions'ta yok; emir = marker_you + mark_exclaim;
                    iyelik sahibi ↔ marker; tr_alt = zamirsiz biçim
  v5 LAY-5 pair_give — verilen nesne alıcının gövdesine girmez (tuval x < alıcı.x − 30)
"""
import json
import re
from collections import defaultdict

PRONOUNS_EN = {"He", "She", "They", "It", "I", "You", "We"}
PRONOUNS_TR = {"O", "Onlar", "Ben", "Sen", "Biz", "Siz"}
FACT_KEYS = ("subject", "verb", "object", "variant", "count", "place", "time", "weather", "state", "other", "person", "holds",
             "distance", "owner", "receiver_person")
OWNER_MARKER = {"1s": "marker_me", "2s": "marker_you"}
PERSONS = {"1s", "2s", "1p", "2p", "3s", "3p"}
PERSON_PRON = {"1s": ("i", "Ben", "marker_me"), "2s": ("you", "Sen", "marker_you"),
               "1p": ("we", "Biz", "marker_me"), "2p": ("you", "Siz", "marker_you")}
TR_2P = re.compile(r"(s[ıiuü]n[ıiuü]z|[dt][ıiuü]n[ıiuü]z)$")
FACT_CONFLICTS = []   # merge_identical doldurur: aynı resim, farklı olgu
FALSE_FRIENDS_TR = ["balık tut"]                       # balık tutmak = balık avlamak
EVIDENTIAL_TR = ["sıkılmış", "şaşırmış"]               # durum sıfatı değil, kanıtsal kip
TIME_WORDS = {"morning": "in the morning", "evening": "in the evening", "night": "at night"}
WEATHER_WORDS = {"rain": "rain", "snow": "snow", "sunny": "sun", "cloudy": "cloud", "windy": "wind"}

VISUAL_KEYS = ("layout", "bg", "sky", "sky_overlay", "weather", "actors", "bubble", "prop", "panels", "show")


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


def sentence_true(c_facts, c, r_facts, place_parents):
    """Doğruluk kuralı (v4 §2 + v5 §1-2). C# `ChoiceBuilder` aynı tabloyu uygular.

    c_facts: cümlenin KAYNAK kaydının facts'i; c: cümle (`mentions`, `person`, `subject_ok`);
    r_facts: sınanan resmin facts'i; place_parents: manifest.place_parents.
    Cümle R için doğrudur <=> c.mentions içindeki HER boyut için:

      boyut             koşul
      ----------------  ----------------------------------------------------------------------
      subject           R.subject == C.subject  veya  R.subject in c.subject_ok
      verb              R.verb == C.verb;  C.verb == "hold" ise aranmaz (nesne `holds`ta bakılır)
      object            R.object == C.object;  C.verb == "hold" ise C.object in R.holds de olur
      variant, count    eşitlik;  C.verb == "hold" ve R.object != C.object ise aranmaz
      place             R.place == C.place  veya  place_parents[R.place] == C.place ("at home")
      person            c.person == R.person   (yalnızca zamirli cümleler söyler: I/You/We/He/She/They)
      time, weather,
      state, other      eşitlik
      (role == "other"  subject: R.other in c.subject_ok;  other: R.subject == C.subject;  verb: R.verb == C.verb
                        yani "listens to" (kaynak verb talk_to) yalnızca talk_to resmini sağlar)

    Olumsuzlukta ters çevirme YOKTUR: kutupluluk (`_neg`, `badge_not`) ve soru eki hiçbir boyutu değiştirmez;
    doğruluk olumlu çekirdeğin eşleşmesidir. Çeldirici: cümleyle aynı `form` + `badges`, ve bu işlev False.
    """
    hold_verb = c_facts.get("verb") == "hold"
    role_other = c.get("role") == "other"
    same_obj = r_facts.get("object") == c_facts.get("object")
    for d in c.get("mentions", []):
        if d == "subject" and role_other:
            ok = r_facts.get("other") in c.get("subject_ok", [])
        elif d == "other" and role_other:
            ok = r_facts.get("subject") == c_facts.get("subject")
        elif d == "subject":
            ok = r_facts.get("subject") == c_facts.get("subject") or r_facts.get("subject") in c.get("subject_ok", [])
        elif d == "verb":
            ok = hold_verb or r_facts.get("verb") == c_facts.get("verb")
        elif d == "object":
            ok = same_obj or (hold_verb and c_facts.get("object") in (r_facts.get("holds") or []))
        elif d in ("variant", "count"):
            ok = (hold_verb and not same_obj) or r_facts.get(d) == c_facts.get(d)
        elif d == "place":
            ok = place_ok(c_facts.get("place"), r_facts.get("place"), place_parents)
        elif d == "person":
            ok = c.get("person") == r_facts.get("person")
        else:
            ok = r_facts.get(d) == c_facts.get(d)
        if not ok:
            return False
    return True


def place_ok(claimed, actual, place_parents):
    return actual == claimed or place_parents.get(actual) == claimed


def own_sentence_gate(r, s, ctx):
    """Kayıt r'nin cümlesi s, KENDİ resmi için doğru mu? Kural `sentence_true` + cümle metninden çıkan iddialar
    (özne etiketi, yer ifadesi, vakit ifadesi, kişi). Hata nedenlerinin listesini döndürür (boş = doğru)."""
    f, bad = r["facts"], []
    pp = ctx["place_parents"]
    if not sentence_true(f, s, f, pp):
        bad.append("kural")
    ids = s.get("subject_ok") or ctx["labels"].get(s["subject"])
    who = f.get("other") if s.get("role") == "other" else f.get("subject")
    if ids and who not in ids:
        bad.append(f"özne iddiası {ids} != {who}")
    for pid, phrases in ctx["places"].items():
        if any(ph in s["en"].lower() for ph in phrases):
            if "place" not in s["mentions"] or not place_ok(pid, f.get("place"), pp):
                bad.append(f"yer iddiası {pid} != {f.get('place')}")
    for tid, phrase in TIME_WORDS.items():
        if phrase in s["en"] and (f.get("time") != tid or "time" not in s["mentions"]):
            bad.append(f"vakit iddiası {tid}")
    if s["person"] in PERSON_PRON and f.get("person") != s["person"]:
        bad.append(f"kişi iddiası {s['person']} != {f.get('person')}")
    fm = s["form"]
    if fm.startswith("wh_"):      # v6: sorulan boyut mentions'ta yok, cevap resmin olgusu
        ans = (s.get("answer") or {}).get("en", "").rstrip(".").lower()
        if fm == "wh_what" and not (f.get("object") and any(n in ans for n in ctx["objects"].get(f["object"], ["?"]))):
            bad.append("wh_what cevabı nesne değil")
        if fm == "wh_where" and not any(ph.replace("at ", "").replace("in ", "").replace("on ", "") in ans
                                        for ph in ctx["places"].get(f.get("place"), {"\0"})):
            bad.append("wh_where cevabı yer değil")
        if fm == "wh_who" and f.get("subject") not in ctx["labels"].get(s["answer"]["en"].rstrip("."), []):
            bad.append("wh_who cevabı özne değil")
        asked = {"wh_what": "object", "wh_where": "place", "wh_who": "subject"}[fm]
        if asked in s["mentions"]:
            bad.append(f"sorulan boyut ({asked}) mentions'ta")
    return bad


def make_ctx(manifest):
    labels = defaultdict(list)
    for c in manifest["characters"]:
        labels[c["en"]].append(c["id"])
        for a in c.get("aliases", []):
            if a["en"] not in PRONOUNS_EN:
                labels[a["en"]].append(c["id"])
    for g in manifest.get("groups", []):
        for sj in g["subjects"]:
            if sj["en"] not in PRONOUNS_EN:
                labels[sj["en"]].append(g["id"])
    places = {}
    for a in manifest["assets"]:
        if a["category"] == "background":
            places[a["id"]] = {a["en"].lower()} | {o["en"].lower() for o in a.get("pose_overrides", {}).values() if "en" in o}
    objects = {a["id"]: [a["en"].lower()] + [c["en"].lower() for c in a.get("contents", [])]
               for a in manifest["assets"] if a["category"] == "object"}
    return {"labels": dict(labels), "places": places, "objects": objects,
            "place_parents": manifest.get("place_parents", {}), "aor_verbs": set(manifest.get("aorist", {}).get("verbs", []))}


def tr_person_ok(person, word):
    w = word.rstrip(".?").lower()
    if person == "1s":
        return w.endswith("m")
    if person == "2s":
        return w.endswith("n")
    if person == "2p":
        return bool(TR_2P.search(w))
    return w.endswith(("z", "k")) and not TR_2P.search(w)       # 1p


def check_v4(name, rows, add, ctx):
    lay_give = ctx["layouts"].get("pair_give")
    for r in rows:
        if r["layout"] in ("show_near", "show_far"):
            if r["layout"] not in ctx["layouts"] or not (r.get("show") or {}).get("obj") or r["actors"]:
                add("ERROR", "LAY-6 show kaydı: layouts/show/actors", r["id"])
        if r["layout"] == "pair_give" and lay_give:
            giver, recv = r["actors"][0], r["actors"][1]
            h = giver.get("hold")
            if h and h.get("anchor"):
                canvas_x = giver["x"] - 128 * giver["s"] + h["anchor"][0] * giver["s"]
                if not canvas_x < recv["x"] - 30:
                    add("ERROR", "LAY-5 verilen nesne alıcıya biniyor", f"{r['id']}: x={canvas_x:.0f}")
            if [(a["x"], a["s"]) for a in r["actors"]] != [(w["x"], w["s"]) for w in lay_give["actors"]]:
                add("ERROR", "LAY-1 yerleşim aktörü uyuşmuyor", r["id"])
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
            # --- OWN (v5): cümle kendi resmi için doğru olmalı
            for why in own_sentence_gate(r, s, ctx):
                add("ERROR", "OWN-1 cümle kendi resmi için doğru değil", f"{r['id']}: {en} [{why}]")
            if pers in ("1s", "1p") and s["form"].endswith("_q"):
                add("ERROR", "PERS-5 1. kişi sorusu", f"{r['id']}: {en}")
            if "açtık" in tr.lower() or " ile " in tr:
                add("ERROR", "LANG-5 'açtık' / büyük harfli 'ile' nesnesi", f"{r['id']}: {tr}")
            # --- PERS
            toks = {w.strip(".,?").lower() for w in en.split()}
            form, bd = s["form"], s.get("badges", [])
            m0 = r["actors"][0].get("marker") if r["actors"] else None
            if form in ("imp", "imp_neg"):      # v6 emir: marker_you + mark_exclaim, 2. tekil
                if pers != "2s" or m0 != "marker_you" or "mark_exclaim" not in bd or not en.endswith("!"):
                    add("ERROR", "IMP-1 emir: marker_you + mark_exclaim + 2s", f"{r['id']}: {en}")
                if (form == "imp_neg") != ("badge_not" in bd):
                    add("ERROR", "IMP-2 emir olumsuzu badge_not", f"{r['id']}: {en}")
            elif form == "poss":                # v6 iyelik: sahip marker'ı
                if m0 != OWNER_MARKER.get(f.get("owner")):
                    add("ERROR", "POSS-1 iyelik sahibi ↔ marker", f"{r['id']}: owner={f.get('owner')} marker={m0}")
            elif pers in PERSON_PRON:
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
                if tr.split()[0] in ("Ben", "Sen", "Biz", "Siz") or (toks & {"i", "we", "i'm", "you"} and not f.get("receiver_person")) or m0:
                    add("ERROR", "PERS-4 üçüncü kişi cümlesinde kişi zamiri/marker", f"{r['id']}: {en}")
            if f.get("receiver_person"):        # v6 nesne zamiri: alıcıda marker
                if len(r["actors"]) < 2 or r["actors"][1].get("marker") != OWNER_MARKER[f["receiver_person"]]:
                    add("ERROR", "PERS-6 alıcı marker'ı receiver_person ile uyuşmuyor", r["id"])
            # --- tr_alt
            if "tr_alt" in s and (pers not in PERSON_PRON or len(s["tr_alt"]) != 1 or
                                  s["tr_alt"][0][1:] != tr.split(" ", 1)[1][1:]):
                add("ERROR", "LANG-6 tr_alt zamirsiz biçim değil", f"{r['id']}: {tr} / {s['tr_alt']}")
            if pers in PERSON_PRON and form not in ("imp", "imp_neg", "poss") and "tr_alt" not in s:
                add("ERROR", "LANG-6 kişi cümlesinde tr_alt yok", f"{r['id']}: {tr}")
            # --- WH
            if form.startswith("wh_") and ("answer" not in s or "badge_question" not in bd):
                add("ERROR", "WH-1 wh cümlesinde answer/badge_question yok", f"{r['id']}: {en}")
            # --- HAB / CAN
            hab = form.startswith("aor")
            if hab and f.get("verb") not in ctx["aor_verbs"]:
                add("ERROR", "HAB-2 geniş zaman alışkanlık fiili değil", f"{r['id']}: {en}")
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
    ctx = make_ctx(manifest)
    ctx["layouts"] = lay
    for c in FACT_CONFLICTS:
        add("ERROR", "FACT-3 aynı resim, farklı olgu", c)
    for name, rows in levels.items():
        check_v4(name, rows, add, ctx)
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
                    if "  " in text or not text[:1].isupper() or text[-1] not in ".?!":
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
                    if s["form"].startswith("prog") and not s.get("badges") and not r["facts"].get("receiver_person"):
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

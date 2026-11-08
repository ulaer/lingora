"""manifest.json + SVG işaretleri -> scenes/A1.jsonl, A2.jsonl, B1.jsonl + coverage.md.

Sıfır model token; tamamen kural tabanlı. Her satır bir görsel sahnedir; aynı görsel birden çok
cümle taşır (takma ad özneler, zaman/olumsuz/soru biçimleri).

  A1  şimdiki zaman: özne × poz(fiil) × nesne[renk|içerik] × adet × yer, gruplar
  A2  dün/yarın, olumsuz, soru (rozetlerle), duygular, günün vakti (gökyüzü rengi), hava durumu
  B1  etkileşimler (konuşmak/dinlemek, el sallamak, aramak), düşünce balonu (istemek, birini düşünmek),
      nesne durumu değişimi (ışığı/TV'yi açıp kapatmak, kapı), sıralı iki panel ("önce …, sonra …")

Kullanım: python tools/combine.py   (game-assets klasöründen)
"""
import json
import re
import sys
from collections import Counter
from itertools import permutations
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import grammar as G  # noqa: E402
import lint as L  # noqa: E402

STRICT = "--strict" in sys.argv

ROOT = Path(__file__).resolve().parent.parent
m = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))
assets = m["assets"]
A = {a["id"]: a for a in assets}
chars = {c["id"]: c for c in m["characters"]}
poses = {p["id"]: p for p in m["poses"]}
verbs = m["verbs"]
colors, numbers = m["colors"], m["numbers"]
LAY = m["layouts"]

# --- SVG içindeki işaretleri oku (data-head / data-hand); içerik ekrana basılmaz ---
POINT = re.compile(r'<g\b[^>]*\bid="([^"]+)"[^>]*>')
for a in assets:
    if a["category"] != "character":
        continue
    path = ROOT / a["file"]
    if not path.exists():
        continue
    head = POINT.search(path.read_text(encoding="utf-8"))
    tag = head.group(0) if head else ""
    for key in ("head", "hand"):
        mm = re.search(rf'data-{key}="(\d+(?:\.\d+)?),\s*(\d+(?:\.\d+)?)"', tag)
        if mm:
            a[key] = [round(float(mm.group(1))), round(float(mm.group(2)))]

char_assets = [a for a in assets if a["category"] == "character" and (ROOT / a["file"]).exists()]
pose_asset = {(a["character"], a["pose"]): a["id"] for a in char_assets}
objects = [a for a in assets if a["category"] == "object"]
places = [a for a in assets if a["category"] == "background"]
PL = {p["id"]: p for p in places}
ORIGINAL_PLACES = {None, "park", "room", "school"}
HUMANS = [c for c in ("tom", "mary", "man", "woman", "doctor", "police") if c in chars]


# ---------------------------------------------------------------- yardımcılar
def places_for(pose, ch):
    allowed = pose.get("places")
    excluded = set(ch.get("place_exclude", []))
    return [None] + [p for p in places if (allowed is None or p["id"] in allowed) and p["id"] not in excluded]


def place_phrase(place, pose=None):
    if not place:
        return None
    o = place.get("pose_overrides", {}).get(pose["id"], {}) if pose else {}
    return o.get("en", place["en"]), o.get("tr_loc", place["tr_loc"])


def subj_of(ch, alias=None):
    s = alias or ch
    return {"en": s["en"], "tr": s["tr"], "plural": False, "tr_plural": False, "id": ch["id"]}


def subjects_for(ch, place_id):
    out = [subj_of(ch)]
    for al in ch.get("aliases", []):
        if "places_only" in al and place_id not in al["places_only"]:
            continue
        out.append(subj_of(ch, al))
    return out


def obj_en_lower(ch):
    return ch["en"] if ch.get("proper") else ch["en"][0].lower() + ch["en"][1:]


def anchor_of(asset_id, pose):
    """Pozun manifest anchor'ı önceliklidir (v1 sahneleriyle birebir uyum); yoksa SVG'deki data-hand."""
    return pose.get("anchor") or A[asset_id].get("hand")


def actor(asset_id, x=128, s=1.0, hold=None, expr=None):
    out = {"char": asset_id, "x": x, "s": s}
    if hold:
        out["hold"] = hold
    if expr:
        out["expr"] = expr
    return out


def sent(form, en_tr, subject, badges=True):
    en, tr = en_tr
    d = {"form": form, "subject": subject, "en": en, "tr": tr}
    b = G.badges_for(form) if badges else []
    if b:
        d["badges"] = b
    return d


def rec(rid, level, bg, actors, sentences, layout="single", **extra):
    r = {"id": rid, "level": level, "layout": layout, "bg": bg, "actors": actors, "sentences": sentences}
    r.update({k: v for k, v in extra.items() if v is not None})
    return r


def fits(pose, obj):
    if pose["requires"] == "none":
        return False
    if pose["requires"] == "any":
        return obj.get("holdable", True) and obj.get("role", "handheld") == "handheld"
    return pose["requires"] in obj["classes"]


def noun_variants(obj):
    """(varyant id, tint, en(n), tr(n)) — renk/içerik dönüşümleri."""
    if obj.get("tint_role") == "contents":
        for c in obj["contents"]:
            yield c["id"], c["tint"], (lambda n, c=c: c["en"]), (lambda n, c=c: c["tr"])
        return
    if not obj["countable"]:
        yield None, None, (lambda n: obj["en"]), (lambda n: obj["tr"])
        return
    for color in [None] + obj.get("tints", []):
        hexv = colors[color]["hex"] if color else (colors[obj["default_tint"]]["hex"] if obj.get("default_tint") else None)

        def en(n, color=color):
            adj = colors[color]["en"] + " " if color else ""
            if n == 1:
                art = ("an" if adj[:1] in "aeiou" else "a") if adj else obj["article_en"]
                return f"{art} {adj}{obj['en']}"
            return f"{numbers[str(n)]['en']} {adj}{obj['en_plural']}"

        def tr(n, color=color):
            if n == 1:
                return f"{colors[color]['tr']} bir {obj['tr']}" if color else f"bir {obj['tr']}"
            return f"{numbers[str(n)]['tr']} {colors[color]['tr'] + ' ' if color else ''}{obj['tr']}"

        yield color, hexv, en, tr


def object_phrase(pose, obj, en_np, tr_np, n):
    en_o, tr_o = en_np(n), tr_np(n)
    if pose["id"] == "stand" and obj.get("measure"):
        en_o, tr_o = f"{obj['measure']['en']} {en_o}", f"{obj['measure']['tr']} {tr_o}"
    if pose.get("tr_case") == "with" and tr_o.endswith(obj["tr"]):
        tr_o = tr_o[: -len(obj["tr"])] + obj["tr_with"]
    return en_o, tr_o


# ---------------------------------------------------------------- A1
A1, A1_base = [], []   # A1_base: varsayılan varyant (renk yok, tek adet) — A2/B1 bunu çoğaltır
for ca in char_assets:
    ch, pose = chars[ca["character"]], poses[ca["pose"]]
    verb = verbs[pose["verb"]]
    for place in places_for(pose, ch):
        pid = place["id"] if place else None
        pp = place_phrase(place, pose)
        items = []
        if pose["requires"] == "none":
            hold = None
            if pose.get("attach"):
                hold = {"obj": pose["attach"], "anchor": anchor_of(ca["id"], pose), "s": pose["scale"]}
            items.append((None, None, 1, hold, "", ""))
        else:
            for obj in objects:
                if not fits(pose, obj):
                    continue
                counts = [1, 2, 3] if (pose.get("allow_count") and obj.get("countable") and obj.get("tints") is not None
                                       and obj["id"] in ("apple", "ball", "book")) else [1]
                for variant, tint, en_np, tr_np in noun_variants(obj):
                    for n in counts:
                        if (variant and obj.get("tint_role") != "contents" or n > 1) and pid not in ORIGINAL_PLACES:
                            continue
                        en_o, tr_o = object_phrase(pose, obj, en_np, tr_np, n)
                        hold = {"obj": obj["id"], "tint": tint, "n": n, "anchor": anchor_of(ca["id"], pose), "s": pose["scale"]}
                        items.append((obj["id"], variant, n, hold, en_o, tr_o))
        for oid, variant, n, hold, en_o, tr_o in items:
            default = (variant is None or (oid and A[oid].get("tint_role") == "contents")) and n == 1
            subs = subjects_for(ch, pid)  # takma adlar tüm varyantlarda (uygulamanın scenes.json'u buna dayanır)
            sentences = [sent("prog", G.verb_clause("prog", s, verb, en_o, tr_o, pp), s["en"]) for s in subs]
            rid = "__".join(x for x in [ca["id"], oid, variant, f"x{n}" if n > 1 else None, pid] if x)
            r = rec(rid, "A1", pid, [actor(ca["id"], hold=hold)], sentences)
            A1.append(r)
            if default:
                A1_base.append((r, ch, pose, verb, en_o, tr_o, place, hold))

# Gruplar (nesnesiz pozlar): "Tom and Mary are walking." / "Tom ve Mary yürüyorlar."
default_layout = m.get("group_layout", {"scale": 0.8, "centers_x": [80, 176]})
for g in m.get("groups", []):
    members = [chars[c] for c in g["members"]]
    merged = {"place_exclude": sorted({p for c in members for p in c.get("place_exclude", [])})}
    for pose in poses.values():
        if pose["requires"] != "none" or pose.get("attach") or pose["id"] == "lie":
            continue
        if not all((c["id"], pose["id"]) in pose_asset for c in members):
            continue
        ids = [pose_asset[(c["id"], pose["id"])] for c in members]
        lay = pose.get("group_layout", default_layout)
        for place in places_for(pose, merged):
            pid = place["id"] if place else None
            sentences = []
            for s in g["subjects"]:
                subj = {"en": s["en"], "tr": s["tr"], "plural": True, "tr_plural": s["tr_plural"]}
                sentences.append(sent("prog", G.verb_clause("prog", subj, verbs[pose["verb"]], place=place_phrase(place, pose)), s["en"]))
            A1.append(rec("__".join(x for x in [g["id"], pose["id"], pid] if x), "A1", pid,
                          [actor(cid, x, lay["scale"]) for cid, x in zip(ids, lay["centers_x"])], sentences, layout="pair"))

# ---------------------------------------------------------------- A2
A2 = []
TENSE_FORMS = [f for f in G.FORMS if f != "prog"]
HOLD_CORE = {"apple", "book", "ball", "phone", "key", "bag", "umbrella"}
for r, ch, pose, verb, en_o, tr_o, place, hold in A1_base:
    if ch["id"] not in HUMANS + ["dog", "cat"]:
        continue
    if pose["id"] == "stand" and hold["obj"] not in HOLD_CORE:  # boyut sınırı: tutma pozunda çekirdek nesneler
        continue
    s = subj_of(ch)
    pp = place_phrase(place, pose)
    sentences = [sent(f, G.verb_clause(f, s, verb, en_o, tr_o, pp), s["en"]) for f in TENSE_FORMS]
    A2.append(rec(r["id"] + "__tense", "A2", r["bg"], r["actors"], sentences))

# Günün vakti: "Tom is reading a book at home at night." / "Tom gece evde bir kitap okuyor."
TOD_POSES = {"read", "eat", "drink", "walk", "lie", "call"}
for r, ch, pose, verb, en_o, tr_o, place, hold in A1_base:
    if not place or pose["id"] not in TOD_POSES or ch["id"] not in HUMANS:
        continue
    if hold and hold.get("obj") not in (None, "book", "phone", "apple", "bread", "glass", "cup", "letter"):
        continue
    for tod in m["times_of_day"]:
        if tod["id"] == "morning" and pose["id"] == "lie":
            continue
        s = subj_of(ch)
        sentences = [sent("prog", G.verb_clause("prog", s, verb, en_o, tr_o, place_phrase(place, pose), (tod["en"], tod["tr"])), s["en"])]
        A2.append(rec(f"{r['id']}__{tod['id']}", "A2", r["bg"], r["actors"], sentences,
                      sky=tod["sky"], sky_overlay=tod["overlay"], time=tod["id"]))

# Duygular: "Tom is tired at school." / "Tom okulda yorgun."  (yüz parçası + işaret)
EMO_PLACES = [None, "room", "school", "park", "office", "hospital", "street"]
EMO_FORMS = ["prog", "prog_neg", "prog_q", "past", "past_neg", "past_q"]
for cid in HUMANS:
    ch = chars[cid]
    aid = pose_asset.get((cid, "stand"))
    if not aid:
        continue
    pose = poses["stand"]
    for emo in m["emotions"]:
        for place in places_for(pose, ch):
            pid = place["id"] if place else None
            if pid not in EMO_PLACES:
                continue
            pp = place_phrase(place)
            sentences = []
            for s in subjects_for(ch, pid):
                forms = EMO_FORMS if s["en"] == ch["en"] else ["prog"]
                sentences += [sent(f, G.adjective_clause(f, s, emo, pp), s["en"]) for f in forms]
            A2.append(rec("__".join(x for x in [cid, emo["id"], pid] if x), "A2", pid, [actor(aid, expr=emo["id"])], sentences))

# Hava durumu: "It is raining in the park." / "Parkta yağmur yağıyor."  + "Tom is walking in the rain."
# v3: yersiz hava kaydı kaldırıldı — resim yine de park gösteriyordu, "It is raining." ile
# "It is raining in the park." aynı resmin iki farklı "doğru" cümlesi oluyordu (C: 5 çift).
OUTDOOR = [p for p in places if p.get("outdoor")]
for w in m["weather"]:
    for place in OUTDOOR:
        pid = place["id"] if place else None
        pp = place_phrase(place)
        sentences = [sent(f, G.weather_clause(f, w, pp), "It") for f in G.FORMS]
        A2.append(rec("__".join(x for x in ["weather", w["id"], pid] if x), "A2", pid or "park", [], sentences,
                      weather=w["id"], sky=w["sky"]))
    if "in" not in w["en"]:
        continue
    for cid in ("tom", "mary", "man", "woman"):
        aid = pose_asset.get((cid, "walk"))
        if not aid:
            continue
        s = subj_of(chars[cid])
        for bg in ("park", "street"):
            # v3 (AMB-2): yer de cümlede — "Tom is walking in the rain in the park." / "Tom parkta yağmurda yürüyor."
            inn = (f"{w['en']['in']} {PL[bg]['en']}", f"{PL[bg]['tr_loc']} {w['tr']['in']}")
            sentences = [sent(f, G.verb_clause(f, s, verbs["walk"], place=inn), s["en"]) for f in ("prog", "past", "prog_q")]
            A2.append(rec(f"{cid}_walk__{w['id']}__{bg}", "A2", bg, [actor(aid)], sentences, weather=w["id"], sky=w["sky"]))

# ---------------------------------------------------------------- B1
B1 = []
B1_FORMS = ["prog", "past", "fut", "prog_q"]
INTER_PLACES = ["park", "room", "school", "office", "hospital", "street", "station", "restaurant", "shop"]


def common_places(pose_ids, cids, limit=3):
    out = [None]
    for p in places:
        if p["id"] not in INTER_PLACES:
            continue
        if all(p["id"] in poses[pi].get("places", []) for pi in pose_ids) and \
                all(p["id"] not in chars[c].get("place_exclude", []) for c in cids):
            out.append(p)
    return out[: limit + 1]


for it in m["interactions"]:
    v = verbs[it["verb"]]
    for a_id, b_id in permutations(HUMANS, 2):
        a_ch, b_ch = chars[a_id], chars[b_id]
        a_asset = pose_asset.get((a_id, it["subject_pose"]))
        if not a_asset or it.get("bubble_content") == "object":
            continue
        b_asset = pose_asset.get((b_id, it["other_pose"] or "stand"))
        if not b_asset:
            continue
        pose_ids = [it["subject_pose"]] + ([it["other_pose"]] if it["other_pose"] else [])
        for place in common_places(pose_ids, [a_id, b_id]):
            pid = place["id"] if place else None
            pp = place_phrase(place)
            sa, sb = subj_of(a_ch), subj_of(b_ch)
            case = {"ile": b_ch["tr"] + " ile", "acc": b_ch["tr_acc"], "dat": b_ch["tr_dat"]}[it["tr_case"]]
            sentences = [sent(f, G.verb_clause(f, sa, v, obj_en_lower(b_ch), case, pp), sa["en"]) for f in B1_FORMS]
            if it.get("reverse"):
                rv = it["reverse"]
                rcase = {"acc": a_ch["tr_acc"], "dat": a_ch["tr_dat"]}[rv["tr_case"]]
                sentences += [sent(f, G.verb_clause(f, sb, verbs[rv["verb"]], obj_en_lower(a_ch), rcase, pp), sb["en"])
                              for f in B1_FORMS]
            if it["other_pose"]:
                # v3: yerleşimin aktör konum/ölçeği kullanılır (balon başa, el sallayan kol karşıdakine binmez)
                layout = "pair_bubble" if it["bubble"] else ("pair_wave" if it["subject_pose"] == "wave" else "pair")
                la, lb = LAY[layout]["actors"]
                acts = [actor(a_asset, la["x"], la["s"]), actor(b_asset, lb["x"], lb["s"])]
                bubble = {"type": it["bubble"]} if it["bubble"] else None
            else:
                hold = None
                if it["subject_pose"] == "call":
                    hold = {"obj": "phone", "anchor": anchor_of(a_asset, poses["call"]), "s": poses["call"]["scale"]}
                la = LAY["bubble"]["actors"][0]
                acts = [actor(a_asset, la["x"], la["s"], hold=hold)]
                layout = "bubble"
                bubble = {"type": it["bubble"], "content": {"char": b_asset}}
            B1.append(rec(f"{it['id']}__{a_id}__{b_id}" + (f"__{pid}" if pid else ""), "B1", pid, acts, sentences,
                          layout=layout, bubble=bubble))

# İstemek: "Tom wants a car." / "Tom bir araba istiyor."  (düşünce balonunda nesne)
want = next(i for i in m["interactions"] if i["id"] == "want")
for cid in HUMANS:
    aid = pose_asset.get((cid, want["subject_pose"]))
    if not aid:
        continue
    s = subj_of(chars[cid])
    for obj in objects:
        if want["object_class"] not in obj["classes"] and obj["id"] not in ("apple", "cake", "ball", "book"):
            continue
        en_o = f"{obj['article_en']} {obj['en']}" if obj["countable"] else obj["en"]
        tr_o = f"bir {obj['tr']}" if obj["countable"] else obj["tr"]
        sentences = [sent(f, G.verb_clause(f, s, verbs["want"], en_o, tr_o), s["en"])
                     for f in ("prog", "prog_neg", "prog_q", "past", "past_neg", "past_q")]
        for al in chars[cid].get("aliases", [])[:1]:
            sa = subj_of(chars[cid], al)
            sentences.append(sent("prog", G.verb_clause("prog", sa, verbs["want"], en_o, tr_o), sa["en"]))
        la = LAY["bubble"]["actors"][0]
        B1.append(rec(f"want__{cid}__{obj['id']}", "B1", None, [actor(aid, la["x"], la["s"])], sentences, layout="bubble",
                      bubble={"type": "thought", "content": {"obj": obj["id"]}}))

# Nesne durumu / sahne nesnesi: "Tom is turning off the light." / "Tom ışığı kapatıyor."
for pa in m["prop_actions"]:
    v = verbs[pa["verb"]]
    prop = A.get(pa.get("prop") or pa.get("hold"))
    obj_en = pa.get("obj_en") or prop.get("en_def") or f"the {prop['en']}"
    obj_tr = pa.get("obj_tr") or prop.get("tr_acc") or prop["tr"]
    for cid in ("tom", "mary", "man", "woman"):
        aid = pose_asset.get((cid, pa["pose"]))
        if not aid:
            continue
        ch = chars[cid]
        for pid in pa["places"]:
            if pid in ch.get("place_exclude", []):
                continue
            pp = place_phrase(PL[pid])
            sentences = []
            for s in subjects_for(ch, pid):
                forms = G.FORMS if s["en"] == ch["en"] else ["prog"]
                sentences += [sent(f, G.verb_clause(f, s, v, obj_en, obj_tr, pp), s["en"]) for f in forms]
            if pa.get("hold"):
                hold = {"obj": pa["hold"], "anchor": anchor_of(aid, poses[pa["pose"]]), "s": poses[pa["pose"]]["scale"]}
                B1.append(rec(f"{pa['id']}__{cid}__{pid}", "B1", pid, [actor(aid, hold=hold)], sentences))
            else:
                B1.append(rec(f"{pa['id']}__{cid}__{pid}", "B1", pid, [actor(aid, 92, 0.88)], sentences, layout="prop",
                              prop={"obj": pa["prop"], "states": pa.get("states"),
                                    "s": A[pa["prop"]].get("prop_scale", LAY["prop"]["prop"]["s"])}))

# Sıralı iki panel: "First, Tom ate an apple at home, then he read a book." / "Tom evde önce bir elma yedi, sonra bir kitap okudu."
SEQ_POSES = ["eat", "drink", "read", "sit", "walk", "talk", "call", "lie"]  # v3: "think" tek başına doğal değil
SEQ_SAME = [{"talk", "call"}]  # v3: aynı eylemin iki hâli sıralı olay sayılmaz
by_key = {}
for r, ch, pose, verb, en_o, tr_o, place, hold in A1_base:
    if ch["id"] not in ("tom", "mary", "man", "woman") or not place or pose["id"] not in SEQ_POSES:
        continue
    key = (ch["id"], place["id"], pose["id"])
    by_key.setdefault(key, (r, ch, pose, verb, en_o, tr_o, place))  # poz başına ilk nesne
seq_groups = {}
for (cid, pid, _), v in by_key.items():
    seq_groups.setdefault((cid, pid), []).append(v)
for (cid, pid), items in seq_groups.items():
    pairs = [(a, b) for a, b in permutations(items, 2) if {a[2]["id"], b[2]["id"]} not in SEQ_SAME]
    for (r1, ch, p1, v1, e1, t1, place), (r2, _, p2, v2, e2, t2, _) in pairs[:12]:
        pron = ch["pronoun_en"]
        # v3: yer iki panele de ait, cümle başına alınır ("At home, Tom first ate …, then he …")
        en = f"{place['en'][0].upper()}{place['en'][1:]}, {ch['en'] if ch.get('proper') else obj_en_lower(ch)} first " \
             f"{G.join(v1['en']['past'], v1['en'].get('prep', ''), e1)}, " \
             f"then {pron} {G.join(v2['en']['past'], v2['en'].get('prep', ''), e2)}."
        tr = f"{ch['tr']} {place['tr_loc']} önce {G.join(t1, v1['tr']['past'])}, sonra {G.join(t2, v2['tr']['past'])}."
        B1.append(rec(f"seq__{r1['id']}__then__{p2['id']}", "B1", pid, [], [{"form": "past", "subject": ch["en"], "en": en, "tr": tr}],
                      layout="panels", panels=[{"layout": "single", "actors": r1["actors"]},
                                               {"layout": "single", "actors": r2["actors"]}]))

# ---------------------------------------------------------------- yaz
out_dir = ROOT / "scenes"
out_dir.mkdir(exist_ok=True)
# v3: aynı resmi veren kayıtlar tek kayıtta birleşir; cümleleri o resmin kabul kümesidir.
levels, merged_counts = {}, {}
for _name, _rows in (("A1", A1), ("A2", A2), ("B1", B1)):
    levels[_name], merged_counts[_name] = L.merge_identical(_rows)
for name, rows in levels.items():
    (out_dir / f"{name}.jsonl").write_text("\n".join(json.dumps(r, ensure_ascii=False) for r in rows) + "\n", encoding="utf-8")

# Uygulama uyumluluğu: Core/Game/GameLibrary scenes.json'u (v1 biçimi) okur. A1 kayıtları cümle başına
# bir sahneye açılır; kimlikler v1 ile aynı kalır (takma ad/grup öznesi için "__as_<özne>" eki).
def slug(text):
    return text.lower().replace(" ", "_")


compat = []
for r in A1:
    group = r["layout"] == "pair"
    for i, s in enumerate(r["sentences"]):
        canonical = i == 0 and not group
        a0 = r["actors"][0]
        hold = a0.get("hold")
        entry = {
            "id": r["id"] if canonical else f"{r['id']}__as_{slug(s['subject'])}",
            "background": r["bg"],
            "character": a0["char"],
            "object": hold["obj"] if hold else None,
            "transform": {"tint": hold.get("tint") if hold else None,
                          "count": hold.get("n", 1) if hold else 0,
                          **({"anchor": hold["anchor"], "scale": hold["s"]} if hold and hold.get("anchor") else {})},
            "subject": s["subject"],
            "en": s["en"],
            "tr": s["tr"],
        }
        if group:
            entry["group"] = [{"character": a["char"], "x": a["x"], "scale": a["s"]} for a in r["actors"]]
        compat.append(entry)

# v3 (C2): ağırlıklı örnekleme — poz ailesi büyüdükçe sahne başı ağırlık azalır;
# weight = n^e / n  (e=0.62): tutma pozu tekdüze seçimde ~%68, ağırlıklı seçimde ~%39.
pose_of = {a["id"]: a["pose"] for a in char_assets}
fam = Counter(pose_of[e["character"]] for e in compat)
exp_ = m.get("sampling", {}).get("exponent", 1.0)
for e in compat:
    n = fam[pose_of[e["character"]]]
    e["weight"] = round(n ** exp_ / n, 6)
total_w = sum(e["weight"] for e in compat)
hold_share = sum(e["weight"] for e in compat if pose_of[e["character"]] == "stand") / total_w
hold_uniform = fam["stand"] / len(compat)
(ROOT / "scenes.json").write_text(
    '{"version": 1, "count": %d, "scenes": [\n%s\n]}\n' % (len(compat), ",\n".join(json.dumps(s, ensure_ascii=False) for s in compat)),
    encoding="utf-8")

# ---------------------------------------------------------------- coverage.md
by_cat = Counter(a["category"] for a in assets if (ROOT / a["file"]).exists())
missing = [a["file"] for a in assets if not (ROOT / a["file"]).exists()]
lines = ["# Coverage raporu (v2)", "",
         f"SVG: **{sum(by_cat.values())}** — " + ", ".join(f"{k} {v}" for k, v in sorted(by_cat.items())), ""]
if missing:
    lines += [f"Eksik dosya: {len(missing)}", ""]
lines += ["| seviye | sahne (görsel) | cümle | farklı EN kelime | farklı TR kelime | özne |", "|---|---|---|---|---|---|"]
all_en, all_tr, total_s = set(), set(), 0
for name, rows in levels.items():
    en_w, tr_w, subs, n = set(), set(), set(), 0
    for r in rows:
        for s in r["sentences"]:
            n += 1
            subs.add(s["subject"])
            en_w.update(w.strip(".,?").lower() for w in s["en"].split())
            tr_w.update(w.strip(".,?").lower() for w in s["tr"].split())
    all_en |= en_w
    all_tr |= tr_w
    total_s += n
    size = (out_dir / f"{name}.jsonl").stat().st_size / 1024
    lines.append(f"| {name} | {len(rows):,} | {n:,} | {len(en_w)} | {len(tr_w)} | {len(subs)} |  <!-- {size:.0f} KB -->")
lines += [f"| **toplam** | **{sum(len(r) for r in levels.values()):,}** | **{total_s:,}** | {len(all_en)} | {len(all_tr)} | |", ""]
feat = Counter()
for r in A2 + B1:
    rid = r["id"]
    key = ("zaman/olumsuz/soru" if rid.endswith("__tense") else "günün vakti" if r.get("time") else
           "hava" if r.get("weather") else "duygu" if r["actors"] and r["actors"][0].get("expr") else
           "sıralı panel" if r["layout"] == "panels" else "istemek (balon)" if rid.startswith("want__") else
           "nesne durumu / prop" if r["layout"] == "prop" or rid.split("__")[0] in {p["id"] for p in m["prop_actions"]} else
           "etkileşim")
    feat[key] += 1
lines += ["## A2/B1 özellik başına sahne", "", *[f"- {k}: {v:,}" for k, v in feat.most_common()], "",
          "## Örnek cümleler", ""]
for name, rows in levels.items():
    step = max(1, len(rows) // 5)
    for r in rows[::step][:5]:
        s = r["sentences"][min(1, len(r["sentences"]) - 1)]
        lines.append(f"- {name} `{r['id']}` — {s['en']} / {s['tr']}")
issues = L.run(levels, m)
errors = sum(len(v) for (sev, _), v in issues.items() if sev == "ERROR")
warns = sum(len(v) for (sev, _), v in issues.items() if sev == "WARN")
lines += ["", "## Kalite kapısı (tools/lint.py)", "",
          f"Birleştirilen aynı-resim kaydı: " + ", ".join(f"{k} {v}" for k, v in merged_counts.items()) +
          " (cümleleri tek kabul kümesinde).", "",
          f"Oyun örneklemesi (scenes.json `weight`): tutma pozu payı tekdüze %{hold_uniform * 100:.0f} → ağırlıklı %{hold_share * 100:.0f}.",
          "", *L.summary_lines(issues)]
(ROOT / "coverage.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
print(" ".join(f"{k}={len(v)}" for k, v in levels.items()), f"cümle={total_s}", f"eksik={len(missing)}",
      f"lint: {errors} hata, {warns} uyarı", f"tutma payı %{hold_uniform * 100:.0f}→%{hold_share * 100:.0f}")
for (sev, code), ex in sorted(issues.items()):
    print(f"  {sev} {code}: {len(ex)}  örn. {ex[0][:100]}")
if STRICT and errors:
    sys.exit(1)

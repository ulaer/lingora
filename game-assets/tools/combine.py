"""manifest.json + SVG işaretleri -> scenes/A1.jsonl, A2.jsonl, B1.jsonl + coverage.md.

Sıfır model token; tamamen kural tabanlı. Her satır bir görsel sahnedir; aynı görsel birden çok
cümle taşır (takma ad özneler, zaman/olumsuz/soru biçimleri).

  A1  şimdiki zaman: özne × poz(fiil) × nesne[renk|içerik] × adet × yer, gruplar
  A2  dün/yarın, olumsuz, soru (rozetlerle), duygular, günün vakti (gökyüzü rengi), hava durumu
  B1  etkileşimler (konuşmak/dinlemek, el sallamak, aramak), düşünce balonu (istemek, birini düşünmek),
      nesne durumu değişimi (ışığı/TV'yi açıp kapatmak, kapı), sıralı iki panel ("önce …, sonra …")

v4: kişi zamirleri (I/You/We/You + actors[].marker), durum sıfatları, ifade fiilleri, geniş zaman
(cal_everyday), "can" (badge_can), give/write/cook; her kayıtta `facts`, her cümlede `mentions` + `person`
(+ gerektiğinde `subject_ok`) — bkz. docs/v4-contract.md.

Kullanım: python tools/combine.py [--strict]   (game-assets klasöründen)
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
    first = [] if pose.get("no_default_place") else [None]
    return first + [p for p in places if (allowed is None or p["id"] in allowed) and p["id"] not in excluded]


def place_phrase(place, pose=None):
    if not place:
        return None
    o = place.get("pose_overrides", {}).get(pose["id"], {}) if pose else {}
    return o.get("en", place["en"]), o.get("tr_loc", place["tr_loc"])


def subj_of(ch, alias=None):
    s = alias or ch
    out = {"en": s["en"], "tr": s["tr"], "plural": False, "tr_plural": False, "id": ch["id"]}
    if alias and alias["en"] in ("He", "She"):  # zamir: aynı zamirli her karakter için doğru
        pron = ch["pronoun_en"]
        out["ok"] = [c["id"] for c in m["characters"] if c["pronoun_en"] == pron]
    return out


def person_subj(person, ok):
    pr = G.PERSON_PRONOUN[person]
    return {"en": pr[0], "tr": pr[1], "plural": G.PERSON_PLURAL[person], "tr_plural": G.PERSON_PLURAL[person],
            "person": person, "ok": ok}


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


FACT_KEYS = ("subject", "verb", "object", "variant", "count", "place", "time", "weather", "state", "other", "person", "holds",
             "distance", "owner", "receiver_person")
GROUP_SET = {g["id"] for g in m.get("groups", [])}
HOLDS_POSES = set(m.get("holds_poses", []))
PRONOUN_SUBJECTS = {"He", "She", "They", "I", "You", "We"}   # bu özneli cümleler `person` söyler


def F(**kw):
    """Resmin olguları (sözleşme §2). Verilmeyen boyut null (count: 0)."""
    f = {k: None for k in FACT_KEYS}
    f["count"] = 0
    f["holds"] = []
    f.update(kw)
    return f


def M(f, fl=(), place=False, time=False):
    """Cümlenin söylediği olgu boyutları. fl: {"variant","count"}; place/time: cümlede yer/vakit sözü var mı."""
    out = []
    for k in ("subject", "verb", "object"):
        if f[k]:
            out.append(k)
    if "variant" in fl and f["variant"]:
        out.append("variant")
    if "count" in fl and f["count"]:
        out.append("count")
    if place and f["place"]:
        out.append("place")
    if time and f["time"]:
        out.append("time")
    for k in ("weather", "state", "other"):
        if f[k]:
            out.append(k)
    return out


def person_of(s):
    return s.get("person") or ("3p" if s.get("plural") else "3s")


def sent(form, en_tr, subject, badges=True, ments=None, subj=None, **extra):
    en, tr = en_tr
    d = {"form": form, "subject": subject, "en": en, "tr": tr}
    b = G.badges_for(form) if badges else []
    if b:
        d["badges"] = b
    d["mentions"] = list(ments) if ments is not None else []
    d["person"] = person_of(subj) if subj else "3s"
    if subj and subj.get("ok"):
        d["subject_ok"] = list(subj["ok"])
    if subj and subj.get("person") in ("1s", "2s", "1p", "2p") and tr.startswith(subj["tr"] + " "):
        d["tr_alt"] = [G.upper_tr(tr[len(subj["tr"]) + 1:])]      # v6: zamirsiz Türkçe de kabul
    d.update(extra)
    return d


def MX(f, dims):
    """Elle sıralı mentions: yalnızca olgusu dolu boyutlar."""
    return [d for d in dims if f.get(d)]


def S(form, clause, s, f, fl=(), place=False, time=False):
    """Olgu kaydından mentions/person/subject_ok türeten cümle kurucu."""
    ments = M(f, fl, place, time)
    if s["en"] in PRONOUN_SUBJECTS:   # v5: I-resmi ≠ he-resmi
        ments.append("person")
    return sent(form, clause, s["en"], ments=ments, subj=s)


def derive_facts(facts, actors):
    """v5: facts.person (resmin kişisi: marker + grup) ve facts.holds (öznenin elinde görünen nesneler)."""
    f = dict(facts)
    if f.get("subject"):
        mk = actors[0].get("marker") if actors else None   # alıcıdaki marker ("bana") öznenin kişisi değildir
        f["person"] = {"marker_me": "1", "marker_you": "2"}.get(mk, "3") + ("p" if f["subject"] in GROUP_SET else "s")
    a0 = actors[0] if actors else None
    f["holds"] = [a0["hold"]["obj"]] if a0 and a0.get("hold") and a0["char"].split("_", 1)[1] in HOLDS_POSES else []
    return f


def rec(rid, level, bg, actors, sentences, layout="single", facts=None, **extra):
    facts = derive_facts(facts, actors)
    r = {"id": rid, "level": level, "layout": layout, "bg": bg, "actors": actors, "sentences": sentences,
         "facts": facts}
    r.update({k: v for k, v in extra.items() if v is not None})
    return r


def pforms(person, forms, emo=None):
    """v5: 1. kişi soru biçimi üretilmez ("Koşuyor muyum?"); "aç" 1p geçmişi "açtık" (= açmak) ile karışır."""
    out = [f for f in forms if not (person in ("1s", "1p") and f.endswith("_q"))]
    if emo and emo["id"] == "hungry" and person == "1p":
        out = [f for f in out if f != "past"]
    return out


def with_marker(actors, marker, only_first=False):
    out = []
    for i, a in enumerate(actors):
        a = dict(a)
        if not only_first or i == 0:
            a["marker"] = marker
        out.append(a)
    return out


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
PERSON_BASE, GROUP_PERSON_BASE = [], []
HOLD_CORE = {"apple", "book", "ball", "phone", "key", "bag", "umbrella"}
POSS_OBJECTS = set(m.get("poss_objects", []))
A1, A1_base = [], []   # A1_base: varsayılan varyant (renk yok, tek adet) — A2/B1 bunu çoğaltır
FL = {}                # kayıt id -> cümle bayrakları (variant/count) — A2 çoğaltması mentions için kullanır
MAIN4 = [c for c in ("tom", "mary", "man", "woman") if c in chars]
PERSON_PLACES = {None}                        # kişi (ben/sen/biz/siz) sahneleri: boyut sınırı
AOR_PLACES = {None}                           # geniş zaman / yetenek: boyut sınırı
OKS_HUMAN = list(MAIN4)                       # "I"/"You" cümlesi herhangi bir ana insanın resmine uyar
GROUP_IDS = [g["id"] for g in m.get("groups", [])]
HUMAN_GROUPS = [g for g in m.get("groups", []) if all(c in HUMANS for c in g["members"])]
for ca in char_assets:
    ch, pose = chars[ca["character"]], poses[ca["pose"]]
    if pose.get("special") == "give":      # çift sahne: aşağıda
        continue
    verb = verbs[pose["verb"]]
    for place in places_for(pose, ch):
        pid = place["id"] if place else None
        pp = place_phrase(place, pose)
        items = []   # (obj id, variant, n, hold, en_o, tr_o, facts-variant, cümlede renk/içerik var mı, sayılabilir mi)
        if pose["requires"] == "none":
            hold = None
            if pose.get("attach"):
                hold = {"obj": pose["attach"], "anchor": anchor_of(ca["id"], pose), "s": pose["scale"]}
            fo = pose.get("fixed_object")
            if fo:
                items.append((fo["obj"], None, 1, None, fo["en"], fo["tr"], None, False, False))
            else:
                items.append((None, None, 1, hold, "", "", None, False, False))
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
                        fvar = variant or obj.get("default_tint")
                        items.append((obj["id"], variant, n, hold, en_o, tr_o, fvar, variant is not None, bool(obj.get("countable"))))
        for oid, variant, n, hold, en_o, tr_o, fvar, fl_var, countable in items:
            default = (variant is None or (oid and A[oid].get("tint_role") == "contents")) and n == 1
            subs = subjects_for(ch, pid)  # takma adlar tüm varyantlarda (uygulamanın scenes.json'u buna dayanır)
            f = F(subject=ch["id"], verb=pose["verb"], object=oid, variant=fvar, count=n if (hold and oid) else 0, place=pid)
            fl = {"variant"} if fl_var else set()
            if countable and hold:
                fl.add("count")
            sentences = [S("prog", G.verb_clause("prog", s, verb, en_o, tr_o, pp), s, f, fl, place=bool(pp)) for s in subs]
            rid = "__".join(x for x in [ca["id"], oid, variant, f"x{n}" if n > 1 else None, pid] if x)
            r = rec(rid, "A1", pid, [actor(ca["id"], hold=hold)], sentences, facts=f)
            FL[rid] = fl
            A1.append(r)
            if default:
                A1_base.append((r, ch, pose, verb, en_o, tr_o, place, hold))

# Veren pozlu çift sahneler: "Tom is giving Mary an apple." / "Tom Mary'ye bir elma veriyor."
GIVE = poses.get("give")
if GIVE:
    gv = verbs[GIVE["verb"]]
    for a_id, b_id in permutations(MAIN4, 2):
        a_ch, b_ch = chars[a_id], chars[b_id]
        a_asset = pose_asset.get((a_id, "give"))
        b_asset = pose_asset.get((b_id, GIVE["receiver_pose"]))
        if not a_asset or not b_asset:
            continue
        for place in places_for(GIVE, a_ch):
            pid = place["id"] if place else None
            if pid in b_ch.get("place_exclude", []):
                continue
            pp = place_phrase(place, GIVE)
            for oid in GIVE["objects"]:
                obj = A[oid]
                variant, tint, en_np, tr_np = next(noun_variants(obj))
                en_o = f"{obj_en_lower(b_ch)} {en_np(1)}"
                tr_o = f"{b_ch['tr_dat']} {tr_np(1)}"
                hold = {"obj": oid, "tint": tint, "n": 1, "anchor": anchor_of(a_asset, GIVE), "s": GIVE["scale"]}
                la, lb = LAY["pair_give"]["actors"]
                acts = [actor(a_asset, la["x"], la["s"], hold=hold), actor(b_asset, lb["x"], lb["s"])]
                f = F(subject=a_id, verb="give", object=oid, variant=obj.get("default_tint"), count=1, place=pid, other=b_id)
                fl = {"count"}
                sentences = [S("prog", G.verb_clause("prog", s, gv, en_o, tr_o, pp), s, f, fl, place=bool(pp))
                             for s in subjects_for(a_ch, pid)]
                rid = "__".join(x for x in ["give", a_id, b_id, oid, pid] if x)
                r = rec(rid, "A1", pid, acts, sentences, layout="pair_give", facts=f)
                FL[rid] = fl
                A1.append(r)
                A1_base.append((r, a_ch, GIVE, gv, en_o, tr_o, place, hold))

# İfade fiilleri (yeni SVG yok; stand + yüz): "Tom is crying." / "Tom ağlıyor."
EXPR_PLACES = [None, "park", "room", "school"]
for ev in m.get("expression_verbs", []):
    verb = verbs[ev["verb"]]
    fpose = {"id": "expr_" + ev["verb"], "verb": ev["verb"], "requires": "none", "places": EXPR_PLACES}
    for cid in HUMANS:
        aid = pose_asset.get((cid, "stand"))
        if not aid:
            continue
        ch = chars[cid]
        for place in places_for(poses["stand"], ch):
            pid = place["id"] if place else None
            if pid not in EXPR_PLACES:
                continue
            pp = place_phrase(place)
            f = F(subject=cid, verb=ev["verb"], place=pid)
            sentences = [S("prog", G.verb_clause("prog", s, verb, "", "", pp), s, f, place=bool(pp)) for s in subjects_for(ch, pid)]
            rid = "__".join(x for x in [cid, "stand", ev["id"], pid] if x)
            r = rec(rid, "A1", pid, [actor(aid, expr=ev["expr"])], sentences, facts=f)
            FL[rid] = set()
            A1.append(r)
            A1_base.append((r, ch, fpose, verb, "", "", place, None))

# Gruplar (nesnesiz pozlar): "Tom and Mary are walking." / "Tom ve Mary yürüyorlar."
default_layout = m.get("group_layout", {"scale": 0.8, "centers_x": [80, 176]})
for g in m.get("groups", []):
    members = [chars[c] for c in g["members"]]
    merged = {"place_exclude": sorted({p for c in members for p in c.get("place_exclude", [])})}
    for pose in poses.values():
        if pose["requires"] != "none" or pose.get("attach") or pose["id"] in ("lie", "cook") or pose.get("fixed_object"):
            continue
        if not all((c["id"], pose["id"]) in pose_asset for c in members):
            continue
        ids = [pose_asset[(c["id"], pose["id"])] for c in members]
        lay = pose.get("group_layout", default_layout)
        for place in places_for(pose, merged):
            pid = place["id"] if place else None
            pp = place_phrase(place, pose)
            f = F(subject=g["id"], verb=pose["verb"], place=pid)
            sentences = []
            for s in g["subjects"]:
                subj = {"en": s["en"], "tr": s["tr"], "plural": True, "tr_plural": s["tr_plural"]}
                if s["en"] == "They":
                    subj["ok"] = list(GROUP_IDS)
                sentences.append(S("prog", G.verb_clause("prog", subj, verbs[pose["verb"]], place=pp), subj, f, place=bool(pp)))
            A1.append(rec("__".join(x for x in [g["id"], pose["id"], pid] if x), "A1", pid,
                          [actor(cid, x, lay["scale"]) for cid, x in zip(ids, lay["centers_x"])], sentences, layout="pair", facts=f))

# Kişi zamirleri, tek kişi: "I am running." (marker_me) / "You are running." (marker_you)
for r0, ch, pose, verb, en_o, tr_o, place, hold in list(A1_base):
    pid = place["id"] if place else None
    if ch["id"] not in MAIN4 or pid not in PERSON_PLACES:
        continue
    if pose["id"] == "stand" and hold["obj"] not in HOLD_CORE:
        continue
    for person in ("1s", "2s"):
        s = person_subj(person, OKS_HUMAN)
        marker = m["persons"][person]["marker"]
        f = r0["facts"]
        pp = place_phrase(place, pose)
        sentences = [S("prog", G.verb_clause("prog", s, verb, en_o, tr_o, pp), s, f, FL[r0["id"]], place=bool(pp))]
        rid = f"{r0['id']}__p{person}"
        r = rec(rid, "A1", r0["bg"], with_marker(r0["actors"], marker, only_first=True), sentences, layout=r0["layout"], facts=f)
        FL[rid] = FL[r0["id"]]
        A1.append(r)
        PERSON_BASE.append((r, person, ch, pose, verb, en_o, tr_o, place))

# Kişi zamirleri, çift: "We are walking." (iki aktörde marker_me) / "You are walking." (marker_you)
for g in HUMAN_GROUPS:
    members = [chars[c] for c in g["members"]]
    merged = {"place_exclude": sorted({p for c in members for p in c.get("place_exclude", [])})}
    for pose in poses.values():
        if pose["requires"] != "none" or pose.get("attach") or pose["id"] in ("lie", "cook") or pose.get("fixed_object"):
            continue
        if not all((c["id"], pose["id"]) in pose_asset for c in members):
            continue
        ids = [pose_asset[(c["id"], pose["id"])] for c in members]
        lay = pose.get("group_layout", default_layout)
        for place in places_for(pose, merged):
            pid = place["id"] if place else None
            if pid not in PERSON_PLACES:
                continue
            pp = place_phrase(place, pose)
            f = F(subject=g["id"], verb=pose["verb"], place=pid)
            for person in ("1p", "2p"):
                s = person_subj(person, [h["id"] for h in HUMAN_GROUPS])
                acts = with_marker([actor(cid, x, lay["scale"]) for cid, x in zip(ids, lay["centers_x"])], m["persons"][person]["marker"])
                sentences = [S("prog", G.verb_clause("prog", s, verbs[pose["verb"]], place=pp), s, f, place=bool(pp))]
                rid = "__".join(x for x in [g["id"], pose["id"], pid] if x) + f"__p{person}"
                A1.append(rec(rid, "A1", pid, acts, sentences, layout="pair", facts=f))
                GROUP_PERSON_BASE.append((A1[-1], person, g, pose, verbs[pose["verb"]], place))

# v6 this / that / these / those: "This is a ball." / "Bu bir top.", "Those are apples." / "Şunlar elma."
for oid in m.get("show_objects", []):
    obj = A[oid]
    variant, tint, en_np, tr_np = next(noun_variants(obj))
    for kind, layout, bg in (("this", "show_near", None), ("that", "show_far", "park")):
        for n in (1, 2):
            np_en = en_np(1) if n == 1 else obj["en_plural"]
            np_tr = tr_np(1) if n == 1 else obj["tr"]
            f = F(object=oid, variant=obj.get("default_tint"), count=n, place=bg, distance="near" if kind == "this" else "far")
            sentences = [sent(kind, G.demo_clause(kind, n > 1, np_en, np_tr), {"this": "This", "that": "That"}[kind],
                              ments=MX(f, ["object", "count", "distance"]))]
            A1.append(rec(f"show__{kind}__{oid}" + ("__x2" if n > 1 else ""), "A1", bg, [], sentences, layout=layout, facts=f,
                          show={"obj": oid, "tint": tint, "n": n}))

# ---------------------------------------------------------------- A2
A2 = []
TENSE_FORMS = [f for f in G.FORMS if f != "prog"]
AOR_FORMS = ["aor", "aor_neg", "aor_q"]
CAN_FORMS = ["can", "can_neg", "can_q"]
CAN_VERBS = set(m.get("can", {}).get("verbs", []))
AOR_VERBS = set(m.get("aorist", {}).get("verbs", []))     # v6: yalnızca alışkanlık fiilleri
IMP_VERBS = set(m.get("imp_verbs", []))
IMP_FORMS = ["imp", "imp_neg"]
PERSON_A2_PLACES = {None}                          # kişi + tüm zamanlar: boyut sınırı
GIVE_A2_OBJECTS, GIVE_A2_PLACES = ("apple", "book"), {None, "park", "school"}
EMO_PERSON_PLACES = [None]
for r, ch, pose, verb, en_o, tr_o, place, hold in A1_base:
    if ch["id"] not in HUMANS + ["dog", "cat"]:
        continue
    if pose["id"] == "stand" and hold["obj"] not in HOLD_CORE:  # boyut sınırı: tutma pozunda çekirdek nesneler
        continue
    s = subj_of(ch)
    pid = place["id"] if place else None
    if pose["id"] == "give" and (hold["obj"] not in GIVE_A2_OBJECTS or pid not in GIVE_A2_PLACES):
        continue
    pp = place_phrase(place, pose)
    f, fl = r["facts"], FL[r["id"]]
    sentences = [S(fm, G.verb_clause(fm, s, verb, en_o, tr_o, pp), s, f, fl, place=bool(pp)) for fm in TENSE_FORMS]
    if pose["id"] == "stand" and ch["id"] in MAIN4 and pid is None and hold["obj"] in POSS_OBJECTS:
        # v6 iyelik: "This is Tom's ball." / "Bu Tom'un topu." (tutan kişi sahibidir)
        f = dict(f, owner=ch["id"])
        o = A[hold["obj"]]
        sentences.append(sent("poss", G.poss_clause(ch["en_gen"], ch["tr_gen"], o["en"], o["tr_poss"]["3s"]), "This",
                              ments=MX(f, ["object", "owner"])))
    if pose["verb"] in AOR_VERBS and pid in AOR_PLACES:   # geniş zaman: "Tom reads a book every day."
        sentences += [S(fm, G.verb_clause(fm, s, verb, en_o, tr_o, pp), s, f, fl, place=bool(pp)) for fm in AOR_FORMS]
    if pose["verb"] in CAN_VERBS and pose["requires"] == "none" and pid in AOR_PLACES:   # yetenek: "Tom can swim."
        sentences += [S(fm, G.verb_clause(fm, s, verb, en_o, tr_o, pp), s, f, fl, place=bool(pp)) for fm in CAN_FORMS]
    A2.append(rec(r["id"] + "__tense", "A2", r["bg"], r["actors"], sentences, layout=r["layout"], facts=f))

# Kişi + zamanlar: "Am I running?" / "Ben koşuyor muyum?", "You ran yesterday." / "Sen dün koştun."
PERSON_A2_CHARS = ("tom", "woman")                # her poz için iki karakter yeter (boyut sınırı)
for r, person, ch, pose, verb, en_o, tr_o, place in PERSON_BASE:
    pid = place["id"] if place else None
    if pid not in PERSON_A2_PLACES or pose["id"] == "stand" or ch["id"] not in PERSON_A2_CHARS:
        continue
    s = person_subj(person, OKS_HUMAN)
    pp = place_phrase(place, pose)
    f, fl = r["facts"], FL[r["id"]]
    sentences = [S(fm, G.verb_clause(fm, s, verb, en_o, tr_o, pp), s, f, fl, place=bool(pp)) for fm in pforms(person, G.FORMS)]
    if person == "2s" and pose["verb"] in IMP_VERBS:   # v6 emir: "Read a book!" / "Bir kitap oku!" (marker_you + mark_exclaim)
        for fm in IMP_FORMS:
            ments = [d for d in M(f, fl, bool(pp)) if d != "subject"] + ["person"]
            sentences.append(sent(fm, G.imperative_clause(fm, verb, en_o, tr_o, pp), "You", ments=ments,
                                  subj={"person": "2s", "tr": "Sen"}))
    A2.append(rec(r["id"] + "__tense", "A2", r["bg"], r["actors"], sentences, layout=r["layout"], facts=f))
for r, person, g, pose, verb, place in GROUP_PERSON_BASE:
    pid = place["id"] if place else None
    if pid not in PERSON_A2_PLACES:
        continue
    s = person_subj(person, [h["id"] for h in HUMAN_GROUPS])
    pp = place_phrase(place, pose)
    f = r["facts"]
    sentences = [S(fm, G.verb_clause(fm, s, verb, place=pp), s, f, place=bool(pp)) for fm in pforms(person, G.FORMS)]
    A2.append(rec(r["id"] + "__tense", "A2", r["bg"], r["actors"], sentences, layout=r["layout"], facts=f))

# v6 iyelik, ben/sen sahip: "This is my book." / "Bu benim kitabım." (aktör nesneyi tutar, marker_me/marker_you)
for r, ch, pose, verb, en_o, tr_o, place, hold in A1_base:
    if pose["id"] != "stand" or ch["id"] not in MAIN4 or place or hold["obj"] not in POSS_OBJECTS:
        continue
    o = A[hold["obj"]]
    for person in ("1s", "2s"):
        pr = m["persons"][person]
        f = dict(r["facts"], owner=person)
        sentences = [sent("poss", G.poss_clause(pr["en_poss"], pr["tr_gen"], o["en"], o["tr_poss"][person]), "This",
                          ments=MX(f, ["object", "owner"]))]
        A2.append(rec(f"{r['id']}__poss{person}", "A2", None, with_marker(r["actors"], pr["marker"], only_first=True),
                      sentences, facts=f))

# v6 nesne zamirleri: "Tom is giving me an apple." / "Tom bana bir elma veriyor.", "Mary is talking to you." / "Mary seninle konuşuyor."
OBJ_FORMS = ["prog", "past", "fut", "prog_q"]
for a_id, b_id in permutations(MAIN4, 2):
    a_ch, b_ch = chars[a_id], chars[b_id]
    sa = subj_of(a_ch)
    for person in ("1s", "2s"):
        pr = m["persons"][person]
        ga, gb = pose_asset.get((a_id, "give")), pose_asset.get((b_id, "stand"))
        if GIVE and ga and gb:
            for oid in GIVE_A2_OBJECTS:
                obj = A[oid]
                variant, tint, en_np, tr_np = next(noun_variants(obj))
                hold = {"obj": oid, "tint": tint, "n": 1, "anchor": anchor_of(ga, GIVE), "s": GIVE["scale"]}
                la, lb = LAY["pair_give"]["actors"]
                acts = [actor(ga, la["x"], la["s"], hold=hold), actor(gb, lb["x"], lb["s"])]
                acts[1]["marker"] = pr["marker"]
                f = F(subject=a_id, verb="give", object=oid, variant=obj.get("default_tint"), count=1, other=b_id,
                      receiver_person=person)
                ments = MX(f, ["subject", "verb", "object", "count", "receiver_person"])
                sentences = [sent(fm, G.verb_clause(fm, sa, verbs["give"], f"{pr['en_obj']} {en_np(1)}", f"{pr['tr_dat']} {tr_np(1)}"),
                                  sa["en"], ments=ments, subj=sa) for fm in OBJ_FORMS]
                A2.append(rec(f"give__{a_id}__{b_id}__{oid}__to{person}", "A2", None, acts, sentences, layout="pair_give", facts=f))
        ta, tb = pose_asset.get((a_id, "talk")), pose_asset.get((b_id, "stand"))
        if ta and tb:
            la, lb = LAY["pair_bubble"]["actors"]
            for place in (None, PL["park"]):
                pid = place["id"] if place else None
                pp = place_phrase(place)
                acts = [actor(ta, la["x"], la["s"]), actor(tb, lb["x"], lb["s"])]
                acts[1]["marker"] = pr["marker"]
                f = F(subject=a_id, verb="talk_to", place=pid, other=b_id, receiver_person=person)
                ments = MX(f, ["subject", "verb", "receiver_person"]) + (["place"] if pp else [])
                sentences = [sent(fm, G.verb_clause(fm, sa, verbs["talk_to"], pr["en_obj"], pr["tr_ins"], pp), sa["en"],
                                  ments=ments, subj=sa) for fm in OBJ_FORMS]
                A2.append(rec(f"talk_to__{a_id}__{b_id}" + (f"__{pid}" if pid else "") + f"__to{person}", "A2", pid, acts,
                              sentences, layout="pair_bubble", bubble={"type": "speech"}, facts=f))

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
        f = dict(r["facts"], time=tod["id"])
        sentences = [S("prog", G.verb_clause("prog", s, verb, en_o, tr_o, place_phrase(place, pose), (tod["en"], tod["tr"])), s, f,
                       FL[r["id"]], place=True, time=True)]
        A2.append(rec(f"{r['id']}__{tod['id']}", "A2", r["bg"], r["actors"], sentences,
                      sky=tod["sky"], sky_overlay=tod["overlay"], time=tod["id"], facts=f))

# Duygular ve durumlar: "Tom is tired at school." / "Tom okulda yorgun."  (yüz parçası + işaret)
EMO_PLACES = [None, "room", "school", "park", "office", "hospital", "street"]
EMO_FORMS = ["prog", "prog_neg", "prog_q", "past", "past_neg", "past_q"]
EMOTIONS = [e for e in m["emotions"] if e.get("kind") != "expression"]
for cid in HUMANS:
    ch = chars[cid]
    aid = pose_asset.get((cid, "stand"))
    if not aid:
        continue
    pose = poses["stand"]
    for emo in EMOTIONS:
        for place in places_for(pose, ch):
            pid = place["id"] if place else None
            if pid not in EMO_PLACES:
                continue
            pp = place_phrase(place)
            f = F(subject=cid, state=emo["id"], place=pid)
            sentences = []
            for s in subjects_for(ch, pid):
                forms = EMO_FORMS if s["en"] == ch["en"] else ["prog"]
                sentences += [S(fm, G.adjective_clause(fm, s, emo, pp), s, f, place=bool(pp)) for fm in forms]
            A2.append(rec("__".join(x for x in [cid, emo["id"], pid] if x), "A2", pid, [actor(aid, expr=emo["id"])], sentences, facts=f))
            # kişi: "I am tired at school." / "Ben okulda yorgunum." (marker_me), "Are you tired?" (marker_you)
            if cid in MAIN4 and pid in EMO_PERSON_PLACES and not emo.get("no_person"):
                for person in ("1s", "2s"):
                    s = person_subj(person, OKS_HUMAN)
                    sentences = [S(fm, G.adjective_clause(fm, s, emo, pp), s, f, place=bool(pp)) for fm in pforms(person, EMO_FORMS, emo)]
                    A2.append(rec("__".join(x for x in [cid, emo["id"], pid] if x) + f"__p{person}", "A2", pid,
                                  with_marker([actor(aid, expr=emo["id"])], m["persons"][person]["marker"]), sentences, facts=f))
for g in HUMAN_GROUPS:
    ids = [pose_asset.get((c, "stand")) for c in g["members"]]
    if not all(ids):
        continue
    merged = {"place_exclude": sorted({p for c in g["members"] for p in chars[c].get("place_exclude", [])})}
    for emo in EMOTIONS:
        for place in places_for(poses["stand"], merged):
            pid = place["id"] if place else None
            if pid not in EMO_PERSON_PLACES or emo.get("no_person"):
                continue
            pp = place_phrase(place)
            f = F(subject=g["id"], state=emo["id"], place=pid)
            for person in ("1p", "2p"):
                s = person_subj(person, [h["id"] for h in HUMAN_GROUPS])
                acts = with_marker([actor(cid, x, default_layout["scale"], expr=emo["id"])
                                    for cid, x in zip(ids, default_layout["centers_x"])], m["persons"][person]["marker"])
                sentences = [S(fm, G.adjective_clause(fm, s, emo, pp), s, f, place=bool(pp)) for fm in pforms(person, EMO_FORMS, emo)]
                A2.append(rec("__".join(x for x in [g["id"], "stand", emo["id"], pid] if x) + f"__p{person}", "A2", pid, acts,
                              sentences, layout="pair", facts=f))

# Hava durumu: "It is raining in the park." / "Parkta yağmur yağıyor."  + "Tom is walking in the rain."
# v3: yersiz hava kaydı kaldırıldı — resim yine de park gösteriyordu, "It is raining." ile
# "It is raining in the park." aynı resmin iki farklı "doğru" cümlesi oluyordu (C: 5 çift).
OUTDOOR = [p for p in places if p.get("outdoor")]
for w in m["weather"]:
    for place in OUTDOOR:
        pid = place["id"] if place else None
        pp = place_phrase(place)
        f = F(weather=w["id"], place=pid)
        sentences = [sent(fm, G.weather_clause(fm, w, pp), "It", ments=M(f, place=True)) for fm in G.FORMS]
        A2.append(rec("__".join(x for x in ["weather", w["id"], pid] if x), "A2", pid or "park", [], sentences,
                      weather=w["id"], sky=w["sky"], facts=f))
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
            f = F(subject=cid, verb="walk", weather=w["id"], place=bg)
            sentences = [S(fm, G.verb_clause(fm, s, verbs["walk"], place=inn), s, f, place=True) for fm in ("prog", "past", "prog_q")]
            A2.append(rec(f"{cid}_walk__{w['id']}__{bg}", "A2", bg, [actor(aid)], sentences, weather=w["id"], sky=w["sky"], facts=f))

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
            case = {"ile": b_ch["tr_ins"], "acc": b_ch["tr_acc"], "dat": b_ch["tr_dat"]}[it["tr_case"]]
            fct = F(subject=a_id, verb=it["verb"], other=b_id, place=pid)
            sentences = [S(f, G.verb_clause(f, sa, v, obj_en_lower(b_ch), case, pp), sa, fct, place=bool(pp)) for f in B1_FORMS]
            if it.get("reverse"):
                # v5 rol kuralı: "Mary listens to Tom" -> role "other" (özne = resmin `other`'ı, nesne = resmin öznesi).
                rv = it["reverse"]
                rcase = {"acc": a_ch["tr_acc"], "dat": a_ch["tr_dat"]}[rv["tr_case"]]
                sbo = dict(sb, ok=[b_id])
                for f in B1_FORMS:
                    rs = sent(f, G.verb_clause(f, sb, verbs[rv["verb"]], obj_en_lower(a_ch), rcase, pp), sb["en"],
                              ments=["subject", "verb", "other"] + (["place"] if pp else []), subj=sbo)
                    rs["role"] = "other"
                    sentences.append(rs)
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
                          layout=layout, bubble=bubble, facts=fct))

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
        fct = F(subject=cid, verb="want", object=obj["id"])
        sentences = [S(f, G.verb_clause(f, s, verbs["want"], en_o, tr_o), s, fct)
                     for f in ("prog", "prog_neg", "prog_q", "past", "past_neg", "past_q")]
        for al in chars[cid].get("aliases", [])[:1]:
            sa = subj_of(chars[cid], al)
            sentences.append(S("prog", G.verb_clause("prog", sa, verbs["want"], en_o, tr_o), sa, fct))
        la = LAY["bubble"]["actors"][0]
        B1.append(rec(f"want__{cid}__{obj['id']}", "B1", None, [actor(aid, la["x"], la["s"])], sentences, layout="bubble",
                      bubble={"type": "thought", "content": {"obj": obj["id"]}}, facts=fct))

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
            fct = F(subject=cid, verb=pa["verb"], object=pa.get("prop") or pa.get("hold"), place=pid)
            for s in subjects_for(ch, pid):
                forms = G.FORMS if s["en"] == ch["en"] else ["prog"]
                sentences += [S(f, G.verb_clause(f, s, v, obj_en, obj_tr, pp), s, fct, place=bool(pp)) for f in forms]
            if pa.get("hold"):
                hold = {"obj": pa["hold"], "anchor": anchor_of(aid, poses[pa["pose"]]), "s": poses[pa["pose"]]["scale"]}
                B1.append(rec(f"{pa['id']}__{cid}__{pid}", "B1", pid, [actor(aid, hold=hold)], sentences, facts=fct))
            else:
                B1.append(rec(f"{pa['id']}__{cid}__{pid}", "B1", pid, [actor(aid, 92, 0.88)], sentences, layout="prop",
                              prop={"obj": pa["prop"], "states": pa.get("states"),
                                    "s": A[pa["prop"]].get("prop_scale", LAY["prop"]["prop"]["s"])}, facts=fct))

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
        fct = F(subject=cid, verb=f"{p1['verb']}>{p2['verb']}", place=pid)
        B1.append(rec(f"seq__{r1['id']}__then__{p2['id']}", "B1", pid, [],
                      [sent("past", (en, tr), ch["en"], ments=M(fct, place=True), subj=subj_of(ch))],
                      layout="panels", panels=[{"layout": "single", "actors": r1["actors"]},
                                               {"layout": "single", "actors": r2["actors"]}], facts=fct))

# v6 wh-soruları: "What is Tom eating?" / "Tom ne yiyor?" -> cevap "An apple." / "Bir elma."  (resim + badge_question)
WHCFG = m.get("wh", {})
wh_seen = Counter()
for r, ch, pose, verb, en_o, tr_o, place, hold in A1_base:
    if ch["id"] not in MAIN4 or pose.get("special") or pose["id"].startswith("expr_"):
        continue
    pid = place["id"] if place else None
    # "Where …?" sorusunun 4 farklı cevabı olabilmesi için yeterince yer (yalnızca park/okul → 2 cevap kalıyordu)
    if pid not in (None, "park", "school", "room", "kitchen", "street", "beach", "office"):
        continue
    kinds = []
    if pose["id"] in WHCFG.get("what_poses", []) and hold and (pose["id"] != "stand" or hold["obj"] in HOLD_CORE):
        kinds.append("what")
    if pose["verb"] in WHCFG.get("where_verbs", []) and pid:
        kinds.append("where")
    if pose["verb"] in WHCFG.get("who_verbs", []) and pid is None:
        kinds.append("who")
    if not kinds:
        continue
    key = (ch["id"], pose["id"], pid)
    wh_seen[key] += 1
    if wh_seen[key] > 2:          # boyut sınırı: poz başına en çok iki nesne
        continue
    pp = place_phrase(place, pose)
    f, fl = r["facts"], FL[r["id"]]
    s = subj_of(ch)
    sentences = []
    for kind in kinds:
        clause = G.wh_clause(kind, s, verb, en_o, tr_o, pp)
        if kind == "what":
            ments = ["subject", "verb"] + (["place"] if pp and pid else [])
            ans = G.answer(en_o, tr_o)
            subj_label = ch["en"]
        elif kind == "where":
            ments = M(f, fl, False)
            ans = G.answer(pp[0], pp[1])
            subj_label = ch["en"]
        else:
            ments = [d for d in M(f, fl, bool(pp)) if d != "subject"]
            ans = G.answer(ch["en"], ch["tr"])
            subj_label = "Who"
        sentences.append(sent("wh_" + kind, clause, subj_label, ments=ments, subj=s if kind != "who" else None, answer=ans))
    B1.append(rec(r["id"] + "__wh", "B1", r["bg"], r["actors"], sentences, layout=r["layout"], facts=f))

# ---------------------------------------------------------------- yaz
out_dir = ROOT / "scenes"
out_dir.mkdir(exist_ok=True)


def compact(r):
    """jsonl boyutu için: facts içinde null/0 boyutlar yazılmaz (okuyan eksik anahtarı null/0 sayar)."""
    r = dict(r)
    r["facts"] = {k: v for k, v in r["facts"].items() if v not in (None, 0, [])}
    return json.dumps(r, ensure_ascii=False, separators=(",", ":"))


# v3: aynı resmi veren kayıtlar tek kayıtta birleşir; cümleleri o resmin kabul kümesidir.
levels, merged_counts = {}, {}
for _name, _rows in (("A1", A1), ("A2", A2), ("B1", B1)):
    levels[_name], merged_counts[_name] = L.merge_identical(_rows)
for name, rows in levels.items():
    (out_dir / f"{name}.jsonl").write_text("\n".join(compact(r) for r in rows) + "\n", encoding="utf-8")

# Uygulama uyumluluğu: Core/Game/GameLibrary scenes.json'u (v1 biçimi) okur. A1 kayıtları cümle başına
# bir sahneye açılır; kimlikler v1 ile aynı kalır (takma ad/grup öznesi için "__as_<özne>" eki).
def slug(text):
    return text.lower().replace(" ", "_")


compat = []
for r in A1:
    if not r["actors"]:     # v6: this/that kayıtları (resim `show` ile) yalnızca kayıt yolundadır
        continue
    group = r["layout"] in ("pair", "pair_give")
    for i, s in enumerate(r["sentences"]):
        canonical = i == 0 and not group
        a0 = r["actors"][0]
        hold = a0.get("hold")
        if hold and hold.get("anchor") and group:
            # v4 düzeltmesi: eski biçimde tutma noktası TUVAL koordinatıdır; çift yerleşimde (vermek) aktörün
            # ölçeği/konumu uygulanmadan yazılırsa nesne alıcının yüzüne biner.
            ax, ay = hold["anchor"]
            x, sc = a0["x"], a0["s"]
            hold = {**hold, "anchor": [round(x - 128 * sc + ax * sc, 1), round(230 - 230 * sc + ay * sc, 1)],
                    "s": round(hold["s"] * sc, 4)}
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
            entry["group"] = [{"character": a["char"], "x": a["x"], "scale": a["s"],
                               **{k: a[k] for k in ("hold", "marker", "expr") if a.get(k)}} for a in r["actors"]]
        # v4: sözleşme alanları (eski alanlar aynen korunur)
        if a0.get("marker"):
            entry["actors"] = r["actors"]
        entry["facts"] = r["facts"]
        entry["mentions"] = s["mentions"]
        entry["person"] = s["person"]
        if s.get("subject_ok"):
            entry["subject_ok"] = s["subject_ok"]
        if s.get("badges"):
            entry["badges"] = s["badges"]
        if a0.get("expr"):
            entry["expr"] = a0["expr"]
        if a0.get("marker"):
            entry["marker"] = a0["marker"]
        compat.append(entry)
ids_seen = [e["id"] for e in compat]
assert len(ids_seen) == len(set(ids_seen)), "scenes.json kimlik çakışması"

# v3 (C2): ağırlıklı örnekleme — poz ailesi büyüdükçe sahne başı ağırlık azalır;
# weight = n^e / n  (e=0.62): tutma pozu tekdüze seçimde ~%68, ağırlıklı seçimde ~%39.
pose_of = {a["id"]: a["pose"] for a in char_assets}
def fam_key(e):  # ifade fiilleri stand pozunu paylaşır; tutma payını bozmasın diye ayrı aile
    if e.get("expr"):
        return "expr:" + e["expr"]
    if e.get("marker") or e.get("group") and any(g.get("marker") for g in e["group"]):
        return "marker"
    return pose_of[e["character"]]


fam = Counter(fam_key(e) for e in compat)
exp_ = m.get("sampling", {}).get("exponent", 1.0)
for e in compat:
    n = fam[fam_key(e)]
    e["weight"] = round(n ** exp_ / n, 6)
total_w = sum(e["weight"] for e in compat)
hold_share = sum(e["weight"] for e in compat if pose_of[e["character"]] == "stand") / total_w
hold_uniform = fam["stand"] / len(compat)
(ROOT / "scenes.json").write_text(
    '{"version": 1, "count": %d, "scenes": [\n%s\n]}\n' % (len(compat), ",\n".join(json.dumps(s, ensure_ascii=False, separators=(",", ":")) for s in compat)),
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

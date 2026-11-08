"""manifest.json -> scenes.json + coverage.md. Sıfır model token; tamamen kural tabanlı.

Sahne = karakter × poz(fiil) × nesne[+içerik | +renk] × adet × yer(opsiyonel).
Kullanım: python tools/combine.py   (game-assets klasöründen)
"""
import json
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
m = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))

colors, numbers = m["colors"], m["numbers"]
chars = {c["id"]: c for c in m["characters"]}
poses = {p["id"]: p for p in m["poses"]}
assets = m["assets"]
char_assets = [a for a in assets if a["category"] == "character"]
objects = [a for a in assets if a["category"] == "object"]
places = [None] + [a for a in assets if a["category"] == "background"]


def places_for(pose, ch):
    """Yersiz (None) + pozun izin verdiği yerler, öznenin dışladıkları hariç
    (ör. yüzmek yalnızca havuz/sahil; hayvanlar okula/havuza girmez)."""
    allowed, excluded = pose.get("places"), set(ch.get("place_exclude", []))
    return [None] + [p for p in places[1:]
                     if (allowed is None or p["id"] in allowed) and p["id"] not in excluded]


def place_phrase(place, pose):
    """Yer ifadesi; poza özel istisna olabilir (sahilde yüzmek -> 'in the sea' / 'denizde')."""
    o = place.get("pose_overrides", {}).get(pose["id"], {})
    return o.get("en", place["en"]), o.get("tr_loc", place["tr_loc"])


def fits(pose, obj):
    if pose["requires"] == "any":
        return obj.get("holdable", True)
    return pose["requires"] in obj["classes"]


def noun_variants(obj):
    """(variant_id, tint_hex, en_np_builder, tr_np_builder) — renk/içerik dönüşümleri."""
    if obj.get("tint_role") == "contents":
        for c in obj["contents"]:
            # "a glass of milk" yerine sade kütle adı: "drinking milk" / "süt içiyor"
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
                art = ("a" if adj and adj[0] not in "aeiou" else "an") if adj else obj["article_en"]
                return f"{art} {adj}{obj['en']}"
            return f"{numbers[str(n)]['en']} {adj}{obj['en_plural']}"

        def tr(n, color=color):
            # Türkçe: "kırmızı bir elma", "iki kırmızı elma" (sayıdan sonra çoğul eki yok)
            if n == 1:
                return f"{colors[color]['tr']} bir {obj['tr']}" if color else f"bir {obj['tr']}"
            return f"{numbers[str(n)]['tr']} {colors[color]['tr'] + ' ' if color else ''}{obj['tr']}"

        yield color, hexv, en, tr


scenes = []
for ca in char_assets:
    ch, pose = chars[ca["character"]], poses[ca["pose"]]
    for obj in objects:
        if not fits(pose, obj):
            continue
        counts = [1, 2, 3] if (pose["allow_count"] and obj.get("countable")) else [1]
        for variant, tint, en_np, tr_np in noun_variants(obj):
            for n in counts:
                for place in places_for(pose, ch):
                    en_obj, tr_obj = en_np(n), tr_np(n)
                    if pose["id"] == "stand" and obj.get("measure"):
                        # kütle adı elde tutulunca ölçü sözcüğü: "a glass of milk" / "bir bardak süt"
                        en_obj = f"{obj['measure']['en']} {en_obj}"
                        tr_obj = f"{obj['measure']['tr']} {tr_obj}"
                    if pose.get("tr_case") == "with" and tr_obj.endswith(obj["tr"]):
                        # Türkçe vasıta hâli: "bir top" -> "bir topla" ("playing with a ball")
                        tr_obj = tr_obj[: -len(obj["tr"])] + obj["tr_with"]
                    en = f"{ch['en']} is {pose['verb_en']} {en_obj}"
                    tr_parts = [ch["tr"]]
                    if place:
                        p_en, p_tr = place_phrase(place, pose)
                        en += f" {p_en}"
                        tr_parts.append(p_tr)
                    tr_parts += [tr_obj, pose["verb_tr"]]
                    sid = "__".join(x for x in [ca["id"], obj["id"], variant, f"x{n}" if n > 1 else None,
                                                place["id"] if place else None] if x)
                    scenes.append({
                        "id": sid,
                        "background": place["id"] if place else None,
                        "character": ca["id"],
                        "object": obj["id"],
                        "transform": {
                            "tint": tint,
                            "count": n,
                            "anchor": pose["anchor"],
                            "scale": pose["scale"],
                        },
                        "en": en + ".",
                        "tr": " ".join(tr_parts) + ".",
                    })

# Nesnesiz (geçişsiz) pozlar: koşmak, yüzmek, oturmak — "Tom parkta koşuyor."
for ca in char_assets:
    ch, pose = chars[ca["character"]], poses[ca["pose"]]
    if pose["requires"] != "none":
        continue
    for place in places_for(pose, ch):
        en, tr = f"{ch['en']} is {pose['verb_en']}", ch["tr"]
        if place:
            p_en, p_tr = place_phrase(place, pose)
            en, tr = f"{en} {p_en}", f"{tr} {p_tr}"
        scenes.append({
            "id": "__".join(x for x in [ca["id"], place["id"] if place else None] if x),
            "background": place["id"] if place else None,
            "character": ca["id"],
            "object": None,
            "transform": {"tint": None, "count": 0},
            "en": en + ".",
            "tr": f"{tr} {pose['verb_tr']}.",
        })

# Özne takma adları: aynı görsel, farklı özne ("Tom" -> "He" / "The boy"; "Kadın" -> "Annem").
asset_char = {a["id"]: chars[a["character"]] for a in char_assets}
for s in scenes:
    s["subject"] = asset_char[s["character"]]["en"]
alias_scenes = []
for s in scenes:
    ch = asset_char[s["character"]]
    for al in ch.get("aliases", []):
        if "places_only" in al and s["background"] not in al["places_only"]:
            continue
        assert s["en"].startswith(ch["en"]) and s["tr"].startswith(ch["tr"])
        slug = al["en"].lower().replace(" ", "_")
        alias_scenes.append({**s, "id": f"{s['id']}__as_{slug}", "subject": al["en"],
                             "en": al["en"] + s["en"][len(ch["en"]):],
                             "tr": al["tr"] + s["tr"][len(ch["tr"]):]})
scenes += alias_scenes

# Grup özneleri: iki karakter yan yana, nesnesiz pozlarda ("Tom and Mary are running." / "Tom ve Mary koşuyorlar.")
pose_asset = {(a["character"], a["pose"]): a["id"] for a in char_assets}
layout = m.get("group_layout", {"scale": 0.8, "centers_x": [80, 176]})
for g in m.get("groups", []):
    members = [chars[c] for c in g["members"]]
    merged = {"place_exclude": sorted({p for c in members for p in c.get("place_exclude", [])})}
    for pose in poses.values():
        if pose["requires"] != "none" or not all((c["id"], pose["id"]) in pose_asset for c in members):
            continue
        ids = [pose_asset[(c["id"], pose["id"])] for c in members]
        lay = pose.get("group_layout", layout)  # geniş pozlar (uyuma, koşma) daha küçük ve aralıklı
        for place in places_for(pose, merged):
            p_en, p_tr = place_phrase(place, pose) if place else ("", "")
            for subj in g["subjects"]:
                verb_tr = pose["verb_tr"] + ("lar" if subj["tr_plural"] else "")  # -yor + lar (ünlü uyumu sabit)
                slug = subj["en"].lower().replace(" ", "_")
                scenes.append({
                    "id": "__".join(x for x in [g["id"], pose["id"], place["id"] if place else None, f"as_{slug}"] if x),
                    "background": place["id"] if place else None,
                    "character": ids[0],
                    "group": [{"character": cid, "x": x, "scale": lay["scale"]}
                              for cid, x in zip(ids, lay["centers_x"])],
                    "object": None,
                    "transform": {"tint": None, "count": 0},
                    "subject": subj["en"],
                    "en": " ".join(x for x in [subj["en"], "are", pose["verb_en"], p_en] if x) + ".",
                    "tr": " ".join(x for x in [subj["tr"], p_tr, verb_tr] if x) + ".",
                })

(ROOT / "scenes.json").write_text(
    '{"version": 1, "count": %d, "scenes": [\n%s\n]}\n' % (
        len(scenes), ",\n".join(json.dumps(s, ensure_ascii=False) for s in scenes)),  # sahne başına bir satır
    encoding="utf-8")

# --- coverage.md ---
by_cat = Counter(a["category"] for a in assets)
by_pose = Counter(s["character"].split("_")[1] for s in scenes)
lemmas = {"en": set(), "tr": set()}
for s in scenes:
    for lang in lemmas:
        lemmas[lang].update(w.strip(".").lower() for w in s[lang].split())
with_place = sum(1 for s in scenes if s["background"])
tinted = sum(1 for s in scenes if s["transform"]["tint"])
multi = sum(1 for s in scenes if s["transform"]["count"] > 1)
aliased = sum(1 for s in scenes if "__as_" in s["id"] and "group" not in s)
grouped = sum(1 for s in scenes if "group" in s)
subjects = Counter(s["subject"] for s in scenes)
# Uygulama içi ek dönüşümler (sahne listesine yazılmaz, çalışma anında üretilir):
# olumsuz ×2, soru ×2 -> ×4 cümle kalıbı (zamirler artık scenes.json içinde)
runtime_mult = 2 * 2

lines = [
    "# Coverage raporu (v1)",
    "",
    "| ölçü | değer |",
    "|---|---|",
    f"| SVG asset | {len(assets)} (karakter-poz {by_cat['character']}, nesne {by_cat['object']}, arka plan {by_cat['background']}) |",
    f"| kanonik sahne (`scenes.json`) | **{len(scenes)}** |",
    f"| yerli sahne / yersiz | {with_place} / {len(scenes) - with_place} |",
    f"| renk/içerik dönüşümlü sahne | {tinted} |",
    f"| çoğaltmalı sahne (iki/üç) | {multi} |",
    f"| takma adlı özne sahnesi (He, The boy, My mother…) | {aliased} |",
    f"| grup sahnesi (Tom and Mary, They, Two dogs…) | {grouped} |",
    f"| farklı özne | {len(subjects)} |",
    f"| farklı İngilizce kelime | {len(lemmas['en'])} |",
    f"| farklı Türkçe kelime | {len(lemmas['tr'])} |",
    f"| sahne / asset oranı | {len(scenes) / len(assets):.1f} |",
    "",
    "## Poz başına sahne",
    "",
    *[f"- {p}: {n}" for p, n in sorted(by_pose.items())],
    "",
    "## Çalışma anı dönüşümleriyle tahmini kapsam",
    "",
    "## Özne başına sahne",
    "",
    *[f"- {k}: {n}" for k, n in subjects.most_common()],
    "",
    f"Aynı görselle üretilebilecek kalıplar (olumsuz, soru): ×{runtime_mult} → "
    f"**≈{len(scenes) * runtime_mult:,} cümle**, yeni asset çizmeden.",
    "",
    "## Örnek sahneler",
    "",
    *[f"- `{s['id']}` — {s['en']} / {s['tr']}" for s in scenes[:: max(1, len(scenes) // 8)][:8]],
    "",
]
(ROOT / "coverage.md").write_text("\n".join(lines), encoding="utf-8")
print(f"{len(scenes)} sahne yazıldı; coverage.md güncellendi")

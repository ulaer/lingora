"""SVG stil kilidi doğrulayıcı. Dosya içeriğini yazdırmaz; yalnızca özet satırlar basar.

Kullanım: python tools/validate.py   (game-assets klasöründen)
"""
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
manifest = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))
palette = {v.upper() for v in manifest["palette"].values()}
FORBIDDEN = ["gradient", "<filter", "opacity", "<text", "<style", "<image", "class=", "href"]
NS = "{http://www.w3.org/2000/svg}"
HUMAN_IDS = {"tom", "mary", "man", "woman", "doctor", "police"}

failures = 0
for asset in manifest["assets"]:
    path = ROOT / asset["file"]
    problems = []
    if not path.exists():
        print(f"MISSING  {asset['file']}")
        failures += 1
        continue
    text = path.read_text(encoding="utf-8")
    size = len(text.encode("utf-8"))
    if size > 6144:
        problems.append(f"size {size}B > 6KB")
    for bad in FORBIDDEN:
        if bad in text.lower():
            problems.append(f"forbidden '{bad}'")
    colors = {c.upper() for c in re.findall(r"#[0-9a-fA-F]{6}\b", text)}
    off = colors - palette
    if off:
        problems.append(f"off-palette {sorted(off)}")
    if re.search(r"(fill|stroke)=\"(?!none|#)[a-z]", text):
        problems.append("named color")
    try:
        root = ET.fromstring(text)
        if root.get("viewBox") != "0 0 256 256":
            problems.append(f"viewBox {root.get('viewBox')}")
        groups = [c for c in root if c.tag == NS + "g"]
        others = [c for c in root if c.tag not in (NS + "g", NS + "title", NS + "desc")]
        if len(groups) != 1 or others:
            problems.append("root must hold exactly one <g>")
        elif groups[0].get("id") != asset["id"]:
            problems.append(f"g id '{groups[0].get('id')}' != '{asset['id']}'")
        if asset.get("tints") and "data-tint" not in text:
            problems.append("missing data-tint")
        if asset.get("tint_role") == "contents" and "data-tint" not in text:
            problems.append("missing data-tint (contents)")
        # v2: animasyon kareleri 1..n (n <= 3), ardışık
        frames = sorted({int(f) for f in re.findall(r'data-frame="(\d+)"', text)})
        if frames and (frames != list(range(1, len(frames) + 1)) or len(frames) > 3):
            problems.append(f"frames {frames}")
        # v2: durumlu nesnelerde her durum grubu var
        for st in asset.get("states", []):
            if f'data-state="{st}"' not in text:
                problems.append(f"missing state '{st}'")
        # v2: insan pozlarında yüz parçaları (konuşan/arayan pozda ağız karelerde)
        if asset["category"] == "character" and asset.get("character") in HUMAN_IDS:
            if 'data-part="eyes"' not in text:
                problems.append("missing eyes part")
            if asset["pose"] not in ("talk", "call") and 'data-part="mouth"' not in text:
                problems.append("missing mouth part")
            if "data-head=" not in text:
                problems.append("missing data-head")
    except ET.ParseError as exc:
        problems.append(f"XML error: {exc}")
    status = "OK      " if not problems else "FAIL    "
    failures += bool(problems)
    print(f"{status}{asset['file']} ({size}B){'  ' + '; '.join(problems) if problems else ''}")

print(f"\n{len(manifest['assets']) - failures}/{len(manifest['assets'])} geçti")
sys.exit(1 if failures else 0)

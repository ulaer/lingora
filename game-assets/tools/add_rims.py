"""v6: rozet/işaret overlay'lerine (badge_*, cal_*, marker_*) arka disk ekler; şekiller değişmez. Tekrar çalıştırılabilir.

Kullanım: python tools/add_rims.py   (game-assets klasöründen)
"""
import re
from pathlib import Path

DISK = '<circle cx="128" cy="128" r="74" fill="#F1FAEE" stroke="#2B2D42" stroke-width="6"/>'
root = Path(__file__).resolve().parent.parent / "assets" / "overlays"
done = 0
for p in sorted(root.glob("*.svg")):
    if not re.match(r"(badge_|cal_|marker_)", p.name):
        continue
    t = p.read_text(encoding="utf-8")
    if DISK in t:
        continue
    new, n = re.subn(r'(<g id="[^"]+"[^>]*>)', lambda mo: mo.group(1) + "\n" + DISK, t, count=1)
    if n:
        p.write_text(new, encoding="utf-8")
        done += 1
print(f"{done} overlay'e arka disk eklendi")

"""Where one tier of the land gives way to the next (docs/design/hills.md "One air", after the boat game's I16 test): for
each pair of stills the handover scenario takes (PerfStage.Handover.cs: the wizard one chunk past a hand-over from a
target, "far", and one chunk short of it, "near", the camera looking at the target), the mean colour of a box round the
target's place on the screen in each, and how far apart the two are as a share of the far one's brightness. The bar is
about 5%. Usage: python3 tools/handover.py <player log> <folder of the stills> [--limit 0.05]"""
import os
import re
import sys

from PIL import Image

LINE = re.compile(r"PERF HANDOVER shot=(\S+) x=(-?\d+) y=(-?\d+) w=(\d+) h=(\d+) metres=(\d+)")
BOX = (0.03, 0.012)


def mean(path, x, y, w, h):
    img = Image.open(path).convert("RGB")
    sx, sy = img.width / w, img.height / h
    cx, cy = x * sx, img.height - y * sy
    bw, bh = BOX[0] * img.width, BOX[1] * img.height
    box = (int(cx - bw), int(cy - bh), int(cx + bw), int(cy + bh))
    crop = img.crop(box)
    px = list(crop.getdata())
    return [sum(p[i] for p in px) / len(px) for i in range(3)]


def main(log, folder, limit):
    seen = {}
    for line in open(log, errors="replace"):
        m = LINE.search(line)
        if m:
            seen[m.group(1)] = tuple(int(v) for v in m.groups()[1:])
    pairs = sorted({n.rsplit("-", 1)[0] for n in seen})
    worst = 0.0
    for pair in pairs:
        far, near = f"{pair}-far", f"{pair}-near"
        if far not in seen or near not in seen:
            continue
        a = mean(os.path.join(folder, f"perf-{far}.png"), *seen[far][:4])
        b = mean(os.path.join(folder, f"perf-{near}.png"), *seen[near][:4])
        bright = max(sum(a) / 3, 1)
        apart = sum(abs(p - q) for p, q in zip(a, b)) / 3 / bright
        worst = max(worst, apart)
        print(f"{pair}: far {a[0]:.0f},{a[1]:.0f},{a[2]:.0f} at {seen[far][4]} m, near {b[0]:.0f},{b[1]:.0f},{b[2]:.0f} at {seen[near][4]} m: "
              f"apart {apart * 100:.1f}% of the far's brightness{'  OVER' if apart > limit else ''}")
    print(f"worst {worst * 100:.1f}% (limit {limit * 100:.0f}%)")
    return 0 if worst <= limit else 1


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    limit = float(sys.argv[sys.argv.index("--limit") + 1]) if "--limit" in sys.argv else 0.05
    if "--limit" in sys.argv:
        args.remove(sys.argv[sys.argv.index("--limit") + 1])
    sys.exit(main(args[0], args[1], limit))

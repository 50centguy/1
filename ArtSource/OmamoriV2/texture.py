# Bakes per-face paint colours (and the screen's talisman glyph) into each Omamori V2 part's UV layout.
import json, os, sys, numpy as np
from PIL import Image, ImageDraw
out = sys.argv[1]
data = json.load(open(os.path.join(out, 'uv_layout.json')))
COLORS = {k: tuple(v) for k, v in data['colors'].items()}; EMISSIVE = set(data['emissive'])

def glyph_image(w, h):
    img = Image.new('L', (w, h), 0); d = ImageDraw.Draw(img); lw = max(2, int(w * .045))
    d.line([(w / 2, h * .10), (w / 2, h * .90)], fill=255, width=lw)
    d.arc([w * .16, h * .30, w * .84, h * .56], 180, 360, fill=255, width=lw)
    cx, cy, rx, ry = w / 2, h * .60, w * .17, h * .075
    d.polygon([(cx, cy - ry), (cx + rx, cy), (cx, cy + ry), (cx - rx, cy)], outline=255, width=lw)
    for y in (h * .06, h * .94): d.ellipse([w / 2 - lw * 1.3, y - lw * 1.3, w / 2 + lw * 1.3, y + lw * 1.3], fill=255)
    return img

rng = np.random.default_rng(7)
for label, part in data['parts'].items():
    size = part['size']
    base = Image.new('RGB', (size, size)); emis = Image.new('RGB', (size, size)); mask = Image.new('L', (size, size))
    db, de, dm = ImageDraw.Draw(base), ImageDraw.Draw(emis), ImageDraw.Draw(mask)
    glass = None
    for p in part['polys']:
        pts = [(u * size, (1 - v) * size) for u, v in p['uv']]
        db.polygon(pts, fill=COLORS[p['col']]); dm.polygon(pts, fill=255)
        if p['col'] in EMISSIVE: de.polygon(pts, fill=COLORS[p['col']])
        if p['col'] == 'glass' and p['normal_y'] < -.9 and (glass is None or p['area'] > glass['area']): glass = p
    arr = np.asarray(base).astype(np.float32); m = np.asarray(mask) > 0
    for _ in range(6):   # pad islands so mips never pull in black
        grown, gm = arr.copy(), m.copy()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            sh = np.roll(np.roll(arr, dy, 0), dx, 1); shm = np.roll(np.roll(m, dy, 0), dx, 1)
            take = ~gm & shm; grown[take] = sh[take]; gm |= take
        arr, m = grown, gm
    arr *= 1 + rng.normal(0, .025, arr.shape[:2])[..., None]
    ea = np.asarray(emis).astype(np.float32)
    if glass is not None:
        U = np.array([[u * size, (1 - v) * size, 1] for u, v in glass['uv'][:3]]); P = np.array(glass['xz'][:3])
        A = np.linalg.solve(U, P)
        xs = [q[0] for q in glass['xz']]; zs = [q[1] for q in glass['xz']]
        gw = 200; gh = int(gw * (max(zs) - min(zs)) / (max(xs) - min(xs)))
        g = np.asarray(glyph_image(gw, gh)).astype(np.float32) / 255
        pts = [(u * size, (1 - v) * size) for u, v in glass['uv']]
        for py in range(max(0, int(min(q[1] for q in pts)) - 1), min(size, int(max(q[1] for q in pts)) + 2)):
            for px in range(max(0, int(min(q[0] for q in pts)) - 1), min(size, int(max(q[0] for q in pts)) + 2)):
                X, Z = np.array([px + .5, py + .5, 1]) @ A
                gx = (X - min(xs)) / (max(xs) - min(xs)); gz = (max(zs) - Z) / (max(zs) - min(zs))
                if 0 <= gx < 1 and 0 <= gz < 1:
                    v = g[int(gz * gh), int(gx * gw)]; glow = np.array([69, 240, 200]) * v
                    arr[py, px] = np.array(COLORS['glass']) * (1 - v) + glow; ea[py, px] = glow
    Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8)).save(os.path.join(out, 'OmamoriV2_%s_basecolor.png' % label))
    if label == 'Screen': Image.fromarray(np.clip(ea, 0, 255).astype(np.uint8)).save(os.path.join(out, 'OmamoriV2_Screen_emission.png'))
    print(label, size, len(part['polys']), 'faces', 'glyph' if glass else '')

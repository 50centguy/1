# Textures for the Berth 04 playable's colour pass and the B1's cyber dressing (Docs/Art/INTERIOR_B1_PASS.md section 25).
#   python -I ArtSource/B1Cyber/prep_b1_cyber.py            (from the project root)
# Writes Assets/RepairProps/Scenes/Berth04_Playable/Textures/*.png:
#   b1_holo_queue.png      glyph alpha for the counter's queue hologram (coloured pale cyan by the Hologram shader)
#   b1_crt_bulletin.png    the port authority's notice on the old TV by the window (CRT green)
#   b1_ecg.png             one tile of the surgery monitor's trace (scrolls sideways at runtime)
#   b1_vitals.png          the readings under the trace
#   b1_gate_lcd.png        the gate's lock panel
#   b1_lease_tag.png       the NSP PORT lease tag / sticker on borrowed equipment
#   nb_*_muted.png         the street's neighbour textures with the palette applied (no saturated green/red/rainbow)
# Fonts: DotGothic16 (OFL) for screens and holograms, Noto Sans SC (OFL) for print. Deterministic: same output every run.
import colorsys
import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "RepairProps", "Scenes", "Berth04_Playable", "Textures")
NB = os.path.join(ROOT, "Assets", "RepairProps", "Scenes", "Temp_InteriorPass", "Textures")
DOT = os.path.join(ROOT, "Assets", "RepairProps", "Cyber", "Resources", "BorderRepairCyber", "Fonts", "DotGothic16-Regular.ttf")
SANS = os.path.join(ROOT, "Assets", "BorderRepair", "Art", "Fonts", "NotoSansSC", "NotoSansSC-Regular.otf")

CRT = (92, 230, 126)        # #5CE67E, the palette's CRT green
CRT_BG = (5, 16, 10)


def font(path, size):
    return ImageFont.truetype(path, size)


def text(d, xy, s, f, fill, anchor="la"):
    d.text(xy, s, font=f, fill=fill, anchor=anchor)


def holo_queue():
    # White glyphs on transparent: the shader takes alpha and colours it. Thin frame, corner ticks, a little noise.
    w, h = 512, 208
    im = Image.new("RGBA", (w, h), (255, 255, 255, 0))
    d = ImageDraw.Draw(im)
    big, mid, small = font(DOT, 112), font(DOT, 30), font(DOT, 18)
    a_frame, a_text, a_dim = 150, 255, 150
    d.rectangle([4, 4, w - 5, h - 5], outline=(255, 255, 255, a_frame), width=2)
    for x, y, sx, sy in ((10, 10, 1, 1), (w - 11, 10, -1, 1), (10, h - 11, 1, -1), (w - 11, h - 11, -1, -1)):
        d.line([x, y, x + 22 * sx, y], fill=(255, 255, 255, 255), width=3)
        d.line([x, y, x, y + 22 * sy], fill=(255, 255, 255, 255), width=3)
    text(d, (22, 18), "受付  RECEPTION", small, (255, 255, 255, a_dim))
    text(d, (22, 34), "07", big, (255, 255, 255, a_text))
    d.line([238, 46, 238, 168], fill=(255, 255, 255, 120), width=2)
    text(d, (256, 50), "次  08", mid, (255, 255, 255, a_text))
    text(d, (256, 96), "待ち  2", mid, (255, 255, 255, a_text))
    text(d, (256, 146), "見積 無料", small, (255, 255, 255, a_dim))
    text(d, (22, 178), "NSP PORT · 北坂4-12 · 義体修理", small, (255, 255, 255, a_dim))
    # Signal level bars on the right edge.
    for i in range(6):
        y = 160 - i * 18
        d.rectangle([470, y, 494, y + 10], fill=(255, 255, 255, 220 if i < 4 else 70))
    im.save(os.path.join(OUT, "b1_holo_queue.png"))


def scanlines(im, strength=.22, pitch=3):
    px = im.load()
    w, h = im.size
    for y in range(h):
        if y % pitch:
            continue
        for x in range(w):
            r, g, b = px[x, y][:3]
            px[x, y] = (int(r * (1 - strength)), int(g * (1 - strength)), int(b * (1 - strength)))


def vignette(im, amount=.35):
    w, h = im.size
    px = im.load()
    for y in range(h):
        for x in range(w):
            dx, dy = (x - w / 2) / (w / 2), (y - h / 2) / (h / 2)
            k = 1 - amount * min(1, (dx * dx + dy * dy) * .6)
            r, g, b = px[x, y][:3]
            px[x, y] = (int(r * k), int(g * k), int(b * k))


def crt_bulletin():
    w, h = 512, 384
    im = Image.new("RGB", (w, h), CRT_BG)
    d = ImageDraw.Draw(im)
    head, body, small = font(DOT, 34), font(DOT, 30), font(DOT, 20)
    d.rectangle([18, 18, w - 19, 66], fill=CRT)
    text(d, (30, 24), "北坂港務局  告知", head, CRT_BG)
    rows = [("第四係留  岸壁給電", body), ("夜間停止  02:00 - 04:00", body), ("", small),
            ("義体修理  届出 No.0412", body), ("受付 07  /  待ち 2", body)]
    y = 88
    for s, f in rows:
        if s:
            text(d, (30, y), s, f, CRT)
        y += 44 if f is body else 18
    d.line([30, y + 4, w - 30, y + 4], fill=(40, 110, 62), width=2)
    text(d, (30, y + 16), "NSP PORT · LEASE 0412 · 11/20", small, (60, 150, 86))
    d.rectangle([w - 58, y + 14, w - 40, y + 38], fill=CRT)           # the cursor
    glow = im.filter(ImageFilter.GaussianBlur(3))
    im = Image.blend(im, glow, .35)
    scanlines(im)
    vignette(im, .45)
    im.save(os.path.join(OUT, "b1_crt_bulletin.png"))


def ecg():
    # Two beats per tile, the trace ends where it starts so the tile repeats.
    w, h = 512, 128
    im = Image.new("RGB", (w, h), CRT_BG)
    d = ImageDraw.Draw(im)
    for x in range(0, w, 32):
        d.line([x, 0, x, h], fill=(10, 34, 18))
    for y in range(0, h, 32):
        d.line([0, y, w, y], fill=(10, 34, 18))
    base = h * .62

    def beat(t):     # t in 0..1 of one beat
        p = .10 * math.exp(-((t - .18) / .035) ** 2)            # P wave
        q = -.10 * math.exp(-((t - .30) / .010) ** 2)
        r = 1.00 * math.exp(-((t - .33) / .012) ** 2)            # R spike
        s = -.22 * math.exp(-((t - .36) / .012) ** 2)
        tw = .22 * math.exp(-((t - .58) / .055) ** 2)            # T wave
        return p + q + r + s + tw
    pts = []
    for x in range(w + 1):
        t = (x / w * 2) % 1
        pts.append((x, base - beat(t) * h * .5))
    trace = Image.new("RGB", (w, h), (0, 0, 0))
    td = ImageDraw.Draw(trace)
    td.line(pts, fill=CRT, width=3)
    glow = trace.filter(ImageFilter.GaussianBlur(4))
    px, tp, gp = im.load(), trace.load(), glow.load()
    for y in range(h):
        for x in range(w):
            r = min(255, px[x, y][0] + tp[x, y][0] + gp[x, y][0] // 2)
            g = min(255, px[x, y][1] + tp[x, y][1] + gp[x, y][1] // 2)
            b = min(255, px[x, y][2] + tp[x, y][2] + gp[x, y][2] // 2)
            px[x, y] = (r, g, b)
    scanlines(im, .18, 2)
    im.save(os.path.join(OUT, "b1_ecg.png"))


def vitals():
    # The monitor's numbers under the trace: four readings, labels dim, values bright.
    w, h = 512, 128
    im = Image.new("RGB", (w, h), CRT_BG)
    d = ImageDraw.Draw(im)
    label, big, mid = font(DOT, 18), font(DOT, 60), font(DOT, 38)
    dim = (60, 150, 86)
    for x, name, value, f, y in ((10, "HR", "72", big, 30), (128, "SpO2 %", "97", big, 30), (250, "NIBP", "118/76", mid, 50), (420, "T °C", "36.4", mid, 50)):
        text(d, (x, 8), name, label, dim)
        text(d, (x, y), value, f, CRT)
    for x in (118, 240, 410):
        d.line([x, 10, x, 118], fill=(20, 60, 34), width=2)
    glow = im.filter(ImageFilter.GaussianBlur(3))
    im = Image.blend(im, glow, .3)
    scanlines(im, .18, 2)
    im.save(os.path.join(OUT, "b1_vitals.png"))


def gate_lcd():
    w, h = 256, 128
    im = Image.new("RGB", (w, h), CRT_BG)
    d = ImageDraw.Draw(im)
    text(d, (14, 10), "ゲート 01", font(DOT, 30), CRT)
    text(d, (14, 52), "施錠  LOCK", font(DOT, 30), CRT)
    text(d, (14, 98), "NSP PORT · 0412", font(DOT, 18), (60, 150, 86))
    scanlines(im, .2, 2)
    im.save(os.path.join(OUT, "b1_gate_lcd.png"))


def lease_tag():
    w, h = 256, 128
    im = Image.new("RGB", (w, h), (214, 206, 180))
    d = ImageDraw.Draw(im)
    rnd = random.Random(412)
    for _ in range(900):      # paper grain and grime
        x, y = rnd.randrange(w), rnd.randrange(h)
        c = rnd.randrange(150, 205)
        d.point((x, y), fill=(c, c - 6, c - 22))
    d.rectangle([3, 3, w - 4, h - 4], outline=(40, 38, 34), width=2)
    bold, small = font(SANS, 26), font(SANS, 15)
    text(d, (12, 8), "NSP PORT", bold, (24, 24, 22))
    text(d, (12, 42), "LEASE 0412 · 北坂港務", small, (24, 24, 22))
    d.rectangle([10, 66, 168, 84], fill=(150, 46, 40))
    text(d, (16, 67), "VOID IF OPENED", small, (226, 220, 204))
    x = 12
    while x < 160:            # barcode
        bw = rnd.choice((1, 1, 2, 3))
        d.rectangle([x, 92, x + bw - 1, 118], fill=(26, 26, 24))
        x += bw + rnd.choice((1, 2, 2, 3))
    text(d, (176, 96), "0412", small, (24, 24, 22))
    im.save(os.path.join(OUT, "b1_lease_tag.png"))


def muted(src, dst, sat=.35, value=1.0, hue_to=None, hue_keep=.0):
    im = Image.open(os.path.join(NB, src)).convert("RGBA")
    px = im.load()
    w, h = im.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            hh, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            if hue_to is not None and s > .2:
                hh = hue_to / 360 * (1 - hue_keep) + hh * hue_keep
            s *= sat
            v *= value
            r2, g2, b2 = colorsys.hsv_to_rgb(hh, s, min(1, v))
            px[x, y] = (int(r2 * 255), int(g2 * 255), int(b2 * 255), a)
    im.save(os.path.join(OUT, dst))


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    holo_queue()
    crt_bulletin()
    ecg()
    vitals()
    gate_lcd()
    lease_tag()
    muted("nb_store_sign.png", "nb_store_sign_muted.png", sat=.32, value=.85)
    muted("nb_store_interior.png", "nb_store_interior_muted.png", sat=.45, value=.82)
    muted("nb_blade_noodle.png", "nb_blade_noodle_muted.png", sat=.75, value=.9, hue_to=30)
    print("wrote", sorted(f for f in os.listdir(OUT) if f.endswith(".png")))

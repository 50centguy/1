"""Builds the clinic NPCs (V2) in Blender (headless) and exports one FBX per character.

    blender -b --factory-startup --python build_npcs.py -- [Name ...]

Low-poly anime construction after the reference library (#256fes sheets, Let's LowPoly tips): lofted rings for limbs
and torso, an ellipsoid head whose front hemisphere carries a planar-projected painted face, a hair cap with an
azimuth-dependent hairline plus tapered strand cards (bangs, side locks, back skirt / ponytail), clothing as offset
shells. Two objects per character: <Name>_Body (static pose) and <Name>_Head (origin at the neck pivot, so the runtime
can turn the head without a skeleton). Empties: Ref_Front (+1 m in front), Ref_Eye (eye height), Ref_HeadTop.
Textures come from paint_npcs.py; tiles and swatches from npc_spec.py.
"""
import bpy
import bmesh
import math
import os
import sys
import json
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import npc_spec as S  # noqa: E402

TEX = os.path.join(HERE, "Textures")
EXPORT = os.path.join(HERE, "Export")
RENDERS = os.path.join(HERE, "Renders")
PX = 1.0 / S.ATLAS


def tile_uv(name, u, v):
    """u, v in [0,1] within a tile (v=0 top) -> atlas UV (GL, v up), half-texel inset."""
    x, y, w, h = S.TILES[name]
    u = min(max(u, 0.0), 1.0)
    v = min(max(v, 0.0), 1.0)
    return ((x + .5 + u * (w - 1)) * PX, 1 - (y + .5 + v * (h - 1)) * PX)


def swatch_uv(key):
    i = S.SW[key]
    x, y, w, h = S.TILES["swatch"]
    cx = x + (i % 8) * S.SWATCH + S.SWATCH / 2
    cy = y + (i // 8) * S.SWATCH + S.SWATCH / 2
    return (cx * PX, 1 - cy * PX)


class Part:
    """Accumulates faces for one object: positions, per-face-corner UVs and a material slot."""

    def __init__(self):
        self.verts = []
        self.faces = []      # (vertex ids, uvs, material)

    def v(self, p):
        self.verts.append(Vector(p))
        return len(self.verts) - 1

    def face(self, ids, uvs, mat=0, outward=None):
        """outward: a point the face should face away from (fixes winding)."""
        if outward is not None and len(ids) >= 3:
            a, b, c = (self.verts[i] for i in ids[:3])
            n = (b - a).cross(c - a)
            centre = sum((self.verts[i] for i in ids), Vector()) / len(ids)
            if n.dot(centre - Vector(outward)) < 0:
                ids = list(reversed(ids))
                uvs = list(reversed(uvs))
        self.faces.append((list(ids), list(uvs), mat))

    def double(self, ids, uvs, mat=0):
        self.faces.append((list(ids), list(uvs), mat))
        self.faces.append((list(reversed(ids)), list(reversed(uvs)), mat))

    def loft(self, rings, centres, tile, cap0=False, cap1=False, mat=0, uv=None, v0=0.0, v1=1.0, skip=None):
        """rings: list of point lists (same count n, ordered by azimuth from the back); centres: axis point per ring.
        uv(tile, u, v) maps ring position to UV; default is the tile's cylindrical map. skip(k) drops columns."""
        n = len(rings[0])
        ids = [[self.v(p) for p in ring] for ring in rings]
        lengths = [0.0]
        for i in range(1, len(rings)):
            lengths.append(lengths[-1] + (Vector(centres[i]) - Vector(centres[i - 1])).length)
        total = lengths[-1] or 1.0
        mapper = uv or (lambda t, u, v: tile_uv(t, u, v))
        for i in range(len(rings) - 1):
            va = v0 + (v1 - v0) * lengths[i] / total
            vb = v0 + (v1 - v0) * lengths[i + 1] / total
            mid = (Vector(centres[i]) + Vector(centres[i + 1])) / 2
            for k in range(n):
                if skip is not None and skip(k):
                    continue
                k2 = (k + 1) % n
                ua, ub = k / n, (k + 1) / n
                self.face([ids[i][k], ids[i][k2], ids[i + 1][k2], ids[i + 1][k]],
                          [mapper(tile, ua, va), mapper(tile, ub, va), mapper(tile, ub, vb), mapper(tile, ua, vb)], mat, outward=mid)
        for cap, idx, c in ((cap0, 0, centres[0]), (cap1, -1, centres[-1])):
            if not cap:
                continue
            ring = ids[idx]
            centre = self.v(c)
            other = Vector(centres[1] if idx == 0 else centres[-2])
            away = Vector(c) + (Vector(c) - other)
            for k in range(n):
                k2 = (k + 1) % n
                self.face([ring[k], ring[k2], centre], [mapper(tile, .5, .98 if idx else .02)] * 3, mat, outward=2 * Vector(c) - away)
        return ids


def ring(c, rx, ry, n, shape=None):
    """Ring around the vertical through c; azimuth 0 at the back (+Y), increasing toward +X, front (-Y) at 180."""
    pts = []
    for k in range(n):
        a = 2 * math.pi * k / n
        x, y = rx * math.sin(a), ry * math.cos(a)
        if shape:
            x, y = shape(a, x, y)
        pts.append(Vector((c[0] + x, c[1] + y, c[2])))
    return pts


def ring_axis(c, axis, rx, ry, n, side=Vector((1, 0, 0))):
    """Ring perpendicular to an arbitrary axis (limbs). rx along `side`, ry along axis x side."""
    axis = Vector(axis).normalized()
    s = (side - axis * side.dot(axis)).normalized()
    t = axis.cross(s)
    return [Vector(c) + s * (rx * math.sin(2 * math.pi * k / n)) + t * (ry * math.cos(2 * math.pi * k / n)) for k in range(n)]


def lerp(a, b, t):
    return a + (b - a) * t


# ------------------------------------------------------------------------------------------------------------- body
# Proportions (metres at height 1.60, scaled by s = height / 1.60): ~6.5 heads. Shoulder line 1.29, chest 1.17,
# waist 1.03, hip 0.88, crotch 0.78, knee 0.46, ankle 0.09. Arms hang in a relaxed A (about 10 degrees out).

def torso_levels(c):
    m = c["sex"] == "m"
    if m:
        return [  # z, rx, ry
            (.78, .135, .092), (.86, .145, .098), (.95, .140, .094), (1.03, .138, .092), (1.11, .148, .100),
            (1.18, .160, .106), (1.245, .174, .100), (1.295, .192, .084), (1.328, .134, .066), (1.355, .062, .058)]
    return [
        (.78, .140, .094), (.87, .158, .104), (.95, .138, .092), (1.03, .110, .080), (1.10, .120, .086),
        (1.17, .136, .098), (1.235, .148, .090), (1.29, .164, .076), (1.322, .120, .060), (1.348, .052, .048)]


def chest_shape(c, z):
    """Front bulge for the bust (female) or pecs (male), by height."""
    m = c["sex"] == "m"
    def shape(a, x, y):
        front = max(0.0, -math.cos(a))
        side = abs(math.sin(a))
        if not m and 1.12 < z < 1.22:
            k = 1 - abs(z - 1.17) / .05
            y -= .020 * k * front ** 2 * (1 - .4 * side)
        if m and 1.13 < z < 1.24:
            y -= .008 * front ** 3
        # Flatten the back a little, round the sides.
        if math.cos(a) > 0:
            y *= .94
        return x, y
    return shape


def arm_points(c, side, s, wide):
    m = c["sex"] == "m"
    sw = (.190 if m else .162) * s * wide
    sh = Vector((side * (sw - .024 * s), .004, 1.284 * s))
    el = Vector((side * (sw + .030 * s), .016, 1.035 * s))
    wr = Vector((side * (sw + .050 * s), -.010, .815 * s))
    return sh, el, wr


def build_body(name, c, P):
    s = c["height"] / 1.60
    m = c["sex"] == "m"
    wide = 1.12 if c["build"] == "stocky" else 1.0
    jacket = c["jacket"]
    sleeves = jacket.get("sleeves", True)
    levels = torso_levels(c)

    # Torso (top tile): neck base down to the hips, 12 sides.
    rings, centres = [], []
    for z, rx, ry in reversed(levels):
        cz = (0, .002, z * s)
        rings.append(ring(cz, rx * s * wide, ry * s * wide, 12, chest_shape(c, z)))
        centres.append(cz)
    P.loft(rings, centres, "top")
    # Neck: short and slim (anime), slightly forward at the top.
    nk = 1.25 if m else 1.12
    neck = [((0, .006, 1.34 * s), .044 * nk), ((0, .000, 1.40 * s), .040 * nk), ((0, -.004, 1.43 * s), .038 * nk)]
    P.loft([ring(p, r * s * wide, r * s * wide * .95, 10) for p, r in neck], [p for p, _ in neck], "skin")
    if c["top"]["detail"] == "turtleneck":
        P.loft([ring((0, .004, 1.32 * s), .052 * s, .050 * s, 10), ring((0, .000, 1.41 * s), .044 * s, .042 * s, 10)],
               [(0, .004, 1.32 * s), (0, .000, 1.41 * s)], "top", v0=0, v1=.12)

    # Pelvis (bottom tile) closing into the crotch, then the legs.
    hip = levels[0]
    pelvis = [ring((0, .002, hip[0] * s), hip[1] * s * wide, hip[2] * s * wide, 12),
              ring((0, .006, .73 * s), .11 * s * wide, .08 * s * wide, 12),
              ring((0, .008, .70 * s), .03 * s, .03 * s, 12)]
    P.loft(pelvis, [(0, .002, hip[0] * s), (0, .006, .73 * s), (0, .008, .70 * s)], "bottom", v0=0, v1=.14)
    for side in (-1, 1):
        hx = side * (.080 if not m else .082) * s * wide
        # x, y, z (in units of s), radius: thigh, knee, calf bulge, ankle.
        pts = [(hx, .004, .80, .088), (hx * .98, .000, .70, .082), (hx * .93, -.004, .58, .068), (hx * .90, -.008, .47, .054),
               (hx * .89, -.002, .42, .056), (hx * .88, .010, .33, .060), (hx * .87, .010, .22, .046), (hx * .86, .004, .12, .036),
               (hx * .86, .004, .08, .036)]
        rs = [ring((x, y, z * s), r * 1.12 * s * wide, r * 1.12 * s * wide * (.92 if z > .5 else 1.0), 10) for x, y, z, r in pts]
        cs = [(x, y, z * s) for x, y, z, _ in pts]
        P.loft(rs[:8], cs[:8], "bottom", v0=.14, v1=1)
        P.loft(rs[7:], cs[7:], "legwear", v0=.8, v1=1)
        # Shoe / boot: chunky, rounded toe, flat sole.
        fx = hx * .86
        boot = c["role"] == "港区装卸工"
        toe_r = .046 if boot else .040
        shoe = [((fx, .050 * s, .040 * s), .040, .034), ((fx, .020 * s, .060 * s), .046, .052 if boot else .044),
                ((fx, -.040 * s, .050 * s), toe_r + .002, .042), ((fx, -.110 * s, .034 * s), toe_r, .030), ((fx, -.150 * s, .024 * s), .030, .020)]
        rs = [ring_axis(p, (0, -1, 0), rx * s * wide, ry * s, 10, side=Vector((1, 0, 0))) for p, rx, ry in shoe]
        rs = [[Vector((q.x, q.y, max(q.z, 0.0))) for q in r] for r in rs]
        P.loft(rs, [p for p, _, _ in shoe], "shoes", cap0=True, cap1=True)
        if boot:
            cuff = [((fx, .012 * s, .085 * s), .050), ((fx, .010 * s, .16 * s), .050)]
            P.loft([ring(p, r * s * wide, r * s * wide, 10) for p, r in cuff], [p for p, _ in cuff], "shoes", v0=0, v1=.3)

    # Arms: deltoid, upper arm, elbow, forearm, wrist; hand as palm + finger block + thumb.
    for side in (-1, 1):
        sh, el, wr = arm_points(c, side, s, wide)
        cyber = (c.get("cyber") == "arm_l" and side == 1) or (c.get("cyber") == "arm_r" and side == -1)
        bare_fore = jacket.get("sleeve_len") == "elbow" or c["top"]["detail"] == "tank"
        bare_upper = c["top"]["detail"] == "tank" and not sleeves
        upper_tile = "skin" if bare_upper else ("sleeve" if sleeves else "top")
        fore_tile = "cyber" if cyber else ("skin" if bare_fore else ("sleeve" if sleeves else "top"))
        k = s * wide * (1.12 if m else 1.0) * 1.12
        ax_u = el - sh
        upper = [(sh + Vector((0, 0, .020 * s)), .050), (sh.lerp(el, .18), .054), (sh.lerp(el, .55), .046), (el, .039)]
        P.loft([ring_axis(p, ax_u, r * k, r * k * .95, 10) for p, r in upper], [p for p, _ in upper], upper_tile, v0=0, v1=.5)
        ax_f = wr - el
        fr = 1.12 if cyber else 1.0
        fore = [(el, .039 * fr), (el.lerp(wr, .25), .043 * fr), (el.lerp(wr, .7), .034 * fr), (wr, .027 * fr)]
        P.loft([ring_axis(p, ax_f, r * k, r * k * .85, 10, side=Vector((side, 0, 0))) for p, r in fore], [p for p, _ in fore], fore_tile, v0=.5, v1=1)
        hand_tile = "cyber" if cyber else "hands"
        down = ax_f.normalized()
        palm_end = wr + down * .085 * s
        tip = palm_end + (down + Vector((0, -.25, 0))).normalized() * .075 * s
        hand = [(wr, .026, .016), (wr + down * .03 * s, .040, .019), (palm_end, .043, .016), (palm_end.lerp(tip, .5), .040, .014), (tip, .030, .009)]
        rs = [ring_axis(p, down, w * s * wide, t * s * wide, 8, side=Vector((0, -1, 0))) for p, w, t in hand]
        P.loft(rs, [p for p, _, _ in hand], hand_tile, cap1=True)
        thumb0 = wr + down * .03 * s + Vector((0, -.02 * s, 0))
        thumb1 = thumb0 + (down * .6 + Vector((-side * .25, -.7, 0))).normalized() * .055 * s
        P.loft([ring_axis(thumb0, thumb1 - thumb0, .013 * s, .011 * s, 6), ring_axis(thumb1, thumb1 - thumb0, .008 * s, .007 * s, 6)],
               [thumb0, thumb1], hand_tile, cap1=True)
        if cyber:
            # Lock collar at the elbow socket and a taped label: the leased arm, remotely locked.
            P.loft([ring_axis(el - ax_f.normalized() * .006, ax_f, .048 * k, .046 * k, 10), ring_axis(el + ax_f.normalized() * .016, ax_f, .048 * k, .046 * k, 10)],
                   [el - ax_f.normalized() * .006, el + ax_f.normalized() * .016], "swatch", uv=lambda t, u, v: swatch_uv("darkmetal"))
        if sleeves:
            end = el + ax_u.normalized() * .02 if jacket.get("sleeve_len") == "elbow" else wr - ax_f.normalized() * .02
            ax = end - sh
            r0 = .060 * k
            r1 = (.052 if jacket.get("sleeve_len") == "elbow" else .044) * k
            mid = sh.lerp(end, .45)
            P.loft([ring_axis(sh + Vector((0, 0, .028 * s)), ax, r0 * .9, r0 * .85, 10), ring_axis(sh.lerp(end, .12), ax, r0, r0 * .95, 10),
                    ring_axis(mid, ax, (r0 + r1) / 2 + .004, (r0 + r1) / 2, 10), ring_axis(end, ax, r1, r1, 10)],
                   [sh, sh.lerp(end, .12), mid, end], "sleeve")

    # Jacket / vest / coat shell over the torso, following its shape with a little ease.
    hem = {"hip": .80, "knee": .48}[jacket["length"]]
    open_front = jacket["pattern"] in ("haori", "cardigan", "workcoat", "dockjacket")
    shell = [(z, rx + .014, ry + .016) for z, rx, ry in levels if z >= .80 and z <= 1.315]
    shell = [(1.33, .070, .060)] + list(reversed(shell))
    if hem < .8:
        shell += [(.70, .168, .120), (.58, .182, .130), (hem, .196, .140)]
    else:
        shell += [(hem - .02, shell[-1][1] + .006, shell[-1][2] + .006)]
    rings, centres = [], []
    for z, rx, ry in shell:
        base = chest_shape(c, z)
        def lapel(a, x, y, z=z, base=base):
            x, y = base(a, x, y)
            f = max(0.0, -math.cos(a))
            if open_front and z > 1.0:
                return x * (1 + .04 * f), y - .006 * f
            return x, y
        cz = (0, .004, z * s)
        rings.append(ring(cz, rx * s * wide, ry * s * wide, 14, lapel))
        centres.append(cz)
    P.loft(rings, centres, "jacket", skip=(lambda k: k == 7) if open_front else None)
    if jacket["pattern"] == "hivis":
        pass

    acc = c["accessories"]
    if "lanyard" in acc:
        strap = [Vector((-.03, -.040, 1.33 * s)), Vector((-.05, -.095, 1.22 * s)), Vector((0, -.125, 1.10 * s)),
                 Vector((.05, -.095, 1.22 * s)), Vector((.03, -.040, 1.33 * s))]
        for a, b in zip(strap[:-1], strap[1:]):
            w = (b - a).cross(Vector((0, -1, 0))).normalized() * .006
            P.double([P.v(a - w), P.v(a + w), P.v(b + w), P.v(b - w)], [swatch_uv("strap")] * 4)
        card = [Vector((-.026, -.130, 1.10 * s)), Vector((.026, -.130, 1.10 * s)), Vector((.026, -.134, 1.025 * s)), Vector((-.026, -.134, 1.025 * s))]
        P.double([P.v(p) for p in card], [swatch_uv("white")] * 4)
    if "headphones" in acc:
        band = ring((0, .01, 1.345 * s), .072 * s, .066 * s, 12)
        ids = [P.v(p) for p in band]
        ids2 = [P.v(p + Vector((0, 0, .022))) for p in band]
        for k in range(12):
            k2 = (k + 1) % 12
            P.double([ids[k], ids[k2], ids2[k2], ids2[k]], [swatch_uv("black")] * 4)
        for sx in (-1, 1):
            cp = Vector((sx * .074 * s, -.03, 1.34 * s))
            P.loft([ring_axis(cp, (sx, 0, 0), .030, .030, 10), ring_axis(cp + Vector((sx * .022, 0, 0)), (sx, 0, 0), .028, .028, 10)],
                   [cp, cp + Vector((sx * .022, 0, 0))], "swatch", cap0=True, cap1=True, uv=lambda t, u, v: swatch_uv("darkmetal"))
    if "towel" in acc:
        # Towel draped round the neck, ends hanging down the chest.
        loop = ring((0, .004, 1.335 * s), .078 * s * wide, .066 * s * wide, 12)
        loop2 = [p + Vector((0, 0, .028 * s)) for p in loop]
        P.loft([[p * 1.0 for p in loop], loop2], [(0, .004, 1.335 * s), (0, .004, 1.363 * s)], "swatch", uv=lambda t, u, v: swatch_uv("white"))
        for sx in (-1, 1):
            a = Vector((sx * .045 * s, -.072 * s, 1.33 * s))
            b = Vector((sx * .060 * s, -.112 * s, 1.12 * s))
            P.loft([ring_axis(a, b - a, .022 * s, .006 * s, 6, side=Vector((1, 0, 0))), ring_axis(b, b - a, .024 * s, .006 * s, 6, side=Vector((1, 0, 0)))],
                   [a, b], "swatch", cap1=True, uv=lambda t, u, v: swatch_uv("white"))


# ------------------------------------------------------------------------------------------------------------- head
HEAD_R = (.094, .102, .124)   # cranium half-extents (x, y, z) in metres at s = 1, before HEAD_SCALE
HEAD_SCALE = 1.10             # anime head over the body: about 6 heads tall


def head_frame(c):
    s = c["height"] / 1.60
    pivot = Vector((0, -.002, 1.40 * s))
    centre = Vector((0, .006, 1.50 * s))
    return s, pivot, centre


def face_uv(c, p, centre, sh):
    """Planar front projection into the top-left frame of the face sheet (GL: u in [0,.5], v in [.5,1])."""
    u = ((p.x - centre.x) / sh + S.FACE_HW) / (2 * S.FACE_HW)
    v = ((p.z - centre.z) / sh - S.FACE_Z0) / (S.FACE_Z1 - S.FACE_Z0)
    return (min(max(u, .004), .996) * .5, .5 + min(max(v, .004), .996) * .5)


def head_point(c, phi, a, centre, s):
    """Anime head: round cranium, broad cheekbones, jaw narrowing to a soft pointed chin, flattened face plane."""
    rx, ry, rz = (r * s for r in HEAD_R)
    m = c["sex"] == "m"
    x = rx * math.sin(phi) * math.sin(a)
    y = ry * math.sin(phi) * math.cos(a)
    z = rz * math.cos(phi)
    zr = z / rz
    if zr < -.05:
        t = min(1.0, (-.05 - zr) / .95)
        # Cheekbones hold the width down to mid-face, then the jaw narrows to the chin.
        jaw = .66 if m else .42
        x *= lerp(1.0, jaw, t ** (1.6 if not m else 1.9))
        if y < 0:
            y = lerp(y, -.070 * s, t * .7)
        else:
            y *= lerp(1.0, .45, t)
        z = lerp(z, z * (1.04 if not m else .94), t)
    if z < 0:
        z *= .82
    if y < -.074 * s:
        y = -.074 * s + (y + .074 * s) * .30
    # Brow ridge / eye area: a gentle recess under the forehead.
    front = max(0.0, -math.cos(a))
    if -.02 < zr < .22:
        y += .004 * s * front * (1 - abs(zr - .1) / .12) if abs(zr - .1) < .12 else 0
    return centre + Vector((x, y, z))


def build_head(name, c, H):
    s0, pivot, centre = head_frame(c)
    s = s0 * HEAD_SCALE
    lat, lon = 12, 18
    grid = [[head_point(c, math.pi * i / lat, 2 * math.pi * k / lon, centre, s) for k in range(lon)] for i in range(lat + 1)]
    top = H.v(grid[0][0])
    bottom = H.v(grid[-1][0])
    ids = [[H.v(p) for p in row] for row in grid[1:-1]]
    rz = HEAD_R[2] * s
    for i in range(len(ids) - 1):
        for k in range(lon):
            k2 = (k + 1) % lon
            quad = [ids[i][k], ids[i][k2], ids[i + 1][k2], ids[i + 1][k]]
            pts = [H.verts[q] for q in quad]
            cen = sum(pts, Vector()) / 4
            if cen.y < centre.y - .040 * s and cen.z < centre.z + .10 * s:
                H.face(quad, [face_uv(c, p, centre, s) for p in pts], 1, outward=centre)
            else:
                H.face(quad, [tile_uv("skin", k / lon, i / lat), tile_uv("skin", (k + 1) / lon, i / lat),
                              tile_uv("skin", (k + 1) / lon, (i + 1) / lat), tile_uv("skin", k / lon, (i + 1) / lat)], 0, outward=centre)
    for k in range(lon):
        k2 = (k + 1) % lon
        H.face([top, ids[0][k], ids[0][k2]], [tile_uv("skin", .5, 0)] * 3, 0, outward=centre)
        tri = [bottom, ids[-1][k2], ids[-1][k]]
        pts = [H.verts[q] for q in tri]
        if (sum(pts, Vector()) / 3).y < centre.y - .02 * s:
            H.face(tri, [face_uv(c, p, centre, s) for p in pts], 1, outward=centre)
        else:
            H.face(tri, [tile_uv("skin2", .5, .5)] * 3, 0, outward=centre)
    # Nose: a small wedge on the face plane, mapped into the face sheet so its shading is painted.
    nz = centre.z - .046 * s
    ny = centre.y - .074 * s
    tipv = Vector((0, ny - .010 * s, nz - .004 * s))
    bridge = Vector((0, ny + .001, nz + .020 * s))
    left, right = Vector((-.009 * s, ny + .002, nz - .006 * s)), Vector((.009 * s, ny + .002, nz - .006 * s))
    for tri in ((bridge, left, tipv), (bridge, tipv, right), (left, Vector((0, ny + .002, nz - .010 * s)), tipv), (tipv, Vector((0, ny + .002, nz - .010 * s)), right)):
        idx = [H.v(p) for p in tri]
        H.face(idx, [face_uv(c, p, centre, s) for p in tri], 1, outward=centre)
    # Ears.
    for sx in (-1, 1):
        e = centre + Vector((sx * HEAD_R[0] * s * .97, .006 * s, -.018 * s))
        pts = [e + Vector((0, .012, .024)) * s, e + Vector((sx * .012, .008, .014)) * s, e + Vector((sx * .010, .010, -.018)) * s, e + Vector((0, -.004, -.022)) * s]
        H.face([H.v(p) for p in pts], [tile_uv("skin2", .5, .5)] * 4, 0, outward=centre)
    build_hair(c, H, centre, s)
    build_head_props(c, H, centre, s * 1.18)   # props were laid out for V1's head scale
    return pivot, centre


def build_hair(c, H, centre, s):
    style = c["hairstyle"]
    shaved = style == "shaved"
    crop = style == "crop"
    rx, ry, rz = (r * s for r in HEAD_R)
    k_out = 1.03 if shaved else (1.06 if crop else 1.10)
    hx, hy, hz = rx * k_out, ry * k_out, rz * (1.03 if shaved or crop else 1.09)
    tile = "hairdark" if shaved else "hair"

    def hairline(a):
        front = (1 - math.cos(a)) / 2          # 0 back .. 1 front
        if shaved:
            return lerp(-.06, .060, front ** 1.6) * s
        if crop:
            return lerp(-.075, .070, front ** 1.8) * s
        return lerp(-.100, .062, front ** 2.2) * s

    def surface(a, z, push=0.0):
        zz = max(-.999, min(.999, z / hz))
        r = math.sin(math.acos(zz))
        return centre + Vector((hx * r * math.sin(a), hy * r * math.cos(a), z)) + Vector((math.sin(a), math.cos(a), 0)) * push

    # Cap.
    lon, lat = 18, 7
    cap = []
    for k in range(lon):
        a = 2 * math.pi * k / lon
        phimax = math.acos(max(-.999, min(.999, hairline(a) / hz)))
        cap.append([surface(a, hz * math.cos(phimax * i / lat)) + Vector((0, 0, .004 * s)) for i in range(1, lat + 1)])
    pole = H.v(centre + Vector((0, 0, hz + .004 * s)))
    ids = [[H.v(p) for p in col] for col in cap]
    for k in range(lon):
        k2 = (k + 1) % lon
        H.face([pole, ids[k][0], ids[k2][0]], [tile_uv(tile, k / lon, 0)] * 3, 0, outward=centre)
        for i in range(lat - 1):
            H.face([ids[k][i], ids[k2][i], ids[k2][i + 1], ids[k][i + 1]],
                   [tile_uv(tile, k / lon, i / lat), tile_uv(tile, (k + 1) / lon, i / lat),
                    tile_uv(tile, (k + 1) / lon, (i + 1) / lat), tile_uv(tile, k / lon, (i + 1) / lat)], 0, outward=centre)
    if shaved:
        return

    def clump(a, root_z, tip, width, thick, bulge=.012, segs=4):
        """Solid tapered hair clump (diamond cross-section) from a root on the cap to a tip point, bowed outward."""
        root = surface(a, root_z, -.004 * s)
        tipv = Vector(tip)
        out = Vector((math.sin(a), math.cos(a), 0))
        ctrl = root.lerp(tipv, .5) + out * bulge * s
        pts, rings_ = [], []
        for i in range(segs + 1):
            t = i / segs
            p = root * (1 - t) ** 2 + ctrl * 2 * t * (1 - t) + tipv * t * t
            pts.append(p)
        for i, p in enumerate(pts):
            t = i / segs
            nxt = pts[min(i + 1, segs)] - pts[max(i - 1, 0)]
            w = width * s * .5 * (1 - t) ** .75 + .0006
            th = thick * s * .5 * (1 - t) ** .6 + .0004
            side = nxt.cross(out).normalized() if nxt.cross(out).length > 1e-6 else Vector((1, 0, 0))
            normal = side.cross(nxt).normalized()
            if normal.dot(out) < 0:
                normal = -normal
            rings_.append([p + normal * th, p + side * w, p - normal * th * .6, p - side * w])
        H.loft(rings_, pts, tile, v0=.05, v1=1, cap1=False)

    front = math.pi
    bangs = c["bangs"]
    if bangs in ("full", "side", "messy"):
        n = {"full": 7, "side": 7, "messy": 9}[bangs]
        for j in range(n):
            t = j / (n - 1)
            a = front + (t - .5) * (1.6 if bangs != "messy" else 1.8)
            if bangs == "full":
                tip_z, lean = (-.004 + .012 * abs(t - .5)) * s, 0
            elif bangs == "side":
                tip_z, lean = (.010 - .045 * t) * s, .030 * t
            else:
                tip_z, lean = (.008 + .022 * ((j * 37) % 5) / 5) * s, .010 * ((j % 3) - 1)
            tip = surface(a, tip_z, .020 * s) + Vector((lean * s, -.006 * s, 0))
            clump(a, .085 * s, tip, .040 if bangs != "messy" else .034, .014, bulge=.010)
    if style in ("bob", "ponytail", "short"):
        for sx in (-1, 1):
            a = front - sx * 1.0
            drop = {"bob": -.115, "ponytail": -.120, "short": -.070}[style]
            tip = surface(a, drop * s, .024 * s) + Vector((0, -.010 * s, 0))
            clump(a, .060 * s, tip, .040, .016, bulge=.008, segs=5)
    if style == "bob":
        n = 14
        for k in range(n):
            a = math.radians(-118 + 236 * k / (n - 1))
            root_z = max(hairline(a) + .05 * s, -.02 * s)
            tip = surface(a, (-.112 - (.010 if k % 2 else 0)) * s, .030 * s)
            tip = centre + (tip - centre) * Vector((1, 1, 1))
            clump(a, root_z, tip + Vector((0, 0, 0)), .052, .020, bulge=.016, segs=5)
    if style == "short":
        for k in range(11):
            a = math.radians(-125 + 250 * k / 10)
            base_z = hairline(a)
            tip = surface(a, base_z - (.040 + .016 * (k % 3)) * s, .036 * s)
            clump(a, base_z + .07 * s, tip, .050, .018, bulge=.014)
        # A couple of tufts sticking up at the crown.
        for k, a in enumerate(() if "hardhat" in c["accessories"] else (2.6, 3.4, 0.4)):
            root = .10 * s
            tip = surface(a, .118 * s, .040 * s) + Vector((0, 0, .022 * s))
            clump(a, root, tip, .036, .014, bulge=.006, segs=3)
    if style == "crop":
        # Short spiky top: a ring of stubby clumps pushed up and back.
        for ring_z, n, length in ((.095, 8, .030), (.065, 12, .034), (.030, 14, .030)):
            for k in range(n):
                a = 2 * math.pi * (k + (.5 if n == 12 else 0)) / n
                fr = (1 - math.cos(a)) / 2
                if ring_z < .05 and fr > .6:
                    continue          # keep the forehead clear
                root_z = ring_z * s
                # Swept up at the front, back elsewhere; tips lie close over the cap.
                tip = surface(a, root_z - length * s * (1 - fr) + .006 * s * fr, .010 * s) + Vector((0, .010 * s, .010 * s * fr))
                clump(a, root_z + .008 * s, tip, .040, .012, bulge=.004, segs=3)
    if style == "ponytail":
        tie = centre + Vector((0, hy + .004 * s, -.040 * s))
        pts = [tie, tie + Vector((0, .030, -.03)) * s, tie + Vector((0, .044, -.12)) * s, tie + Vector((0, .036, -.22)) * s, tie + Vector((0, .024, -.31)) * s]
        radii = [.024, .040, .044, .030, .004]
        H.loft([ring_axis(p, (0, .2, -1), r * s, r * s * .8, 10) for p, r in zip(pts, radii)], pts, tile, v0=.1, v1=1)
        H.loft([ring_axis(tie + Vector((0, -.004, .006)), (0, .3, -1), .022 * s, .020 * s, 8), ring_axis(tie + Vector((0, .006, -.008)), (0, .3, -1), .023 * s, .021 * s, 8)],
               [tie + Vector((0, -.004, .006)), tie + Vector((0, .006, -.008))], "swatch", uv=lambda t, u, v: swatch_uv("clip"))
        for k in range(5):
            a = math.radians(-60 + 30 * k)
            clump(a, hairline(a) + .06 * s, tie + Vector((0, .006, .004)), .050, .018, bulge=.010, segs=3)


def build_head_props(c, H, centre, s):
    acc = c["accessories"]
    sw = lambda key: (lambda t, u, v: swatch_uv(key))  # noqa: E731
    if "cigarette" in acc:
        a = centre + Vector((.012 * s, -.068 * s, -.070 * s))
        b = a + Vector((.035, -.045, -.018)) * s
        H.loft([ring_axis(a, b - a, .0045, .0045, 5), ring_axis(b, b - a, .0045, .0045, 5)], [a, b], "swatch", uv=sw("cig"))
        e = b + (b - a).normalized() * .006
        H.loft([ring_axis(b, b - a, .0047, .0047, 5), ring_axis(e, b - a, .0047, .0047, 5)], [b, e], "swatch", cap1=True, uv=sw("ember"))
    if "earring" in acc:
        for sx in (-1, 1):
            e = centre + Vector((sx * .079 * s, .006 * s, -.040 * s))
            H.loft([ring_axis(e, (0, 0, -1), .004, .004, 4), ring_axis(e + Vector((0, 0, -.022)), (0, 0, -1), .004, .004, 4)],
                   [e, e + Vector((0, 0, -.022))], "swatch", cap1=True, uv=sw("gold"))
    if "hairclip" in acc:
        p = centre + Vector((.074 * s, -.040 * s, .052 * s))
        q = p + Vector((.006, -.024, -.010)) * s
        H.loft([ring_axis(p, q - p, .006 * s, .004 * s, 4), ring_axis(q, q - p, .006 * s, .004 * s, 4)], [p, q], "swatch", cap0=True, cap1=True, uv=sw("clip"))
    if "goggles" in acc:
        rr = math.sqrt(max(0.0, 1 - (.056 / .108) ** 2)) * 1.07
        band = ring(centre + Vector((0, .002, .056 * s)), .080 * s * rr, .090 * s * rr, 14)
        ids = [H.v(p) for p in band]
        ids2 = [H.v(p + Vector((0, 0, .018 * s))) for p in band]
        for k in range(14):
            k2 = (k + 1) % 14
            H.face([ids[k], ids[k2], ids2[k2], ids2[k]], [sw("strap")(0, 0, 0)] * 4, 0, outward=centre + Vector((0, 0, .067 * s)))
        for sx in (-1, 1):
            p = centre + Vector((sx * .032 * s, -.090 * s * rr + .004 * s, .066 * s))
            q = p + Vector((0, -.022, .004)) * s
            H.loft([ring_axis(p, q - p, .022 * s, .020 * s, 8), ring_axis(q, q - p, .023 * s, .021 * s, 8)], [p, q], "swatch", uv=sw("darkmetal"))
            H.loft([ring_axis(q, q - p, .023 * s, .021 * s, 8), ring_axis(q + (q - p) * .05, q - p, .001, .001, 8)], [q, q + (q - p) * .05], "swatch", uv=sw("lens"))
    if "hardhat" in acc:
        dome_c = centre + Vector((0, .002, .040 * s))
        rings, cs = [], []
        for i in range(5):
            phi = math.radians(90 - 90 * i / 4)
            z = .082 * s * math.sin(math.radians(90) - phi) if False else .080 * s * math.cos(phi)
            r = math.sin(phi)
            cz = dome_c + Vector((0, 0, z))
            rings.append(ring(cz, .102 * s * r + .001, .114 * s * r + .001, 14))
            cs.append(cz)
        H.loft(rings, cs, "swatch", cap1=True, uv=sw("yellow"))
        brim = [ring(dome_c, .102 * s, .114 * s, 14), ring(dome_c + Vector((0, -.004, -.004)), .120 * s, .145 * s, 14, lambda a, x, y: (x, y * (1.15 if math.cos(a) < 0 else .9)))]
        H.loft(brim, [dome_c, dome_c + Vector((0, 0, -.004))], "swatch", uv=sw("yellow"))
        lamp = dome_c + Vector((0, -.112 * s, .030 * s))
        tipl = lamp + Vector((0, -.022, 0)) * s
        H.loft([ring_axis(lamp, (0, -1, 0), .016 * s, .016 * s, 8), ring_axis(tipl, (0, -1, 0), .016 * s, .016 * s, 8)], [lamp, tipl], "swatch", uv=sw("darkmetal"))
        H.loft([ring_axis(tipl, (0, -1, 0), .016 * s, .016 * s, 8), ring_axis(tipl + Vector((0, -.001, 0)), (0, -1, 0), .001, .001, 8)],
               [tipl, tipl + Vector((0, -.001, 0))], "swatch", uv=sw("amber_led"))


# ---------------------------------------------------------------------------------------------------------- export
def make_object(name, part, materials, origin):
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    verts = [bm.verts.new(p - origin) for p in part.verts]
    uv = bm.loops.layers.uv.new("UVMap")
    for ids, uvs, mat in part.faces:
        if len(set(ids)) < len(ids):
            continue
        try:
            f = bm.faces.new([verts[i] for i in ids])
        except ValueError:
            continue          # duplicate (double-sided cards already exist reversed)
        f.material_index = mat
        for loop, coord in zip(f.loops, uvs):
            loop[uv].uv = coord
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bm.to_mesh(mesh)
    bm.free()
    for m in materials:
        mesh.materials.append(m)
    for poly in mesh.polygons:
        poly.use_smooth = True
    obj = bpy.data.objects.new(name, mesh)
    obj.location = origin
    bpy.context.scene.collection.objects.link(obj)
    return obj


def material(name, image_path, emission=0.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(image_path, check_existing=True)
    tex.interpolation = "Closest"
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = .85
    if emission > 0:
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = emission
    return mat


def empty(name, loc, parent=None):
    e = bpy.data.objects.new(name, None)
    e.location = loc
    bpy.context.scene.collection.objects.link(e)
    if parent:
        e.parent = parent
        e.location = Vector(loc) - parent.location
    return e


def clear():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.images):
        for item in list(coll):
            coll.remove(item)


def build(name):
    c = S.CHARACTERS[name]
    clear()
    body_mat = material("M_NPC_%s" % name, os.path.join(TEX, "T_NPC_%s.png" % name))
    face_mat = material("M_NPC_%s_Face" % name, os.path.join(TEX, "T_NPC_%s_Face.png" % name))
    body = Part()
    build_body(name, c, body)
    head = Part()
    pivot, centre = build_head(name, c, head)
    root = bpy.data.objects.new("NPC_" + name, None)
    bpy.context.scene.collection.objects.link(root)
    b = make_object(name + "_Body", body, [body_mat], Vector((0, 0, 0)))
    h = make_object(name + "_Head", head, [body_mat, face_mat], pivot)
    b.parent = root
    h.parent = root
    empty("Ref_Front", (0, -1, 0), root)
    empty("Ref_Eye", (0, -.07, centre.z - .01), root)
    empty("Ref_HeadTop", (0, 0, centre.z + .13), root)
    tris = sum(len(p.vertices) - 2 for o in (b, h) for p in o.data.polygons)
    return root, b, h, tris, centre


def export(name, root):
    os.makedirs(EXPORT, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in [root] + list(root.children_recursive):
        o.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=os.path.join(EXPORT, "NPC_%s.fbx" % name), use_selection=True, axis_forward="-Z", axis_up="Y",
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", object_types={"EMPTY", "MESH"},
                             mesh_smooth_type="FACE", use_mesh_modifiers=True, bake_space_transform=False, path_mode="STRIP")


def render(name, centre):
    os.makedirs(RENDERS, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 900, 900
    scene.world = bpy.data.worlds.new("w") if scene.world is None else scene.world
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = (.08, .08, .09, 1)
    scene.world.node_tree.nodes["Background"].inputs[1].default_value = 1.0
    light = bpy.data.lights.new("key", "SUN")
    light.energy = 3.0
    lo = bpy.data.objects.new("key", light)
    lo.rotation_euler = (math.radians(50), 0, math.radians(-30))
    scene.collection.objects.link(lo)
    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = "ORTHO"
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    shots = [("front", 0), ("three", -35), ("side", -90), ("back", 180)]
    for label, yaw in shots:
        r = math.radians(yaw)
        cam_data.ortho_scale = 1.9
        cam.location = Vector((3 * math.sin(r), -3 * math.cos(r), .9))
        cam.rotation_euler = (math.radians(90), 0, r)
        scene.render.filepath = os.path.join(RENDERS, "%s_%s.png" % (name, label))
        bpy.ops.render.render(write_still=True)
    cam_data.ortho_scale = .42
    for label, yaw in (("face", 0), ("face3", -30)):
        r = math.radians(yaw)
        cam.location = Vector((1.5 * math.sin(r), -1.5 * math.cos(r), centre.z))
        cam.rotation_euler = (math.radians(90), 0, r)
        scene.render.filepath = os.path.join(RENDERS, "%s_%s.png" % (name, label))
        bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    names = [a for a in argv if a in S.CHARACTERS] or list(S.CHARACTERS)
    want_render = "--render" in argv or "render" in argv
    report = {}
    for name in names:
        root, b, h, tris, centre = build(name)
        export(name, root)
        report[name] = dict(tris=tris, body_tris=sum(len(p.vertices) - 2 for p in b.data.polygons), head_tris=sum(len(p.vertices) - 2 for p in h.data.polygons))
        if want_render:
            render(name, centre)
        print("built", name, report[name])
    with open(os.path.join(HERE, "report.json"), "w", encoding="utf-8") as fp:
        json.dump(report, fp, indent=2)


main()

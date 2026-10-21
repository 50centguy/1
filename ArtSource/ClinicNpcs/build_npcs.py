"""Builds the clinic NPCs in Blender (headless) and exports one FBX per character.

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
def build_body(name, c, P):
    s = c["height"] / 1.60
    m = c["sex"] == "m"
    wide = 1.18 if c["build"] == "stocky" else 1.0
    jacket = c["jacket"]
    sleeves = jacket.get("sleeves", True)

    # Torso (top tile) from hip to neck base; female chest shaped at the front.
    def chest(a, x, y):
        front = max(0.0, -math.cos(a))
        return x, y - (.018 * front ** 3 if not m else 0)
    levels = [  # z, rx, ry
        (.84, .150, .098), (.92, .124, .084), (.99, .116, .080), (1.08, .132, .088), (1.16, .140, .092),
        (1.25, .150, .082), (1.31, .158 if not m else .175, .070), (1.35, .060, .045)]
    rings, centres = [], []
    for z, rx, ry in levels:
        cz = (0, 0, z * s)
        rings.append(ring(cz, rx * s * wide, ry * s * wide, 10, chest if 1.0 < z < 1.2 else None))
        centres.append(cz)
    # Neck first so the tile's top (collar, neckline) lands at the neck.
    P.loft(list(reversed(rings)), list(reversed(centres)), "top", cap0=False)
    # Neck.
    P.loft([ring((0, .004, 1.33 * s), .036 * s * wide, .034 * s * wide, 8), ring((0, .006, 1.44 * s), .033 * s * wide, .031 * s * wide, 8)],
           [(0, .004, 1.33 * s), (0, .006, 1.44 * s)], "skin")
    if c["top"]["detail"] == "turtleneck":
        P.loft([ring((0, .004, 1.33 * s), .046 * s, .044 * s, 8), ring((0, .006, 1.41 * s), .042 * s, .040 * s, 8)],
               [(0, .004, 1.33 * s), (0, .006, 1.41 * s)], "top", v0=0, v1=.12)

    # Pelvis into the legs (bottom tile).
    pelvis = [ring((0, 0, .84 * s), .150 * s * wide, .098 * s * wide, 10), ring((0, .005, .76 * s), .150 * s * wide, .094 * s * wide, 10)]
    P.loft(pelvis, [(0, 0, .84 * s), (0, .005, .76 * s)], "bottom", v0=0, v1=.12)
    P.loft([pelvis[1], ring((0, .005, .72 * s), .05 * s, .04 * s, 10)], [(0, .005, .76 * s), (0, .005, .72 * s)], "bottom", v0=.12, v1=.16)
    for side in (-1, 1):
        x0 = side * .078 * s * wide
        pts = [(x0, .005, .78 * s, .078), (side * .074 * s * wide, .0, .62 * s, .066), (side * .07 * s, -.004, .45 * s, .050),
               (side * .07 * s, .006, .32 * s, .054), (side * .07 * s, .004, .14 * s, .036), (side * .07 * s, .0, .08 * s, .034)]
        rings = [ring((x, y, z), r * s * wide, r * s * wide, 8) for x, y, z, r in pts]
        cs = [(x, y, z) for x, y, z, _ in pts]
        P.loft(rings[:5], cs[:5], "bottom", v0=.16, v1=1)
        P.loft(rings[4:], cs[4:], "legwear", v0=.8, v1=1)
        # Shoe: heel to toe, slightly flattened.
        fx = side * .07 * s
        shoe = [((fx, .045 * s, .045 * s), .036, .03), ((fx, .0, .05 * s), .040, .045), ((fx, -.07 * s, .038 * s), .040, .036),
                ((fx, -.15 * s, .025 * s), .030, .022)]
        rings = [ring_axis(p, (0, -1, 0), rx * s * wide, ry * s, 8, side=Vector((1, 0, 0))) for p, rx, ry in shoe]
        rings = [[Vector((q.x, q.y, max(q.z, 0.0))) for q in r] for r in rings]
        P.loft(rings, [p for p, _, _ in shoe], "shoes", cap0=True, cap1=True)

    # Arms: shoulder -> elbow -> wrist, hands as flattened lofts. Cyber left forearm on Tobi.
    for side in (-1, 1):
        sx = side
        sh = Vector((sx * .165 * s * wide, .0, 1.29 * s))
        el = Vector((sx * .215 * s * wide, .012, 1.04 * s))
        wr = Vector((sx * .232 * s * wide, -.012, .82 * s))
        tip = Vector((sx * .236 * s * wide, -.020, .70 * s))
        cyber = c.get("cyber") == "arm_l" and side == 1
        def limb(a, b, ra, rb, tile, v0, v1, n=8):
            ax = b - a
            P.loft([ring_axis(a, ax, ra, ra, n), ring_axis(b, ax, rb, rb, n)], [a, b], tile, v0=v0, v1=v1)
        upper_tile = "top" if not sleeves else "sleeve"
        limb(sh, el, .046 * s * wide, .036 * s * wide, upper_tile if not (c["top"]["detail"] == "tank" and not sleeves) else "skin", 0, .5)
        fore = "cyber" if cyber else ("top" if not sleeves else "sleeve")
        if jacket.get("sleeve_len") == "elbow" or c["top"]["detail"] == "tank":
            fore = "cyber" if cyber else "skin"
        limb(el, wr, .036 * s * wide, .028 * s * wide, fore, .5, 1)
        hand_tile = "cyber" if cyber else "hands"
        hand = [(wr, .026, .016), (wr.lerp(tip, .45), .036, .014), (tip, .022, .010)]
        rings = [ring_axis(p, tip - wr, ry * s, rx * s, 6, side=Vector((sx, 0, 0))) for p, ry, rx in hand]
        P.loft(rings, [p for p, _, _ in hand], hand_tile, cap1=True)
        # Thumb.
        tb = wr.lerp(tip, .25) + Vector((0, -.02 * s, 0))
        P.loft([ring_axis(tb, (sx * -.2, -.6, -1), .010 * s, .010 * s, 5), ring_axis(tb + Vector((sx * -.006, -.012, -.04)) * s, (sx * -.2, -.6, -1), .006 * s, .006 * s, 5)],
               [tb, tb + Vector((sx * -.006, -.012, -.04)) * s], hand_tile, cap1=True)
        if sleeves:
            end = el if jacket.get("sleeve_len") == "elbow" else wr + (el - wr).normalized() * .02
            ax = end - sh
            r0, r1 = .052 * s * wide, (.046 if jacket.get("sleeve_len") == "elbow" else .041) * s * wide
            P.loft([ring_axis(sh + Vector((0, 0, .015)), ax, r0, r0, 8), ring_axis(sh.lerp(end, .5), ax, (r0 + r1) / 2 + .004, (r0 + r1) / 2 + .004, 8),
                    ring_axis(end, ax, r1, r1, 8)], [sh, sh.lerp(end, .5), end], "sleeve")

    # Jacket / vest / coat shell.
    hem = {"hip": .80, "knee": .46}[jacket["length"]]
    open_front = jacket["pattern"] in ("haori", "cardigan", "workcoat")
    levels = [(1.345, .085, .058), (1.33, .135, .072), (1.30, .165 if not m else .185, .084), (1.25, .162, .098), (1.16, .152, .104),
              (1.06, .142, .096), (.98, .134, .094), (.88, .160, .110)]
    if hem < .8:
        levels += [(.72, .175, .125), (.58, .188, .135), (hem, .200, .145)]
    else:
        levels += [(hem, .168, .114)]
    rings, centres = [], []
    for z, rx, ry in levels:
        def lapel(a, x, y, z=z):
            f = max(0.0, -math.cos(a))
            # The shell follows the chest it covers.
            if not m and 1.0 < z < 1.27:
                y -= .022 * s * f ** 3
            # Open fronts fold back a little at the chest so the shirt shows.
            if open_front and z > 1.0:
                return x * (1 + .05 * f), y - .006 * f
            return x, y
        cz = (0, .004, z * s)
        rings.append(ring(cz, rx * s * wide, ry * s * wide, 12, lapel))
        centres.append(cz)
    rings.reverse(); centres.reverse()
    skip = (lambda k: k == 6) if open_front else None
    ids = P.loft(rings, centres, "jacket", skip=skip, v0=0, v1=1)
    if open_front:
        # Inner face of the opening edges so the shell does not look paper-thin from the front.
        pass
    if jacket["pattern"] == "hivis":
        # Shoulder straps over the vest's open top.
        pass

    # Accessories on the body.
    acc = c["accessories"]
    if "lanyard" in acc:
        strap = [Vector((-.03, -.035, 1.36 * s)), Vector((-.05, -.085, 1.24 * s)), Vector((0, -.112, 1.11 * s)),
                 Vector((.05, -.085, 1.24 * s)), Vector((.03, -.035, 1.36 * s))]
        for a, b in zip(strap[:-1], strap[1:]):
            nrm = Vector((0, -1, 0))
            w = (b - a).cross(nrm).normalized() * .006
            ids = [P.v(a - w), P.v(a + w), P.v(b + w), P.v(b - w)]
            P.double(ids, [swatch_uv("strap")] * 4)
        card = [Vector((-.026, -.118, 1.11 * s)), Vector((.026, -.118, 1.11 * s)), Vector((.026, -.122, 1.035 * s)), Vector((-.026, -.122, 1.035 * s))]
        P.double([P.v(p) for p in card], [swatch_uv("white")] * 4)
    if "headphones" in acc:
        pts = ring((0, .01, 1.36 * s), .07 * s, .065 * s, 10)
        ids = [P.v(p) for p in pts]
        ids2 = [P.v(p + Vector((0, 0, .022))) for p in pts]
        for k in range(10):
            k2 = (k + 1) % 10
            P.double([ids[k], ids[k2], ids2[k2], ids2[k]], [swatch_uv("black")] * 4)
        for sx in (-1, 1):
            cp = Vector((sx * .07 * s, -.03, 1.35 * s))
            P.loft([ring_axis(cp, (sx, 0, 0), .028, .028, 8), ring_axis(cp + Vector((sx * .02, 0, 0)), (sx, 0, 0), .028, .028, 8)],
                   [cp, cp + Vector((sx * .02, 0, 0))], "swatch", cap0=True, cap1=True, uv=lambda t, u, v: swatch_uv("darkmetal"))


# ------------------------------------------------------------------------------------------------------------- head
def head_frame(c):
    s = c["height"] / 1.60
    pivot = Vector((0, .006, 1.42 * s))
    centre = Vector((0, .004, 1.525 * s))
    return s, pivot, centre


HEAD = 1.18   # anime head scale over the body's proportions


def face_uv(c, p, centre, sh):
    """Planar front projection into the top-left frame of the face sheet (GL: u in [0,.5], v in [.5,1])."""
    u = ((p.x - centre.x) / sh + S.FACE_HW) / (2 * S.FACE_HW)
    v = ((p.z - centre.z) / sh - S.FACE_Z0) / (S.FACE_Z1 - S.FACE_Z0)
    u = min(max(u, .004), .996)
    v = min(max(v, .004), .996)
    return (u * .5, .5 + v * .5)


def build_head(name, c, H):
    s0, pivot, centre = head_frame(c)
    s = s0 * HEAD
    centre = centre + Vector((0, 0, -.004))
    rx, ry, rz = .080 * s, .090 * s, .108 * s
    lat, lon = 9, 14
    grid = []
    for i in range(lat + 1):
        phi = math.pi * i / lat              # 0 top .. pi bottom
        row = []
        for k in range(lon):
            a = 2 * math.pi * k / lon        # 0 back, pi front
            x = rx * math.sin(phi) * math.sin(a)
            y = ry * math.sin(phi) * math.cos(a)
            z = rz * math.cos(phi)
            # Narrow toward a pointed chin, flatten the face, push the jaw forward.
            if z < -.02 * s:
                t = min(1.0, (-.02 * s - z) / (rz - .02 * s))
                x *= lerp(1.0, .42, t ** 1.3)
                if y < 0:
                    y = lerp(y, -.060 * s, t * .6)
                else:
                    y *= lerp(1.0, .55, t)
            if y < -.072 * s:
                y = -.072 * s + (y + .072 * s) * .35
            row.append(centre + Vector((x, y, z)))
        grid.append(row)
    top = H.v(grid[0][0])
    bottom = H.v(grid[-1][0])
    ids = [[H.v(p) for p in row] for row in grid[1:-1]]
    for i in range(len(ids) - 1):
        for k in range(lon):
            k2 = (k + 1) % lon
            quad = [ids[i][k], ids[i][k2], ids[i + 1][k2], ids[i + 1][k]]
            pts = [H.verts[q] for q in quad]
            cen = sum(pts, Vector()) / 4
            if cen.y < centre.y - .028 * s and cen.z < centre.z + .09 * s:
                uvs = [face_uv(c, p, centre, s) for p in pts]
                H.face(quad, uvs, 1, outward=centre)
            else:
                uvs = [tile_uv("skin", k / lon, i / lat), tile_uv("skin", (k + 1) / lon, i / lat),
                       tile_uv("skin", (k + 1) / lon, (i + 1) / lat), tile_uv("skin", k / lon, (i + 1) / lat)]
                H.face(quad, uvs, 0, outward=centre)
    for k in range(lon):
        k2 = (k + 1) % lon
        H.face([top, ids[0][k], ids[0][k2]], [tile_uv("skin", .5, 0)] * 3, 0, outward=centre)
        tri = [bottom, ids[-1][k2], ids[-1][k]]
        pts = [H.verts[q] for q in tri]
        cen = sum(pts, Vector()) / 3
        if cen.y < centre.y - .02 * s:
            H.face(tri, [face_uv(c, p, centre, s) for p in pts], 1, outward=centre)
        else:
            H.face(tri, [tile_uv("skin2", .5, .5)] * 3, 0, outward=centre)
    if c["hairstyle"] == "shaved":
        for sx in (-1, 1):
            e = centre + Vector((sx * rx * .98, .01 * s, -.012 * s))
            pts = [e + Vector((0, .012, .02)), e + Vector((sx * .014, .004, .01)), e + Vector((sx * .01, .006, -.022)), e + Vector((0, -.008, -.018))]
            H.double([H.v(p) for p in pts], [tile_uv("skin2", .5, .5)] * 4)
    build_hair(c, H, centre, (rx, ry, rz), s)
    build_head_props(c, H, centre, s)
    return pivot, centre


def build_hair(c, H, centre, radii, s):
    rx, ry, rz = radii
    style = c["hairstyle"]
    k_out = 1.10 if style != "shaved" else 1.025
    hx, hy, hz = rx * k_out, ry * k_out, rz * (1.08 if style != "shaved" else 1.02)
    tile = "hair" if style != "shaved" else "hairdark"

    def hairline(a):
        # Height (relative to centre) where the cap ends, by azimuth: high at the forehead, low at the nape.
        front = (1 - math.cos(a)) / 2          # 0 back .. 1 front
        if style == "shaved":
            return lerp(-.05, .052, front ** 1.6) * s
        return lerp(-.095, .058, front ** 2.2) * s

    lon, lat = 16, 6
    cap = []
    for k in range(lon):
        a = 2 * math.pi * k / lon
        zmax = max(-0.999, min(0.999, hairline(a) / hz))
        phimax = math.acos(zmax)
        col = []
        for i in range(1, lat + 1):
            phi = phimax * i / lat
            x = hx * math.sin(phi) * math.sin(a)
            y = hy * math.sin(phi) * math.cos(a)
            z = hz * math.cos(phi)
            if z < -.02 * s and y > 0:
                y *= lerp(1.0, .85, min(1, (-.02 * s - z) / .08))
            col.append(centre + Vector((x, y, z + .006 * s)))
        cap.append(col)
    pole = H.v(centre + Vector((0, 0, hz + .006 * s)))
    ids = [[H.v(p) for p in col] for col in cap]
    for k in range(lon):
        k2 = (k + 1) % lon
        H.face([pole, ids[k][0], ids[k2][0]], [tile_uv(tile, k / lon, 0)] * 3, 0, outward=centre)
        for i in range(lat - 1):
            H.face([ids[k][i], ids[k2][i], ids[k2][i + 1], ids[k][i + 1]],
                   [tile_uv(tile, k / lon, i / lat), tile_uv(tile, (k + 1) / lon, i / lat),
                    tile_uv(tile, (k + 1) / lon, (i + 1) / lat), tile_uv(tile, k / lon, (i + 1) / lat)], 0, outward=centre)
    if style == "shaved":
        return

    def surface(a, z, push=0.0):
        """Point on the hair shell at azimuth a and height z (relative), pushed outward."""
        zz = max(-.999, min(.999, z / hz))
        phi = math.acos(zz)
        r = math.sin(phi)
        out = Vector((hx * r * math.sin(a), hy * r * math.cos(a), 0))
        return centre + Vector((out.x, out.y, z)) + Vector((math.sin(a), math.cos(a), 0)) * push

    def strand(root_a, root_z, tip, width, bend=0.0, segs=3, thick=.006):
        """Tapered double-sided card from a root on the shell to a tip point."""
        root = surface(root_a, root_z, .002)
        tipv = Vector(tip)
        side = Vector((math.cos(root_a), -math.sin(root_a), 0))
        prev = None
        for i in range(segs + 1):
            t = i / segs
            p = root.lerp(tipv, t)
            out = Vector((math.sin(root_a), math.cos(root_a), 0))
            p += out * (math.sin(t * math.pi) * (.012 + bend) * s)
            w = width * s * (1 - t) ** .8 * .5
            pair = (H.v(p - side * w), H.v(p + side * w))
            if prev:
                v0, v1 = (i - 1) / segs, t
                H.double([prev[0], prev[1], pair[1], pair[0]],
                         [tile_uv(tile, .3, v0), tile_uv(tile, .7, v0), tile_uv(tile, .7, v1), tile_uv(tile, .3, v1)])
            prev = pair

    bangs = c["bangs"]
    front = math.pi
    if bangs in ("full", "side", "messy"):
        n = 7 if bangs != "messy" else 9
        for j in range(n):
            t = j / (n - 1)
            a = front + (t - .5) * (1.55 if bangs != "messy" else 1.75)
            root_z = .07 * s
            if bangs == "full":
                tip_z = (.010 + .010 * abs(t - .5)) * s
                lean = 0
            elif bangs == "side":
                tip_z = (.0 - .03 * t) * s
                lean = .035 * t
            else:
                tip_z = (.02 + .02 * ((j * 37) % 5) / 5) * s
                lean = .01 * ((j % 3) - 1)
            base = surface(a, tip_z, .012 * s)
            tip = base + Vector((lean * s * (1 if bangs == "side" else 1), 0, 0))
            strand(a, root_z, tip, .030 if bangs != "messy" else .026, bend=.004)
    # Side locks framing the face.
    if style in ("bob", "ponytail", "short"):
        for sx in (-1, 1):
            a = front - sx * .95
            drop = {"bob": -.10, "ponytail": -.115, "short": -.06}[style]
            tip = surface(a, drop * s, .016 * s) + Vector((0, -.008 * s, 0))
            strand(a, .045 * s, tip, .030, bend=.004, segs=4)
    if style == "bob":
        # Back skirt: from the shell's lower sides/back down to the jaw, flared, jagged tips; open at the face.
        n = 18
        top, bot = [], []
        for k in range(n + 1):
            a = math.radians(-110 + 220 * k / n)       # around the back, -110..110 deg from the back centre
            z0 = hairline(a) - .005 * s
            top.append(surface(a, max(z0, -.07 * s), .003 * s))
            depth = -.105 * s - (.012 * s if k % 2 else 0)
            p = surface(a, -.06 * s, .022 * s)
            bot.append(Vector((p.x * 1.0, p.y, centre.z + depth)) + Vector((math.sin(a), math.cos(a), 0)) * .012 * s)
        tids = [H.v(p) for p in top]
        bids = [H.v(p) for p in bot]
        for k in range(n):
            H.double([tids[k], tids[k + 1], bids[k + 1], bids[k]],
                     [tile_uv(tile, k / n, .2), tile_uv(tile, (k + 1) / n, .2), tile_uv(tile, (k + 1) / n, 1), tile_uv(tile, k / n, 1)])
    if style == "ponytail":
        tie = centre + Vector((0, hy + .004 * s, -.050 * s))
        pts = [tie, tie + Vector((0, .028, -.03)) * s, tie + Vector((0, .040, -.12)) * s, tie + Vector((0, .030, -.22)) * s, tie + Vector((0, .020, -.30)) * s]
        radii_p = [.022, .034, .038, .026, .004]
        rings = [ring_axis(p, (0, .2, -1), r * s, r * s * .8, 8) for p, r in zip(pts, radii_p)]
        H.loft(rings, pts, tile, v0=.1, v1=1)
        H.loft([ring_axis(tie + Vector((0, -.004, .006)) * 1, (0, .3, -1), .020 * s, .018 * s, 8), ring_axis(tie + Vector((0, .006, -.008)), (0, .3, -1), .021 * s, .019 * s, 8)],
               [tie + Vector((0, -.004, .006)), tie + Vector((0, .006, -.008))], "swatch", uv=lambda t, u, v: swatch_uv("clip"))
    if style == "short":
        for k in range(10):
            a = math.radians(-120 + 240 * k / 9)
            base_z = hairline(a)
            tip = surface(a, base_z - (.035 + .015 * (k % 3)) * s, .03 * s)
            strand(a, base_z + .04 * s, tip, .045, bend=.006)


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

"""
边境维修站 · 工作台区域 · 美术复核对照图（同机位并排）
由 Claude 辅助编写。运行：blender -b --factory-startup --python compare_shots.py -- <参考图路径> [<改前截图目录>]
输出：Reports/Compare/*.png
  C01 游戏镜头：Blender R06 | Unity U06
  C02 近距镜头：Blender R03 | Unity U03
  C03 V3 参考 02 | Unity 近距 U03 | Unity 游戏镜头 U06
  C04–C07 正面 / 斜俯 / 侧面剖切 / 去杂物：Blender | Unity
  C08 / C09 改前 | 改后（游戏镜头 / 近距镜头，Unity）——需要传入改前截图目录
"""
import os
import sys

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "Common"))
import br_hardsurface as hs  # noqa: E402

for ch, g in {"F": ["11111", "10000", "10000", "11110", "10000", "10000", "10000"],
              "Y": ["10001", "10001", "01010", "00100", "00100", "00100", "00100"],
              "W": ["10001", "10001", "10001", "10101", "10101", "11011", "10001"]}.items():
    hs.FONT.setdefault(ch, g)   # 共用字库缺的字母（与 build_workbench_area.py 相同）

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
REF = args[0] if args else None
BEFORE = args[1] if len(args) > 1 else None
OUT = os.path.join(HERE, "Reports", "Compare")
os.makedirs(OUT, exist_ok=True)
R = os.path.join(HERE, "Renders")
U = os.path.join(HERE, "Reports", "Unity", "Screenshots")
H = 720            # 每格高度（像素）
GAP = 10
LABEL_H = 34


def load(path):
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(h, w, 4)[::-1, :, :3]          # 行 0 在顶部


def fit(a, h=H):
    sh, sw = a.shape[:2]
    w = int(round(sw * h / sh))
    ys = (np.arange(h) * sh / h).astype(int)
    xs = (np.arange(w) * sw / w).astype(int)
    return a[ys][:, xs]


def sheet(name, items):
    tiles = []
    for label, path in items:
        if path is None or not os.path.exists(path):
            print("[compare] skip missing", path)
            return
        t = fit(load(path))
        lab = np.ones((LABEL_H, t.shape[1], 3)) * 0.08
        hs.draw_text(lab, label, 10, 8, 3, np.array([0.92, 0.88, 0.76]))
        tiles.append(np.vstack([lab, t]))
    gap = np.ones((tiles[0].shape[0], GAP, 3)) * 0.02
    row = tiles[0]
    for t in tiles[1:]:
        row = np.hstack([row, gap, t])
    hs.save_image(name, np.clip(row, 0, 1), OUT)
    print("[compare]", name)


sheet("C01_game_camera_blender_vs_unity.png", [("BLENDER R06 GAME CAM", os.path.join(R, "R06_game_camera.png")),
                                                ("UNITY U06 GAME CAM", os.path.join(U, "U06_game_camera.png"))])
sheet("C02_closeup_blender_vs_unity.png", [("BLENDER R03 CLOSE-UP", os.path.join(R, "R03_player_closeup.png")),
                                           ("UNITY U03 CLOSE-UP", os.path.join(U, "U03_player_closeup.png"))])
sheet("C03_reference_vs_unity.png", [("V3 REFERENCE 02 (MOOD ONLY)", REF), ("UNITY U03 CLOSE-UP", os.path.join(U, "U03_player_closeup.png")),
                                     ("UNITY U06 GAME CAM", os.path.join(U, "U06_game_camera.png"))])
for n, (rb, ub, lab) in enumerate([("R01_front.png", "U01_front.png", "FRONT"), ("R02_oblique_top.png", "U02_oblique_top.png", "OBLIQUE"),
                                   ("R04_side_cutaway.png", "U04_side_cutaway.png", "SIDE CUTAWAY"),
                                   ("R05_clutter_hidden_work_area.png", "U05_clutter_hidden_work_area.png", "CLUTTER HIDDEN")]):
    sheet(f"C0{4 + n}_{lab.lower().replace(' ', '_')}_blender_vs_unity.png",
          [(f"BLENDER {lab}", os.path.join(R, rb)), (f"UNITY {lab}", os.path.join(U, ub))])
if BEFORE:
    sheet("C08_before_after_game_camera.png", [("BEFORE U06", os.path.join(BEFORE, "U06_game_camera.png")),
                                               ("AFTER U06", os.path.join(U, "U06_game_camera.png"))])
    sheet("C09_before_after_closeup.png", [("BEFORE U03", os.path.join(BEFORE, "U03_player_closeup.png")),
                                           ("AFTER U03", os.path.join(U, "U03_player_closeup.png"))])

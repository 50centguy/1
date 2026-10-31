"""Shared spec for the clinic NPCs (V2): who they are, their proportions, outfits and the texture atlas layout.

V2 over V1: ~6.5-head anime proportions with real shoulders, hips, knees and hands; a shaped anime head (cheekbones,
narrow jaw, small nose) instead of an egg; hair built from solid tapered clumps instead of paper cards; a larger, more
detailed painted face; and Luo (罗亚), the surgery patient.

Read by paint_npcs.py (system Python + Pillow: paints the textures) and build_npcs.py (Blender: builds the meshes,
maps UVs into the same tiles, exports one FBX per character). Units: metres, Blender axes (Z up, character faces -Y).

Style follows the reference library's low-poly anime sheets (#256fes, "Let's LowPoly", eye-blink tips): a few hundred
faces per part, everything that reads as detail is painted, the face is a separate texture sheet with 2x2 frames
(eyes open / closed x mouth closed / open) chosen at runtime by UV offset.
"""

ATLAS = 512          # body atlas size (px)
FACE = 512           # face sheet: 2 x 2 frames of 256 px

# Body atlas tiles: name -> (x, y, w, h) in px, origin top-left.
TILES = {
    "skin":    (0, 0, 64, 64),
    "hair":    (64, 0, 128, 128),
    "top":     (192, 0, 128, 128),
    "jacket":  (320, 0, 128, 128),
    "sleeve":  (448, 0, 64, 128),
    "bottom":  (0, 128, 128, 128),
    "legwear": (128, 128, 64, 128),
    "shoes":   (192, 128, 64, 64),
    "hands":   (256, 128, 64, 64),
    "cyber":   (320, 128, 64, 128),
    "swatch":  (384, 128, 128, 128),   # 8 x 8 flat swatches of 16 px for small props
    "skin2":   (0, 64, 64, 64),        # skin seen in shade (soles of hands, under chin)
    "hairdark": (192, 128 + 64, 64, 64),
}
SWATCH = 16

# Face projection window (head-local, metres): u spans x in [-FACE_HW, FACE_HW], v spans z in [FACE_Z0, FACE_Z1].
FACE_HW = 0.094
FACE_Z0, FACE_Z1 = -0.135, 0.105   # relative to head centre (V2 head: chin at about -0.129)

CHARACTERS = {
    # Landlady of the stall block. Ash-violet bob, half-rim glasses, haori-like jacket over a turtleneck, cigarette.
    "Kasumi": dict(
        display="霞", role="房东",
        height=1.60, build="slim", sex="f", age="mid",
        skin=(232, 196, 178), hair=(118, 104, 134), hair_hi=(176, 160, 192), eyes=(120, 70, 140),
        hairstyle="bob", bangs="side",
        top=dict(color=(62, 58, 66), detail="turtleneck"),
        jacket=dict(color=(124, 56, 60), pattern="haori", length="hip", sleeves=True),
        bottom=dict(kind="pants", color=(48, 44, 50)),
        legwear=(40, 38, 44), shoes=(30, 28, 30),
        accessories=["glasses_half", "cigarette", "earring"],
        cyber=None, mouth="smirk", brows="flat", lashes="mature",
    ),
    # Night nurse at the block care station. Salmon-pink low ponytail, scrubs, black prosthetic left forearm.
    "Tobi": dict(
        display="鳶", role="夜班护理员",
        height=1.56, build="slim", sex="f", age="young",
        skin=(240, 210, 192), hair=(224, 128, 120), hair_hi=(250, 196, 180), eyes=(96, 140, 120),
        hairstyle="ponytail", bangs="full",
        top=dict(color=(148, 196, 186), detail="scrubs"),
        jacket=dict(color=(214, 214, 206), pattern="cardigan", length="hip", sleeves=True, sleeve_len="elbow"),
        bottom=dict(kind="pants", color=(132, 182, 172)),
        legwear=(220, 220, 214), shoes=(236, 236, 230),
        accessories=["lanyard", "hairclip"],
        cyber="arm_l", mouth="soft", brows="worried", lashes="young",
    ),
    # Second-hand cyberware dealer, also runs the quayside parts line. Shaved head, grey beard, goggles, work coat.
    "Gen": dict(
        display="源", role="二手义体商",
        height=1.68, build="stocky", sex="m", age="old",
        skin=(206, 160, 128), hair=(150, 150, 146), hair_hi=(200, 200, 194), eyes=(70, 60, 50),
        hairstyle="shaved", bangs="none",
        top=dict(color=(84, 92, 70), detail="tank"),
        jacket=dict(color=(74, 66, 54), pattern="workcoat", length="knee", sleeves=True),
        bottom=dict(kind="pants", color=(56, 58, 52)),
        legwear=(56, 58, 52), shoes=(46, 36, 28),
        accessories=["goggles", "beard"],
        cyber="eye_r", mouth="flat", brows="heavy", lashes="none",
    ),
    # Night line patrol inspector for the port conduits. Short messy black hair, hard hat with lamp, hi-vis vest.
    "Rin": dict(
        display="凛", role="夜班管线巡检员",
        height=1.62, build="slim", sex="f", age="young",
        skin=(226, 190, 164), hair=(36, 34, 44), hair_hi=(92, 90, 120), eyes=(150, 100, 50),
        hairstyle="short", bangs="messy",
        top=dict(color=(60, 66, 74), detail="hoodie"),
        jacket=dict(color=(236, 150, 40), pattern="hivis", length="hip", sleeves=False),
        bottom=dict(kind="pants", color=(52, 56, 64)),
        legwear=(52, 56, 64), shoes=(64, 56, 46),
        accessories=["hardhat", "headphones"],
        cyber=None, mouth="soft", brows="flat", lashes="young",
    ),
    # Dock loader, crane crew 3, laid off when the port switched to automatic cranes; his leased cyber right arm was
    # remotely locked when the contract ran out (the cartridge's LUO.Y). Cropped hair, stubble, port work jacket over
    # a tee, cargo trousers, safety boots, a towel round the neck. The surgery patient.
    "Luo": dict(
        display="罗亚", role="港区装卸工",
        height=1.74, build="stocky", sex="m", age="mid",
        skin=(214, 170, 138), hair=(42, 38, 36), hair_hi=(96, 88, 82), eyes=(74, 58, 46),
        hairstyle="crop", bangs="none",
        top=dict(color=(150, 150, 142), detail="tee"),
        jacket=dict(color=(46, 62, 92), pattern="dockjacket", length="hip", sleeves=True, sleeve_len="elbow"),
        bottom=dict(kind="pants", color=(88, 90, 74)),
        legwear=(88, 90, 74), shoes=(70, 52, 36),
        accessories=["towel", "stubble"],
        cyber="arm_r", mouth="flat", brows="heavy", lashes="none",
    ),
}

# Swatch slots (index in the 8x8 grid) used by props.
SW = dict(black=0, white=1, metal=2, darkmetal=3, red_led=4, amber_led=5, glass=6, ember=7,
          yellow=8, reflective=9, gold=10, strap=11, lens=12, beard=13, cig=14, clip=15)

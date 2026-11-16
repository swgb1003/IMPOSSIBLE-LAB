# IMPOSSIBLE LAB — 研究室の質感の画像を作る（自作。乱数の種を固定しているので何度でも同じ画像になる）
# 実行：python -I ArtSource/textures/make_lab_textures.py
# 出力：Assets/_ImpossibleLab/Art/Textures/Lab/
#   brushed_*  … 金属のヘアライン（横方向の細い筋）。法線と、色の画像（RGB=白、A=つやの割合）
#   plastic_*  … 樹脂の細かなつやむら（法線はごく弱く）
#   plaster_*  … 壁：Poly Haven white_plaster_02（CC0）の凹凸とつやむら。色は白に近づけた版
import os
import numpy as np
from PIL import Image, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "Assets", "_ImpossibleLab", "Art", "Textures", "Lab")
PH = os.path.join(ROOT, "Assets", "_ImpossibleLab", "Art", "Textures", "PolyHaven", "white_plaster_02")
os.makedirs(OUT, exist_ok=True)
N = 512
rng = np.random.default_rng(20261116)


def tile_blur(a, rx, ry):
    """くり返しても継ぎ目が出ないよう、端を回り込ませてぼかす（箱型の平均）。"""
    out = a.copy()
    for _ in range(2):
        if rx > 0:
            out = sum(np.roll(out, k, axis=1) for k in range(-rx, rx + 1)) / (2 * rx + 1)
        if ry > 0:
            out = sum(np.roll(out, k, axis=0) for k in range(-ry, ry + 1)) / (2 * ry + 1)
    return out


def normalize(a):
    a = a - a.min()
    return a / max(1e-6, a.max())


def normal_map(h, strength):
    """高さから法線（OpenGL 形式、くり返し対応）。"""
    dx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * strength
    dy = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * strength
    n = np.stack([-dx, dy, np.ones_like(h)], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return Image.fromarray(((n * 0.5 + 0.5) * 255).astype(np.uint8), "RGB")


def base(rgb, smooth):
    """URP Lit の色の画像。A につやの割合を入れ、マテリアルの Smoothness と掛け合わせる（Smoothness の元＝色の画像の A）。
    金属度はマテリアルの値をそのまま使える（金属度の画像を使わない）。"""
    s = (np.clip(smooth, 0, 1) * 255).astype(np.uint8)
    c = (np.clip(rgb, 0, 1) * 255).astype(np.uint8)
    if c.ndim == 2:
        c = np.stack([c, c, c], axis=-1)
    return Image.fromarray(np.dstack([c, s]), "RGBA")


# ---- 金属のヘアライン ----
noise = rng.random((N, N))
streak = normalize(tile_blur(noise, 48, 0))               # 横に長くのばした筋
fine = normalize(tile_blur(rng.random((N, N)), 2, 1))     # 細かなざらつき
h = 0.75 * streak + 0.25 * fine
normal_map(h, 1.6).save(os.path.join(OUT, "brushed_nor_gl.png"))
base(np.ones((N, N)), 0.75 + 0.25 * (streak - 0.5) + 0.05 * (fine - 0.5)).save(os.path.join(OUT, "brushed_base.png"))

# ---- 樹脂の細かなつやむら ----
blot = normalize(tile_blur(rng.random((N, N)), 24, 24))
grain = normalize(tile_blur(rng.random((N, N)), 1, 1))
normal_map(0.6 * blot + 0.4 * grain, 0.35).save(os.path.join(OUT, "plastic_nor_gl.png"))
base(np.ones((N, N)), 0.88 + 0.14 * (blot - 0.5) + 0.05 * (grain - 0.5)).save(os.path.join(OUT, "plastic_base.png"))

# ---- 壁（Poly Haven white_plaster_02、CC0）----
diff = Image.open(os.path.join(PH, "white_plaster_02_diff_1k.jpg")).convert("L")
d = np.asarray(diff, dtype=np.float32) / 255.0
d = 0.93 + 0.10 * (d - d.mean())                          # 色はほぼ白、むらだけ薄く残す
Image.open(os.path.join(PH, "white_plaster_02_nor_gl_1k.jpg")).save(os.path.join(OUT, "plaster_nor_gl.jpg"), quality=95)
r = np.asarray(Image.open(os.path.join(PH, "white_plaster_02_rough_1k.jpg")).convert("L"), dtype=np.float32) / 255.0
base(d, 0.55 + 0.45 * (1.0 - r)).save(os.path.join(OUT, "plaster_base.png"))
print("written", sorted(os.listdir(OUT)))

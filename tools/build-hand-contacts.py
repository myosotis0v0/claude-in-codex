"""Regenerate both contact poses without resampling the cuff or neighboring fingers."""
from pathlib import Path
import numpy as np
from PIL import Image
from scipy.ndimage import map_coordinates

layers = Path(__file__).resolve().parents[1] / "plugins/claude-in-codex/assets/motion/layers"
for name, center in [("keyboard", 925), ("mouse", 323)]:
    source = Image.open(layers / (name + "-paw.png")).convert("RGBA")
    original = np.asarray(source)
    result = original.copy()
    y, x = np.indices(original.shape[:2], dtype=float)
    width = np.clip((27 - abs(x-center)) / 10, 0, 1)
    width = width*width*(3-2*width)
    v = y.copy()
    for _ in range(20):
        t = np.clip((v-1194)/36, 0, 1)
        v = y - 17*width*t*t*(3-2*t)
    moved = (v < y) & (y <= 1249)
    premul = np.asarray(source.convert("RGBa"))
    sampled = np.stack([map_coordinates(premul[:,:,i], [v[moved], x[moved]], order=1)
                        for i in range(4)], axis=-1)
    result[moved] = np.asarray(Image.fromarray(sampled[None,:,:], "RGBa").convert("RGBA"))[0]
    dst = layers / (name + "-paw-contact.png")
    Image.fromarray(result).save(dst, optimize=True)
    changed = np.any(np.asarray(Image.open(dst)) != original, axis=2)
    assert changed.any() and not changed[(abs(x-center)>=27)|(y<=1194)].any()
    assert Image.open(dst).getbbox()[3] >= source.getbbox()[3]+15
    print(name, int(changed.sum()), "changed pixels; cuff and side fingers unchanged")

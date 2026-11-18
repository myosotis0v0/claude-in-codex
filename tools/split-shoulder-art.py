"""Recover the fixed chest ornament and two hands for the separate cape layer."""
from pathlib import Path
import numpy as np
from PIL import Image
from scipy import ndimage as nd

root = Path(__file__).resolve().parents[1]
layers = root / "plugins/claude-in-codex/assets/motion/layers"
base = np.array(Image.open(root / "assets/claude-v2/base.png").convert("RGBA"))
paws = np.array(Image.open(layers / "clothes-paws.png").convert("RGBA"))
matte = np.array(Image.open(root / "assets/claude-motion-v4/chest-matte.png").convert("RGBA"))
h, w = matte.shape[:2]
y, x = np.indices((h, w))
r, g, b, a = base[:h].astype(int).transpose(2, 0, 1)
inside = matte[:, :, 3] > 5
near = nd.distance_transform_edt(~inside)
not_hair = ~((r > 1.6*b) & (r-g > 38) & (r > 125))
mask = ((near < 10) | ((near < 30) & (x > 503) & (x < 742) & (y < 1085) & (y > 1050))) & (a > 0) & not_hair
mask = (nd.binary_fill_holes(mask) | inside) & ((near <= 1.5) | ((x > 454) & (x < 790))) & (a > 0)

def save(name, pixels):
    frame = Image.new("RGBA", (w, paws.shape[0]))
    pixels[pixels[:, :, 3] == 0] = 0
    frame.paste(Image.fromarray(pixels), (0, 0))
    frame.save(layers / (name + ".png"), optimize=True)

chest = base[:h].copy()
chest[:, :, 3] *= mask
chest[mask & (a >= 250), 3] = 255
chest = np.array(Image.alpha_composite(Image.fromarray(matte), Image.fromarray(chest)))
# The original single sprite joins the jaw ink to the collar. Keep the
# collar lobes but not the stray horizontal pieces of the moving jaw.
half_x = np.minimum(x, 1244-x)
collar_top = np.interp(half_x,[502,513,521,532,546,558,572,589,598,610,622],
                       [1080,1076,1068,1061,1056,1055,1057,1064,1060,1058,1057])
chest[:,:,3] = np.rint(chest[:,:,3] * np.clip(y-collar_top+1,0,1))
save("chest-ornament", chest)
for name, half in [("mouse-paw", x < w//2), ("keyboard-paw", x >= w//2)]:
    hand = paws[:h].copy()
    hand[:, :, 3] *= half & (matte[:, :, 3] == 0)
    save(name, hand)

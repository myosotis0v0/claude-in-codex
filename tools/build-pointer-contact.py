"""Apply the approved fingertip displacement in opposite hand coordinates."""
from pathlib import Path
import numpy as np
from PIL import Image
from scipy.ndimage import map_coordinates

layers = Path(__file__).resolve().parents[1] / "plugins/claude-in-codex/assets/motion/layers"
paw = Image.open(layers / "mouse-paw.png").convert("RGBa")
y, x = np.indices((paw.height, paw.width), dtype=float)
u, v = x.copy(), y.copy()
for _ in range(12):
    w = np.clip((v-1162)/62, 0, 1)
    w = w*w*(3-2*w)
    dx = 4*np.clip((u-315)/55, -1, 1)*w
    dy = (4+14*np.exp(-((u-316)/40)**4))*w
    u, v = x-dx, y-dy
channels = [map_coordinates(np.asarray(paw)[:,:,i], [v,u], order=1, mode="constant") for i in range(4)]
Image.fromarray(np.stack(channels, axis=-1), "RGBa").convert("RGBA").save(layers / "mouse-paw-contact.png", optimize=True)

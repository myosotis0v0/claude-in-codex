"""Package and check the fixed-camera, 512px menu-pitch comparison."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[2]
out = root / "previews/menu-pitch-volume"
out.mkdir(parents=True, exist_ok=True)
frames = []
font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 16)
for i in range(72):
    with Image.open(root / f"artifacts/menu-pitch-volume/{i:03}.png") as src:
        assert src.size == (1024, 512)
        # Central jaw-to-collar corridor, excluding the orange flower below it.
        for r, g, b, a in src.crop((759, 397, 774, 421)).getdata():
            assert a >= 245 and not (r-g > 82 and g-b > 34), f"neck gap: frame {i}"
        pet = src.resize((640, 320), Image.Resampling.LANCZOS)
    board = Image.new("RGB", (640, 355), "#202022")
    board.paste(pet, (0, 35), pet)
    d = ImageDraw.Draw(board)
    d.text((12, 6), "上一版  /  近颈支点", font=font, fill="#eee3d7")
    d.text((332, 6), "新版  /  整头透视", font=font, fill="#ffa661")
    frames.append(board)
palette = frames[24].quantize(colors=256)
seq = [f.quantize(palette=palette, dither=Image.Dither.NONE) for f in frames]
path = out / "menu-pitch.gif"
seq[0].save(path, save_all=True, append_images=seq[1:], duration=[30, 40, 30]*24,
            disposal=1, loop=0)
with Image.open(path) as gif:
    total = 0
    for i in range(gif.n_frames):
        gif.seek(i)
        total += gif.info["duration"]
    assert total == 2400

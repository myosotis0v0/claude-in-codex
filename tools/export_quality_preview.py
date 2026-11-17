"""Assemble same-pose sampling diagnostics, without changing the character artwork."""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "artifacts/quality-0.4.1"
OUT = ROOT / "previews"
LABEL = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 14)
SMALL = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 11)
BACKGROUND = "#202022"


def board(before, after):
    image = Image.new("RGB", (520, 516), BACKGROUND)
    draw = ImageDraw.Draw(image)
    draw.text((20, 12), "BEFORE / 1x LINEAR", font=LABEL, fill="#bdbdc3")
    draw.text((280, 12), "AFTER / 2x HIGH QUALITY", font=LABEL, fill="#ff9b4d")
    for x, source in ((24, before), (284, after)):
        image.paste(source, (x, 42), source)
        crop = source.crop((38, 32, 94, 88)).resize((224, 224), Image.Resampling.NEAREST)
        image.paste(crop, (x, 268), crop)
    draw.text((24, 498), "Same pose and physical size. Hair detail shown at 4x nearest zoom.", font=SMALL, fill="#bdbdc3")
    return image


def main():
    OUT.mkdir(exist_ok=True)
    frames = []
    for i in range(90):
        with Image.open(SOURCE / "before" / f"{i:03}.png") as a, Image.open(SOURCE / "after" / f"{i:03}.png") as b:
            frames.append(board(a.convert("RGBA"), b.convert("RGBA")))
    frames[45].save(OUT / "quality-before-after.png", optimize=True)
    samples = Image.new("RGB", (520 * 3, 516 * 3))
    for i in range(9):
        samples.paste(frames[i * 10], ((i % 3) * 520, (i // 3) * 516))
    palette = samples.quantize(colors=256)
    indexed = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames]
    indexed[0].save(OUT / "quality-before-after.gif", save_all=True, append_images=indexed[1:],
                    duration=[30, 30, 40] * 30, loop=0, optimize=True, disposal=1)
    with Image.open(OUT / "quality-before-after.gif") as gif:
        total = 0
        for i in range(gif.n_frames):
            gif.seek(i)
            total += gif.info["duration"]
        assert total == 3000
    print(OUT / "quality-before-after.gif")


if __name__ == "__main__":
    main()

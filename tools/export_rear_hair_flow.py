"""Package paired offscreen renders; never modify source artwork."""
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
RAW = ROOT / "artifacts/rear-hair-flow"
OUT = ROOT / "previews/rear-hair-flow"
FONT = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 17)
SMALL = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 13)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    frames = []
    for i in range(180):
        board = Image.new("RGB", (720, 616), "#202022")
        draw = ImageDraw.Draw(board)
        for x, name, label in [(16, "before", "调整前：前后发混在同一部件"), (368, "after", "调整后：后发独立横向飘动")]:
            draw.text((x + 4, 10), label, font=FONT, fill="#eeeeee")
            image = Image.open(RAW / "frames" / name / f"{i:03}.png").convert("RGBA")
            board.paste(image, (x, 40), image)
            crop = image.crop((16, 224, 128, 320)).resize((224, 192), Image.Resampling.LANCZOS)
            board.paste(crop, (x + 48, 400), crop)
        draw.text((20, 375), "同一默认微风输入 / 下方为后发局部放大 2x", font=SMALL, fill="#ff9b4d")
        frames.append(board)
    samples = Image.new("RGB", (720 * 3, 616 * 3))
    for i in range(9):
        samples.paste(frames[i * 20], ((i % 3) * 720, (i // 3) * 616))
    palette = samples.quantize(colors=256)
    indexed = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames]
    target = OUT / "rear-hair-before-after.gif"
    indexed[0].save(target, save_all=True, append_images=indexed[1:], duration=[30, 30, 40] * 60, loop=0, disposal=1, optimize=True)
    with Image.open(target) as decoded:
        duration = 0
        for i in range(decoded.n_frames):
            decoded.seek(i)
            duration += decoded.info["duration"]
        assert duration == 6000
        count = decoded.n_frames
    frames[45].save(OUT / "rear-hair-before-after.png", optimize=True)
    parts = Image.new("RGB", (1000, 306), "#202022")
    draw = ImageDraw.Draw(parts)
    for i, (name, title) in enumerate([("hair-front-left", "左前发"), ("hair-front-center", "中前发"), ("hair-front-right", "右前发"), ("hair-curtain", "重新归属的后发")]):
        image = Image.open(ROOT / "assets/claude-motion-v3/layers" / f"{name}.png").convert("RGBA")
        image = image.resize((240, 240), Image.Resampling.LANCZOS)
        parts.paste(image, (i * 250 + 5, 14), image)
        draw.text((i * 250 + 20, 270), title, font=FONT, fill="#eeeeee")
    parts.save(OUT / "corrected-parts.png", optimize=True)
    (OUT / "preview-manifest.json").write_text(json.dumps({"SourceFrames": 180, "Fps": 30, "Milliseconds": duration,
        "DecodedGifFrames": count, "Input": "Actual default ambient amplitude; same front poses, spring states and frame times on both sides; no amplified test wind."}, indent=2), encoding="utf-8")
    print(f"Rear preview: {count} decoded frames, {duration} ms")


if __name__ == "__main__":
    main()

"""Package offscreen head-attachment comparisons, without editing source artwork."""
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
RAW = ROOT / "artifacts/rear-hair-attachment"
OUT = ROOT / "previews/rear-hair-attachment"
FONT = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 17)
SMALL = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 13)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    frames = []
    for i in range(180):
        board = Image.new("RGB", (720, 800), "#202022")
        draw = ImageDraw.Draw(board)
        for x, title in [(20, "调整前：后发下端不随头部"), (372, "调整后：整体随头 + 局部风摆")]:
            draw.text((x, 10), title, font=FONT, fill="#eeeeee")
        for row, (state, label) in enumerate([("idle", "真实待机幅度"), ("shake", "原有摇头动作 / 非放大参数")]):
            y = 44 + row * 376
            draw.text((24, y), label, font=SMALL, fill="#ff9b4d")
            for x, name in [(16, "before"), (368, "after")]:
                image = Image.open(RAW / "frames" / name / state / f"{i:03}.png").convert("RGBA")
                board.paste(image, (x, y + 20), image)
        frames.append(board)
    sample = Image.new("RGB", (2160, 2400))
    for i in range(9):
        sample.paste(frames[i * 20], ((i % 3) * 720, (i // 3) * 800))
    palette = sample.quantize(colors=256)
    indexed = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames]
    target = OUT / "head-follow-before-after.gif"
    indexed[0].save(target, save_all=True, append_images=indexed[1:], duration=[30, 30, 40] * 60, loop=0, disposal=1, optimize=True)
    with Image.open(target) as gif:
        count = gif.n_frames
        duration = 0
        for i in range(count):
            gif.seek(i)
            duration += gif.info["duration"]
    assert count == 180 and duration == 6000
    frames[55].save(OUT / "head-follow-before-after.png")
    board = Image.new("RGB", (720, 330), "#202022")
    draw = ImageDraw.Draw(board)
    for x, name, label in [(20, "before", "调整前：旧脸线残留在前发"), (372, "after", "调整后：保留发梢，清理残线")]:
        image = Image.open(RAW / "frames" / name / "shake/055.png").convert("RGBA")
        crop = image.crop((210, 220, 280, 290)).resize((280, 280), Image.Resampling.LANCZOS)
        board.paste(crop, (x + 20, 44), crop)
        draw.text((x, 12), label, font=FONT, fill="#eeeeee")
    board.save(OUT / "right-cheek-before-after.png")
    (OUT / "manifest.json").write_text(json.dumps({"Frames": count, "DurationMs": duration, "Fps": 30,
        "Input": "Existing idle and shake motion amplitudes; same spring inputs. Baseline rendered using the unmodified 0.5.2 code before changing attachment. No native UI proof."}, indent=2), encoding="utf-8")
    print(f"Attachment preview: {count} frames, {duration} ms")


if __name__ == "__main__":
    main()

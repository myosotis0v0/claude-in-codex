"""Compare offscreen menu renders, validate protected poses and package local-only previews."""
import argparse
import json
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
BEFORE = ROOT / "artifacts/menu-eye-roll/before"
AFTER = ROOT / "artifacts/menu-eye-roll/after"
OUT = ROOT / "previews/menu-eye-roll"
FONT = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 17)
SMALL = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 13)


def orange_count(image):
    total = 0
    for region in [(83, 154, 112, 181), (144, 154, 174, 181)]:
        crop = image.crop(region)
        for y in range(crop.height):
            for x in range(crop.width):
                r, g, b, a = crop.getpixel((x, y))
                total += a > 230 and r > 180 and 75 < g < 185 and b < 100
    return total


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--before", type=Path, default=BEFORE)
    parser.add_argument("--after", type=Path, default=AFTER)
    parser.add_argument("--output", type=Path, default=OUT)
    parser.add_argument("--reduced", action="store_true", help="Compare the former large roll with the reduced version.")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    before = json.loads((args.before / "menu-poses.json").read_text(encoding="utf-8"))
    after = json.loads((args.after / "menu-poses.json").read_text(encoding="utf-8"))
    frames = []
    changed = 0
    for i, (old, new) in enumerate(zip(before, after, strict=True)):
        old.setdefault("EyeRoll", 0)
        assert {k: v for k, v in old.items() if k != "EyeRoll"} == {k: v for k, v in new.items() if k != "EyeRoll"}
        a = Image.open(args.before / "menu" / f"{i:03}.png").convert("RGBA")
        b = Image.open(args.after / "menu" / f"{i:03}.png").convert("RGBA")
        diff = ImageChops.difference(a, b)
        bbox = diff.convert("RGB").getbbox()
        if bbox:
            assert 46 <= bbox[0] and bbox[2] <= 208 and 132 <= bbox[1] and bbox[3] <= 188, (i, bbox)
            changed += 1
        if i == 30:
            before_orange, after_orange = orange_count(a), orange_count(b)
            if args.reduced:
                assert before_orange > 10 and before_orange * 2 < after_orange < 450, (before_orange, after_orange)
            else:
                assert before_orange > 100 and before_orange * .25 < after_orange < before_orange * .65, (before_orange, after_orange)
        board = Image.new("RGB", (720, 440), "#202022")
        draw = ImageDraw.Draw(board)
        titles = ["上一版：幅度较大", "调整后：减少约四成"] if args.reduced else ["原版：轻微上看", "调整：克制地向上翻眼"]
        for x, image, title in [(20, a, titles[0]), (372, b, titles[1])]:
            draw.text((x, 12), title, font=FONT, fill="#eeeeee")
            board.paste(image, (x + 40, 48), image)
            crop = image.crop((42, 132, 208, 183)).resize((332, 102), Image.Resampling.LANCZOS)
            board.paste(crop, (x - 8, 316), crop)
        draw.text((24, 289), "睫毛保留小幅随动 / 眉毛、嘴型与停顿节奏不变", font=SMALL, fill="#ff9b4d")
        frames.append(board)
    sample = Image.new("RGB", (2160, 1320))
    for i in range(9):
        sample.paste(frames[i * 13], ((i % 3) * 720, (i // 3) * 440))
    palette = sample.quantize(colors=256)
    indexed = [f.quantize(palette=palette, dither=Image.Dither.NONE) for f in frames]
    target = args.output / "menu-before-after.gif"
    indexed[0].save(target, save_all=True, append_images=indexed[1:], duration=[30, 30, 40] * 40, loop=0, disposal=1, optimize=True)
    with Image.open(target) as gif:
        duration = 0
        for i in range(gif.n_frames):
            gif.seek(i)
            duration += gif.info["duration"]
        assert duration == 4000
    frames[30].save(args.output / "menu-before-after.png")
    (args.output / "validation.json").write_text(json.dumps({"Success": True, "Frames": len(frames), "ChangedEyeFrames": changed,
        "Fps": 30, "DurationMs": duration, "OutsideEyeChanges": 0, "OtherPoseFieldsEqual": True,
        "PeakOrangePixelsBefore": before_orange, "PeakOrangePixelsAfter": after_orange,
        "Scope": "Same existing authored menu timing, head/hair/face and cursor inputs. New iris-only upward offset under the existing lash. No sclera/lower lids added; local preview only."}, indent=2), encoding="utf-8")
    print(args.output)


if __name__ == "__main__":
    main()

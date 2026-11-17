"""Package offline MotionLab renders into review media and a self-contained local gallery."""
import html
import json
from pathlib import Path
import shutil
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "artifacts" / "motion-batch-v1"
ASSETS = ROOT / "assets" / "claude-motion-v1"
OUT = ROOT / "previews" / "motion-batch-v1"
CATALOG = json.loads((ASSETS / "motions.json").read_text(encoding="utf-8"))["Catalog"]
FONT = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 13)
SMALL = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 11)
DURATIONS = [30, 30, 40] * 60


def background(size, light=False):
    return Image.new("RGBA", size, "#f4f4f5" if light else "#202022")


def gif(frames, path):
    samples = Image.new("RGB", (frames[0].width * 4, frames[0].height * 3))
    for i in range(12):
        samples.paste(frames[i * 15].convert("RGB"), ((i % 4) * frames[0].width, (i // 4) * frames[0].height))
    palette = samples.quantize(colors=256)
    indexed = [frame.convert("RGB").quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames]
    indexed[0].save(path, save_all=True, append_images=indexed[1:], duration=DURATIONS, loop=0, optimize=True, disposal=1)
    with Image.open(path) as image:
        duration = 0
        for frame in range(image.n_frames):
            image.seek(frame)
            duration += image.info["duration"]
        assert duration == 6000, (path, duration)


def main():
    for directory in [OUT, OUT / "clips", OUT / "posters", OUT / "atlases"]:
        directory.mkdir(parents=True, exist_ok=True)
    rendered = {}
    checks = []
    media = []
    for motion in CATALOG + [{"Id": "rig-debug", "Label": "骨骼与蒙皮"}]:
        name = motion["Id"]
        paths = sorted((SOURCE / name).glob("[0-9][0-9][0-9].png"))
        assert len(paths) == 180, (name, len(paths))
        frames = [Image.open(path).convert("RGBA") for path in paths]
        assert all(frame.size == (336, 336) for frame in frames)
        assert ImageChops.difference(frames[0], frames[84]).convert("RGB").getbbox(), f"Static clip: {name}"
        atlas = Image.new("RGBA", (336 * 12, 336 * 15))
        for i, frame in enumerate(frames):
            atlas.paste(frame, ((i % 12) * 336, (i // 12) * 336))
        atlas.save(OUT / "atlases" / f"{name}.png", compress_level=3)
        frames[84].save(OUT / "posters" / f"{name}.png")
        frames[0].save(OUT / "clips" / f"{name}.webp", save_all=True, append_images=frames[1:],
                       duration=[33, 33, 34] * 60, lossless=True, loop=0, method=1)
        opaque = []
        for frame in frames:
            bg = background(frame.size)
            bg.alpha_composite(frame)
            opaque.append(bg.convert("RGB"))
        gif(opaque, OUT / "clips" / f"{name}.gif")
        # Keep smaller thumbnails in memory for the two batch overviews.
        rendered[name] = [frame.resize((176, 176), Image.Resampling.LANCZOS) for frame in frames]
        checks.append(f"{name}: 180 nonblank source frames, 336px, animated GIF duration 6000ms")
        media.append({**motion, "Frames": 180, "Fps": 30, "Seconds": 6})
        print(f"Exported {name}", flush=True)

    for page, motions in enumerate([CATALOG[:7] + [{"Id": "rig-debug", "Label": "骨骼与蒙皮"}], CATALOG[7:]], 1):
        frames = []
        for i in range(180):
            board = background((784, 464))
            draw = ImageDraw.Draw(board)
            draw.text((16, 10), f"CLAUDE / MOTION STUDIES  {page}/2", font=SMALL, fill="#eeeeee")
            draw.text((626, 10), "30 FPS / 6 SEC", font=SMALL, fill="#b7b7bd")
            for j, motion in enumerate(motions):
                x, y = 16 + (j % 4) * 192, 36 + (j // 4) * 212
                board.alpha_composite(rendered[motion["Id"]][i], (x, y))
                draw.text((x + 8, y + 180), motion["Label"], font=FONT, fill="#eeeeee")
            frames.append(board.convert("RGB"))
        gif(frames, OUT / f"overview-{page}.gif")
        frames[84].save(OUT / f"overview-{page}.png")

    parts = sorted((ASSETS / "layers").glob("*.png"))
    sheet = background((864, 32 + ((len(parts) + 3) // 4) * 208))
    draw = ImageDraw.Draw(sheet)
    draw.text((16, 8), "SEPARATED PARTS / TRUE ALPHA", font=SMALL, fill="#eeeeee")
    for i, path in enumerate(parts):
        x, y = 16 + (i % 4) * 212, 32 + (i // 4) * 208
        for yy in range(y, y + 176, 12):
            for xx in range(x, x + 192, 12):
                draw.rectangle((xx, yy, min(xx + 11, x + 191), min(yy + 11, y + 175)), fill="#38383c" if ((xx-x)//12+(yy-y)//12)%2 else "#2a2a2e")
        image = Image.open(path).convert("RGBA")
        box = image.getchannel("A").getbbox()
        assert box
        image = image.crop(box)
        image.thumbnail((180, 164), Image.Resampling.LANCZOS)
        sheet.alpha_composite(image, (x + (192-image.width)//2, y + (176-image.height)//2))
        draw.text((x, y + 180), path.stem, font=SMALL, fill="#eeeeee")
    sheet.save(OUT / "parts.png")
    shutil.copyfile(SOURCE / "report.json", OUT / "rig-validation.json")
    manifest = {"clips": media, "framesPerAtlas": 180, "cell": 336, "columns": 12,
                "checks": checks, "background": "GIFs have a dark background; WebP and atlas PNGs preserve alpha."}
    (OUT / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    template = (ROOT / "tools" / "motion_gallery.html").read_text(encoding="utf-8")
    (OUT / "index.html").write_text(template.replace("__MOTION_DATA__", json.dumps(media, ensure_ascii=False)), encoding="utf-8")
    print(f"Validated {len(media)} animations; gallery: {OUT / 'index.html'}")


if __name__ == "__main__":
    if "--gallery-only" in sys.argv:
        manifest = json.loads((OUT / "manifest.json").read_text(encoding="utf-8"))
        template = (ROOT / "tools" / "motion_gallery.html").read_text(encoding="utf-8")
        (OUT / "index.html").write_text(template.replace("__MOTION_DATA__", json.dumps(manifest["clips"], ensure_ascii=False)), encoding="utf-8")
    else:
        main()

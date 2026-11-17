"""Encode validated offscreen frames and assemble source-aligned review artifacts."""
import hashlib
import json
from pathlib import Path
import sys
import zipfile

from PIL import Image, ImageChops, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "artifacts/front-hair-v2"
ASSETS = ROOT / "assets/claude-motion-v2"
OUT = ROOT / "previews/front-hair-v2"
DURATIONS = [30, 30, 40] * 60
FONT = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 16)
SMALL = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 12)
LABELS = {
    "left-only": "左束独立检查", "center-only": "中束独立检查", "right-only": "右束独立检查",
    "wind": "三束合成微风", "idle": "自然待机", "thinking": "思考动作", "shake": "摇头响应", "pat": "摸头响应",
}


def background(size, light=False):
    return Image.new("RGBA", size, "#f4f4f5" if light else "#202022")


def gif(frames, target):
    samples = Image.new("RGB", (frames[0].width * 3, frames[0].height * 3))
    for i in range(9):
        samples.paste(frames[i * 20].convert("RGB"), ((i % 3) * frames[0].width, (i // 3) * frames[0].height))
    palette = samples.quantize(colors=256)
    indexed = [im.convert("RGB").quantize(palette=palette, dither=Image.Dither.NONE) for im in frames]
    indexed[0].save(target, save_all=True, append_images=indexed[1:], duration=DURATIONS, loop=0, optimize=True, disposal=1)
    with Image.open(target) as decoded:
        duration = 0
        for i in range(decoded.n_frames):
            decoded.seek(i)
            duration += decoded.info["duration"]
        assert duration == 6000, (target, duration)
        return {"File": target.name, "DecodedFrames": decoded.n_frames, "Milliseconds": duration}


def main():
    OUT.mkdir(exist_ok=True)
    report = json.loads((ASSETS / "deformation-report.json").read_text(encoding="utf-8"))
    registration = json.loads((ASSETS / "registration-report.json").read_text(encoding="utf-8"))
    assert report["Success"] and registration["GatePassed"]
    clips = {}
    media = []
    for name in LABELS:
        paths = sorted((SOURCE / "frames" / name).glob("[0-9][0-9][0-9].png"))
        assert len(paths) == 180
        images = [Image.open(p).convert("RGBA") for p in paths]
        assert all(im.size == (336, 336) for im in images)
        assert ImageChops.difference(images[15], images[60]).convert("RGB").getbbox(), name
        clips[name] = images
        opaque = []
        for image in images:
            board = background(image.size)
            board.alpha_composite(image)
            opaque.append(board.convert("RGB"))
        media.append(gif(opaque, OUT / f"{name}.gif"))
        images[0].save(OUT / f"{name}.webp", save_all=True, append_images=images[1:], duration=[33, 33, 34] * 60,
                       lossless=True, loop=0, method=1)
        images[45].save(OUT / f"{name}.png")

    overview = []
    for frame in range(180):
        board = background((616, 700))
        draw = ImageDraw.Draw(board)
        draw.text((18, 10), "FRONT HAIR / INDEPENDENT SKINNING / 30 FPS", font=SMALL, fill="#ff9b4d")
        for i, name in enumerate(["left-only", "center-only", "right-only", "wind"]):
            x, y = 16 + i % 2 * 300, 36 + i // 2 * 328
            image = clips[name][frame].resize((280, 280), Image.Resampling.LANCZOS)
            board.alpha_composite(image, (x, y))
            draw.text((x + 8, y + 286), LABELS[name], font=FONT, fill="#eeeeee")
        overview.append(board.convert("RGB"))
    media.append(gif(overview, OUT / "front-hair-dynamics.gif"))
    overview[45].save(OUT / "front-hair-dynamics.png", optimize=True)

    for light in (False, True):
        board = background((720, 820), light)
        draw = ImageDraw.Draw(board)
        color = "#343438" if light else "#eeeeee"
        for x, key, label in [(16, "old", "静态原稿"), (368, "new", "新分层＋保留下颌补全")]:
            draw.text((x + 8, 12), label, font=FONT, fill=color)
            image = Image.open(SOURCE / f"neutral-{key}-336.png").convert("RGBA")
            board.alpha_composite(image, (x, 40))
        draw.text((24, 390), "SAME CANVAS / SAME SCALE / SAME POSITION", font=SMALL, fill="#ef8c35")
        for x, file, label in [(16, ROOT / "assets/claude-motion-v1/layers/face.png", "原脸部补底"),
                               (368, ASSETS / "layers/face.png", "完整头型／原下颌弧度保留")]:
            draw.text((x + 8, 428), label, font=FONT, fill=color)
            face = Image.open(file).convert("RGBA").crop((180, 180, 1074, 1150))
            face = face.resize((312, 339), Image.Resampling.LANCZOS)
            board.alpha_composite(face, (x + 12, 462))
        board.convert("RGB").save(OUT / f"static-comparison-{'light' if light else 'dark'}.png", optimize=True)

    sheet = background((960, 1024))
    draw = ImageDraw.Draw(sheet)
    draw.text((20, 12), "BOUND PARTS / ACTUAL REGISTERED MATERIALS", font=SMALL, fill="#ff9b4d")
    parts = [("face", "完整脸型"), ("hair-front-left", "左束"), ("hair-front-center", "中束"),
             ("hair-front-right", "右束"), ("hair-cap", "头顶固定遮盖层"), ("hair-backing", "发束后方承托")]
    for i, (name, label) in enumerate(parts):
        x, y = 16 + i % 3 * 312, 44 + i // 3 * 476
        image = Image.open(ASSETS / "layers" / f"{name}.png").convert("RGBA")
        image = image.crop(image.getchannel("A").getbbox())
        image.thumbnail((280, 420), Image.Resampling.LANCZOS)
        sheet.alpha_composite(image, (x + (288 - image.width) // 2, y + (420 - image.height) // 2))
        draw.text((x + 8, y + 430), label, font=FONT, fill="#eeeeee")
    sheet.convert("RGB").save(OUT / "bound-parts.png", optimize=True)

    # Phase samples expose local seam defects that a whole-character GIF can hide.
    detail = background((1024, 536))
    draw = ImageDraw.Draw(detail)
    draw.text((16, 8), "LEFT / CENTER / RIGHT / WIND - PHASES 0.0 / 1.5 / 3.0 / 4.5s", font=SMALL, fill="#ff9b4d")
    for row, name in enumerate(["left-only", "center-only", "right-only", "wind"]):
        for column, frame in enumerate([0, 45, 90, 135]):
            image = clips[name][frame].crop((84, 112, 252, 184)).resize((252, 108), Image.Resampling.NEAREST)
            detail.alpha_composite(image, (column * 256, 28 + row * 124))
    detail.convert("RGB").save(OUT / "seam-phase-contact.png", optimize=True)

    package_data(media)


def package_data(media=None):
    if media is None:
        media = json.loads((OUT / "preview-manifest.json").read_text(encoding="utf-8"))["Media"]
    files = sorted(p for p in (ASSETS / "layers").glob("*.png") if p.name != "hair.png")
    payload = {p.relative_to(ASSETS).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
    for name in ["rig-v2.json", "registration-report.json", "deformation-report.json", "chin-neck-report.json", "README.md", "PROMPTS.md"]:
        payload[name] = hashlib.sha256((ASSETS / name).read_bytes()).hexdigest()
    manifest = {"Version": 2, "Profile": "CPU 2x high-quality rendering, front-angle clamps and +/-4 degree head validation",
                "SourceFrames": 180, "Fps": 30, "Seconds": 6, "Media": media, "PayloadSha256": payload,
                "Notes": "Solo clips use 1.5x wind input for inspection; combined wind uses the actual default ambient amplitude. Other clips are offline pose demonstrations, not actual Codex events. GIF may coalesce identical quantized frames while keeping exact 6000ms duration."}
    (OUT / "preview-manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    archive = OUT / "front-hair-production-v2.zip"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for name in payload:
            z.write(ASSETS / name, name)
        for path in sorted((ROOT / "plugins/claude-in-codex/src/MotionCore").glob("*.cs")):
            z.write(path, "MotionCore/" + path.name)
    with zipfile.ZipFile(archive) as z:
        assert z.testzip() is None
    print(f"Validated {len(media)} GIFs; data/code archive: {archive.stat().st_size} bytes")


if __name__ == "__main__":
    if "--package-only" in sys.argv:
        package_data()
    else:
        main()

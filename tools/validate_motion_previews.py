"""Audit delivered media against the motion catalogue and runtime-candidate file manifest."""
import hashlib
import json
from pathlib import Path
import re
import subprocess
import zipfile

from PIL import Image, ImageChops, ImageStat, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "assets" / "claude-motion-v1"
OUT = ROOT / "previews" / "motion-batch-v1"
RAW = ROOT / "artifacts" / "motion-batch-v1"


def main():
    checks = []

    def check(condition, message):
        assert condition, message
        checks.append(message)

    report = json.loads((RAW / "report.json").read_text(encoding="utf-8"))
    check(report["Success"] and len(report["Clips"]) == 14, "14 clips covered by the current rig verification report")
    media = json.loads((OUT / "manifest.json").read_text(encoding="utf-8"))
    check(len(media["clips"]) == 15, "14 requested animations plus one skeleton demonstration exported")
    measurements = []
    for clip in media["clips"]:
        name = clip["Id"]
        atlas = Image.open(OUT / "atlases" / f"{name}.png").convert("RGBA")
        check(atlas.size == (4032, 5040), f"{name}: atlas has all 180 cells")
        for i in [0, 35, 84, 120, 179]:
            cell = atlas.crop(((i % 12) * 336, (i // 12) * 336, (i % 12 + 1) * 336, (i // 12 + 1) * 336))
            raw = Image.open(RAW / name / f"{i:03}.png").convert("RGBA")
            check(all(channel.getbbox() is None for channel in ImageChops.difference(cell, raw).split()), f"{name}: atlas cell {i} equals source RGBA")
        previous = None
        first = None
        jumps = []
        for path in sorted((RAW / name).glob("[0-9][0-9][0-9].png")):
            rgba = Image.open(path).convert("RGBA")
            frame = Image.new("RGB", rgba.size, "#202022")
            frame.paste(rgba, mask=rgba.getchannel("A"))
            if first is None:
                first = frame.copy()
            if previous is not None:
                jumps.append(sum(ImageStat.Stat(ImageChops.difference(previous, frame)).mean) / 3)
            previous = frame
        join = sum(ImageStat.Stat(ImageChops.difference(previous, first)).mean) / 3
        check(join <= max(jumps) + 0.05, f"{name}: loop seam has no larger visual jump than normal adjacent frames")
        check(max(jumps) < 12, f"{name}: no full-frame temporal flash")
        for extension in ["gif", "webp"]:
            with Image.open(OUT / "clips" / f"{name}.{extension}") as animated:
                elapsed = 0
                alpha_seen = False
                for i in range(animated.n_frames):
                    animated.seek(i)
                    animated.load()
                    elapsed += animated.info.get("duration", 0)
                    if extension == "webp":
                        alpha_seen |= animated.convert("RGBA").getchannel("A").getextrema()[0] == 0
                check(elapsed == 6000, f"{name}.{extension}: exact 6-second decoded duration")
                if extension == "webp":
                    check(alpha_seen, f"{name}: WebP preserves transparency")
        measurements.append({"id": name, "maxAdjacentMeanDifference": max(jumps), "loopJoinMeanDifference": join})

    face = Image.open(ASSETS / "layers" / "face.png").getchannel("A")
    # A concave cheek notch is exterior, not a hole; flood from the canvas boundary.
    holes = face.point(lambda a: 0 if a < 230 else 255)
    ImageDraw.floodfill(holes, (0, 0), 128, thresh=0)
    check(holes.histogram()[0] == 0, "completed face underlay has no enclosed low-alpha holes")
    for part, source in [("face", "face-underlay"), ("hair", "hair-complete"), ("rear-hair", "rear-hair-underlay"), ("accessory", "accessory"), ("neck", "body-underlay"), ("clothes-paws", "body-underlay")]:
        final = Image.open(ASSETS / "layers" / f"{part}.png").convert("RGBA")
        generated = Image.open(ASSETS / "generated" / f"{source}.png").convert("RGBA")
        solid = ImageChops.multiply(final.getchannel("A").point(lambda a: 255 if a > 235 else 0), generated.getchannel("A").point(lambda a: 255 if a > 235 else 0))
        difference = ImageChops.difference(final.convert("RGB"), generated.convert("RGB"))
        check(all(ImageChops.multiply(channel.point(lambda d: 255 if d > 3 else 0), solid).getbbox() is None for channel in difference.split()), f"{part}: no original-color backfill fragments reintroduced into completed layer")
    eye = Image.open(ASSETS / "layers" / "eye.png").convert("RGBA")
    check(eye.size == (148, 244), "approved orange eye source dimensions retained")
    white_pixels = sum(1 for r, g, b, a in eye.get_flattened_data() if a > 235 and r > 210 and g > 200 and b > 185)
    check(white_pixels == 0, "no opaque sclera/highlight added to the simple orange eye asset")
    accessory = Image.open(ASSETS / "layers" / "accessory.png").getchannel("A").getbbox()
    check(accessory[0] > 700, "flower and ribbon layer stays on the viewer-right side")
    check(all(clip.get("ChinHoles", -1) == 0 for clip in report["Clips"]), "all 14 clips pass the new under-chin corridor coverage checks")
    front = Image.open(ASSETS / "layers" / "hair.png").convert("RGBA")
    rear = Image.open(ASSETS / "layers" / "rear-hair.png").convert("RGBA")
    for point in [(475, 1055), (775, 1055), (475, 1070), (775, 1070), (625, 1090)]:
        check(front.getpixel(point)[3] < 32 and rear.getpixel(point)[3] > 245, f"chin {point}: front U-rim removed and independent rear support filled")

    payload = json.loads((ASSETS / "payload-manifest.json").read_text(encoding="utf-8"))
    for file in payload["Files"]:
        path = ASSETS / file["Path"]
        check(path.is_file() and hashlib.sha256(path.read_bytes()).hexdigest().upper() == file["Sha256"], f"payload hash matches {file['Path']}")
    check(payload["Bytes"] < 12_000_000, "runtime-candidate data remains below 12 MB")
    with zipfile.ZipFile(OUT / "motion-assets-v1.zip", "w", zipfile.ZIP_DEFLATED) as archive:
        for file in payload["Files"]:
            archive.write(ASSETS / file["Path"], file["Path"])
        archive.write(ASSETS / "payload-manifest.json", "payload-manifest.json")
        archive.write(ASSETS / "README.md", "README.md")
        archive.write(ASSETS / "generation.json", "generation.json")
    with zipfile.ZipFile(OUT / "motion-assets-v1.zip") as archive:
        check(archive.testzip() is None, "asset archive CRC validation passes")
        check(not any(path.lower().endswith((".dll", ".exe")) or path.startswith("generated/") or "/generated/" in path for path in archive.namelist()), "asset archive excludes native Skia and generated-source intermediates")
    page = (OUT / "index.html").read_text(encoding="utf-8")
    check("__MOTION_DATA__" not in page and "https://" not in page and "fetch(" not in page, "local preview has complete embedded metadata and no remote requests")
    code = re.search(r"<script>(.*?)</script>", page, re.S).group(1)
    script_path = RAW / "gallery-check.js"
    script_path.write_text(code, encoding="utf-8")
    subprocess.run(["node", "--check", str(script_path)], check=True, capture_output=True)
    check(True, "gallery JavaScript passes syntax validation; browser interaction remains manual")
    final = {"success": True, "checks": checks, "rigChecks": len(report["Checks"]), "mediaMeasurements": measurements,
             "runtimeCandidateBytes": payload["Bytes"], "assetZipBytes": (OUT / "motion-assets-v1.zip").stat().st_size,
             "scope": "Offline artifact audit; no desktop interaction, installed-plugin changes or runtime performance claim."}
    (OUT / "delivery-validation.json").write_text(json.dumps(final, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"success": True, "checks": len(checks), "runtimeBytes": payload["Bytes"]}))


if __name__ == "__main__":
    main()

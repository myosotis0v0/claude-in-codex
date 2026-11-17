"""Assemble the paired offscreen WPF renders; never starts the desktop companion."""
import argparse
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path, default=ROOT / "artifacts" / "gaze-preview")
    parser.add_argument("--output", type=Path, default=ROOT / "previews" / "gaze-before-after.gif")
    args = parser.parse_args()
    report = json.loads((args.input / "gaze-report.json").read_text(encoding="utf-8"))
    assert report["Success"]
    font = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 14)
    title = ImageFont.truetype("C:/Windows/Fonts/segoeuib.ttf", 18)
    labels = ("FIXED LASHES", "LINKED LASHES") if report.get("Comparison") == "Lashes" else ("BEFORE", "AFTER")
    frames = []
    for entry in report["Frames"]:
        canvas = Image.new("RGB", (600, 472), "#191919")
        draw = ImageDraw.Draw(canvas)
        for x, label, prefix in [(20, labels[0], "before"), (324, labels[1], "after")]:
            draw.text((x, 12), label, font=title, fill="#eeeeee" if prefix == "before" else "#ff963f")
            pet = Image.open(args.input / f"{prefix}-{entry['Frame']:03}.png").convert("RGBA")
            canvas.paste(pet, (x, 42), pet)
            detail = pet.crop((70, 151, 188, 194)).resize((236, 86), Image.Resampling.LANCZOS)
            canvas.paste(detail, (x + 10, 314), detail)
        draw.line((300, 12, 300, 402), fill="#393939")
        draw.line((20, 410, 580, 410), fill="#393939")
        draw.text((20, 422), entry["Stage"], font=font, fill="#eeeeee")
        draw.text((20, 443), "Same cursor path / 30 fps / 8 seconds", font=font, fill="#aaaaaa")
        cx, cy = 554, 443
        draw.line((cx - 22, cy, cx + 22, cy), fill="#777777")
        draw.line((cx, cy - 22, cx, cy + 22), fill="#777777")
        x, y = entry["Input"]
        dot = (cx + x * 3.5, cy + y * 3.5)
        draw.line((cx, cy, *dot), fill="#ff963f", width=2)
        draw.ellipse((dot[0] - 3, dot[1] - 3, dot[0] + 3, dot[1] + 3), fill="#ff963f")
        frames.append(canvas)

    # One palette prevents frame-to-frame color flicker; GIF timing is in 10 ms ticks.
    samples = Image.new("RGB", (600 * 4, 472 * 3))
    for i in range(12):
        samples.paste(frames[i * 20], ((i % 4) * 600, (i // 4) * 472))
    palette = samples.quantize(colors=256)
    indexed = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames]
    args.output.parent.mkdir(parents=True, exist_ok=True)
    durations = [30, 30, 40] * 80
    indexed[0].save(args.output, save_all=True, append_images=indexed[1:], duration=durations,
                    loop=0, optimize=False, disposal=1)
    frames[45].save(args.output.with_suffix(".png"))
    with Image.open(args.output) as gif:
        duration = 0
        for frame in range(gif.n_frames):
            gif.seek(frame)
            duration += gif.info["duration"]
        assert duration == 8000, duration
        print(f"{args.output}: {gif.n_frames} frames, {duration} ms, {args.output.stat().st_size} bytes")
    print(f"Offscreen checks passed: {len(report['Checks'])}")


if __name__ == "__main__":
    main()

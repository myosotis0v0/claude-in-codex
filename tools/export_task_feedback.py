"""Package deterministic WPF feedback renders, without touching source assets."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
RAW = ROOT / "artifacts/task-feedback/release/wpf-feedback/frames"
OUT = ROOT / "previews/task-feedback"
FONT = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 16)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    frames = []
    for i in range(240):
        image = Image.new("RGB", (288, 320), "#202022")
        draw = ImageDraw.Draw(image)
        title = "开始 · 思考 3 秒" if i < 90 else "完成 · 提示 2 秒" if 150 <= i < 210 else "恢复待机"
        draw.text((22, 12), title, font=FONT, fill="#ff9b4d")
        pet = Image.open(RAW / f"{i:03}.png").convert("RGBA")
        image.paste(pet, (25, 48), pet)
        frames.append(image)
    palette = frames[45].quantize(colors=256)
    indexed = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames]
    indexed[0].save(OUT / "task-feedback.gif", save_all=True, append_images=indexed[1:], duration=[30, 30, 40] * 80, loop=0, disposal=1, optimize=True)
    poster = Image.new("RGB", (864, 320), "#202022")
    for x, i in enumerate([45, 120, 180]):
        poster.paste(frames[i], (x * 288, 0))
    poster.save(OUT / "task-feedback-states.png")
    print(OUT)


if __name__ == "__main__":
    main()

"""Build the README contact sheet from verified offline pose posters."""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
POSTERS = ROOT / "previews/motion-batch-v1/posters"
OUTPUT = ROOT / "previews/motion-states.png"
ITEMS = [
    ("idle", "安静待机", "AUTO"), ("focus", "眼神跟随", "CURSOR"),
    ("breeze", "发丝微风", "ADDITIVE"), ("thinking", "歪头思考", "TASK HOOK"),
    ("happy", "开心微笑", "RESERVED"), ("surprise", "轻微惊讶", "RESERVED"),
    ("shy", "害羞回避", "RESERVED"), ("annoyed", "无语不满", "RESERVED"),
    ("sleepy", "困倦点头", "LONG IDLE"), ("nod", "点头回应", "CONFIRM"),
    ("shake", "摇头回应", "RESERVED"), ("talk", "简化说话", "PREVIEW ONLY"),
    ("wink", "眨单眼", "EXPAND"), ("pat", "摸头反应", "HOLD HEAD"),
    ("rig-debug", "骨骼与蒙皮", "DEBUG ONLY"), ("activity", "思考气泡", "TASK HOOK"),
]


def main():
    label = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 16)
    small = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 11)
    title = ImageFont.truetype("C:/Windows/Fonts/segoeuib.ttf", 20)
    board = Image.new("RGBA", (960, 1096), "#202022")
    draw = ImageDraw.Draw(board)
    draw.text((24, 16), "CLAUDE IN CODEX / MOTION STATES", font=title, fill="#ff9b4d")
    for i, (name, text, trigger) in enumerate(ITEMS):
        x, y = 16 + i % 4 * 236, 56 + i // 4 * 260
        source = ROOT / "previews/thinking-bubble.png" if name == "activity" else POSTERS / f"{name}.png"
        image = Image.open(source).convert("RGBA")
        image.thumbnail((212, 212), Image.Resampling.LANCZOS)
        board.alpha_composite(image, (x + (220 - image.width) // 2, y + (212 - image.height) // 2))
        draw.text((x + 8, y + 218), text, font=label, fill="#f4f4f5")
        draw.text((x + 8, y + 241), trigger, font=small, fill="#b6b6bc")
    board.convert("RGB").save(OUTPUT, optimize=True)
    print(OUTPUT)


if __name__ == "__main__":
    main()

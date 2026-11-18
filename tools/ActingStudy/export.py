"""Export the sorted, side-by-side MotionCore frames; keep media out of Git."""
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
RAW, OUT = ROOT / "artifacts/acting-study", ROOT / "previews/acting-study"
OUT.mkdir(parents=True, exist_ok=True)
FONT = "C:/Windows/Fonts/msyh.ttc"
LABEL, SMALL = (ImageFont.truetype(FONT, s) for s in (19, 14))
BG, LINE, FG, ACCENT = "#202022", "#423c39", "#eee3d7", "#ffa661"
ITEMS = [
    ("thinking", "思考", "走神断片", 88, 180),
    ("complete", "完成", "蓄力弹起", 14, 72),
    ("sleepy", "困倦", "打盹惊醒", 85, 180),
    ("pat", "摸头", "闭眼蹭手", 46, 180),
    ("wink", "展开", "侧倾亮眼", 15, 72),
    ("menu", "打开菜单", "抬头挑眉", 12, 54),
]

def card(item, i):
    key, title, variant, _, _ = item
    with Image.open(RAW / key / f"{i:03}.png") as im:
        assert im.size == (1024, 512)
        pet = im.convert("RGBA").resize((528, 264), Image.Resampling.LANCZOS)
    board = Image.new("RGB", (528, 303), BG)
    board.paste(pet, (0, 38), pet)
    d = ImageDraw.Draw(board)
    d.text((14, 9), title+"  /  原版", font=LABEL, fill=FG)
    d.text((278, 9), variant+"  /  候选", font=LABEL, fill=ACCENT)
    d.line((264, 8, 264, 292), fill=LINE)
    return board

def export():
    sheet = Image.new("RGB", (1056, 909), BG)
    for k, item in enumerate(ITEMS):
        key, _, _, poster, n = item
        frames = [card(item, i) for i in range(n)]
        sample = Image.new("RGB", (528*3, 303*3))
        for j in range(9):
            sample.paste(frames[j*(n-1)//8], (j%3*528, j//3*303))
        palette = sample.quantize(colors=256)
        seq = [im.quantize(palette=palette, dither=Image.Dither.NONE) for im in frames]
        path = OUT / (key+".gif")
        seq[0].save(path, save_all=True, append_images=seq[1:], loop=0,
                    duration=([30, 40, 30]*(n//3)), disposal=1, optimize=False)
        with Image.open(path) as check:
            assert sum(frame_duration(check, j) for j in range(check.n_frames)) == n*100//3
        a, b = frames[poster].crop((0, 39, 264, 303)), frames[poster].crop((264, 39, 528, 303))
        assert ImageChops.difference(a.crop((15,10,250,260)), b.crop((15,10,250,260))).getbbox()
        sheet.paste(frames[poster], (k%2*528, k//2*303))
    sheet.save(OUT/"comparison.png")

def frame_duration(gif, i):
    gif.seek(i)
    return gif.info["duration"]

if __name__ == "__main__":
    export()

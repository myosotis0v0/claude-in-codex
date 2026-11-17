"""Validate protected regions, package offscreen previews, and build current README sheets."""
import json
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
RAW = ROOT / "artifacts/expression-preview"
OUT = ROOT / "previews/expression-preview"
FONT = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 17)
SMALL = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 13)


def save_gif(frames, name):
    sample = Image.new("RGB", (frames[0].width * 3, frames[0].height * 3))
    for i in range(9):
        sample.paste(frames[i * (len(frames) // 9)], ((i % 3) * frames[0].width, (i // 3) * frames[0].height))
    palette = sample.quantize(colors=256)
    indexed = [f.quantize(palette=palette, dither=Image.Dither.NONE) for f in frames]
    indexed[0].save(OUT / name, save_all=True, append_images=indexed[1:], duration=([30, 30, 40] * ((len(frames) + 2) // 3))[:len(frames)], loop=0, disposal=1, optimize=True)
    with Image.open(OUT / name) as gif:
        duration = 0
        for i in range(gif.n_frames):
            gif.seek(i)
            duration += gif.info["duration"]
        assert duration == len(frames) * 1000 // 30


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    before = json.loads((RAW / "before/pat-poses.json").read_text(encoding="utf-8"))
    after = json.loads((RAW / "after/pat-poses.json").read_text(encoding="utf-8"))
    ignored = {"EyeLeft", "EyeRight", "SmileEyes", "SoftSmileEyes"}
    for a, b in zip(before, after, strict=True):
        a.setdefault("EyeRoll", 0)
        assert {k: v for k, v in a.items() if k not in ignored} == {k: v for k, v in b.items() if k not in ignored}
    frames = []
    changed_bounds = None
    for i in range(180):
        board = Image.new("RGB", (720, 440), "#202022")
        draw = ImageDraw.Draw(board)
        images = [Image.open(RAW / stage / "pat" / f"{i:03}.png").convert("RGBA") for stage in ["before", "after"]]
        diff = ImageChops.difference(*images)
        bbox = diff.convert("RGB").getbbox()
        if bbox:
            assert 46 <= bbox[0] and bbox[2] <= 208 and 132 <= bbox[1] and bbox[3] <= 188, (i, bbox)
            changed_bounds = bbox
        for x, image, title in zip([20, 372], images, ["调整前：慢闭合、弧线下坠", "调整后：快速闭合、保留停留"]):
            draw.text((x, 12), title, font=FONT, fill="#eeeeee")
            board.paste(image, (x + 40, 48), image)
            crop = image.crop((42, 132, 208, 183)).resize((332, 102), Image.Resampling.LANCZOS)
            board.paste(crop, (x - 8, 316), crop)
        draw.text((24, 289), "仅眼部变动 / 头部、发丝、嘴型与其它参数逐帧一致", font=SMALL, fill="#ff9b4d")
        frames.append(board)
    assert changed_bounds is not None
    save_gif(frames, "pat-eyes-before-after.gif")
    frames[60].save(OUT / "pat-eyes-before-after.png")
    menu = []
    for i in range(120):
        image = Image.new("RGB", (360, 440), "#202022")
        draw = ImageDraw.Draw(image)
        title = "点击前" if i < 15 else "点击 · 看向上方菜单" if i < 57 else "恢复鼠标跟随"
        draw.text((22, 12), title, font=FONT, fill="#ff9b4d")
        pet = Image.open(RAW / "after/menu" / f"{i:03}.png").convert("RGBA")
        image.paste(pet, (61, 48), pet)
        crop = pet.crop((42, 132, 208, 183)).resize((332, 102), Image.Resampling.LANCZOS)
        image.paste(crop, (14, 316), crop)
        draw.text((22, 289), "保留冷淡表情 / 不添加笑容或腮红", font=SMALL, fill="#eeeeee")
        menu.append(image)
    save_gif(menu, "menu-look.gif")
    menu[30].save(OUT / "menu-look.png")
    items = [
        ("idle", "安静待机", "AUTO"), ("focus", "眼神跟随", "CURSOR"), ("breeze", "发丝微风", "ADDITIVE"),
        ("thinking", "开始思考", "START / 3s"), ("complete", "任务完成", "STOP / 2s"), ("menu", "看向菜单", "PREVIEW / CLICK"),
        ("pat", "摸头闭眼", "PREVIEW / HOLD"), ("wink", "眨单眼", "EXPAND"), ("sleepy", "困倦点头", "LONG IDLE"),
        ("nod", "点头回应", "CONFIRM"), ("happy", "开心微笑", "RESERVED"), ("surprise", "轻微惊讶", "RESERVED"),
        ("shy", "害羞回避", "RESERVED"), ("annoyed", "无语不满", "RESERVED"), ("shake", "摇头回应", "RESERVED"),
        ("talk", "简化说话", "PREVIEW ONLY"),
    ]
    sheet = Image.new("RGB", (1200, 1480), "#202022")
    draw = ImageDraw.Draw(sheet)
    draw.text((24, 16), "当前 V4 素材 / 16 状态静态预览", font=FONT, fill="#ff9b4d")
    draw.text((24, 44), "已安装 0.5.4 · 摸头调整与看菜单为 0.5.5 候选，尚未实装", font=SMALL, fill="#bbbbbb")
    for i, (name, title, tag) in enumerate(items):
        x, y = (i % 4) * 300, 80 + (i // 4) * 350
        pet = Image.open(RAW / "after/posters" / f"{name}.png").convert("RGBA")
        sheet.paste(pet, (x, y), pet)
        draw.text((x + 20, y + 302), title, font=FONT, fill="#eeeeee")
        draw.text((x + 20, y + 326), tag, font=SMALL, fill="#aaaaaa")
    sheet.save(ROOT / "previews/motion-states.png")
    neutral = Image.new("RGB", (340, 340), "#202022")
    pet = Image.open(RAW / "after/neutral.png").convert("RGBA")
    neutral.paste(pet, (20, 20), pet)
    neutral.save(ROOT / "previews/current-material.png")
    parts = [
        ("hair-cap", "头顶发盖"), ("hair-front-left", "左前发"), ("hair-front-center", "中前发"), ("hair-front-right", "右前发"),
        ("rear-hair", "原后发底层"), ("hair-curtain", "独立后发帘"), ("hair-backing", "补全后发承托"), ("accessory", "花饰与缎带"),
        ("face", "完整脸底 / 保留原下颌"), ("neck", "补全脖子"), ("clothes-paws", "衣服与手掌"), ("eye", "原简化瞳孔 · 局部放大"),
    ]
    sheet = Image.new("RGB", (1200, 1180), "#202022")
    draw = ImageDraw.Draw(sheet)
    draw.text((24, 14), "部件拆分", font=FONT, fill="#ff9b4d")
    draw.text((24, 40), "头发、脸部、配饰与表情贴片", font=SMALL, fill="#bbbbbb")
    layers = ROOT / "assets/claude-motion-v4/layers"
    for i, (name, title) in enumerate(parts):
        x, y = 16 + (i % 4) * 296, 76 + (i // 4) * 306
        tile = Image.new("RGB", (264, 264))
        td = ImageDraw.Draw(tile)
        for gy in range(0, 264, 12):
            for gx in range(0, 264, 12):
                td.rectangle((gx, gy, gx + 11, gy + 11), fill="#28282b" if (gx // 12 + gy // 12) % 2 else "#242426")
        part = Image.open(layers / f"{name}.png").convert("RGBA")
        part.thumbnail((264, 264), Image.Resampling.LANCZOS)
        tile.paste(part, ((264 - part.width) // 2, (264 - part.height) // 2), part)
        sheet.paste(tile, (x, y))
        draw.text((x + 4, y + 272), title, font=SMALL, fill="#eeeeee")
    draw.text((20, 1012), "眉毛与嘴型贴片（局部放大）", font=FONT, fill="#ff9b4d")
    names = ["brow-left", "brow-right", "mouth-neutral", "mouth-smile", "mouth-happy", "mouth-surprise", "mouth-a", "mouth-i", "mouth-u"]
    for i, name in enumerate(names):
        part = Image.open(layers / f"{name}.png").convert("RGBA")
        bbox = part.getbbox()
        if bbox:
            part = part.crop(bbox)
        part.thumbnail((100, 65), Image.Resampling.LANCZOS)
        x = 24 + i * 130
        sheet.paste(part, (x + (100 - part.width) // 2, 1055 + (65 - part.height) // 2), part)
        draw.text((x, 1136), name.replace("mouth-", "").replace("brow-", "brow "), font=SMALL, fill="#bbbbbb")
    sheet.save(ROOT / "previews/material-parts-v4.png")
    (OUT / "validation.json").write_text(json.dumps({"Success": True, "ProtectedPatPoseFrames": 180,
        "ChangedOutsideEyeRegion": 0, "PatFrames": 180, "MenuFrames": 120, "Fps": 30,
        "Scope": "Existing 0.5.4 installed build untouched. Candidate 0.5.5 previews only; paired baseline captured before eye changes, same controller and material. Head, hair, mouth and non-eye pose fields are equal frame by frame."}, indent=2), encoding="utf-8")
    print(OUT)


if __name__ == "__main__":
    main()

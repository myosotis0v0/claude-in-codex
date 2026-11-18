"""Export fixed-eye pose and timed A/B; check the silhouette and neck in the raw RGBA frames."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[2]
raw, out = root / "artifacts/menu-keyform", root / "previews/menu-keyform"
out.mkdir(parents=True, exist_ok=True)
font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 17)

def board(im, labels):
    pet = im.resize((800, 400), Image.Resampling.LANCZOS)
    b = Image.new("RGB", (800, 440), "#202022")
    b.paste(pet, (0, 40), pet)
    d = ImageDraw.Draw(b)
    for x, label, color in zip((16, 416), labels, ("#eee3d7", "#ffa661")):
        d.text((x, 9), label, fill=color, font=font)
    return b

def crown(im, offset):
    return [next(y for y in range(150) if im.getpixel((offset+x,y))[3]>200)
            for x in range(165, 347, 4)]

with Image.open(raw/"fixed-gaze.png") as still:
    reference = crown(still, 0)
    static_error = max(abs(a-b) for a,b in zip(reference,crown(still,512)))
    assert static_error <= 1
    board(still, ("正面  /  固定眼神", "微仰  /  相同眼神")).save(out/"fixed-gaze.png")

frames, errors = [], []
for i in range(72):
    with Image.open(raw/f"{i:03}.png") as im:
        errors.extend(abs(a-b) for a,b in zip(reference,crown(im,512)))
        for r,g,b,a in im.crop((759,397,774,421)).convert("RGBA").get_flattened_data():
            assert a>=245 and not (r-g>82 and g-b>34), f"neck gap at frame {i}"
        frames.append(board(im, ("上一版  /  整头网格", "新版  /  微仰关键姿势")))
assert max(errors)<=4, max(errors)
sample=Image.new("RGB",(800,440*3))
for j,i in enumerate((0,23,42)):
    sample.paste(frames[i],(0,j*440))
palette=sample.quantize(colors=256)
seq=[f.quantize(palette=palette,dither=Image.Dither.NONE) for f in frames]
gif=out/"menu.gif"
seq[0].save(gif,save_all=True,append_images=seq[1:],duration=[30,40,30]*24,loop=0,disposal=1)
with Image.open(gif) as g:
    total=0
    for i in range(g.n_frames):
        g.seek(i);total+=g.info["duration"]
    assert total==2400
print(f"Checked 72 frames; maximum sampled crown delta: static {static_error}px, moving {max(errors)}px")

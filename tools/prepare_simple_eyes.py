"""Prepare the revised flat-color rig without sclera or lower eyelids."""
from pathlib import Path
import json
import math
from PIL import Image, ImageChops, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
GEN = Path(r'C:\Users\huangtianyi\.codex\generated_images\019ecf8f-77ad-7832-a0ef-057eba955363')
OUT = ROOT / 'assets' / 'claude-v2'
PREVIEW = ROOT / 'previews'
OUT.mkdir(parents=True, exist_ok=True)
base = Image.open(GEN / 'exec-ef20093b-4243-4d66-ba86-03b5852d2360.png').convert('RGBA')
eye = Image.open(GEN / 'exec-402a36a4-0818-4cd6-a1ee-3e88e8e89c05.png').convert('RGBA')
eye = eye.crop(eye.getchannel('A').point(lambda a: 255 if a > 128 else 0).getbbox())
# Quantize the generated fill to the two requested solid colors, retaining alpha.
pixels = eye.load()
for y in range(eye.height):
    for x in range(eye.width):
        r, g, b, a = pixels[x, y]
        pixels[x, y] = (*((255, 140, 43) if r > 130 else (50, 31, 36)), a)
eye = eye.resize((148, 244), Image.Resampling.LANCZOS)
eye.save(OUT / 'eye.png')
base.save(OUT / 'base.png')
union = Image.new('L', base.size)
specs = [('left', (368, 789, 563, 929), (388, 654)),
         ('right', (703, 789, 901, 929), (728, 654))]
masks = []
for name, box, origin in specs:
    mask = Image.new('L', base.size)
    mp = mask.load()
    for y in range(box[1], box[3]):
        for x in range(box[0], box[2]):
            r, g, b, a = base.getpixel((x,y))
            if r > 215 and g > 180 and b > 165 and a > 240:
                mp[x,y] = 255
    assert sum(mask.histogram()[1:]) > 8000, 'Motion mask detection failed'
    mask.save(OUT / f'{name}-motion-mask.png')
    masks.append((mask, origin))
    union = ImageChops.lighter(union, mask)
front = base.copy()
front.putalpha(ImageChops.subtract(base.getchannel('A'), union))
front.save(OUT / 'foreground.png')
underlay = base.copy()
underlay.putalpha(ImageChops.multiply(base.getchannel('A'), union))
underlay.save(OUT / 'skin-underlay.png')

def render(dx, dy):
    result = underlay.copy()
    for mask, origin in masks:
        part = Image.new('RGBA', base.size)
        part.alpha_composite(eye, (origin[0]+dx, origin[1]+dy))
        part.putalpha(ImageChops.multiply(part.getchannel('A'), mask))
        result.alpha_composite(part)
    result.alpha_composite(front)
    return result

manifest = {'version': 2, 'canvas': list(base.size), 'eyeSize': list(eye.size),
            'maxOffset': [20,10], 'inwardOffset': 18,
            'layers': ['skin-underlay','clippedEyes','foreground'],
            'sclera': False, 'lowerEyelid': False,
            'eyes': [{'name': n, 'origin': list(o), 'mask': n+'-motion-mask.png'} for n,b,o in specs]}
(OUT / 'rig.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
render(0,0).resize((256,256),Image.Resampling.LANCZOS).save(PREVIEW / 'claude-v2-small.png')
sheet = Image.new('RGB',(768,286),'#202124')
draw = ImageDraw.Draw(sheet)
for i,(label,dx) in enumerate([('LEFT',-18),('CENTER',0),('RIGHT',18)]):
    frame=render(dx,0).resize((256,256),Image.Resampling.LANCZOS)
    sheet.paste(frame,(i*256,24),frame)
    draw.text((i*256+108,7),label,fill='#eeeeee')
sheet.save(PREVIEW / 'gaze-v2-contact-sheet.png')
frames=[]
for i in range(72):
    frame=render(round(18*math.sin(2*math.pi*i/72)),round(8*math.sin(4*math.pi*i/72)))
    for channel in ImageChops.difference(frame,base).split():
        assert ImageChops.multiply(channel,ImageChops.invert(union)).getbbox() is None
    bg=Image.new('RGBA',base.size,'#202124')
    bg.alpha_composite(frame)
    frames.append(bg.resize((256,256),Image.Resampling.LANCZOS).convert('RGB'))
frames[0].save(PREVIEW / 'gaze-v2-preview.gif',save_all=True,append_images=frames[1:],duration=60,loop=0)
print('Validated 72 frames; fixed lashes and skin outside motion masks unchanged.')

"""Slice generated artwork into an occlusion-safe eye rig and render previews."""
from collections import deque
from pathlib import Path
import json
import math
from PIL import Image, ImageChops, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'assets' / 'claude'
GENERATED = Path(r'C:\Users\huangtianyi\.codex\generated_images\019ecf8f-77ad-7832-a0ef-057eba955363')
OUT.mkdir(parents=True, exist_ok=True)
base = Image.open(GENERATED / 'exec-fafc749e-f169-4219-8956-e82268b76756.png').convert('RGBA')
iris = Image.open(GENERATED / 'exec-6214963d-e5ed-4efe-b423-ae2e9ef9b1ed.png').convert('RGBA')
iris = iris.crop(iris.getbbox()).resize((136, 196), Image.Resampling.LANCZOS)
iris.save(OUT / 'iris.png')
base.save(OUT / 'base.png')

def eye_mask(seed, bounds):
    mask = Image.new('L', base.size)
    pixels, mp = base.load(), mask.load()
    queue, seen = deque([seed]), set()
    x0, y0, x1, y1 = bounds
    while queue:
        x, y = queue.popleft()
        if (x, y) in seen or not (x0 <= x < x1 and y0 <= y < y1):
            continue
        seen.add((x, y))
        r, g, b, a = pixels[x, y]
        if a < 240 or r < 220 or g < 195 or b < 180:
            continue
        mp[x, y] = 255
        queue.extend(((x-1, y), (x+1, y), (x, y-1), (x, y+1)))
    assert sum(mask.histogram()[1:]) > 8000, 'Eye aperture detection failed'
    return mask

eyes = [
    {'name': 'left', 'seed': (365, 815), 'bounds': (330, 780, 535, 910), 'origin': [395, 688]},
    {'name': 'right', 'seed': (900, 815), 'bounds': (725, 780, 935, 910), 'origin': [732, 688]},
]
union = Image.new('L', base.size)
for eye in eyes:
    mask = eye_mask(eye['seed'], eye['bounds'])
    mask.save(OUT / (eye['name'] + '-aperture.png'))
    eye['mask'] = mask
    union = ImageChops.lighter(union, mask)

# Keep all fixed artwork above the moving irises, including antialiased lashes.
foreground = base.copy()
foreground.putalpha(ImageChops.subtract(base.getchannel('A'), union))
foreground.save(OUT / 'foreground.png')
underlay = base.copy()
underlay.putalpha(ImageChops.multiply(base.getchannel('A'), union))
underlay.save(OUT / 'eye-underlay.png')
rig = {'canvas': list(base.size), 'irisSize': list(iris.size), 'maxOffset': [22, 12],
       'layers': ['eye-underlay', 'clippedIrises', 'foreground'],
       'eyes': [{'name': e['name'], 'origin': e['origin'], 'mask': e['name']+'-aperture.png'} for e in eyes]}
(OUT / 'rig.json').write_text(json.dumps(rig, indent=2), encoding='utf-8')

def render(dx, dy):
    result = underlay.copy()
    for eye in eyes:
        layer = Image.new('RGBA', base.size)
        layer.alpha_composite(iris, (eye['origin'][0]+dx, eye['origin'][1]+dy))
        layer.putalpha(ImageChops.multiply(layer.getchannel('A'), eye['mask']))
        result.alpha_composite(layer)
    result.alpha_composite(foreground)
    return result

previews = ROOT / 'previews'
previews.mkdir(exist_ok=True)
still = render(0, 0)
still.resize((256, 256), Image.Resampling.LANCZOS).save(previews / 'claude-small.png')
sheet = Image.new('RGB', (768, 286), '#202124')
draw = ImageDraw.Draw(sheet)
for i, (label, dx) in enumerate([('LOOK LEFT', -22), ('CENTER', 0), ('LOOK RIGHT', 22)]):
    frame = render(dx, 0).resize((256, 256), Image.Resampling.LANCZOS)
    sheet.paste(frame, (i*256, 24), frame)
    draw.text((i*256+92, 7), label, fill='#eeeeee')
sheet.save(previews / 'gaze-contact-sheet.png')
frames = []
for i in range(72):
    dx = round(22*math.sin(2*math.pi*i/72))
    dy = round(12*math.sin(4*math.pi*i/72))
    frame = render(dx, dy)
    changed = ImageChops.difference(frame, base)
    for channel in changed.split():
        outside = ImageChops.multiply(channel, ImageChops.invert(union))
        assert outside.getbbox() is None, 'Movement changed pixels outside eye apertures'
    bg = Image.new('RGBA', base.size, '#202124')
    bg.alpha_composite(frame)
    frames.append(bg.resize((256, 256), Image.Resampling.LANCZOS).convert('RGB'))
frames[0].save(previews / 'gaze-preview.gif', save_all=True, append_images=frames[1:], duration=60, loop=0)
(previews / 'validation.json').write_text(json.dumps({
    'framesChecked': len(frames), 'outsideAperturePixelsChanged': 0,
    'animationDurationMs': 4320, 'previewSize': [256,256],
    'scope': 'Asset compositor only; not Windows runtime validation'
}, indent=2), encoding='utf-8')
print(json.dumps({'assets': str(OUT), 'previews': str(previews), 'rig': rig}, indent=2))

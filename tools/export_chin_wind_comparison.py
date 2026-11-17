"""Compare saved pre-fix artwork and persistent additive wind without controlling the desktop."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
from export_motion_previews import gif

ROOT=Path(__file__).resolve().parents[1]
RAW=ROOT/'artifacts'/'chin-wind-fix'
OUT=ROOT/'previews'/'motion-batch-v1'
FONT=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',14)


def make(left_dir,right_dir,left_label,right_label,path,neck=False):
    frames=[]
    for i in range(180):
        board=Image.new('RGBA',(592,440),'#202022')
        draw=ImageDraw.Draw(board)
        for x,folder,label in [(20,left_dir,left_label),(316,right_dir,right_label)]:
            image=Image.open(folder/f'{i:03}.png').convert('RGBA')
            draw.text((x,10),label,font=FONT,fill='#eeeeee' if x==20 else '#ff9b4a')
            small=image.resize((256,256),Image.Resampling.LANCZOS)
            board.alpha_composite(small,(x,40))
            region=(105,249,231,295) if neck else (12,116,114,260)
            detail=image.crop(region)
            if neck:
                detail=detail.resize((252,92),Image.Resampling.LANCZOS)
                board.alpha_composite(detail,(x,324))
            else:
                detail=detail.resize((83,117),Image.Resampling.LANCZOS)
                board.alpha_composite(detail,(x+82,306))
        if not neck:
            label='待机' if i<30 or i>=171 else '思考' if i<106 else '说话'
            draw.text((252,286),label,font=FONT,fill='#b4b4bb')
        frames.append(board.convert('RGB'))
    gif(frames,path)
    frames[84].save(path.with_suffix('.png'))


make(RAW/'before'/'talk',ROOT/'artifacts'/'motion-batch-v1'/'talk','后发修复前','后发补底后',OUT/'chin-before-after.gif',True)
make(RAW/'demo-off',RAW/'demo-on','无常驻微风','常驻微风叠加',OUT/'ambient-wind-comparison.gif')
print('Saved neck correction and idle/thinking/talk ambient-layer comparisons.')

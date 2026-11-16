"""Export generated icon artwork at native plugin/launcher sizes."""
from pathlib import Path
from PIL import Image

root=Path(__file__).resolve().parents[1]
source=Path(r'C:\Users\huangtianyi\.codex\generated_images\019ecf8f-77ad-7832-a0ef-057eba955363\exec-01759487-2988-48fa-9bdb-aa55dff74a55.png')
out=root/'plugins'/'claude-in-codex'/'assets'
image=Image.open(source).convert('RGBA')
for size in [32,48,64,128,256,512]:
    image.resize((size,size),Image.Resampling.LANCZOS).save(out/f'icon-{size}.png')
image.resize((256,256),Image.Resampling.LANCZOS).save(out/'app.ico',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
image.resize((128,128),Image.Resampling.LANCZOS).save(root/'previews'/'plugin-icon.png')

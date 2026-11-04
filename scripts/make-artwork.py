#!/usr/bin/env python3
"""Generate all A1 artwork from the unchanged approved master. Requires Pillow.
Run only on the branding preparation branch until its public rollout is approved.
"""
from pathlib import Path
import hashlib, io, json, struct
from PIL import Image, ImageDraw, ImageFont, ImageOps

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / 'branding/universal'
MASTER = ASSETS / 'master.png'
EXPECTED = '65f3556135b5e04304def0c28d0b6895cf1233e22e3c81c0eede0621cb50185c'
NAVY = '#050b25'

def font(size):
    for name in ('/System/Library/Fonts/Supplemental/Arial.ttf', 'DejaVuSans.ttf'):
        try: return ImageFont.truetype(name, size)
        except OSError: pass
    return ImageFont.load_default()

def main():
    if hashlib.sha256(MASTER.read_bytes()).hexdigest() != EXPECTED:
        raise SystemExit('Approved master changed; use the original owner-supplied PNG.')
    master = Image.open(MASTER).convert('RGB')
    w, h = master.size
    # Crop only the existing central N and download mark; do not redraw the approved design.
    crop = master.crop((round(w*.30), round(h*.32), round(w*.70), round(h*.67)))
    small = ImageOps.pad(crop, (512,512), color=NAVY, method=Image.Resampling.LANCZOS)
    small.save(ASSETS / 'small.png')
    outputs=[]
    def save(im, relative, size=None):
        path=ROOT/relative;path.parent.mkdir(parents=True,exist_ok=True)
        if size: im=im.resize((size,size),Image.Resampling.LANCZOS)
        im.save(path);outputs.append(relative)
    def icon(size): return small if size <= 48 else master
    save(master,'packaging/icon-1024.png',1024)
    entries=[];blobs=[];offset=6+7*16
    for size in (16,24,32,48,64,128,256):
        im=icon(size).convert('RGBA').resize((size,size),Image.Resampling.LANCZOS)
        buf=io.BytesIO();im.save(buf,format='PNG');data=buf.getvalue()
        entries.append(struct.pack('<BBBBHHII',size%256,size%256,0,0,1,32,len(data),offset))
        blobs.append(data);offset+=len(data)
    (ROOT/'windows/App/app.ico').write_bytes(struct.pack('<HHH',0,1,7)+b''.join(entries)+b''.join(blobs))
    outputs.append('windows/App/app.ico')
    for size in (16,48,128): save(icon(size),f'extension/icons/{size}.png',size)
    for size in (16,24,32,48,64,128,256,512):
        save(icon(size),f'branding/universal/generated/linux/{size}.png',size)
    save(master,'linux/packaging/nexonic-download-manager.png',256)
    save(master,'linux/App/icon.png',256) if (ROOT/'linux/App/icon.png').exists() else None
    for size in (16,32,128,256,512):
        for scale in (1,2):
            name=f'icon_{size}x{size}'+('@2x' if scale==2 else '')+'.png'
            save(small if size<=32 else master,f'branding/universal/generated/macos/AppIcon.iconset/{name}',size*scale)
    for density,size in [('mdpi',48),('hdpi',72),('xhdpi',96),('xxhdpi',144),('xxxhdpi',192)]:
        save(icon(size),f'android/app/src/main/res/mipmap-{density}/ic_launcher.png',size)
    fg=Image.new('RGBA',(432,432),(0,0,0,0))
    fg.alpha_composite(master.convert('RGBA').resize((288,288),Image.Resampling.LANCZOS),(72,72))
    save(fg,'android/app/src/main/res/drawable/ic_launcher_foreground.png')
    mono=Image.new('RGBA',small.size,'white');mono.putalpha(small.convert('L').point(lambda x: 255 if x>100 else 0))
    canvas=Image.new('RGBA',(432,432));canvas.alpha_composite(mono.resize((288,288),Image.Resampling.LANCZOS),(72,72))
    save(canvas,'android/app/src/main/res/drawable/ic_launcher_monochrome.png')
    for level,extra in [(26,''),(33,'  <monochrome android:drawable="@drawable/ic_launcher_monochrome" />\n')]:
        p=ROOT/f'android/app/src/main/res/mipmap-anydpi-v{level}/ic_launcher.xml';p.parent.mkdir(parents=True,exist_ok=True)
        p.write_text('<?xml version="1.0" encoding="utf-8"?>\n<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">\n  <background android:drawable="@color/ic_launcher_background" />\n  <foreground android:drawable="@drawable/ic_launcher_foreground" />\n'+extra+'</adaptive-icon>\n')
        outputs.append(str(p.relative_to(ROOT)))
    p=ROOT/'android/app/src/main/res/values/icon-background.xml'
    p.write_text('<?xml version="1.0" encoding="utf-8"?>\n<resources><color name="ic_launcher_background">'+NAVY+'</color></resources>\n')
    outputs.append(str(p.relative_to(ROOT)))
    save(master,'ios/App/Assets.xcassets/AppIcon.appiconset/icon-1024.png',1024)
    save(small,'site/favicon.png',48);save(master,'site/icon.png',512)
    og=Image.new('RGB',(1200,630),NAVY);og.paste(master.resize((430,430),Image.Resampling.LANCZOS),(40,100))
    d=ImageDraw.Draw(og)
    for text,y,size in [('Nexonic',150,64),('Universal Download',245,46),('Manager',305,46),('One App. Every Download.',410,29)]:
        d.text((500,y),text,fill='white',font=font(size))
    save(og,'site/og.png')
    preview=Image.new('RGB',(1200,760),NAVY);d=ImageDraw.Draw(preview)
    d.text((32,24),'Nexonic — A1 Universal Hub',fill='white',font=font(34))
    preview.paste(master.resize((280,280),Image.Resampling.LANCZOS),(32,90))
    d.text((360,100),'Approved master, preserved unchanged',fill='white',font=font(25))
    for y,bg in [(180,'#10172c'),(440,'#edf0f8')]:
        d.rectangle((345,y,1165,y+235),fill=bg)
        x=365
        for size in (16,24,32,48,64,128):
            im=icon(size).resize((size,size),Image.Resampling.LANCZOS)
            preview.paste(im,(x,y+55))
            d.text((x,y+195),str(size)+' px',fill='white' if y==180 else NAVY,font=font(15));x+=130
    d.text((32,690),'Small icons retain the original N and download symbol. No public rollout yet.',fill='white',font=font(22))
    save(preview,'branding/universal/preview.png')
    manifest={'master_sha256':EXPECTED,'outputs':{name:hashlib.sha256((ROOT/name).read_bytes()).hexdigest() for name in outputs}}
    (ASSETS/'generated-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    print(f'Generated {len(outputs)} platform assets from the approved master.')
if __name__=='__main__': main()

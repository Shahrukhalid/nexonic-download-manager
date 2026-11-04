#!/usr/bin/env python3
"""Generate official Nexonic icon derivatives from the owner-supplied PNG. Requires Pillow.
The master stays byte-for-byte unchanged; no replacement artwork is drawn.
"""
from pathlib import Path
import hashlib
import io
import json
import struct
import PIL
from PIL import Image, ImageDraw, ImageFont, ImageOps

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / 'branding/universal'
MASTER = ASSETS / 'master.png'
EXPECTED = 'cab9578c52922d1fce461f5910157138dfcf5636ddbc83f42c2d06efefa694d1'
CROP = (304, 56, 1232, 984)
NAVY = '#10172c'


def font(size):
    for name in ('/System/Library/Fonts/Supplemental/Arial.ttf', 'DejaVuSans.ttf'):
        try: return ImageFont.truetype(name, size)
        except OSError: pass
    return ImageFont.load_default()


def rounded_alpha(size, box, radius):
    mask = Image.new('L', (size * 4, size * 4))
    ImageDraw.Draw(mask).rounded_rectangle(tuple(round(x * 4) for x in box), radius=radius * 4, fill=255)
    return mask.resize((size, size), Image.Resampling.LANCZOS)


def main():
    if hashlib.sha256(MASTER.read_bytes()).hexdigest() != EXPECTED:
        raise SystemExit('Approved master changed; use the exact owner-supplied PNG.')
    source = Image.open(MASTER).convert('RGB')
    if source.size != (1536, 1024): raise SystemExit('Unexpected approved source dimensions')
    cropped = source.crop(CROP)
    # Transparency only outside the existing rounded white tile; the N/swoosh/arrow are not redrawn.
    tile = cropped.convert('RGBA')
    tile.putalpha(rounded_alpha(928, (16, 12, 912, 906), 248))
    saturation = source.convert('HSV').getchannel('S')
    colored = saturation.point(lambda s: 255 if s > 80 else 0)
    bounds = colored.getbbox()
    mark_box = (bounds[0]-12, bounds[1]-12, bounds[2]+12, bounds[3]+12)
    mark_rgb = source.crop(mark_box)
    mark = mark_rgb.convert('RGBA')
    mark.putalpha(colored.crop(mark_box))
    small = ImageOps.pad(mark_rgb, (512, 512), color='white', method=Image.Resampling.LANCZOS)
    small.putalpha(rounded_alpha(512, (0, 0, 511, 511), 112))
    outputs = []

    def save(im, name, size=None):
        p = ROOT/name; p.parent.mkdir(parents=True, exist_ok=True)
        if size: im = im.resize((size, size), Image.Resampling.LANCZOS)
        im.save(p, optimize=True)
        outputs.append(name)

    def icon(size): return small if size <= 48 else tile
    def png_bytes(im, size):
        buf = io.BytesIO(); im.resize((size,size), Image.Resampling.LANCZOS).save(buf, format='PNG', optimize=True)
        return buf.getvalue()

    save(cropped, 'branding/universal/cropped.png')
    save(small, 'branding/universal/small.png')
    save(tile, 'packaging/icon-1024.png', 1024)
    entries = []; blobs = []; sizes = (16, 24, 32, 48, 64, 128, 256)
    offset = 6 + len(sizes)*16
    for size in sizes:
        data = png_bytes(icon(size), size)
        entries.append(struct.pack('<BBBBHHII',size%256,size%256,0,0,1,32,len(data),offset))
        blobs.append(data); offset += len(data)
    (ROOT/'windows/App/app.ico').write_bytes(struct.pack('<HHH',0,1,len(sizes))+b''.join(entries)+b''.join(blobs))
    outputs.append('windows/App/app.ico')
    for size in (16, 48, 128): save(icon(size), f'extension/icons/{size}.png', size)
    for size in (16,24,32,48,64,128,256,512): save(icon(size),f'branding/universal/generated/linux/{size}.png',size)
    save(tile,'linux/packaging/nexonic-download-manager.png',256)
    for size in (16,32,128,256,512):
        for scale in (1,2):
            name=f'icon_{size}x{size}'+('@2x' if scale==2 else '')+'.png'
            save(small if size<=32 else tile,f'branding/universal/generated/macos/AppIcon.iconset/{name}',size*scale)
    # ICNS PNG chunks preserve the small-size derivative and Retina variants portably.
    blocks=[]
    for kind,size,im in [(b'icp4',16,small),(b'icp5',32,small),(b'icp6',64,tile),(b'ic07',128,tile),
                         (b'ic08',256,tile),(b'ic09',512,tile),(b'ic10',1024,tile),(b'ic11',32,small),
                         (b'ic12',64,small),(b'ic13',256,tile),(b'ic14',512,tile)]:
        data=png_bytes(im,size); blocks.append(kind+struct.pack('>I',len(data)+8)+data)
    name='branding/universal/generated/macos/AppIcon.icns'
    (ROOT/name).write_bytes(b'icns'+struct.pack('>I',8+sum(map(len,blocks)))+b''.join(blocks));outputs.append(name)
    for density,size in [('mdpi',48),('hdpi',72),('xhdpi',96),('xxhdpi',144),('xxxhdpi',192)]:
        save(icon(size),f'android/app/src/main/res/mipmap-{density}/ic_launcher.png',size)
    # Put every colored element inside Android's 66dp safe region on a 108dp foreground canvas.
    foreground=Image.new('RGBA',(432,432)); fitted=ImageOps.contain(mark,(264,264),Image.Resampling.LANCZOS)
    foreground.alpha_composite(fitted,((432-fitted.width)//2,(432-fitted.height)//2))
    save(foreground,'android/app/src/main/res/drawable/ic_launcher_foreground.png')
    monochrome=Image.new('RGBA',foreground.size,'white');monochrome.putalpha(foreground.getchannel('A'))
    save(monochrome,'android/app/src/main/res/drawable/ic_launcher_monochrome.png')
    tray=Image.new('RGBA',mark.size,'white');tray.putalpha(mark.getchannel('A'))
    tray=ImageOps.pad(tray,(64,64),color=(0,0,0,0),method=Image.Resampling.LANCZOS)
    save(small,'linux/App/tray.png',64)
    save(tray,'branding/universal/generated/macos/MenuBarIcon.png')
    for level,extra in [(26,''),(33,'  <monochrome android:drawable="@drawable/ic_launcher_monochrome" />\n')]:
        name=f'android/app/src/main/res/mipmap-anydpi-v{level}/ic_launcher.xml';p=ROOT/name;p.parent.mkdir(parents=True,exist_ok=True)
        p.write_text('<?xml version="1.0" encoding="utf-8"?>\n<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">\n  <background android:drawable="@color/ic_launcher_background" />\n  <foreground android:drawable="@drawable/ic_launcher_foreground" />\n'+extra+'</adaptive-icon>\n');outputs.append(name)
    name='android/app/src/main/res/values/icon-background.xml'
    (ROOT/name).write_text('<?xml version="1.0" encoding="utf-8"?>\n<resources><color name="ic_launcher_background">#FFFFFF</color></resources>\n');outputs.append(name)
    save(cropped,'ios/App/Assets.xcassets/AppIcon.appiconset/icon-1024.png',1024)
    save(small,'site/favicon.png',48);save(tile,'site/icon.png',512)
    og=Image.new('RGB',(1200,630),'#f4f6fc');og.paste(tile.resize((430,430),Image.Resampling.LANCZOS),(40,100),tile.resize((430,430),Image.Resampling.LANCZOS))
    d=ImageDraw.Draw(og)
    for text,y,size in [('Nexonic',150,64),('Universal Download',245,46),('Manager',305,46),('One App. Every Download.',410,29)]: d.text((500,y),text,fill=NAVY,font=font(size))
    save(og,'site/og.png')
    preview=Image.new('RGB',(1200,760),'#f4f6fc');d=ImageDraw.Draw(preview)
    d.text((30,22),'Nexonic — official gradient icon',fill=NAVY,font=font(34))
    display=tile.resize((280,280),Image.Resampling.LANCZOS);preview.paste(display,(30,90),display)
    d.text((350,100),'Original design, cropped for platform icons',fill=NAVY,font=font(24))
    for y,bg,fg in [(180,'#10172c','white'),(440,'#e8edf8',NAVY)]:
        d.rectangle((345,y,1165,y+235),fill=bg);x=365
        for size in (16,24,32,48,64,128):
            im=icon(size).resize((size,size),Image.Resampling.LANCZOS);preview.paste(im,(x,y+55),im)
            d.text((x,y+195),f'{size} px',fill=fg,font=font(15));x+=130
    d.text((30,690),'White tile · original N, orbit and arrow · small icons use a tighter crop',fill=NAVY,font=font(21))
    save(preview,'branding/universal/preview.png')
    manifest={'master_sha256':EXPECTED,'master_size':list(source.size),'tile_crop':list(CROP),'mark_crop':list(mark_box),
              'generator_pillow_version':PIL.__version__,'outputs':{name:hashlib.sha256((ROOT/name).read_bytes()).hexdigest() for name in outputs}}
    (ASSETS/'generated-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    print(f'Generated {len(outputs)} assets from the official original; master SHA-256 preserved.')


if __name__=='__main__': main()

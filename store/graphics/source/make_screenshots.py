"""
Frames the raw app screenshots for the Play Store: a purple background in the app's colors, a caption on top and the
screenshot with rounded corners. Phone shots come out at 1080x1920 (9:16), tablet shots at 2560x1440 (16:9), as
24-bit PNGs, which is what Play Console asks for.

    python store/graphics/source/make_screenshots.py

Inputs are in store/graphics/screenshots/raw/ (the app area only, without Android's status and navigation bars);
results go to store/graphics/screenshots/. Change the captions below and run it again.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont

SHOTS = Path(__file__).resolve().parents[1] / 'screenshots'
FONT = 'C:/Windows/Fonts/segoeuib.ttf'

CAPTIONS = {
    'phone-1-plot': 'Plot your story beat by beat',
    'phone-2-pictures': 'Pictures for places, faces and moods',
    'phone-3-arrows': 'Drag to move. Pull a dot to draw an arrow.',
    'phone-4-colors': 'Color-code anything',
    'phone-5-overview': 'Your whole story on one infinite board',
    'tablet-1-overview': 'Your whole story on one infinite board',
    'tablet-2-acts': 'Big, readable notes, made for touch',
    'tablet-3-select': 'Select a group to move, recolor or copy it',
    'tablet-4-search': 'Find anything on your board',
}


def background(w, h):
    """The feature graphic's look: a diagonal purple gradient with a faint dot grid."""
    top, bottom = (125, 88, 242), (85, 48, 207)
    img = Image.new('RGB', (w, h))
    px = img.load()
    for y in range(h):
        for x in range(0, w, 4):
            t = min(1, max(0, (x / w) * 0.35 + (y / h) * 0.65))
            c = tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3))
            for dx in range(4):
                if x + dx < w:
                    px[x + dx, y] = c
    d = ImageDraw.Draw(img, 'RGBA')
    step = round(w / 32)
    for y in range(step // 2, h, step):
        for x in range(step // 2, w, step):
            d.ellipse([x - 2, y - 2, x + 2, y + 2], fill=(255, 255, 255, 30))
    return img


def wrap(text, font, width, draw):
    lines, line = [], ''
    for word in text.split():
        trial = f'{line} {word}'.strip()
        if draw.textlength(trial, font=font) <= width or not line:
            line = trial
        else:
            lines.append(line)
            line = word
    return lines + [line]


def frame(name, shot):
    tablet = name.startswith('tablet')
    w, h = (2560, 1440) if tablet else (1080, 1920)
    font_size, top, gap, margin = (84, 70, 60, 70) if tablet else (70, 110, 70, 70)
    img = background(w, h)
    d = ImageDraw.Draw(img)
    font = ImageFont.truetype(FONT, font_size)
    lines = wrap(CAPTIONS[name], font, w - 2 * margin - (600 if tablet else 0), d)
    # Every shot in a set keeps the same caption space (two lines on phones), so the screenshots line up.
    line_h = round(font_size * 1.2)
    block = line_h * max(len(lines), 1 if tablet else 2)
    y = top + (block - line_h * len(lines)) // 2
    for line in lines:
        d.text((w / 2, y), line, font=font, fill='white', anchor='ma')
        y += line_h
    y = top + block
    # The screenshot, as large as fits under the caption.
    room_w, room_h = w - 2 * margin, h - y - gap - margin
    scale = min(room_w / shot.width, room_h / shot.height)
    sw, sh = round(shot.width * scale), round(shot.height * scale)
    shot = shot.resize((sw, sh), Image.LANCZOS)
    x0, y0 = (w - sw) // 2, y + gap
    radius = round(min(sw, sh) * 0.04)
    mask = Image.new('L', (sw, sh), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, sw - 1, sh - 1], radius=radius, fill=255)
    shadow = Image.new('L', (w, h), 0)
    ImageDraw.Draw(shadow).rounded_rectangle([x0, y0 + 24, x0 + sw, y0 + sh + 24], radius=radius, fill=150)
    shadow = shadow.filter(ImageFilter.GaussianBlur(40))
    img.paste((30, 10, 80), (0, 0), shadow)
    img.paste(shot, (x0, y0), mask)
    return img


if __name__ == '__main__':
    for name in CAPTIONS:
        raw = SHOTS / 'raw' / f'{name}.png'
        frame(name, Image.open(raw).convert('RGB')).save(SHOTS / f'{name}.png')
        print('Saved', SHOTS / f'{name}.png')

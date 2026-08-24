"""Rebuild Vibespan.ico from the brand artwork.

Run from the repo root:  python tools/make-icon.py

Two things here are deliberate and easy to undo by accident.

Size-specific artwork. From 32px up the icon uses the whole mark - starburst, pixel
dissipation, chevron. At 24px and below that detail is finer than the pixel grid can
express and the tile turns to mush, so the small entries are cropped to the starburst
alone, which stays recognisable at any size. Shipping different artwork per size is
normal icon practice.

DIB rather than PNG below 256. Pillow's own ICO writer emits PNG-compressed entries at
every size. Windows has accepted those since Vista, but the convention - and what the
in-box csc.exe /win32icon expects most reliably - is a real BITMAPINFOHEADER entry
below 256px, so those are written by hand here.
"""
import io
import os
import struct

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = os.path.join(ROOT, 'VS-Logo-A.png')
TARGET = os.path.join(ROOT, 'Vibespan.ico')

# Square crops into the 1254x1254 source. The artwork carries ~19% dead margin on every
# side, which would otherwise leave the mark a smudge adrift in a cream tile.
FULL_BOX = (123, 87, 1119, 1083)     # whole mark, 11% breathing room
SMALL_BOX = (95, 205, 855, 965)      # starburst only, for 24px and below

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SMALL_UP_TO = 24


def dib(img):
    """A 32bpp BMP icon entry: header, bottom-up BGRA, then the 1bpp AND mask.

    The mask is all zeros because Windows uses the alpha channel on 32bpp icons, but it
    must still be present and its rows padded to 4 bytes or the entry is rejected.
    """
    w, h = img.size
    px = img.load()
    xor = bytearray()
    for y in range(h - 1, -1, -1):
        for x in range(w):
            r, g, b, a = px[x, y]
            xor += bytes((b, g, r, a))
    stride = ((w + 31) // 32) * 4
    mask = bytes(stride * h)
    header = struct.pack('<IiiHHIIiiII', 40, w, h * 2, 1, 32, 0,
                         len(xor) + len(mask), 0, 0, 0, 0)
    return header + bytes(xor) + mask


def png(img):
    buf = io.BytesIO()
    img.save(buf, 'PNG', optimize=True)
    return buf.getvalue()


def main():
    src = Image.open(SOURCE).convert('RGBA')
    full = src.crop(FULL_BOX)
    small = src.crop(SMALL_BOX)

    blobs = []
    for size in SIZES:
        base = small if size <= SMALL_UP_TO else full
        frame = base.resize((size, size), Image.LANCZOS)
        blobs.append(png(frame) if size == 256 else dib(frame))

    out = bytearray(struct.pack('<HHH', 0, 1, len(SIZES)))
    offset = 6 + 16 * len(SIZES)
    for size, blob in zip(SIZES, blobs):
        byte = 0 if size == 256 else size      # 256 is encoded as 0 in an ICONDIRENTRY
        out += struct.pack('<BBBBHHII', byte, byte, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
    for blob in blobs:
        out += blob

    with open(TARGET, 'wb') as fh:
        fh.write(bytes(out))
    print('%s  %d bytes, %d sizes' % (os.path.basename(TARGET), len(out), len(SIZES)))


if __name__ == '__main__':
    main()

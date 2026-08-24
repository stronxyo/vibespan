"""Regenerate src/Brand.cs by tracing the starburst out of the brand artwork.

Run from the repo root:  python tools/trace-mark.py

Why trace instead of hand-drawing: the mark has twelve spokes at irregular angles with
rounded caps, and eyeballing that into path data produces something that is nearly the
logo. The trace is checked back against the source raster and reports an IoU, so drift
is measurable rather than a matter of opinion.

Why vector instead of embedding a PNG: the tray icon is TINTED at run time with the
colour of the worst current usage level, so a fixed-colour bitmap cannot do the job.

Pipeline: threshold the artwork, trace EVERY ink component with a Moore-neighbour walk -
the starburst, all thirty-odd pixel-dissipation squares and the chevron - find any
enclosed holes and trace those too, simplify with Ramer-Douglas-Peucker, then normalise
the whole set together into a 0..100 box so Stretch.Uniform works.

An earlier version kept only the largest component on the grounds that the loose squares
are finer than a 16px tray icon can express. That is true, and it is still the wrong
call: the dissipation is the logo's whole idea, and a mark that drops it is a different
mark. The specks fade to a wash at tray size rather than resolving individually, which
is what the artwork does to the eye anyway.
"""
import os
import textwrap
from collections import deque

import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = os.path.join(ROOT, 'VS-Logo-A.png')
TARGET = os.path.join(ROOT, 'src', 'Brand.cs')

INK_THRESHOLD = 60      # channel-sum distance from the flat background
RDP_EPSILON = 1.4       # px, in source resolution
MIN_HOLE_PX = 30
MIN_PART_PX = 60     # drops antialiasing crumbs, keeps every real dissipation square


def label(mask):
    """Label 4-connected components. Returns (labels, [(size, id), ...] largest first)."""
    h, w = mask.shape
    lab = np.zeros(mask.shape, np.int32)
    found, cur = [], 0
    for y in range(h):
        for x in range(w):
            if mask[y, x] and lab[y, x] == 0:
                cur += 1
                q = deque([(y, x)])
                lab[y, x] = cur
                n = 0
                while q:
                    cy, cx = q.popleft()
                    n += 1
                    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        ny, nx = cy + dy, cx + dx
                        if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and lab[ny, nx] == 0:
                            lab[ny, nx] = cur
                            q.append((ny, nx))
                found.append((n, cur))
    found.sort(reverse=True)
    return lab, found


def enclosed_holes(mask):
    """Background pixels unreachable from the border are holes inside the mark."""
    h, w = mask.shape
    bg = ~mask
    seen = np.zeros_like(bg)
    q = deque()
    for x in range(w):
        for y in (0, h - 1):
            if bg[y, x] and not seen[y, x]:
                seen[y, x] = True
                q.append((y, x))
    for y in range(h):
        for x in (0, w - 1):
            if bg[y, x] and not seen[y, x]:
                seen[y, x] = True
                q.append((y, x))
    while q:
        cy, cx = q.popleft()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = cy + dy, cx + dx
            if 0 <= ny < h and 0 <= nx < w and bg[ny, nx] and not seen[ny, nx]:
                seen[ny, nx] = True
                q.append((ny, nx))
    return bg & ~seen


def trace(padded, start):
    """Moore-neighbour boundary walk. `padded` must have a 1px false border."""
    nb = [(-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1)]
    sy, sx = start
    contour = [(sy, sx)]
    back = 7
    cy, cx = sy, sx
    while True:
        for k in range(8):
            d = (back + 1 + k) % 8
            ny, nx = cy + nb[d][0], cx + nb[d][1]
            if padded[ny, nx]:
                back = (d + 5) % 8
                cy, cx = ny, nx
                contour.append((cy, cx))
                break
        else:
            break
        if (cy, cx) == (sy, sx) and len(contour) > 2:
            break
        if len(contour) > 400000:
            break
    return contour


def rdp(points, eps):
    a = np.asarray(points, float)
    keep = np.zeros(len(a), bool)
    keep[0] = keep[-1] = True
    stack = [(0, len(a) - 1)]
    while stack:
        i, j = stack.pop()
        if j <= i + 1:
            continue
        vx, vy = a[j] - a[i]
        length = np.hypot(vx, vy)
        seg = a[i + 1:j] - a[i]
        dist = (np.abs(vx * seg[:, 1] - vy * seg[:, 0]) / length if length
                else np.hypot(seg[:, 0], seg[:, 1]))
        k = int(np.argmax(dist))
        if dist[k] > eps:
            k += i + 1
            keep[k] = True
            stack.append((i, k))
            stack.append((k, j))
    return a[keep]


def fmt(v):
    s = ('%.2f' % v).rstrip('0').rstrip('.')
    return s if s not in ('-0', '') else '0'


def rings_of(mask, min_px):
    """Every component outline in `mask`, plus the holes inside each, as (points, is_hole)."""
    h, w = mask.shape
    lab, found = label(mask)
    out = []
    for n, cid in found:
        if n < min_px:
            continue
        part = (lab == cid)
        pad = np.zeros((h + 2, w + 2), bool)
        pad[1:-1, 1:-1] = part
        out.append((trace(pad, tuple(np.argwhere(pad)[0])), False))

        holes = enclosed_holes(part)
        if not holes.any():
            continue
        hlab, hfound = label(holes)
        for hn, hid in hfound:
            if hn < MIN_HOLE_PX:
                continue
            hp = np.zeros((h + 2, w + 2), bool)
            hp[1:-1, 1:-1] = (hlab == hid)
            out.append((trace(hp, tuple(np.argwhere(hp)[0])), True))
    return out


def main():
    rgb = np.asarray(Image.open(SOURCE).convert('RGB')).astype(int)
    ink = np.abs(rgb - rgb[2, 2]).sum(axis=2) > INK_THRESHOLD

    ys, xs = np.nonzero(ink)
    m = ink[ys.min():ys.max() + 1, xs.min():xs.max() + 1]

    rings = rings_of(m, MIN_PART_PX)
    simple = [(rdp(r, RDP_EPSILON), hole) for r, hole in rings]
    solids = sum(1 for _, hole in simple if not hole)

    allp = np.vstack([r for r, _ in simple])
    ymin, xmin = allp.min(axis=0)
    ymax, xmax = allp.max(axis=0)
    span = max(ymax - ymin, xmax - xmin)
    ox = (span - (xmax - xmin)) / 2.0
    oy = (span - (ymax - ymin)) / 2.0

    parts = []
    for ring, _ in simple:
        pts = [((px - xmin + ox) / span * 100.0, (py - ymin + oy) / span * 100.0)
               for py, px in ring]
        d = 'M' + fmt(pts[0][0]) + ',' + fmt(pts[0][1])
        d += ''.join('L' + fmt(x) + ',' + fmt(y) for x, y in pts[1:]) + 'Z'
        parts.append(d)
    path = ''.join(parts)

    iou = check(path, [hole for _, hole in simple], m)
    print('%d shapes (%d solid, %d holes), %d points, %d chars, IoU %.4f'
          % (len(simple), solids, len(simple) - solids,
             sum(len(r) for r, _ in simple), len(path), iou))
    if iou < 0.97:
        raise SystemExit('trace drifted from the artwork (IoU %.4f) - not writing' % iou)

    body = ('\n' + ' ' * 12 + '+ "').join(c + '"' for c in textwrap.wrap(path, 96))
    with open(TARGET, 'w', newline='\n') as fh:
        fh.write(TEMPLATE % (solids, iou, 100 * (1 - iou), body))
    print('wrote', os.path.relpath(TARGET, ROOT))


def check(path, is_hole, source_mask):
    """Rasterise the emitted path and compare it back to the artwork."""
    n = 512
    img = Image.new('L', (n, n), 0)
    draw = ImageDraw.Draw(img)
    for i, ring in enumerate([r for r in path.split('M') if r]):
        pts = [tuple(float(v) for v in p.split(','))
               for p in ring.rstrip('Z').replace('L', ' ').split()]
        draw.polygon([(x / 100 * n, y / 100 * n) for x, y in pts],
                     fill=0 if is_hole[i] else 255)
    got = np.asarray(img) > 127

    h, w = source_mask.shape
    span = max(h, w)
    sq = np.zeros((span, span), bool)
    sq[(span - h) // 2:(span - h) // 2 + h, (span - w) // 2:(span - w) // 2 + w] = source_mask
    ref = np.asarray(Image.fromarray((sq * 255).astype('uint8'))
                     .resize((n, n), Image.LANCZOS)) > 127
    return (got & ref).sum() / float((got | ref).sum())


TEMPLATE = '''// The Vibespan mark, as vector geometry.
//
// GENERATED by tools/trace-mark.py from VS-Logo-A.png - do not hand-edit.
//
// Traced rather than eyeballed: every outline is extracted from the artwork - the starburst,
// the full pixel-dissipation field and the chevron, %d shapes in all - simplified with
// Ramer-Douglas-Peucker, and checked back against the source raster at %.4f IoU (%.2f%% of
// pixels differ). The generator refuses to write below 0.97, so drift is caught rather than
// argued about.
//
// Vector and not a bitmap because the tray icon is TINTED at run time - it takes the colour
// of the worst current usage level - so a fixed-colour PNG could not do the job, and this
// also stays sharp at any DPI.
//
// Coordinates run 0..100 on both axes with the mark centred, so Stretch.Uniform fills
// whatever box it is given.
using System.Windows.Media;

namespace Vibespan
{
    public static class Brand
    {
        const string StarburstPath =
            "%s;

        /// <summary>The starburst, frozen once. Fill it with whatever brush the caller wants.</summary>
        public static readonly Geometry Starburst = Build();

        static Geometry Build()
        {
            Geometry g = Geometry.Parse(StarburstPath);
            g.Freeze();
            return g;
        }
    }
}
'''


if __name__ == '__main__':
    main()

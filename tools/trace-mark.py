"""Regenerate src/Brand.cs by tracing the starburst out of the brand artwork.

Run from the repo root:  python tools/trace-mark.py

Why trace instead of hand-drawing: the mark has twelve spokes at irregular angles with
rounded caps, and eyeballing that into path data produces something that is nearly the
logo. The trace is checked back against the source raster and reports an IoU, so drift
is measurable rather than a matter of opinion.

Why vector instead of embedding a PNG: the tray icon is TINTED at run time with the
colour of the worst current usage level, so a fixed-colour bitmap cannot do the job.

Pipeline: threshold the artwork, keep the largest connected component (the starburst -
the loose pixel-dissipation squares and the chevron are separate components and are
deliberately dropped, being finer than a 16px tray icon can express), trace the outline
with a Moore-neighbour walk, find any enclosed holes and trace those too, simplify with
Ramer-Douglas-Peucker, then normalise into a 0..100 box so Stretch.Uniform works.
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


def largest_component(ink):
    """Label 4-connected components and return a mask of the biggest one."""
    h, w = ink.shape
    lab = np.zeros(ink.shape, np.int32)
    best, best_n, cur = 0, 0, 0
    for y in range(h):
        for x in range(w):
            if ink[y, x] and lab[y, x] == 0:
                cur += 1
                q = deque([(y, x)])
                lab[y, x] = cur
                n = 0
                while q:
                    cy, cx = q.popleft()
                    n += 1
                    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        ny, nx = cy + dy, cx + dx
                        if 0 <= ny < h and 0 <= nx < w and ink[ny, nx] and lab[ny, nx] == 0:
                            lab[ny, nx] = cur
                            q.append((ny, nx))
                if n > best_n:
                    best_n, best = n, cur
    return lab == best


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


def main():
    rgb = np.asarray(Image.open(SOURCE).convert('RGB')).astype(int)
    ink = np.abs(rgb - rgb[2, 2]).sum(axis=2) > INK_THRESHOLD
    mask = largest_component(ink)

    ys, xs = np.nonzero(mask)
    m = mask[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    h, w = m.shape

    rings = []
    pad = np.zeros((h + 2, w + 2), bool)
    pad[1:-1, 1:-1] = m
    rings.append(trace(pad, tuple(np.argwhere(pad)[0])))

    holes = enclosed_holes(m)
    if holes.sum() > MIN_HOLE_PX:
        hole_mask = largest_component(holes)      # only meaningful holes; specks are noise
        for blob in (hole_mask,):
            hp = np.zeros((h + 2, w + 2), bool)
            hp[1:-1, 1:-1] = blob
            if hp.sum() >= MIN_HOLE_PX:
                rings.append(trace(hp, tuple(np.argwhere(hp)[0])))

    simple = [rdp(r, RDP_EPSILON) for r in rings]

    allp = np.vstack(simple)
    ymin, xmin = allp.min(axis=0)
    ymax, xmax = allp.max(axis=0)
    span = max(ymax - ymin, xmax - xmin)
    ox = (span - (xmax - xmin)) / 2.0
    oy = (span - (ymax - ymin)) / 2.0

    parts = []
    for ring in simple:
        pts = [((px - xmin + ox) / span * 100.0, (py - ymin + oy) / span * 100.0)
               for py, px in ring]
        d = 'M' + fmt(pts[0][0]) + ',' + fmt(pts[0][1])
        d += ''.join('L' + fmt(x) + ',' + fmt(y) for x, y in pts[1:]) + 'Z'
        parts.append(d)
    path = ''.join(parts)

    iou = check(path, m)
    print('rings %d, %d points, %d chars, IoU %.4f'
          % (len(simple), sum(len(s) for s in simple), len(path), iou))
    if iou < 0.97:
        raise SystemExit('trace drifted from the artwork (IoU %.4f) - not writing' % iou)

    body = ('\n' + ' ' * 12 + '+ "').join(c + '"' for c in textwrap.wrap(path, 96))
    with open(TARGET, 'w', newline='\n') as fh:
        fh.write(TEMPLATE % (iou, 100 * (1 - iou), body))
    print('wrote', os.path.relpath(TARGET, ROOT))


def check(path, source_mask):
    """Rasterise the emitted path and compare it back to the artwork."""
    n = 512
    img = Image.new('L', (n, n), 0)
    draw = ImageDraw.Draw(img)
    for i, ring in enumerate([r for r in path.split('M') if r]):
        pts = [tuple(float(v) for v in p.split(','))
               for p in ring.rstrip('Z').replace('L', ' ').split()]
        draw.polygon([(x / 100 * n, y / 100 * n) for x, y in pts],
                     fill=255 if i == 0 else 0)
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
// Traced rather than eyeballed: the outline is extracted from the artwork, simplified with
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

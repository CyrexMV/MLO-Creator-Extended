# Makes the window, door and frame models that come with MLO Creator (models\mlo_*.glb): simple boxes with flat colours, a generated wood grain and glass.
# Made here, so they are free to use (CC0). Run: python tools\make_models.py   (writes into models\ next to this folder)
# glTF axes: x = width, y = up, z = depth (the program turns them into its own frame: u = width, n = depth, z = up). A door or frame is one model per file
# (the file name says "door" or "frame"); a window file holds several windows, one child node each.
import json, math, os, struct, zlib

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'models')
CREDIT = {'author': 'MLO Creator', 'license': 'CC0 1.0 (public domain)', 'source': 'made by tools/make_models.py'}


# ---------- textures ----------
def png(w, h, rgb):
    raw = b''.join(b'\x00' + bytes(rgb[y * w * 3:(y + 1) * w * 3]) for y in range(h))
    chunk = lambda t, d: struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b'')


def wood(base, size=256, seed=1):
    # vertical grain: stripes along y that wander a little, with darker rings
    out = bytearray(size * size * 3)
    for y in range(size):
        for x in range(size):
            u = x / size + 0.015 * math.sin(y / size * 6.283 * 2 + seed) + 0.006 * math.sin(y / size * 6.283 * 9 + seed * 3)
            ring = 0.5 + 0.5 * math.sin(u * 6.283 * 11 + 1.7 * math.sin(u * 6.283 * 3 + seed))
            fine = 0.5 + 0.5 * math.sin(u * 6.283 * 57 + seed * 5)
            k = 0.86 + 0.12 * ring + 0.05 * fine
            for c in range(3): out[(y * size + x) * 3 + c] = max(0, min(255, int(base[c] * k)))
    return png(size, size, out)


# ---------- geometry: boxes ----------
class Mesh:
    def __init__(self): self.prims = {}         # material name -> [pos, nrm, uv, idx]

    def box(self, mat, x0, x1, y0, y1, z0, z1, uvs=1.0):
        p = self.prims.setdefault(mat, [[], [], [], []]); pos, nrm, uv, idx = p
        faces = [((1, 0, 0), [(x1, y0, z1), (x1, y0, z0), (x1, y1, z0), (x1, y1, z1)], (2, 1)), ((-1, 0, 0), [(x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0)], (2, 1)),
                 ((0, 1, 0), [(x0, y1, z1), (x1, y1, z1), (x1, y1, z0), (x0, y1, z0)], (0, 2)), ((0, -1, 0), [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)], (0, 2)),
                 ((0, 0, 1), [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)], (0, 1)), ((0, 0, -1), [(x1, y0, z0), (x0, y0, z0), (x0, y1, z0), (x1, y1, z0)], (0, 1))]
        for n, vs, (a, b) in faces:
            base = len(pos) // 3
            for v in vs: pos.extend(v); nrm.extend(n); uv.extend((v[a] * uvs, 1 - v[b] * uvs))
            idx.extend((base, base + 1, base + 2, base, base + 2, base + 3))

    def bar(self, mat, ax, ay, bx, by, t, z0, z1):
        # a bar in the wall's plane from (ax, ay) to (bx, by), t wide, z0..z1 deep (for arches and round windows)
        p = self.prims.setdefault(mat, [[], [], [], []]); pos, nrm, uv, idx = p
        dx, dy = bx - ax, by - ay; l = math.hypot(dx, dy) or 1e-6; ux, uy = dx / l, dy / l; nx, ny = -uy * t / 2, ux * t / 2
        c = [(ax + nx, ay + ny), (bx + nx, by + ny), (bx - nx, by - ny), (ax - nx, ay - ny)]
        def quad(vs, n):
            base = len(pos) // 3
            for v in vs: pos.extend(v); nrm.extend(n); uv.extend((v[0], 1 - v[1]))
            idx.extend((base, base + 1, base + 2, base, base + 2, base + 3))
        quad([(c[0][0], c[0][1], z1), (c[3][0], c[3][1], z1), (c[2][0], c[2][1], z1), (c[1][0], c[1][1], z1)], (0, 0, 1))
        quad([(c[0][0], c[0][1], z0), (c[1][0], c[1][1], z0), (c[2][0], c[2][1], z0), (c[3][0], c[3][1], z0)], (0, 0, -1))
        for i in range(4):
            a, b = c[i], c[(i + 1) % 4]; ex, ey = b[0] - a[0], b[1] - a[1]; el = math.hypot(ex, ey) or 1e-6
            quad([(a[0], a[1], z0), (a[0], a[1], z1), (b[0], b[1], z1), (b[0], b[1], z0)], (ey / el, -ex / el, 0))

    def fan(self, mat, cx, cy, r, a0, a1, n, z):
        # a flat fan (a pane in an arch or a round window), both sides
        p = self.prims.setdefault(mat, [[], [], [], []]); pos, nrm, uv, idx = p
        for side in (1, -1):
            base = len(pos) // 3; pos.extend((cx, cy, z * side)); nrm.extend((0, 0, side)); uv.extend((0.5, 0.5))
            for i in range(n + 1):
                a = a0 + (a1 - a0) * i / n; pos.extend((cx + r * math.cos(a), cy + r * math.sin(a), z * side)); nrm.extend((0, 0, side)); uv.extend((0.5 + 0.5 * math.cos(a), 0.5 - 0.5 * math.sin(a)))
            for i in range(n):
                if side > 0: idx.extend((base, base + 1 + i, base + 2 + i))
                else: idx.extend((base, base + 2 + i, base + 1 + i))


MATS = {
    'White': {'c': (0.93, 0.93, 0.91), 'm': 0.0, 'r': 0.6},
    'Offwhite': {'c': (0.86, 0.85, 0.80), 'm': 0.0, 'r': 0.6},
    'Anthracite': {'c': (0.17, 0.18, 0.19), 'm': 0.3, 'r': 0.45},
    'Aluminium': {'c': (0.62, 0.64, 0.66), 'm': 0.8, 'r': 0.35},
    'Steel': {'c': (0.46, 0.48, 0.50), 'm': 0.5, 'r': 0.5},
    'Chrome': {'c': (0.80, 0.80, 0.82), 'm': 1.0, 'r': 0.2},
    'Brass': {'c': (0.78, 0.62, 0.30), 'm': 1.0, 'r': 0.3},
    'Black': {'c': (0.06, 0.06, 0.06), 'm': 0.2, 'r': 0.5},
    'Groove': {'c': (0.22, 0.15, 0.10), 'm': 0.0, 'r': 0.8},
    'Green': {'c': (0.20, 0.33, 0.25), 'm': 0.0, 'r': 0.6},
    'Grey_Light': {'c': (0.74, 0.75, 0.76), 'm': 0.2, 'r': 0.5},
    'Red': {'c': (0.55, 0.10, 0.09), 'm': 0.0, 'r': 0.5},
    'Navy': {'c': (0.10, 0.15, 0.28), 'm': 0.0, 'r': 0.5},
    'Teal': {'c': (0.12, 0.38, 0.40), 'm': 0.0, 'r': 0.6},
    'Cream': {'c': (0.92, 0.88, 0.78), 'm': 0.0, 'r': 0.6},
    'Stone': {'c': (0.72, 0.69, 0.63), 'm': 0.0, 'r': 0.9},
    'Glass': {'c': (0.70, 0.84, 0.92, 0.30), 'm': 0.0, 'r': 0.05, 'blend': True},
    'Glass_Frosted': {'c': (0.88, 0.92, 0.95, 0.55), 'm': 0.0, 'r': 0.4, 'blend': True},
    'Oak': {'c': (1, 1, 1), 'm': 0.0, 'r': 0.7, 'tex': ('oak', (184, 140, 96))},
    'Walnut': {'c': (1, 1, 1), 'm': 0.0, 'r': 0.7, 'tex': ('walnut', (104, 70, 48))},
}


def write_glb(path, title, units):
    # units: [(node name, Mesh)]; one unit is a door or a frame, several are windows side by side
    bin_ = bytearray(); views = []; accs = []
    def view(data, target=None):
        while len(bin_) % 4: bin_.append(0)
        v = {'buffer': 0, 'byteOffset': len(bin_), 'byteLength': len(data)}
        if target: v['target'] = target
        bin_.extend(data); views.append(v); return len(views) - 1
    def acc(vals, comps, ctype, typ, target, mm=False):
        fmt = '<%d%s' % (len(vals), 'f' if ctype == 5126 else 'I')
        a = {'bufferView': view(struct.pack(fmt, *vals), target), 'componentType': ctype, 'count': len(vals) // comps, 'type': typ}
        if mm: a['min'] = [min(vals[i::comps]) for i in range(comps)]; a['max'] = [max(vals[i::comps]) for i in range(comps)]
        accs.append(a); return len(accs) - 1
    used = []
    for _, m in units:
        for k in m.prims:
            if k not in used: used.append(k)
    images, textures, materials, texOf = [], [], [], {}
    for k in used:
        d = MATS[k]; c = d['c']; mat = {'name': k, 'pbrMetallicRoughness': {'baseColorFactor': [c[0], c[1], c[2], c[3] if len(c) > 3 else 1.0], 'metallicFactor': d['m'], 'roughnessFactor': d['r']}}
        if d.get('blend'): mat['alphaMode'] = 'BLEND'
        if 'tex' in d:
            nm, base = d['tex']
            if nm not in texOf:
                images.append({'bufferView': view(wood(base, seed=len(images) + 1)), 'mimeType': 'image/png', 'name': nm}); textures.append({'source': len(images) - 1, 'sampler': 0}); texOf[nm] = len(textures) - 1
            mat['pbrMetallicRoughness']['baseColorTexture'] = {'index': texOf[nm]}
        materials.append(mat)
    meshes, nodes = [], []
    for name, m in units:
        prims = []
        for k, (pos, nrm, uv, idx) in m.prims.items():
            prims.append({'attributes': {'POSITION': acc(pos, 3, 5126, 'VEC3', 34962, True), 'NORMAL': acc(nrm, 3, 5126, 'VEC3', 34962), 'TEXCOORD_0': acc(uv, 2, 5126, 'VEC2', 34962)},
                          'indices': acc(idx, 1, 5125, 'SCALAR', 34963), 'material': used.index(k)})
        meshes.append({'name': name, 'primitives': prims})
    if len(units) == 1:
        nodes.append({'name': units[0][0], 'mesh': 0}); roots = [0]
    else:
        nodes.append({'name': title, 'children': list(range(1, len(units) + 1))}); roots = [0]; x = 0.0
        for i, (name, m) in enumerate(units):
            xs = [m.prims[k][0][j] for k in m.prims for j in range(0, len(m.prims[k][0]), 3)]; w = max(xs) - min(xs)
            nodes.append({'name': name, 'mesh': i, 'translation': [x + w / 2, 0, 0]}); x += w + 0.6
    gltf = {'asset': {'version': '2.0', 'generator': 'MLO Creator make_models.py', 'extras': dict(CREDIT, title=title)}, 'scene': 0, 'scenes': [{'nodes': roots}], 'nodes': nodes, 'meshes': meshes,
            'materials': materials, 'accessors': accs, 'bufferViews': views, 'buffers': [{'byteLength': 0}]}
    if images: gltf['images'] = images; gltf['textures'] = textures; gltf['samplers'] = [{'magFilter': 9729, 'minFilter': 9987, 'wrapS': 10497, 'wrapT': 10497}]
    while len(bin_) % 4: bin_.append(0)
    gltf['buffers'][0]['byteLength'] = len(bin_)
    js = json.dumps(gltf, separators=(',', ':')).encode('utf-8')
    while len(js) % 4: js += b' '
    out = struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(bin_)) + struct.pack('<II', len(js), 0x4E4F534A) + js + struct.pack('<II', len(bin_), 0x004E4942) + bytes(bin_)
    with open(path, 'wb') as f: f.write(out)
    print('%-34s %7.1f KB  %s' % (os.path.basename(path), len(out) / 1024, ', '.join(n for n, _ in units)))


# ---------- windows ----------
def window(w, h, frame='White', glass='Glass', cols=1, rows=None, fw=0.06, bar=0.035, d=0.10, sill=True):
    # rows: the heights of the rows as fractions from the bottom (default: equal); cols: equal columns
    rows = rows or [1.0]; m = Mesh(); x0, x1 = -w / 2, w / 2
    m.box(frame, x0, x0 + fw, 0, h, -d / 2, d / 2); m.box(frame, x1 - fw, x1, 0, h, -d / 2, d / 2)
    m.box(frame, x0 + fw, x1 - fw, 0, fw, -d / 2, d / 2); m.box(frame, x0 + fw, x1 - fw, h - fw, h, -d / 2, d / 2)
    iw, ih = w - 2 * fw, h - 2 * fw
    xs = [x0 + fw + iw * i / cols for i in range(cols + 1)]
    tot = sum(rows); ys = [fw]
    for r in rows: ys.append(ys[-1] + ih * r / tot)
    for x in xs[1:-1]: m.box(frame, x - bar / 2, x + bar / 2, fw, h - fw, -d * 0.35, d * 0.35)
    for y in ys[1:-1]: m.box(frame, x0 + fw, x1 - fw, y - bar / 2, y + bar / 2, -d * 0.35, d * 0.35)
    for i in range(cols):
        for j in range(len(rows)):
            m.box(glass, xs[i] + (bar / 2 if i else 0), xs[i + 1] - (bar / 2 if i < cols - 1 else 0), ys[j] + (bar / 2 if j else 0), ys[j + 1] - (bar / 2 if j < len(rows) - 1 else 0), -0.004, 0.004)
    if sill: m.box(frame, x0 - 0.03, x1 + 0.03, -0.035, 0, -d / 2 - 0.05, d / 2 + 0.02)
    return m


# ---------- doors (0.9 x 2.1 m, 4.5 cm thick, the knob on one side) ----------
W, H, T = 0.9, 2.1, 0.045


def knob(m, mat, lever=False, y=1.0, x=None):
    x = (W / 2 - 0.075) if x is None else x
    for s in (1, -1):
        z0, z1 = sorted((s * T / 2, s * (T / 2 + 0.06)))
        m.box(mat, x - 0.025, x + 0.025, y - 0.025, y + 0.025, z0, z1)                                    # the rose and the stem
        if lever:
            zl0, zl1 = sorted((s * (T / 2 + 0.045), s * (T / 2 + 0.065))); m.box(mat, x - 0.13, x + 0.01, y - 0.012, y + 0.012, zl0, zl1)
        else:
            zk0, zk1 = sorted((s * (T / 2 + 0.04), s * (T / 2 + 0.075))); m.box(mat, x - 0.035, x + 0.035, y - 0.035, y + 0.035, zk0, zk1)


def door_panel(slab='White', panel='Offwhite', handle='Chrome', cols=2, rows=None):
    m = Mesh(); m.box(slab, -W / 2, W / 2, 0, H, -T / 2, T / 2)
    rows = rows or [(0.12, 0.62), (0.72, 1.32), (1.42, 1.98)]
    for s in (1, -1):
        z0, z1 = sorted((s * T / 2, s * (T / 2 + 0.008)))
        pw = (W - 0.2 - 0.08 * (cols - 1)) / cols
        for i in range(cols):
            xa = -W / 2 + 0.1 + i * (pw + 0.08); xb = xa + pw
            for (ya, yb) in rows: m.box(panel, xa, xb, ya, yb, z0, z1)
    knob(m, handle); return m


def door_flush(slab='Oak', handle='Brass', grooves=0, groove_mat='Groove', lever=True):
    m = Mesh(); m.box(slab, -W / 2, W / 2, 0, H, -T / 2, T / 2)
    for g in range(grooves):
        y = H * (g + 1) / (grooves + 1)
        for s in (1, -1):
            z0, z1 = sorted((s * T / 2, s * (T / 2 + 0.002))); m.box(groove_mat, -W / 2 + 0.06, W / 2 - 0.06, y - 0.006, y + 0.006, z0, z1)
    knob(m, handle, lever=lever); return m


def door_glass(frame='Aluminium', handle='Steel'):
    m = Mesh(); s, top, bot = 0.09, 0.09, 0.22
    m.box(frame, -W / 2, -W / 2 + s, 0, H, -T / 2, T / 2); m.box(frame, W / 2 - s, W / 2, 0, H, -T / 2, T / 2)
    m.box(frame, -W / 2 + s, W / 2 - s, 0, bot, -T / 2, T / 2); m.box(frame, -W / 2 + s, W / 2 - s, H - top, H, -T / 2, T / 2)
    m.box('Glass', -W / 2 + s, W / 2 - s, bot, H - top, -0.005, 0.005)
    for sg in (1, -1):                                                                                     # a long bar handle on both faces
        x = W / 2 - 0.12; za, zb = sorted((sg * T / 2, sg * (T / 2 + 0.05))); zc, zd = sorted((sg * (T / 2 + 0.05), sg * (T / 2 + 0.07)))
        m.box(handle, x - 0.012, x + 0.012, 0.75, 0.79, za, zb); m.box(handle, x - 0.012, x + 0.012, 1.31, 1.35, za, zb); m.box(handle, x - 0.016, x + 0.016, 0.72, 1.38, zc, zd)
    return m


def door_half_glass(slab='White', handle='Chrome'):
    m = Mesh(); s, mid = 0.11, 1.05
    m.box(slab, -W / 2, W / 2, 0, mid, -T / 2, T / 2)
    m.box(slab, -W / 2, -W / 2 + s, mid, H, -T / 2, T / 2); m.box(slab, W / 2 - s, W / 2, mid, H, -T / 2, T / 2); m.box(slab, -W / 2 + s, W / 2 - s, H - s, H, -T / 2, T / 2)
    m.box('Glass', -W / 2 + s, W / 2 - s, mid, H - s, -0.005, 0.005)
    m.box(slab, -0.01, 0.01, mid, H - s, -0.012, 0.012); m.box(slab, -W / 2 + s, W / 2 - s, mid + (H - s - mid) / 2 - 0.01, mid + (H - s - mid) / 2 + 0.01, -0.012, 0.012)
    for sg in (1, -1):
        z0, z1 = sorted((sg * T / 2, sg * (T / 2 + 0.008))); m.box('Offwhite', -W / 2 + 0.1, W / 2 - 0.1, 0.15, mid - 0.12, z0, z1)
    knob(m, handle); return m


def door_metal():
    m = Mesh(); m.box('Steel', -W / 2, W / 2, 0, H, -T / 2, T / 2)
    for sg in (1, -1):
        z0, z1 = sorted((sg * T / 2, sg * (T / 2 + 0.003))); m.box('Aluminium', -W / 2 + 0.03, W / 2 - 0.03, 0.02, 0.27, z0, z1)        # kick plate
    knob(m, 'Black', lever=True); return m


def door_modern():
    m = Mesh(); m.box('Anthracite', -W / 2, W / 2, 0, H, -T / 2, T / 2)
    for sg in (1, -1):
        x = W / 2 - 0.1; za, zb = sorted((sg * T / 2, sg * (T / 2 + 0.04))); zc, zd = sorted((sg * (T / 2 + 0.04), sg * (T / 2 + 0.06)))
        m.box('Chrome', x - 0.01, x + 0.01, 0.65, 0.68, za, zb); m.box('Chrome', x - 0.01, x + 0.01, 1.42, 1.45, za, zb); m.box('Chrome', x - 0.014, x + 0.014, 0.6, 1.5, zc, zd)
    return m


# ---------- doors with windows in them (French doors), and more ----------
def door_french(frame='White', cols=2, rows=5, w=W, h=H, bottom=0.22):
    # a glazed door: stiles and rails round panes with glazing bars, a solid kick rail at the bottom
    m = Mesh(); s, top = 0.1, 0.1
    m.box(frame, -w / 2, -w / 2 + s, 0, h, -T / 2, T / 2); m.box(frame, w / 2 - s, w / 2, 0, h, -T / 2, T / 2)
    m.box(frame, -w / 2 + s, w / 2 - s, 0, bottom, -T / 2, T / 2); m.box(frame, -w / 2 + s, w / 2 - s, h - top, h, -T / 2, T / 2)
    gx0, gx1, gy0, gy1, b = -w / 2 + s, w / 2 - s, bottom, h - top, 0.025
    m.box('Glass', gx0, gx1, gy0, gy1, -0.005, 0.005)
    for i in range(1, cols): x = gx0 + (gx1 - gx0) * i / cols; m.box(frame, x - b / 2, x + b / 2, gy0, gy1, -0.018, 0.018)
    for j in range(1, rows): y = gy0 + (gy1 - gy0) * j / rows; m.box(frame, gx0, gx1, y - b / 2, y + b / 2, -0.018, 0.018)
    return m


def door_glazed_black():
    m = door_french('Black', cols=1, rows=4, bottom=0.12); knob(m, 'Chrome', lever=True); return m


def door_patio():
    # a sliding patio door, 2.4 x 2.2 m: two glass panels in an aluminium frame, the one in front a little forward
    m = Mesh(); w, h, f = 2.4, 2.2, 0.06
    m.box('Aluminium', -w / 2, w / 2, h - f, h, -0.06, 0.06); m.box('Aluminium', -w / 2, w / 2, 0, f * 0.6, -0.06, 0.06)
    m.box('Aluminium', -w / 2, -w / 2 + f, 0, h, -0.06, 0.06); m.box('Aluminium', w / 2 - f, w / 2, 0, h, -0.06, 0.06)
    for k, (x0, x1, z) in enumerate([(-w / 2 + f, 0.03, -0.03), (-0.03, w / 2 - f, 0.03)]):
        m.box('Aluminium', x0, x0 + 0.05, f * 0.6, h - f, z - 0.02, z + 0.02); m.box('Aluminium', x1 - 0.05, x1, f * 0.6, h - f, z - 0.02, z + 0.02)
        m.box('Aluminium', x0, x1, f * 0.6, f * 0.6 + 0.08, z - 0.02, z + 0.02); m.box('Aluminium', x0, x1, h - f - 0.05, h - f, z - 0.02, z + 0.02)
        m.box('Glass', x0 + 0.05, x1 - 0.05, f * 0.6 + 0.08, h - f - 0.05, z - 0.004, z + 0.004)
    m.box('Black', 0.05, 0.075, 0.85, 1.25, 0.05, 0.07); m.box('Black', -0.075, -0.05, 0.85, 1.25, -0.07, -0.05)
    return m


def door_barn():
    # planks with a Z brace on both faces
    m = Mesh(); m.box('Oak', -W / 2, W / 2, 0, H, -T / 2, T / 2)
    for i in range(1, 6):
        x = -W / 2 + W * i / 6
        for s in (1, -1): z0, z1 = sorted((s * T / 2, s * (T / 2 + 0.002))); m.box('Groove', x - 0.004, x + 0.004, 0.02, H - 0.02, z0, z1)
    for s in (1, -1):
        za, zb = sorted((s * T / 2, s * (T / 2 + 0.025)))
        for y in (0.25, H - 0.35): m.box('Walnut', -W / 2 + 0.05, W / 2 - 0.05, y, y + 0.14, za, zb)
        m.bar('Walnut', -W / 2 + 0.12, 0.39, W / 2 - 0.12, H - 0.35, 0.13, za, zb)
    knob(m, 'Black', lever=True); return m


def door_cottage():
    # painted boards with a small window of four panes at the top
    m = Mesh(); wy0, wy1, wx = 1.45, 1.9, 0.22
    m.box('Green', -W / 2, W / 2, 0, wy0, -T / 2, T / 2); m.box('Green', -W / 2, W / 2, wy1, H, -T / 2, T / 2)
    m.box('Green', -W / 2, -wx, wy0, wy1, -T / 2, T / 2); m.box('Green', wx, W / 2, wy0, wy1, -T / 2, T / 2)
    m.box('Glass', -wx, wx, wy0, wy1, -0.005, 0.005); m.box('White', -0.012, 0.012, wy0, wy1, -0.015, 0.015); m.box('White', -wx, wx, (wy0 + wy1) / 2 - 0.012, (wy0 + wy1) / 2 + 0.012, -0.015, 0.015)
    for s in (1, -1):
        z0, z1 = sorted((s * T / 2, s * (T / 2 + 0.006))); m.box('White', -wx - 0.03, wx + 0.03, wy0 - 0.03, wy0, z0, z1); m.box('White', -wx - 0.03, wx + 0.03, wy1, wy1 + 0.03, z0, z1)
        m.box('White', -wx - 0.03, -wx, wy0, wy1, z0, z1); m.box('White', wx, wx + 0.03, wy0, wy1, z0, z1)
    knob(m, 'Brass'); return m


def door_louvred():
    # a frame with slats across it, tilted (the gaps let air through, you cannot see through)
    m = Mesh(); s = 0.09
    m.box('White', -W / 2, -W / 2 + s, 0, H, -T / 2, T / 2); m.box('White', W / 2 - s, W / 2, 0, H, -T / 2, T / 2)
    m.box('White', -W / 2 + s, W / 2 - s, 0, 0.2, -T / 2, T / 2); m.box('White', -W / 2 + s, W / 2 - s, H - s, H, -T / 2, T / 2); m.box('White', -W / 2 + s, W / 2 - s, 1.0, 1.12, -T / 2, T / 2)
    pos, nrm, uv, idx = m.prims.setdefault('White', [[], [], [], []])
    for (y0, y1) in ((0.2, 1.0), (1.12, H - s)):
        n = int((y1 - y0) / 0.05)
        for i in range(n):
            # a slat: a thin board tilted 45 degrees about the width, seen from both sides
            y = y0 + (i + 0.5) * (y1 - y0) / n; base = len(pos) // 3
            for (x, yy, zz) in [(-W / 2 + s, y - 0.022, -0.022), (W / 2 - s, y - 0.022, -0.022), (W / 2 - s, y + 0.022, 0.022), (-W / 2 + s, y + 0.022, 0.022)]:
                pos.extend((x, yy, zz)); nrm.extend((0, 0.707, -0.707)); uv.extend((x, yy))
            idx.extend((base, base + 1, base + 2, base, base + 2, base + 3, base, base + 2, base + 1, base, base + 3, base + 2))
    knob(m, 'Chrome'); return m


def slab_with_window(m, mat, x0, x1, y0, y1, glass='Glass', w=W, h=H, t=T):
    # a solid leaf with a glazed hole in it (x0..x1, y0..y1)
    m.box(mat, -w / 2, w / 2, 0, y0, -t / 2, t / 2); m.box(mat, -w / 2, w / 2, y1, h, -t / 2, t / 2)
    m.box(mat, -w / 2, x0, y0, y1, -t / 2, t / 2); m.box(mat, x1, w / 2, y0, y1, -t / 2, t / 2)
    m.box(glass, x0, x1, y0, y1, -0.005, 0.005)


def door_front_fanlight(slab='Navy', handle='Brass'):
    # a front door: raised panels below, a row of three small panes at the top
    m = Mesh(); gx, gy0, gy1 = 0.3, 1.62, 1.92
    slab_with_window(m, slab, -gx, gx, gy0, gy1)
    for x in (-0.1, 0.1): m.box(slab, x - 0.012, x + 0.012, gy0, gy1, -0.015, 0.015)
    for s in (1, -1):
        z0, z1 = sorted((s * T / 2, s * (T / 2 + 0.01)))
        for (xa, xb) in ((-W / 2 + 0.1, -0.04), (0.04, W / 2 - 0.1)):
            for (ya, yb) in ((0.15, 0.7), (0.8, 1.5)): m.box(slab, xa, xb, ya, yb, z0, z1)
    m.box(handle, -0.14, 0.14, 1.53, 1.57, T / 2, T / 2 + 0.01)                                 # a letter plate on the outside
    knob(m, handle); return m


def door_vision(slab='Oak', glass='Glass_Frosted', handle='Steel'):
    # a flush door with a narrow upright window (an office or a bathroom door)
    m = Mesh(); slab_with_window(m, slab, -0.08, 0.08, 0.45, 1.85, glass); knob(m, handle, lever=True); return m


def door_fire_exit():
    # a red steel door: a small window, a push bar on one face and a kick plate
    m = Mesh(); slab_with_window(m, 'Red', -0.15, 0.15, 1.35, 1.75)
    za, zb = T / 2, T / 2 + 0.045; zc, zd = T / 2 + 0.045, T / 2 + 0.075
    for x in (-0.33, 0.3): m.box('Steel', x - 0.03, x + 0.03, 0.97, 1.07, za, zb)
    m.box('Steel', -0.36, 0.33, 0.99, 1.05, zc, zd)
    for s in (1, -1): z0, z1 = sorted((s * T / 2, s * (T / 2 + 0.003))); m.box('Steel', -W / 2 + 0.03, W / 2 - 0.03, 0.02, 0.27, z0, z1)
    knob(m, 'Black', lever=True); return m


def door_stable(mat='Teal', handle='Black'):
    # a stable (Dutch) door: two halves with a ledge on the lower one, four panes in the upper
    m = Mesh(); y = 1.05
    m.box(mat, -W / 2, W / 2, 0, y - 0.005, -T / 2, T / 2)
    gx0, gx1, gy0, gy1 = -W / 2 + 0.12, W / 2 - 0.12, y + 0.2, H - 0.12
    m.box(mat, -W / 2, W / 2, y + 0.005, gy0, -T / 2, T / 2); m.box(mat, -W / 2, W / 2, gy1, H, -T / 2, T / 2)
    m.box(mat, -W / 2, gx0, gy0, gy1, -T / 2, T / 2); m.box(mat, gx1, W / 2, gy0, gy1, -T / 2, T / 2)
    m.box('Glass', gx0, gx1, gy0, gy1, -0.005, 0.005)
    m.box(mat, -0.012, 0.012, gy0, gy1, -0.015, 0.015); m.box(mat, gx0, gx1, (gy0 + gy1) / 2 - 0.012, (gy0 + gy1) / 2 + 0.012, -0.015, 0.015)
    m.box(mat, -W / 2, W / 2, y - 0.045, y - 0.005, T / 2, T / 2 + 0.06)
    knob(m, handle, y=0.92); return m


def door_clear_glass():
    # a glass door: clear glass with steel fittings at the corners and a long bar handle
    m = Mesh(); m.box('Glass', -W / 2, W / 2, 0, H, -0.006, 0.006)
    for (y0, y1) in ((0, 0.09), (H - 0.09, H)):
        m.box('Steel', -W / 2, -W / 2 + 0.18, y0, y1, -0.018, 0.018)
    for sg in (1, -1):
        x = W / 2 - 0.12; za, zb = sorted((sg * 0.006, sg * 0.05)); zc, zd = sorted((sg * 0.05, sg * 0.07))
        m.box('Steel', x - 0.012, x + 0.012, 0.75, 0.79, za, zb); m.box('Steel', x - 0.012, x + 0.012, 1.31, 1.35, za, zb); m.box('Steel', x - 0.016, x + 0.016, 0.72, 1.38, zc, zd)
    return m


def door_ribbed(mat='Walnut', handle='Black'):
    # a modern door with upright ribs on both faces
    m = Mesh(); m.box(mat, -W / 2, W / 2, 0, H, -T / 2, T / 2)
    n = int((W - 0.08) / 0.06)
    for i in range(n):
        x = -W / 2 + 0.04 + (i + 0.5) * (W - 0.08) / n
        for s in (1, -1): z0, z1 = sorted((s * T / 2, s * (T / 2 + 0.006))); m.box(mat, x - 0.01, x + 0.01, 0.05, H - 0.05, z0, z1)
    knob(m, handle, lever=True); return m


def door_security():
    # a dark steel security door: raised steel bands, a peephole and a lever
    m = Mesh(); m.box('Anthracite', -W / 2, W / 2, 0, H, -T / 2, T / 2)
    for s in (1, -1):
        z0, z1 = sorted((s * T / 2, s * (T / 2 + 0.006)))
        for y in (0.25, 0.65, 1.35, 1.75): m.box('Steel', -W / 2 + 0.06, W / 2 - 0.06, y - 0.03, y + 0.03, z0, z1)
        z2, z3 = sorted((s * T / 2, s * (T / 2 + 0.015))); m.box('Black', -0.015, 0.015, 1.55, 1.58, z2, z3)
    knob(m, 'Chrome', lever=True); return m


# ---------- sliding doors (the name says "sliding": they slide aside into the wall, towards the side away from their handle) ----------
def flush_pull(m, mat, x, y0=0.85, y1=1.25, t=T):
    # a pull set into both faces near the leading edge (a sliding door has no knob)
    for s in (1, -1): z0, z1 = sorted((s * t / 2, s * (t / 2 + 0.004))); m.box(mat, x - 0.02, x + 0.02, y0, y1, z0, z1)


def door_sliding_glass(frame='Aluminium'):
    # a glass panel in a slim aluminium frame, an upright bar handle at the leading edge
    m = Mesh(); s = 0.06
    m.box(frame, -W / 2, -W / 2 + s, 0, H, -T / 2, T / 2); m.box(frame, W / 2 - s, W / 2, 0, H, -T / 2, T / 2)
    m.box(frame, -W / 2 + s, W / 2 - s, 0, 0.12, -T / 2, T / 2); m.box(frame, -W / 2 + s, W / 2 - s, H - s, H, -T / 2, T / 2)
    m.box('Glass', -W / 2 + s, W / 2 - s, 0.12, H - s, -0.005, 0.005)
    for sg in (1, -1): za, zb = sorted((sg * T / 2, sg * (T / 2 + 0.03))); m.box('Black', W / 2 - 0.05, W / 2 - 0.03, 0.7, 1.4, za, zb)
    return m


def door_sliding_wood(mat='Oak'):
    # a pocket door: a flush leaf with a pull set into each face
    m = Mesh(); m.box(mat, -W / 2, W / 2, 0, H, -T / 2, T / 2); flush_pull(m, 'Black', W / 2 - 0.08); return m


def door_sliding_barn():
    # a barn door on a track: boards, a Z brace and a long black pull
    m = Mesh(); m.box('Walnut', -W / 2, W / 2, 0, H, -T / 2, T / 2)
    for i in range(1, 6):
        x = -W / 2 + W * i / 6
        for s in (1, -1): z0, z1 = sorted((s * T / 2, s * (T / 2 + 0.002))); m.box('Groove', x - 0.004, x + 0.004, 0.02, H - 0.02, z0, z1)
    for s in (1, -1):
        za, zb = sorted((s * T / 2, s * (T / 2 + 0.025)))
        for y in (0.25, H - 0.35): m.box('Oak', -W / 2 + 0.05, W / 2 - 0.05, y, y + 0.14, za, zb)
        m.bar('Oak', -W / 2 + 0.12, 0.39, W / 2 - 0.12, H - 0.35, 0.13, za, zb)
        zc, zd = sorted((s * (T / 2 + 0.025), s * (T / 2 + 0.05))); m.box('Black', W / 2 - 0.1, W / 2 - 0.075, 0.8, 1.35, zc, zd)
    return m


def door_sliding_shop():
    # an automatic shop door: clear glass with a thin aluminium rail at the top and the bottom, nothing to take hold of
    m = Mesh(); m.box('Glass', -W / 2, W / 2, 0.06, H - 0.08, -0.006, 0.006)
    m.box('Aluminium', -W / 2, W / 2, 0, 0.06, -0.02, 0.02); m.box('Aluminium', -W / 2, W / 2, H - 0.08, H, -0.02, 0.02)
    m.box('Aluminium', -W / 2, -W / 2 + 0.03, 0.06, H - 0.08, -0.015, 0.015); m.box('Aluminium', W / 2 - 0.03, W / 2, 0.06, H - 0.08, -0.015, 0.015)
    return m


# ---------- swing doors (the name says "swing": they open both ways by themselves, like a kitchen or a café door) ----------
def door_swing_kitchen():
    # stainless steel with a round porthole, push plates and kick plates on both faces
    m = Mesh(); r, cy = 0.17, 1.5; n = 20
    m.box('Steel', -W / 2, W / 2, 0, cy - r, -T / 2, T / 2); m.box('Steel', -W / 2, W / 2, cy + r, H, -T / 2, T / 2)
    m.box('Steel', -W / 2, -r, cy - r, cy + r, -T / 2, T / 2); m.box('Steel', r, W / 2, cy - r, cy + r, -T / 2, T / 2)
    # the corners of the square hole round the porthole, then its ring and its glass
    for (qx, qy, a0) in ((r, r, 0), (-r, r, math.pi / 2), (-r, -r, math.pi), (r, -r, 3 * math.pi / 2)):
        p = m.prims.setdefault('Steel', [[], [], [], []]); pos, nrm, uv, idx = p
        for side in (1, -1):
            z = side * T / 2; base = len(pos) // 3; pos.extend((qx, cy + qy, z)); nrm.extend((0, 0, side)); uv.extend((0, 0))
            for i in range(9):
                a = a0 + (math.pi / 2) * i / 8; pos.extend((r * math.cos(a), cy + r * math.sin(a), z)); nrm.extend((0, 0, side)); uv.extend((0, 0))
            for i in range(8):
                if side > 0: idx.extend((base, base + 2 + i, base + 1 + i))
                else: idx.extend((base, base + 1 + i, base + 2 + i))
    for i in range(n):
        a0, a1 = 2 * math.pi * i / n, 2 * math.pi * (i + 1) / n
        m.bar('Chrome', r * math.cos(a0), cy + r * math.sin(a0), r * math.cos(a1), cy + r * math.sin(a1), 0.03, -T / 2 - 0.008, T / 2 + 0.008)
    m.fan('Glass', 0, cy, r, 0, 2 * math.pi, n, 0.004)
    for s in (1, -1):
        z0, z1 = sorted((s * T / 2, s * (T / 2 + 0.003)))
        m.box('Aluminium', -W / 2 + 0.03, W / 2 - 0.03, 0.02, 0.32, z0, z1); m.box('Aluminium', W / 2 - 0.2, W / 2 - 0.06, 0.9, 1.25, z0, z1)
    return m


def door_swing_cafe(mat='Oak'):
    # a café door: a panel below, glass with bars above, brass push plates on both faces
    m = door_french(mat, cols=2, rows=3, bottom=0.9)
    for s in (1, -1):
        z0, z1 = sorted((s * T / 2, s * (T / 2 + 0.004))); m.box('Brass', W / 2 - 0.2, W / 2 - 0.06, 0.95, 1.3, z0, z1)
        z2, z3 = sorted((s * T / 2, s * (T / 2 + 0.008))); m.box(mat, -W / 2 + 0.16, W / 2 - 0.16, 0.18, 0.72, z2, z3)
    return m


# ---------- garage doors (2.5 x 2.2 m): kind "door" (the file name says door), they take the size of the opening ----------
GW, GH, GT = 2.5, 2.2, 0.06


def garage_sectional():
    m = Mesh(); m.box('White', -GW / 2, GW / 2, 0, GH, -GT / 2, GT / 2)
    for i in range(1, 4):
        y = GH * i / 4
        for s in (1, -1): z0, z1 = sorted((s * GT / 2, s * (GT / 2 + 0.004))); m.box('Grey_Light', -GW / 2 + 0.02, GW / 2 - 0.02, y - 0.008, y + 0.008, z0, z1)
    for s in (1, -1):
        for i in range(4):
            y = GH * (i + 0.5) / 4
            for x in (-GW / 2 + 0.15, GW / 2 - 0.15): z0, z1 = sorted((s * GT / 2, s * (GT / 2 + 0.003))); m.box('Grey_Light', x - 0.05, x + 0.05, y - 0.15, y + 0.15, z0, z1)
    z0, z1 = sorted((GT / 2, GT / 2 + 0.04)); m.box('Black', -0.15, 0.15, 0.25, 0.29, z0, z1)
    return m


def garage_roller():
    m = Mesh(); m.box('Steel', -GW / 2, GW / 2, 0, GH, -GT / 2, GT / 2)
    n = int(GH / 0.08)
    for i in range(n):
        y = GH * (i + 0.5) / n
        for s in (1, -1): z0, z1 = sorted((s * GT / 2, s * (GT / 2 + 0.008))); m.box('Grey_Light', -GW / 2, GW / 2, y - 0.012, y + 0.012, z0, z1)
    m.box('Steel', -GW / 2 - 0.05, GW / 2 + 0.05, GH, GH + 0.3, -0.18, 0.18)                       # the box the shutter rolls into
    for x in (-GW / 2 - 0.04, GW / 2 + 0.04): m.box('Aluminium', x - 0.04, x + 0.04, 0, GH, -0.06, 0.06)     # the guide rails
    m.box('Aluminium', -GW / 2, GW / 2, 0, 0.05, -0.04, 0.04)
    return m


def garage_modern():
    m = Mesh(); m.box('Anthracite', -GW / 2, GW / 2, 0, GH, -GT / 2, GT / 2)
    for i in range(1, 8):
        y = GH * i / 8
        for s in (1, -1): z0, z1 = sorted((s * GT / 2, s * (GT / 2 + 0.003))); m.box('Black', -GW / 2 + 0.01, GW / 2 - 0.01, y - 0.005, y + 0.005, z0, z1)
    m.box('Glass', -GW / 2 + 0.2, GW / 2 - 0.2, GH * 6 / 8 + 0.04, GH * 7 / 8 - 0.04, -GT / 2 - 0.002, GT / 2 + 0.002)
    return m


def garage_wood():
    # carriage style: vertical boards, an X brace on each half, two handles in the middle
    m = Mesh(); m.box('Oak', -GW / 2, GW / 2, 0, GH, -GT / 2, GT / 2)
    for i in range(1, 12):
        x = -GW / 2 + GW * i / 12
        for s in (1, -1): z0, z1 = sorted((s * GT / 2, s * (GT / 2 + 0.002))); m.box('Groove', x - 0.004, x + 0.004, 0.02, GH - 0.02, z0, z1)
    s = 1; za, zb = GT / 2, GT / 2 + 0.03
    for (x0, x1) in ((-GW / 2 + 0.08, -0.04), (0.04, GW / 2 - 0.08)):
        m.box('Walnut', x0, x1, 0.08, 0.2, za, zb); m.box('Walnut', x0, x1, GH - 0.2, GH - 0.08, za, zb); m.box('Walnut', x0, x0 + 0.12, 0.2, GH - 0.2, za, zb); m.box('Walnut', x1 - 0.12, x1, 0.2, GH - 0.2, za, zb)
        m.bar('Walnut', x0 + 0.12, 0.2, x1 - 0.12, GH - 0.2, 0.1, za, zb); m.bar('Walnut', x0 + 0.12, GH - 0.2, x1 - 0.12, 0.2, 0.1, za, zb)
    for x in (-0.15, 0.15): m.box('Black', x - 0.012, x + 0.012, 1.0, 1.3, zb, zb + 0.04)
    return m


def garage_glass():
    m = Mesh(); m.box('Anthracite', -GW / 2, GW / 2, 0, GH, -GT / 2, GT / 2)
    for row in range(3):
        y0, y1 = GH * (row + 0.12) / 3, GH * (row + 0.88) / 3
        m.box('Glass', -GW / 2 + 0.14, GW / 2 - 0.14, y0, y1, -GT / 2 - 0.004, GT / 2 + 0.004)
        m.box('Black', -GW / 2 + 0.08, GW / 2 - 0.08, y0 - 0.025, y0 + 0.025, GT / 2, GT / 2 + 0.025)
    return m


def garage_ribbed():
    m = Mesh(); m.box('Black', -GW / 2, GW / 2, 0, GH, -GT / 2, GT / 2)
    for i in range(1, 18):
        x = -GW / 2 + GW * i / 18
        for s in (1, -1):
            z0, z1 = sorted((s * GT / 2, s * (GT / 2 + 0.012)))
            m.box('Aluminium', x - 0.018, x + 0.018, 0.03, GH - 0.03, z0, z1)
    for y in (0.03, GH - 0.03):
        m.box('Aluminium', -GW / 2, GW / 2, y - 0.025, y + 0.025, -GT / 2, GT / 2)
    return m


def garage_red():
    m = Mesh(); m.box('Red', -GW / 2, GW / 2, 0, GH, -GT / 2, GT / 2)
    for i in range(1, 4):
        y = GH * i / 4
        for s in (1, -1):
            z0, z1 = sorted((s * GT / 2, s * (GT / 2 + 0.006)))
            m.box('Black', -GW / 2 + 0.03, GW / 2 - 0.03, y - 0.012, y + 0.012, z0, z1)
    m.box('Black', -0.18, 0.18, 0.25, 0.29, GT / 2, GT / 2 + 0.04)
    return m


def garage_industrial():
    m = Mesh(); m.box('Steel', -GW / 2, GW / 2, 0, GH, -GT / 2, GT / 2)
    for x in (-GW / 2 + 0.06, GW / 2 - 0.06):
        m.box('Black', x - 0.035, x + 0.035, 0, GH, -GT / 2 - 0.02, GT / 2 + 0.02)
    for y in (0.04, GH - 0.04):
        m.box('Black', -GW / 2, GW / 2, y - 0.035, y + 0.035, -GT / 2 - 0.02, GT / 2 + 0.02)
    for x in (-0.65, 0.65):
        m.box('Glass', x - 0.38, x + 0.38, GH * 0.58, GH * 0.82, -GT / 2 - 0.004, GT / 2 + 0.004)
    return m


# ---------- more windows ----------
def window_bars():
    m = window(1.0, 1.2); bx = [-0.36 + 0.12 * i for i in range(7)]
    for x in bx: m.box('Black', x - 0.01, x + 0.01, 0.0, 1.2, 0.09, 0.11)
    for y in (0.1, 1.1): m.box('Black', -0.45, 0.45, y - 0.015, y + 0.015, 0.085, 0.115)
    return m


def window_arched(w=1.0, h=1.9, frame='White'):
    # a window with a round top: the square part, then a half circle of bars round a fan of glass
    m = Mesh(); r = w / 2; hs = h - r; d = 0.10; fw = 0.06
    m.box(frame, -w / 2, -w / 2 + fw, 0, hs, -d / 2, d / 2); m.box(frame, w / 2 - fw, w / 2, 0, hs, -d / 2, d / 2); m.box(frame, -w / 2 + fw, w / 2 - fw, 0, fw, -d / 2, d / 2)
    m.box(frame, -w / 2 + fw, w / 2 - fw, hs - 0.02, hs + 0.02, -d * 0.35, d * 0.35); m.box(frame, -0.0175, 0.0175, fw, hs, -d * 0.35, d * 0.35)
    m.box('Glass', -w / 2 + fw, w / 2 - fw, fw, hs, -0.004, 0.004)
    n = 14
    for i in range(n):
        a0, a1 = math.pi * i / n, math.pi * (i + 1) / n; rr = r - fw / 2
        m.bar(frame, rr * math.cos(a0), hs + rr * math.sin(a0), rr * math.cos(a1), hs + rr * math.sin(a1), fw, -d / 2, d / 2)
    m.fan('Glass', 0, hs, r - fw, 0, math.pi, n, 0.004)
    for a in (math.pi / 4, math.pi / 2, 3 * math.pi / 4): m.bar(frame, 0, hs, (r - fw) * math.cos(a), hs + (r - fw) * math.sin(a), 0.025, -d * 0.35, d * 0.35)
    m.box(frame, -w / 2 - 0.03, w / 2 + 0.03, -0.035, 0, -d / 2 - 0.05, d / 2 + 0.02)
    return m


def window_round(dia=0.7, frame='Aluminium'):
    m = Mesh(); r = dia / 2; n = 20; d = 0.12; fw = 0.07
    for i in range(n):
        a0, a1 = 2 * math.pi * i / n, 2 * math.pi * (i + 1) / n; rr = r - fw / 2
        m.bar(frame, rr * math.cos(a0), r + rr * math.sin(a0), rr * math.cos(a1), r + rr * math.sin(a1), fw, -d / 2, d / 2)
    m.fan('Glass', 0, r, r - fw + 0.005, 0, 2 * math.pi, n, 0.004)
    return m


def window_louvre(w=0.8, h=0.6):
    # a vent: slats in a frame, no glass
    m = window(w, h, glass='Grey_Light', sill=False); n = int((h - 0.12) / 0.06)
    for i in range(n): y = 0.06 + (i + 0.5) * (h - 0.12) / n; m.box('Grey_Light', -w / 2 + 0.06, w / 2 - 0.06, y - 0.012, y + 0.012, -0.03, 0.03)
    return m


def window_half_round(w=1.2, frame='White'):
    # a fanlight: a half circle with radial bars, standing on a straight bottom bar
    m = Mesh(); r = w / 2; d = 0.10; fw = 0.06; n = 14
    m.box(frame, -w / 2, w / 2, 0, fw, -d / 2, d / 2)
    for i in range(n):
        a0, a1 = math.pi * i / n, math.pi * (i + 1) / n; rr = r - fw / 2
        m.bar(frame, rr * math.cos(a0), fw + rr * math.sin(a0), rr * math.cos(a1), fw + rr * math.sin(a1), fw, -d / 2, d / 2)
    m.fan('Glass', 0, fw, r - fw, 0, math.pi, n, 0.004)
    for a in (math.pi / 6, math.pi / 3, math.pi / 2, 2 * math.pi / 3, 5 * math.pi / 6): m.bar(frame, 0, fw, (r - fw) * math.cos(a), fw + (r - fw) * math.sin(a), 0.022, -d * 0.35, d * 0.35)
    return m


def window_leaded(w=0.8, h=1.2, step=0.12):
    # a leaded light: a black frame with a lattice of thin diagonal cames over the glass
    m = window(w, h, frame='Black', fw=0.05, sill=True); xa, xb, ya, yb = -w / 2 + 0.05, w / 2 - 0.05, 0.05, h - 0.05
    for k in (1, -1):
        c = -2 * (w + h)
        while c < 2 * (w + h):
            # the line y = k x + c inside the glass
            lo, hi = (ya - c, yb - c) if k == 1 else (c - yb, c - ya); x0, x1 = max(xa, lo), min(xb, hi)
            if x1 - x0 > 0.01: m.bar('Black', x0, k * x0 + c, x1, k * x1 + c, 0.008, -0.007, 0.007)
            c += step * math.sqrt(2)
    return m


# ---------- frames: round a 0.9 x 2.1 m opening ----------
def door_frame(mat='White'):
    m = Mesh(); j, d, c, ct = 0.04, 0.16, 0.07, 0.015          # jamb thickness, wall depth, casing width and thickness
    ow = W + 0.01
    m.box(mat, -ow / 2 - j, -ow / 2, 0, H + 0.005, -d / 2, d / 2); m.box(mat, ow / 2, ow / 2 + j, 0, H + 0.005, -d / 2, d / 2); m.box(mat, -ow / 2 - j, ow / 2 + j, H + 0.005, H + 0.005 + j, -d / 2, d / 2)
    for s in (1, -1):
        z0, z1 = sorted((s * d / 2, s * (d / 2 + ct)))
        m.box(mat, -ow / 2 - j - c, -ow / 2, 0, H + 0.005 + j + c, z0, z1); m.box(mat, ow / 2, ow / 2 + j + c, 0, H + 0.005 + j + c, z0, z1)
        m.box(mat, -ow / 2, ow / 2, H + 0.005 + j, H + 0.005 + j + c, z0, z1)
    return m


def door_frame_classic(mat='White'):
    # wide casing with plinth blocks at the foot and a cap over the head
    m = Mesh(); j, d, c, ct = 0.04, 0.16, 0.10, 0.018; ow = W + 0.01; top = H + 0.005 + j + c
    m.box(mat, -ow / 2 - j, -ow / 2, 0, H + 0.005, -d / 2, d / 2); m.box(mat, ow / 2, ow / 2 + j, 0, H + 0.005, -d / 2, d / 2); m.box(mat, -ow / 2 - j, ow / 2 + j, H + 0.005, H + 0.005 + j, -d / 2, d / 2)
    for s in (1, -1):
        z0, z1 = sorted((s * d / 2, s * (d / 2 + ct))); zp0, zp1 = sorted((s * d / 2, s * (d / 2 + ct + 0.01)))
        m.box(mat, -ow / 2 - j - c, -ow / 2, 0.2, top, z0, z1); m.box(mat, ow / 2, ow / 2 + j + c, 0.2, top, z0, z1)
        m.box(mat, -ow / 2, ow / 2, H + 0.005 + j, top, z0, z1)
        for x0, x1 in ((-ow / 2 - j - c - 0.005, -ow / 2), (ow / 2, ow / 2 + j + c + 0.005)): m.box(mat, x0, x1, 0, 0.2, zp0, zp1)          # plinth blocks
        zc0, zc1 = sorted((s * d / 2, s * (d / 2 + ct + 0.025))); m.box(mat, -ow / 2 - j - c - 0.03, ow / 2 + j + c + 0.03, top, top + 0.06, zc0, zc1)          # the cap
    return m


def door_frame_transom(mat='White', th=0.45):
    # a frame with a glazed transom over the door
    m = Mesh(); j, d, c, ct = 0.04, 0.16, 0.07, 0.015; ow = W + 0.01; hd = H + 0.005; ht = hd + j + th
    m.box(mat, -ow / 2 - j, -ow / 2, 0, ht, -d / 2, d / 2); m.box(mat, ow / 2, ow / 2 + j, 0, ht, -d / 2, d / 2)
    m.box(mat, -ow / 2, ow / 2, hd, hd + j, -d / 2, d / 2); m.box(mat, -ow / 2 - j, ow / 2 + j, ht, ht + j, -d / 2, d / 2)
    m.box('Glass', -ow / 2, ow / 2, hd + j, ht, -0.004, 0.004)
    for x in (-ow / 6, ow / 6): m.box(mat, x - 0.012, x + 0.012, hd + j, ht, -0.015, 0.015)
    for s in (1, -1):
        z0, z1 = sorted((s * d / 2, s * (d / 2 + ct)))
        m.box(mat, -ow / 2 - j - c, -ow / 2, 0, ht + j + c, z0, z1); m.box(mat, ow / 2, ow / 2 + j + c, 0, ht + j + c, z0, z1)
        m.box(mat, -ow / 2, ow / 2, ht + j, ht + j + c, z0, z1)
    return m


def door_frame_sidelights(mat='White', sw=0.3):
    # a frame with a tall glazed side light on each side of the door (solid at the foot)
    m = Mesh(); j, d, c, ct = 0.04, 0.16, 0.07, 0.015; ow = W + 0.01; hd = H + 0.005; xo = ow / 2 + j + sw
    for sg in (1, -1):
        a, b = sorted((sg * ow / 2, sg * (ow / 2 + j))); m.box(mat, a, b, 0, hd, -d / 2, d / 2)                  # the post between the door and the side light
        a, b = sorted((sg * xo, sg * (xo + j))); m.box(mat, a, b, 0, hd, -d / 2, d / 2)                          # the outer post
        a, b = sorted((sg * (ow / 2 + j), sg * xo)); m.box(mat, a, b, 0, 0.35, -d / 2, d / 2); m.box('Glass', a, b, 0.35, hd, -0.004, 0.004)
    m.box(mat, -xo - j, xo + j, hd, hd + j, -d / 2, d / 2)
    for s in (1, -1):
        z0, z1 = sorted((s * d / 2, s * (d / 2 + ct)))
        m.box(mat, -xo - j - c, -xo - j, 0, hd + j + c, z0, z1); m.box(mat, xo + j, xo + j + c, 0, hd + j + c, z0, z1)
        m.box(mat, -xo - j, xo + j, hd + j, hd + j + c, z0, z1)
    return m


# ---------- window frames: round a 1.0 x 1.3 m window opening, with a sill (shut at the bottom: the program tells them from door frames by that) ----------
def window_frame(mat='White', w0=1.0, h0=1.3, c=0.07, ct=0.015, d=0.16, cap=False, keystone=False, sill_t=0.04):
    m = Mesh(); j = 0.04; xs = w0 / 2 + j + c + 0.04
    m.box(mat, -xs, xs, 0, sill_t, -d / 2 - 0.06, d / 2 + 0.02)                                              # the sill, standing out on the outside (-z)
    y0 = sill_t; yi0 = y0 + j; yi1 = yi0 + h0; top = yi1 + j + c
    m.box(mat, -w0 / 2 - j, w0 / 2 + j, y0, yi0, -d / 2, d / 2)                                                # the lining: bottom, sides, head
    m.box(mat, -w0 / 2 - j, -w0 / 2, yi0, yi1, -d / 2, d / 2); m.box(mat, w0 / 2, w0 / 2 + j, yi0, yi1, -d / 2, d / 2)
    m.box(mat, -w0 / 2 - j, w0 / 2 + j, yi1, yi1 + j, -d / 2, d / 2)
    for s in (1, -1):
        z0, z1 = sorted((s * d / 2, s * (d / 2 + ct)))
        m.box(mat, -w0 / 2 - j - c, -w0 / 2, y0, top, z0, z1); m.box(mat, w0 / 2, w0 / 2 + j + c, y0, top, z0, z1)
        m.box(mat, -w0 / 2, w0 / 2, yi1 + j, top, z0, z1)
        if s > 0: m.box(mat, -w0 / 2, w0 / 2, y0, yi0, z0, z1)                                                # the stool on the inside
        if cap: zc0, zc1 = sorted((s * d / 2, s * (d / 2 + ct + 0.025))); m.box(mat, -w0 / 2 - j - c - 0.03, w0 / 2 + j + c + 0.03, top, top + 0.06, zc0, zc1)
        if keystone: zk0, zk1 = sorted((s * d / 2, s * (d / 2 + ct + 0.02))); m.box(mat, -0.08, 0.08, yi1, top + 0.08, zk0, zk1)
    return m


if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    write_glb(os.path.join(OUT, 'mlo_windows_basic.glb'), 'MLO Windows', [
        ('Casement_Single', window(0.8, 1.2)),
        ('Casement_Double', window(1.4, 1.3, cols=2)),
        ('Cross_Four_Pane', window(1.2, 1.4, cols=2, rows=[1, 1])),
        ('Tall_With_Transom', window(0.9, 2.0, rows=[3, 1])),
        ('Sash_Six_Over_Six', window(1.0, 1.6, cols=3, rows=[1, 1, 1, 1], bar=0.025)),
        ('Small_Frosted', window(0.6, 0.6, glass='Glass_Frosted')),
        ('Casement_Double_Oak', window(1.4, 1.3, frame='Oak', cols=2)),
    ])
    write_glb(os.path.join(OUT, 'mlo_windows_modern.glb'), 'MLO Modern Windows', [
        ('Modern_Wide_Three', window(2.4, 1.4, frame='Anthracite', cols=3, fw=0.05, bar=0.04)),
        ('Modern_Square', window(1.2, 1.2, frame='Anthracite', fw=0.05)),
        ('Modern_Tall_Slim', window(0.6, 2.2, frame='Anthracite', fw=0.05, sill=False)),
        ('Shopfront', window(2.4, 2.5, frame='Aluminium', cols=2, rows=[4, 1], fw=0.07, bar=0.06, sill=False)),
        ('Aluminium_Sliding', window(1.8, 1.2, frame='Aluminium', cols=2, fw=0.05, bar=0.06)),
    ])
    write_glb(os.path.join(OUT, 'mlo_door_panel_white.glb'), 'Door Panel White', [('Door_Panel_White', door_panel())])
    write_glb(os.path.join(OUT, 'mlo_door_oak.glb'), 'Door Oak', [('Door_Oak', door_flush('Oak', 'Brass'))])
    write_glb(os.path.join(OUT, 'mlo_door_walnut_grooved.glb'), 'Door Walnut Grooved', [('Door_Walnut_Grooved', door_flush('Walnut', 'Black', grooves=4))])
    write_glb(os.path.join(OUT, 'mlo_door_glass_aluminium.glb'), 'Door Glass Aluminium', [('Door_Glass_Aluminium', door_glass())])
    write_glb(os.path.join(OUT, 'mlo_door_half_glass.glb'), 'Door Half Glass', [('Door_Half_Glass', door_half_glass())])
    write_glb(os.path.join(OUT, 'mlo_door_metal.glb'), 'Door Metal', [('Door_Metal', door_metal())])
    write_glb(os.path.join(OUT, 'mlo_door_modern_dark.glb'), 'Door Modern Dark', [('Door_Modern_Dark', door_modern())])
    write_glb(os.path.join(OUT, 'mlo_door_french_white.glb'), 'Door French White', [('Door_French_White', door_french('White'))])
    write_glb(os.path.join(OUT, 'mlo_door_french_oak.glb'), 'Door French Oak', [('Door_French_Oak', door_french('Oak', cols=2, rows=4))])
    write_glb(os.path.join(OUT, 'mlo_door_glazed_black.glb'), 'Door Glazed Black', [('Door_Glazed_Black', door_glazed_black())])
    write_glb(os.path.join(OUT, 'mlo_door_patio_sliding.glb'), 'Door Patio Sliding', [('Door_Patio_Sliding', door_patio())])
    write_glb(os.path.join(OUT, 'mlo_door_barn_oak.glb'), 'Door Barn Oak', [('Door_Barn_Oak', door_barn())])
    write_glb(os.path.join(OUT, 'mlo_door_cottage_green.glb'), 'Door Cottage Green', [('Door_Cottage_Green', door_cottage())])
    write_glb(os.path.join(OUT, 'mlo_door_louvred_white.glb'), 'Door Louvred White', [('Door_Louvred_White', door_louvred())])
    write_glb(os.path.join(OUT, 'mlo_garage_door_sectional.glb'), 'Garage Door Sectional', [('Garage_Door_Sectional', garage_sectional())])
    write_glb(os.path.join(OUT, 'mlo_garage_door_roller.glb'), 'Garage Door Roller Shutter', [('Garage_Door_Roller', garage_roller())])
    write_glb(os.path.join(OUT, 'mlo_garage_door_modern.glb'), 'Garage Door Modern', [('Garage_Door_Modern', garage_modern())])
    write_glb(os.path.join(OUT, 'mlo_garage_door_wood.glb'), 'Garage Door Wood', [('Garage_Door_Wood', garage_wood())])
    write_glb(os.path.join(OUT, 'mlo_garage_door_glass.glb'), 'Garage Door Glass', [('Garage_Door_Glass', garage_glass())])
    write_glb(os.path.join(OUT, 'mlo_garage_door_ribbed.glb'), 'Garage Door Ribbed', [('Garage_Door_Ribbed', garage_ribbed())])
    write_glb(os.path.join(OUT, 'mlo_garage_door_red.glb'), 'Garage Door Red', [('Garage_Door_Red', garage_red())])
    write_glb(os.path.join(OUT, 'mlo_garage_door_industrial.glb'), 'Garage Door Industrial', [('Garage_Door_Industrial', garage_industrial())])
    write_glb(os.path.join(OUT, 'mlo_windows_special.glb'), 'MLO Special Windows', [
        ('Arched_Top', window_arched()),
        ('Round_Porthole', window_round()),
        ('Security_Bars', window_bars()),
        ('Industrial_Grid', window(1.6, 1.4, frame='Black', cols=4, rows=[1, 1, 1], fw=0.045, bar=0.025)),
        ('Transom_Wide', window(1.6, 0.5, cols=2, sill=False)),
        ('Vent_Louvre', window_louvre()),
    ])
    write_glb(os.path.join(OUT, 'mlo_door_frame_white.glb'), 'Door Frame White', [('Door_Frame_White', door_frame('White'))])
    write_glb(os.path.join(OUT, 'mlo_door_frame_oak.glb'), 'Door Frame Oak', [('Door_Frame_Oak', door_frame('Oak'))])

    # ---- more windows ----
    write_glb(os.path.join(OUT, 'mlo_windows_classic.glb'), 'MLO Classic Windows', [
        ('Sash_Two_Over_Two', window(0.9, 1.6, cols=2, rows=[1, 1], bar=0.025)),
        ('Georgian_Twelve_Pane', window(1.0, 1.5, frame='Offwhite', cols=3, rows=[1, 1, 1, 1], bar=0.02)),
        ('Casement_Triple', window(2.0, 1.3, cols=3)),
        ('Gothic_Arch_Oak', window_arched(0.9, 1.8, 'Oak')),
        ('Arched_Wide', window_arched(1.6, 2.0)),
        ('Half_Round_Fanlight', window_half_round()),
        ('Leaded_Diamond', window_leaded()),
        ('Porthole_Brass', window_round(0.5, 'Brass')),
        ('Cottage_Green', window(0.8, 1.0, frame='Green', cols=2, rows=[1, 1], bar=0.025)),
    ])
    write_glb(os.path.join(OUT, 'mlo_windows_more.glb'), 'MLO More Windows', [
        ('Picture_Window', window(1.8, 1.5, fw=0.05)),
        ('Tilt_Turn_Anthracite', window(0.9, 1.4, frame='Anthracite', fw=0.05)),
        ('Bathroom_Frosted_Tall', window(0.5, 1.0, glass='Glass_Frosted')),
        ('Basement_Hopper', window(1.0, 0.45, sill=False)),
        ('Ribbon_Window', window(3.0, 0.6, frame='Anthracite', cols=4, fw=0.05, bar=0.04, sill=False)),
        ('Shopfront_Wide', window(3.2, 2.6, frame='Aluminium', cols=3, rows=[4, 1], fw=0.07, bar=0.06, sill=False)),
        ('Store_Display_Black', window(2.0, 2.2, frame='Black', cols=2, fw=0.06, bar=0.05, sill=False)),
        ('Glass_Block', window(1.0, 1.0, frame='Grey_Light', glass='Glass_Frosted', cols=5, rows=[1, 1, 1, 1, 1], fw=0.05, bar=0.03, sill=False)),
        ('Kitchen_Double_Red', window(1.2, 1.0, frame='Red', cols=2, fw=0.05)),
    ])

    # ---- more doors ----
    write_glb(os.path.join(OUT, 'mlo_door_front_navy.glb'), 'Door Front Navy', [('Door_Front_Navy', door_front_fanlight('Navy', 'Brass'))])
    write_glb(os.path.join(OUT, 'mlo_door_front_red.glb'), 'Door Front Red', [('Door_Front_Red', door_front_fanlight('Red', 'Brass'))])
    write_glb(os.path.join(OUT, 'mlo_door_four_panel_oak.glb'), 'Door Four Panel Oak', [('Door_Four_Panel_Oak', door_panel('Oak', 'Oak', 'Brass', rows=[(0.12, 0.95), (1.05, 1.98)]))])
    write_glb(os.path.join(OUT, 'mlo_door_shaker_grey.glb'), 'Door Shaker Grey', [('Door_Shaker_Grey', door_panel('Grey_Light', 'Grey_Light', 'Black', cols=1, rows=[(0.15, 0.95), (1.05, 1.95)]))])
    write_glb(os.path.join(OUT, 'mlo_door_cream_six_panel.glb'), 'Door Cream Six Panel', [('Door_Cream_Six_Panel', door_panel('Cream', 'Offwhite', 'Brass'))])
    write_glb(os.path.join(OUT, 'mlo_door_bathroom_frosted.glb'), 'Door Bathroom Frosted', [('Door_Bathroom_Frosted', door_vision('White', 'Glass_Frosted', 'Chrome'))])
    write_glb(os.path.join(OUT, 'mlo_door_office_vision.glb'), 'Door Office Vision', [('Door_Office_Vision', door_vision('Oak', 'Glass', 'Steel'))])
    write_glb(os.path.join(OUT, 'mlo_door_fire_exit.glb'), 'Door Fire Exit', [('Door_Fire_Exit', door_fire_exit())])
    write_glb(os.path.join(OUT, 'mlo_door_stable_teal.glb'), 'Door Stable Teal', [('Door_Stable_Teal', door_stable())])
    write_glb(os.path.join(OUT, 'mlo_door_glass_clear.glb'), 'Door Glass Clear', [('Door_Glass_Clear', door_clear_glass())])
    write_glb(os.path.join(OUT, 'mlo_door_ribbed_walnut.glb'), 'Door Ribbed Walnut', [('Door_Ribbed_Walnut', door_ribbed())])
    write_glb(os.path.join(OUT, 'mlo_door_security_steel.glb'), 'Door Security Steel', [('Door_Security_Steel', door_security())])

    # ---- sliding doors and swing doors (they open by themselves in the game: the export's client/doors.lua) ----
    write_glb(os.path.join(OUT, 'mlo_door_sliding_glass.glb'), 'Door Sliding Glass', [('Door_Sliding_Glass', door_sliding_glass())])
    write_glb(os.path.join(OUT, 'mlo_door_sliding_glass_black.glb'), 'Door Sliding Glass Black', [('Door_Sliding_Glass_Black', door_sliding_glass('Black'))])
    write_glb(os.path.join(OUT, 'mlo_door_sliding_oak.glb'), 'Door Sliding Oak', [('Door_Sliding_Oak', door_sliding_wood('Oak'))])
    write_glb(os.path.join(OUT, 'mlo_door_sliding_white.glb'), 'Door Sliding White', [('Door_Sliding_White', door_sliding_wood('White'))])
    write_glb(os.path.join(OUT, 'mlo_door_sliding_barn.glb'), 'Door Sliding Barn', [('Door_Sliding_Barn', door_sliding_barn())])
    write_glb(os.path.join(OUT, 'mlo_door_sliding_shop.glb'), 'Door Sliding Shop', [('Door_Sliding_Shop', door_sliding_shop())])
    write_glb(os.path.join(OUT, 'mlo_door_swing_kitchen.glb'), 'Door Swing Kitchen', [('Door_Swing_Kitchen', door_swing_kitchen())])
    write_glb(os.path.join(OUT, 'mlo_door_swing_cafe.glb'), 'Door Swing Cafe', [('Door_Swing_Cafe', door_swing_cafe())])

    # ---- more door frames ----
    write_glb(os.path.join(OUT, 'mlo_door_frame_walnut.glb'), 'Door Frame Walnut', [('Door_Frame_Walnut', door_frame('Walnut'))])
    write_glb(os.path.join(OUT, 'mlo_door_frame_black.glb'), 'Door Frame Black', [('Door_Frame_Black', door_frame('Black'))])
    write_glb(os.path.join(OUT, 'mlo_door_frame_anthracite.glb'), 'Door Frame Anthracite', [('Door_Frame_Anthracite', door_frame('Anthracite'))])
    write_glb(os.path.join(OUT, 'mlo_door_frame_classic_white.glb'), 'Door Frame Classic White', [('Door_Frame_Classic_White', door_frame_classic('White'))])
    write_glb(os.path.join(OUT, 'mlo_door_frame_classic_oak.glb'), 'Door Frame Classic Oak', [('Door_Frame_Classic_Oak', door_frame_classic('Oak'))])
    write_glb(os.path.join(OUT, 'mlo_door_frame_transom_white.glb'), 'Door Frame Transom White', [('Door_Frame_Transom_White', door_frame_transom('White'))])
    write_glb(os.path.join(OUT, 'mlo_door_frame_sidelights_white.glb'), 'Door Frame Sidelights White', [('Door_Frame_Sidelights_White', door_frame_sidelights('White'))])

    # ---- window frames ----
    write_glb(os.path.join(OUT, 'mlo_window_frame_white.glb'), 'Window Frame White', [('Window_Frame_White', window_frame('White'))])
    write_glb(os.path.join(OUT, 'mlo_window_frame_oak.glb'), 'Window Frame Oak', [('Window_Frame_Oak', window_frame('Oak'))])
    write_glb(os.path.join(OUT, 'mlo_window_frame_walnut.glb'), 'Window Frame Walnut', [('Window_Frame_Walnut', window_frame('Walnut'))])
    write_glb(os.path.join(OUT, 'mlo_window_frame_anthracite.glb'), 'Window Frame Anthracite', [('Window_Frame_Anthracite', window_frame('Anthracite', c=0.05))])
    write_glb(os.path.join(OUT, 'mlo_window_frame_classic_white.glb'), 'Window Frame Classic White', [('Window_Frame_Classic_White', window_frame('White', c=0.10, ct=0.018, cap=True))])
    write_glb(os.path.join(OUT, 'mlo_window_frame_stone.glb'), 'Window Frame Stone', [('Window_Frame_Stone', window_frame('Stone', c=0.14, ct=0.03, keystone=True, sill_t=0.06))])

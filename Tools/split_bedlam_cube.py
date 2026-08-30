"""Turn the downloaded Bedlam Cube model into thirteen separate, solved-pose pieces.

    python Tools/split_bedlam_cube.py <source.glb> Assets/ArtAssets/Play/bedlam_cube.glb

WHY THIS EXISTS. The Sketchfab model is THREE meshes - one per colour - each holding several
disconnected pieces, plus one already-assembled 4x4x4 cube. Room3-2N needs thirteen objects the
player can pick up one at a time, and it needs to know where each of them goes. Neither is
recoverable at runtime: splitting a mesh by connected components and then solving an exact cover
is not work a scene builder should be doing every time it runs.

So it is done once, here, and the result is a .glb whose thirteen nodes ARE the thirteen pieces,
each already rotated into the pose it holds in a solved cube and each carrying that pose as its
node translation. `SceneBuilder` then has nothing to compute: instantiate, read every child's
local position as that piece's home anchor, and scatter the pieces themselves across the floor.

WHAT IT DOES, in order:

  1. Splits each mesh into connected components by welding vertices at 0.001 and unioning
     triangles. Components inside the assembled cube's bounding box are dropped - that cube is
     the answer, and its same-coloured pieces are fused into each other anyway.
  2. Voxelises each piece on the model's own 50mm lattice. Inside/outside by ray parity
     (Moeller-Trumbore) over five random directions with a majority vote, because a single axis
     ray lands on a shared face often enough to matter. Twelve pieces of five cells and one of
     four - 64 in total, which is the check that the split found the real puzzle.
  3. Solves the 4x4x4 exact cover with Algorithm X / dancing links over all 24 rotations of each
     piece. The Bedlam Cube has 19,186 solutions; any one of them will do, and the first is found
     in well under a second.
  4. Writes the pieces out at `--cell` metres per cell, rotated into their solved orientation,
     each mesh centred on its own bounding box so the node translation is the whole of where it
     goes. The cube is centred on the origin, so a piece's translation is its offset from the
     cube's middle.

NO AXIS CORRECTION IS APPLIED, deliberately. The source is authored Z-up, and normally that would
be turned into glTF's Y-up on the way out - but every piece and the cube they make are
axis-aligned in either frame, and which of the three axes ends up vertical in Unity is arbitrary
for a cube. `SceneBuilder` measures each piece's height off its renderer rather than assuming one,
which is the same rule the rest of that file already follows for imported models.

The licence block in `asset.extras` is copied through unchanged: this is a derivative of a CC-BY
model, the attribution travels inside the file, and `SceneBuilder.ReadModelCredits` reads it from
there. See docs/asset-licences.md.

Also prints the 64-cell occupancy map, which is what `SceneBuilder.BedlamSolvedCells` holds - the
one number in this pipeline that has to be copied by hand, and which the build then checks back
against the node translations it reads out of the file.

Standard library plus numpy.
"""
import argparse
import itertools
import json
import struct
import sys

import numpy as np

CUBE = 4
# The lattice the model is drawn on, in its own units. Every piece's bounding box is an exact
# multiple of it, which is the first thing that says the split found real polycubes.
SOURCE_CELL = 50.0
# The assembled cube in the source, in source units. Anything inside it is the answer rather than
# a piece, and its same-coloured neighbours are welded to each other so it cannot be split anyway.
SOLVED_MIN = np.array([-1217.0, -879.0, -1.0])
SOLVED_MAX = np.array([-1015.0, -677.0, 201.0])


# ----------------------------------------------------------------------------- reading a .glb

def read_glb(path):
    data = open(path, 'rb').read()
    magic, _, total = struct.unpack('<III', data[:12])
    if magic != 0x46546C67:
        raise SystemExit(f'{path} is not a .glb')
    offset, js, bin_at = 12, None, None
    while offset < total:
        length, kind = struct.unpack('<II', data[offset:offset + 8])
        if kind == 0x4E4F534A:
            js = json.loads(data[offset + 8:offset + 8 + length])
        else:
            bin_at = offset + 8
        offset += 8 + length
    return data, js, bin_at


def accessor(data, js, bin_at, index):
    acc = js['accessors'][index]
    view = js['bufferViews'][acc['bufferView']]
    at = bin_at + view.get('byteOffset', 0) + acc.get('byteOffset', 0)
    dtype = {5126: '<f4', 5125: '<u4', 5123: '<u2', 5121: '<u1'}[acc['componentType']]
    width = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}[acc['type']]
    stride = view.get('byteStride')
    item = np.dtype(dtype).itemsize
    if stride and stride != item * width:
        raw = np.frombuffer(data, np.uint8, stride * acc['count'], at).reshape(acc['count'], stride)
        out = raw[:, :item * width].copy().view(dtype).reshape(acc['count'], width)
    else:
        out = np.frombuffer(data, dtype, acc['count'] * width, at).reshape(acc['count'], width)
    return out if width > 1 else out.reshape(-1)


def connected_components(positions, indices):
    """Vertex index groups, welding coincident vertices so a shared corner joins two triangles."""
    _, welded = np.unique(np.round(positions, 3), axis=0, return_inverse=True)
    parent = list(range(welded.max() + 1))

    def find(x):
        root = x
        while parent[root] != root:
            root = parent[root]
        while parent[x] != root:
            parent[x], x = root, parent[x]
        return root

    for a, b, c in welded[indices].reshape(-1, 3):
        for pair in ((a, b), (a, c)):
            ra, rb = find(pair[0]), find(pair[1])
            if ra != rb:
                parent[rb] = ra

    groups = {}
    for vertex in range(len(welded)):
        groups.setdefault(find(welded[vertex]), []).append(vertex)
    return [np.array(v) for v in groups.values()]


# ------------------------------------------------------------------------------- voxelising

def inside(points, verts, faces, rng):
    """Point-in-solid by ray parity, five directions, majority vote.

    An axis-aligned ray in a model built out of fused unit cubes lands exactly on a shared face
    often enough that a single test is unreliable. Random directions cannot line up with the
    lattice, and a vote of five makes a grazing hit on any one of them harmless.
    """
    a, b, c = verts[faces[:, 0]], verts[faces[:, 1]], verts[faces[:, 2]]
    e1, e2 = b - a, c - a
    votes = np.zeros(len(points), int)
    for _ in range(5):
        d = rng.normal(size=3)
        d /= np.linalg.norm(d)
        h = np.cross(d, e2)
        det = (e1 * h).sum(1)
        ok = np.abs(det) > 1e-9
        inv = np.where(ok, 1.0 / np.where(ok, det, 1.0), 0.0)
        for i, p in enumerate(points):
            s = p - a
            u = inv * (s * h).sum(1)
            q = np.cross(s, e1)
            v = inv * (d * q).sum(1)
            t = inv * (e2 * q).sum(1)
            hit = ok & (u >= 0) & (u <= 1) & (v >= 0) & (u + v <= 1) & (t > 1e-6)
            votes[i] += hit.sum() % 2
    return votes >= 3


def rotations():
    """The 24 proper rotations of a cube, as integer matrices."""
    out = []
    for perm in itertools.permutations(range(3)):
        for sign in itertools.product((1, -1), repeat=3):
            m = np.zeros((3, 3), int)
            for row, (col, s) in enumerate(zip(perm, sign)):
                m[row, col] = s
            if round(np.linalg.det(m)) == 1:
                out.append(m)
    return out


# ------------------------------------------------------------- exact cover (Algorithm X / DLX)

def solve(pieces, rots):
    """One packing of the 4x4x4, as {piece: (rotation index, offset)}.

    Columns are the 64 cells followed by the 13 pieces, so "every cell filled once" and "every
    piece used once" are the same constraint expressed twice and one algorithm answers both.
    """
    rows = []
    for pi, cells in enumerate(pieces):
        base, seen = np.array(cells), set()
        for ri, m in enumerate(rots):
            turned = base @ m.T
            turned = turned - turned.min(0)
            key = tuple(sorted(map(tuple, turned.tolist())))
            if key in seen:
                continue                      # a piece with a symmetry repeats its own placements
            seen.add(key)
            extent = turned.max(0) + 1
            for ox in range(CUBE - extent[0] + 1):
                for oy in range(CUBE - extent[1] + 1):
                    for oz in range(CUBE - extent[2] + 1):
                        cols = [64 + pi] + [(x + ox) * 16 + (y + oy) * 4 + (z + oz)
                                            for x, y, z in turned]
                        rows.append((cols, pi, ri, (ox, oy, oz)))

    columns = 64 + len(pieces)
    left, right, up, down, col_of, row_of = {}, {}, {}, {}, {}, {}
    size = [0] * columns
    head = columns
    for c in range(columns):
        up[c] = down[c] = col_of[c] = c
        left[c] = c - 1 if c else head
        right[c] = c + 1 if c < columns - 1 else head
    left[head], right[head] = columns - 1, 0

    node = columns + 1
    for index, (cols, _, _, _) in enumerate(rows):
        first = None
        for c in cols:
            n, node = node, node + 1
            col_of[n], row_of[n] = c, index
            up[n], down[n] = up[c], c
            down[up[c]] = n
            up[c] = n
            size[c] += 1
            if first is None:
                left[n] = right[n] = first = n
            else:
                left[n], right[n] = left[first], first
                right[left[first]] = n
                left[first] = n

    def cover(c):
        right[left[c]], left[right[c]] = right[c], left[c]
        i = down[c]
        while i != c:
            j = right[i]
            while j != i:
                down[up[j]], up[down[j]] = down[j], up[j]
                size[col_of[j]] -= 1
                j = right[j]
            i = down[i]

    def uncover(c):
        i = up[c]
        while i != c:
            j = left[i]
            while j != i:
                size[col_of[j]] += 1
                down[up[j]] = up[down[j]] = j
                j = left[j]
            i = up[i]
        right[left[c]] = left[right[c]] = c

    chosen = []

    def search():
        if right[head] == head:
            return True
        best, fewest, c = None, 1 << 30, right[head]
        while c != head:
            if size[c] < fewest:
                best, fewest = c, size[c]
            c = right[c]
        if fewest == 0:
            return False
        cover(best)
        r = down[best]
        while r != best:
            chosen.append(row_of[r])
            j = right[r]
            while j != r:
                cover(col_of[j])
                j = right[j]
            if search():
                return True
            j = left[r]
            while j != r:
                uncover(col_of[j])
                j = left[j]
            chosen.pop()
            r = down[r]
        uncover(best)
        return False

    sys.setrecursionlimit(10000)
    if not search():
        raise SystemExit('no packing of the 4x4x4 exists for these pieces')
    return {rows[i][1]: (rows[i][2], rows[i][3]) for i in chosen}


# ------------------------------------------------------------------------------- writing a .glb

def pad(blob, to=4, filler=b'\0'):
    return blob + filler * (-len(blob) % to)


def write_glb(path, meshes, materials, extras):
    """meshes: list of (name, positions Nx3, normals Nx3, indices, material, translation)."""
    binary, views, accessors, gltf_meshes, nodes = bytearray(), [], [], [], []

    def add_view(payload, target):
        views.append({'buffer': 0, 'byteOffset': len(binary),
                      'byteLength': len(payload), 'target': target})
        binary.extend(pad(payload))
        return len(views) - 1

    def add_accessor(view, kind, ctype, count, mn=None, mx=None):
        acc = {'bufferView': view, 'componentType': ctype, 'count': count, 'type': kind}
        if mn is not None:
            acc['min'], acc['max'] = mn, mx
        accessors.append(acc)
        return len(accessors) - 1

    for name, positions, normals, indices, material, translation in meshes:
        p = np.asarray(positions, '<f4')
        n = np.asarray(normals, '<f4')
        i = np.asarray(indices, '<u4')
        pa = add_accessor(add_view(p.tobytes(), 34962), 'VEC3', 5126, len(p),
                          p.min(0).tolist(), p.max(0).tolist())
        na = add_accessor(add_view(n.tobytes(), 34962), 'VEC3', 5126, len(n))
        ia = add_accessor(add_view(i.tobytes(), 34963), 'SCALAR', 5125, len(i))
        gltf_meshes.append({'name': name,
                            'primitives': [{'attributes': {'POSITION': pa, 'NORMAL': na},
                                            'indices': ia, 'material': material}]})
        nodes.append({'name': name, 'mesh': len(gltf_meshes) - 1,
                      'translation': [float(v) for v in translation]})

    root = len(nodes)
    nodes.append({'name': 'BedlamCube', 'children': list(range(root))})

    js = {'asset': {'version': '2.0', 'generator': 'Tools/split_bedlam_cube.py', 'extras': extras},
          'scene': 0, 'scenes': [{'nodes': [root]}], 'nodes': nodes,
          'meshes': gltf_meshes, 'materials': materials, 'accessors': accessors,
          'bufferViews': views, 'buffers': [{'byteLength': len(binary)}]}

    json_chunk = pad(json.dumps(js, separators=(',', ':')).encode('utf-8'), filler=b' ')
    binary = pad(bytes(binary))
    out = struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(json_chunk) + 8 + len(binary))
    out += struct.pack('<II', len(json_chunk), 0x4E4F534A) + json_chunk
    out += struct.pack('<II', len(binary), 0x004E4942) + binary
    open(path, 'wb').write(out)


# ------------------------------------------------------------------------------------- main

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('source')
    ap.add_argument('destination')
    ap.add_argument('--cell', type=float, default=0.2,
                    help='metres per cell in the output; the cube is four of these across')
    args = ap.parse_args()

    data, js, bin_at = read_glb(args.source)
    rng = np.random.default_rng(7)

    loose = []
    for mesh in js['meshes']:
        prim = mesh['primitives'][0]
        positions = accessor(data, js, bin_at, prim['attributes']['POSITION']).astype(np.float64)
        normals = accessor(data, js, bin_at, prim['attributes']['NORMAL']).astype(np.float64)
        indices = accessor(data, js, bin_at, prim['indices']).astype(np.int64)
        triangles = indices.reshape(-1, 3)

        for group in connected_components(positions, indices):
            lo, hi = positions[group].min(0), positions[group].max(0)
            if (lo >= SOLVED_MIN).all() and (hi <= SOLVED_MAX).all():
                continue
            # Cut down to this component alone before anything measures it. Keeping the whole
            # mesh's vertex array and only filtering the triangles reads as harmless and is not:
            # every `min`, `max` and bounds test below would then be taken over all six pieces
            # sharing the colour, and the piece would be normalised against a box it is not in.
            keep = np.zeros(len(positions), bool)
            keep[group] = True
            remap = np.full(len(positions), -1)
            remap[group] = np.arange(len(group))
            faces = remap[triangles[keep[triangles].all(1)]]
            verts = positions[group]

            dims = np.round((hi - lo) / SOURCE_CELL).astype(int)
            centres = np.array([lo + (np.array(c) + 0.5) * SOURCE_CELL
                                for c in np.ndindex(*dims)])
            cells = [c for c, hit in zip(np.ndindex(*dims), inside(centres, verts, faces, rng))
                     if hit]
            loose.append({'positions': verts, 'normals': normals[group], 'faces': faces,
                          'material': prim.get('material', 0), 'lo': lo,
                          'cells': np.array(cells)})

    filled = sum(len(p['cells']) for p in loose)
    print(f'{len(loose)} pieces, {filled} cells '
          f'({", ".join(str(len(p["cells"])) for p in loose)})')
    if len(loose) != 13 or filled != CUBE ** 3:
        raise SystemExit('that is not a Bedlam Cube - expected 13 pieces filling 64 cells')

    rots = rotations()
    packing = solve([p['cells'] for p in loose], rots)

    occupancy = [-1] * 64
    written, table = [], []
    for pi, piece in enumerate(loose):
        ri, offset = packing[pi]
        m = rots[ri]

        # The cells, turned and dropped into place. Normalising by the turned set's own minimum is
        # what makes an integer rotation of cell INDICES agree with a rotation of the solid: both
        # land the piece's corner at zero, so the constant the two differ by cancels.
        turned = piece['cells'] @ m.T
        turned = turned - turned.min(0)
        placed = turned + offset
        for x, y, z in placed:
            occupancy[x * 16 + y * 4 + z] = pi

        # The same rotation on the geometry. Vertices come in as source units off the piece's own
        # bounding box; they leave as metres off the middle of the assembled cube.
        grid = (piece['positions'] - piece['lo']) / SOURCE_CELL
        grid = grid @ m.T
        grid = grid - grid.min(0)
        world = (grid + offset - CUBE / 2.0) * args.cell
        normals = piece['normals'] @ m.T

        # SANITY: the solid must land inside the cells the integer arithmetic says it does. Every
        # vertex has to sit in one of them, which is the check that a rotation of cell INDICES and
        # a rotation of the SOLID were normalised the same way. Get that wrong and the cube still
        # assembles - out of pieces that are each a cell adrift of the hole they fill.
        #
        # Tested per vertex against the closed boxes rather than by flooring a coordinate into a
        # cell: a vertex on a shared face belongs to both cells beside it, and flooring picks one
        # of them arbitrarily.
        #
        # The tolerance is loose because the model's cubes are ROUNDED, and a fillet at the corner
        # where three cells meet bulges a few hundredths of a cell into the diagonal one. What
        # this is looking for is a piece a WHOLE cell out, so anything under a quarter is noise.
        overshoot = max(
            float(np.abs(np.clip(v, placed, placed + 1) - v).sum(1).min())
            for v in (grid + offset)[:, None, :])
        if overshoot > 0.25:
            raise SystemExit(f'piece {pi}: geometry sits {overshoot:.2f} cells outside the cells '
                             'it is solved into - rotation and cell indices disagree')

        # Centred on its own bounding box, so the node's translation IS where the piece goes and
        # the mesh needs no offset of its own. Taken off the CELLS rather than off the vertices:
        # the geometry is rounded at every edge, so its bounds are a millimetre inside the cells
        # it fills and a centre measured from them would drift by half of that.
        centre = (placed.min(0) + placed.max(0) + 1 - CUBE) * 0.5 * args.cell

        written.append((f'Piece_{pi:02d}', world - centre, normals,
                        piece['faces'].reshape(-1), piece['material'], centre))
        table.append((pi, ri, offset, len(piece['cells'])))

    write_glb(args.destination, written, js['materials'], js['asset'].get('extras', {}))

    print(f'wrote {args.destination}: 13 pieces, cube {CUBE * args.cell:.2f}m across')
    for pi, ri, offset, cells in table:
        print(f'  Piece_{pi:02d}  {cells} cells  rotation {ri:2d}  offset {tuple(offset)}')
    print('\nSceneBuilder.BedlamSolvedCells (cell x*16 + y*4 + z -> piece):')
    for x in range(CUBE):
        rows = [', '.join(f'{occupancy[x * 16 + y * 4 + z]:2d}' for z in range(CUBE))
                for y in range(CUBE)]
        print('    ' + ',  '.join(rows) + ',')


if __name__ == '__main__':
    main()

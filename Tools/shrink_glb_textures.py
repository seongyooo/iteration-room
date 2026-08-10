"""Downscale the textures embedded in a .glb, in place.

Why this exists: glTFast imports a .glb's textures as sub-assets it creates itself, so they never
pass through Unity's TextureImporter - which means `EditorUserBuildSettings.overrideMaxTextureSize`
and every per-texture import setting have nothing to act on. The only place the size can be changed
is inside the file.

The furniture models ship 31 maps at 2048x2048 between them, which decode to ~496 MB and, with
mipmaps, made up 97% of a WebGL build. For furniture seen across a small whitebox room that is
overkill on any platform, not just the web.

    python Tools/shrink_glb_textures.py --max 1024 Assets/ArtAssets/Furniture/*.glb
    python Tools/shrink_glb_textures.py --max 1024 --dry-run <file>

Originals are recoverable with `git checkout HEAD -- Assets/ArtAssets/` - they are committed
through Git LFS. Re-running on an already-shrunk file is a no-op.

Format notes, since getting these wrong corrupts the model rather than just the picture:
  - A .glb is a 12-byte header then chunks; chunk 0 is JSON, chunk 1 is the binary blob.
  - Everything - meshes, animations, images - points into that blob by bufferView, so replacing
    any image means recomputing EVERY bufferView offset, not just the image ones.
  - The JSON chunk pads with spaces, the BIN chunk pads with zeros, both to 4 bytes.
"""
import argparse
import io
import json
import os
import struct
import sys

from PIL import Image

JSON_CHUNK = 0x4E4F534A
BIN_CHUNK = 0x004E4942
GLB_MAGIC = 0x46546C67


def read_glb(path):
    with open(path, "rb") as f:
        data = f.read()

    magic, version, _length = struct.unpack("<III", data[:12])
    if magic != GLB_MAGIC:
        raise ValueError(f"{path} is not a glb")
    if version != 2:
        raise ValueError(f"{path}: unsupported glb version {version}")

    gltf, blob, pos = None, b"", 12
    while pos < len(data):
        length, kind = struct.unpack("<II", data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + length]
        if kind == JSON_CHUNK:
            gltf = json.loads(body.decode("utf-8"))
        elif kind == BIN_CHUNK:
            blob = body
        pos += 8 + length

    if gltf is None:
        raise ValueError(f"{path}: no JSON chunk")
    return gltf, blob


def write_glb(path, gltf, blob):
    json_bytes = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    # Spaces for JSON, zeros for BIN - the spec is specific about the padding byte, and a viewer
    # that re-parses the JSON chunk will choke on trailing nulls.
    json_bytes += b" " * (-len(json_bytes) % 4)
    blob += b"\x00" * (-len(blob) % 4)

    total = 12 + 8 + len(json_bytes) + (8 + len(blob) if blob else 0)
    with open(path, "wb") as f:
        f.write(struct.pack("<III", GLB_MAGIC, 2, total))
        f.write(struct.pack("<II", len(json_bytes), JSON_CHUNK))
        f.write(json_bytes)
        if blob:
            f.write(struct.pack("<II", len(blob), BIN_CHUNK))
            f.write(blob)


def view_bytes(gltf, blob, index):
    view = gltf["bufferViews"][index]
    if view.get("buffer", 0) != 0:
        raise ValueError("bufferView points at a buffer other than the embedded one")
    start = view.get("byteOffset", 0)
    return blob[start:start + view["byteLength"]]


def shrink_image(raw, mime, limit):
    """Returns re-encoded bytes, or None if this image is already small enough."""
    with Image.open(io.BytesIO(raw)) as im:
        if max(im.size) <= limit:
            return None

        scale = limit / max(im.size)
        size = (max(1, round(im.width * scale)), max(1, round(im.height * scale)))

        # Palette images cannot be resampled meaningfully - the interpolation would run over
        # palette indices rather than colours - so they are promoted first.
        if im.mode == "P":
            im = im.convert("RGBA" if "transparency" in im.info else "RGB")
        elif im.mode not in ("RGB", "RGBA", "L"):
            im = im.convert("RGB")

        resized = im.resize(size, Image.LANCZOS)

        out = io.BytesIO()
        if mime == "image/jpeg":
            # These are base colour maps; 92 is past the point where the difference survives
            # being viewed across a room.
            resized.convert("RGB").save(out, format="JPEG", quality=92, optimize=True)
        else:
            # PNG stays PNG. The normal and metallic-roughness maps are in this branch and
            # re-encoding them as JPEG would put block artefacts into surface data, where they
            # read as dents in the lighting rather than as softness.
            resized.save(out, format="PNG", optimize=True)
        return out.getvalue()


def shrink(path, limit, dry_run):
    gltf, blob = read_glb(path)
    images = gltf.get("images", [])
    views = gltf.get("bufferViews", [])

    replacements = {}
    for i, image in enumerate(images):
        if "bufferView" not in image:
            print(f"  [{i}] skipped: image is a URI, not embedded")
            continue

        raw = view_bytes(gltf, blob, image["bufferView"])
        mime = image.get("mimeType", "image/png")
        new = shrink_image(raw, mime, limit)
        if new is None:
            print(f"  [{i}] already <= {limit}px, left alone")
            continue

        replacements[image["bufferView"]] = new
        print(f"  [{i}] {len(raw) / 1048576:6.2f} MB -> {len(new) / 1048576:6.2f} MB  ({mime})")

    if not replacements:
        print("  nothing to do")
        return False

    if dry_run:
        print("  --dry-run: not written")
        return False

    # Every bufferView is relaid out, not just the image ones: replacing an image changes the
    # length of the blob at that point, so every offset after it moves.
    out = bytearray()
    for index, view in enumerate(views):
        data = replacements.get(index)
        if data is None:
            data = view_bytes(gltf, blob, index)

        out += b"\x00" * (-len(out) % 4)          # 4-byte align every view
        view["byteOffset"] = len(out)
        view["byteLength"] = len(data)
        view["buffer"] = 0
        out += data

    gltf["buffers"][0]["byteLength"] = len(out)
    gltf["buffers"][0].pop("uri", None)

    # Written beside the original and swapped in only after it reads back cleanly, so an
    # exception halfway through cannot leave a truncated model where the model used to be.
    temp = path + ".tmp"
    write_glb(temp, gltf, bytes(out))

    # Compared against the PADDED length: write_glb rounds the BIN chunk up to 4 bytes, so a blob
    # whose length is not already a multiple of 4 reads back longer than it went in. Comparing
    # against the raw length failed a perfectly good file.
    check_gltf, check_blob = read_glb(temp)
    expected = len(out) + (-len(out) % 4)
    if len(check_blob) != expected or len(check_gltf.get("images", [])) != len(images):
        os.remove(temp)
        raise ValueError(f"{path}: verification failed "
                         f"(blob {len(check_blob)} != {expected}, "
                         f"images {len(check_gltf.get('images', []))} != {len(images)})")

    # Every bufferView must land inside the blob it points into - the one way a bad relayout
    # shows up as a model that loads but renders as garbage rather than as a parse error.
    for index, view in enumerate(check_gltf.get("bufferViews", [])):
        end = view.get("byteOffset", 0) + view["byteLength"]
        if end > len(check_blob):
            os.remove(temp)
            raise ValueError(f"{path}: bufferView {index} runs past the end of the blob")

    before = os.path.getsize(path)
    os.replace(temp, path)
    after = os.path.getsize(path)
    print(f"  {before / 1048576:.1f} MB -> {after / 1048576:.1f} MB")
    return True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--max", type=int, default=1024, help="longest edge in pixels")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("paths", nargs="+")
    args = parser.parse_args()

    for path in args.paths:
        print(f"\n=== {path} ===")
        shrink(path, args.max, args.dry_run)


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""Cap the resolution of the textures embedded in a .glb, in place.

WHY THIS EXISTS
---------------
A texture audit on 2026-09-05 found 3,053 MB of texture memory across twenty glb
files, every one of them landing in memory as ARGB32 or RGB24 - completely
uncompressed. `realistic_tree.glb` alone carried four 4096x4096 maps at 181.4 MB
each. Textures were 90.3% of a 945 MB player build.

The obvious fix is block compression: BC7 is a quarter of the bytes at the same
resolution. It is not available here. These models come in through glTFast's
ScriptedImporter, which exposes mip-map generation and filtering and NOTHING about
format - the textures are sub-assets with no importer of their own, so there is no
setting to change and an AssetPostprocessor cannot reach them. Getting BC7 would
mean extracting every texture, importing each as its own asset, and rewriting the
materials to point at them: about 120 textures across 17 used models.

Halving the resolution saves exactly as much - a quarter of the bytes - and needs
none of that, because it changes the file Unity imports rather than how Unity
imports it. That is the trade this takes: resolution for plumbing.

WHAT IT DOES NOT CLAIM
----------------------
The saving that matters is not on disk. The embedded images are already PNG; what
shrinks is what Unity DECOMPRESSES them into, which is the number that drives VRAM,
the player build and a WebGL download.

And it IS a quality change. Which cap suits which model is a judgement about how
close the player gets to it - this game holds objects at true size in front of the
camera, so a clipboard and a tree do not deserve the same number. Pass one per file.

SAFETY
------
glb is tracked by Git LFS in this project, so `git checkout -- <file>` is the undo.
Nothing here writes anywhere but the file named.

USAGE
    python Tools/shrink_glb_textures.py <file.glb> [max-size]
"""

import io
import json
import struct
import sys

from PIL import Image

JSON_CHUNK = b"JSON"
BIN_CHUNK = b"BIN\x00"


def read_glb(path):
    with open(path, "rb") as handle:
        data = handle.read()

    magic, version, total = struct.unpack("<4sII", data[:12])
    if magic != b"glTF":
        raise SystemExit("%s is not a glb" % path)
    if version != 2:
        raise SystemExit("%s is glTF %d; this only knows version 2" % (path, version))

    chunks = {}
    offset = 12
    while offset < total:
        length, kind = struct.unpack("<I4s", data[offset : offset + 8])
        chunks[kind] = data[offset + 8 : offset + 8 + length]
        offset += 8 + length

    # The trailing pad has to come off before a parser sees it - see `pad4`.
    text = chunks[JSON_CHUNK].decode("utf-8").rstrip(" \x00")
    return json.loads(text), chunks[BIN_CHUNK]


def pad4(blob, filler=b"\x00"):
    """Pad to a four-byte boundary, which glTF requires of every chunk and view.

    The filler is not cosmetic. The spec pads a JSON chunk with SPACES and a binary
    chunk with zeros, because a reader is entitled to hand the JSON chunk straight to
    a parser. Padding JSON with nulls produced a file Unity accepted and Python's own
    `json.loads` refused, which is how this was noticed.
    """
    return blob + filler * (-len(blob) % 4)


def shrink(path, cap):
    doc, binary = read_glb(path)
    views = doc.get("bufferViews", [])
    images = doc.get("images", [])
    if not images:
        print("  no embedded images - nothing to do")
        return False

    # Which buffer views are pictures. Everything else - vertices, indices, animation -
    # has to come through byte for byte.
    image_view = {}
    for index, image in enumerate(images):
        if "bufferView" in image:
            image_view[image["bufferView"]] = index

    replacement = {}
    saved_pixels = 0
    for view_index, image_index in sorted(image_view.items()):
        view = views[view_index]
        start = view.get("byteOffset", 0)
        blob = binary[start : start + view["byteLength"]]

        picture = Image.open(io.BytesIO(blob))
        width, height = picture.size
        if max(width, height) <= cap:
            print("      image_%-2d %5dx%-5d  already within %d" % (image_index, width, height, cap))
            continue

        scale = cap / float(max(width, height))
        new_size = (max(1, int(round(width * scale))), max(1, int(round(height * scale))))
        # LANCZOS: these are photographic material maps, and the cheaper filters put
        # visible ringing into bark and fabric at this kind of reduction.
        smaller = picture.resize(new_size, Image.LANCZOS)

        out = io.BytesIO()
        smaller.save(out, format="PNG", optimize=True)
        replacement[view_index] = out.getvalue()

        saved_pixels += width * height - new_size[0] * new_size[1]
        print("      image_%-2d %5dx%-5d -> %5dx%-5d" % (image_index, width, height, new_size[0], new_size[1]))

    if not replacement:
        print("  every image already within %d - left alone" % cap)
        return False

    # THE WHOLE BINARY CHUNK IS REBUILT, not patched. Shrinking one image moves every
    # view after it, so each view's byteOffset is written from where it actually lands
    # rather than adjusted by a delta.
    rebuilt = bytearray()
    for view_index, view in enumerate(views):
        start = view.get("byteOffset", 0)
        blob = replacement.get(view_index, binary[start : start + view["byteLength"]])
        view["byteOffset"] = len(rebuilt)
        view["byteLength"] = len(blob)
        rebuilt += pad4(blob)

    doc["buffers"][0]["byteLength"] = len(rebuilt)

    json_chunk = pad4(json.dumps(doc, separators=(",", ":")).encode("utf-8"), b" ")
    bin_chunk = pad4(bytes(rebuilt))
    total = 12 + 8 + len(json_chunk) + 8 + len(bin_chunk)

    with open(path, "wb") as handle:
        handle.write(struct.pack("<4sII", b"glTF", 2, total))
        handle.write(struct.pack("<I4s", len(json_chunk), JSON_CHUNK))
        handle.write(json_chunk)
        handle.write(struct.pack("<I4s", len(bin_chunk), BIN_CHUNK))
        handle.write(bin_chunk)

    print("  %.1f megapixels removed; file is now %.1f MB" % (saved_pixels / 1e6, total / 1048576))
    return True


if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    target = sys.argv[1]
    limit = int(sys.argv[2]) if len(sys.argv) > 2 else 2048
    print("%s  (cap %d)" % (target, limit))
    shrink(target, limit)

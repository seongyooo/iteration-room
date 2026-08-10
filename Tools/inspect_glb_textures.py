"""List the images embedded in a .glb: index, name, format, pixel size, byte size.

Read-only. A .glb is a 12-byte header followed by chunks; chunk 0 is JSON describing the
asset, chunk 1 is the binary blob everything else points into by bufferView. Embedded textures
live in that blob as whole PNG/JPEG files, which is why they can be opened directly.

    python Tools/inspect_glb_textures.py Assets/ArtAssets/Furniture/messy_bed.glb
"""
import io
import json
import struct
import sys

from PIL import Image


def read_glb(path):
    with open(path, "rb") as f:
        data = f.read()

    magic, version, _total = struct.unpack("<III", data[:12])
    assert magic == 0x46546C67, f"{path} is not a glb (bad magic)"
    assert version == 2, f"unsupported glb version {version}"

    gltf, blob, pos = None, b"", 12
    while pos < len(data):
        length, kind = struct.unpack("<II", data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + length]
        if kind == 0x4E4F534A:      # 'JSON'
            gltf = json.loads(body.decode("utf-8"))
        elif kind == 0x004E4942:    # 'BIN'
            blob = body
        pos += 8 + length

    return gltf, blob


def image_bytes(gltf, blob, image):
    if "bufferView" not in image:
        raise ValueError("image is a URI, not embedded - not handled")
    view = gltf["bufferViews"][image["bufferView"]]
    start = view.get("byteOffset", 0)
    return blob[start:start + view["byteLength"]]


def main(paths):
    for path in paths:
        gltf, blob = read_glb(path)
        images = gltf.get("images", [])
        print(f"\n=== {path} ===")
        print(f"{len(images)} embedded image(s), BIN chunk {len(blob) / 1048576:.1f} MB")

        # What each image is actually used for, so a normal map is recognisable as one.
        usage = {}
        for mat in gltf.get("materials", []):
            pbr = mat.get("pbrMetallicRoughness", {})
            slots = {
                "baseColor": pbr.get("baseColorTexture"),
                "metallicRoughness": pbr.get("metallicRoughnessTexture"),
                "normal": mat.get("normalTexture"),
                "occlusion": mat.get("occlusionTexture"),
                "emissive": mat.get("emissiveTexture"),
            }
            for slot, ref in slots.items():
                if ref is None:
                    continue
                source = gltf["textures"][ref["index"]].get("source")
                if source is not None:
                    usage.setdefault(source, set()).add(slot)

        total_raw = 0
        for i, image in enumerate(images):
            raw = image_bytes(gltf, blob, image)
            with Image.open(io.BytesIO(raw)) as im:
                w, h, mode = im.size[0], im.size[1], im.mode
            # What Unity actually holds once decoded, which is the number that matters for the
            # build report and for a browser tab's memory.
            decoded = w * h * 4
            total_raw += decoded
            slots = ",".join(sorted(usage.get(i, {"?"})))
            print(f"  [{i:2}] {w:5}x{h:<5} {mode:5} {image.get('mimeType', '?'):12}"
                  f" file {len(raw) / 1048576:6.1f} MB   decoded {decoded / 1048576:7.1f} MB   {slots}")

        print(f"  decoded total: {total_raw / 1048576:.1f} MB")


if __name__ == "__main__":
    main(sys.argv[1:])

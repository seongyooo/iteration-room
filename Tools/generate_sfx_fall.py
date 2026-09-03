r"""The cable car's FALL: the long drop, and the structure it strikes on the way down.

Same exception, same reason as `generate_sfx_ai.py` - see its header for the argument and for the
stereo trap that costs an octave if it is missed. This is a second file rather than more entries in
that one because these are the ending's last thirty seconds and are regenerated on their own.

    set ELEVENLABS_API_KEY=sk_...
    C:\eleven\python Tools\generate_sfx_fall.py

Licence: model output on a paid plan - `docs/asset-licences.md`.
"""
import os, sys, io, wave, array, math
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
from elevenlabs.client import ElevenLabs

api = ElevenLabs(api_key=os.environ["ELEVENLABS_API_KEY"])
OUT = r"C:\Users\seonl\Desktop\c\2026\summer\Iteration\Assets\Audio\SFX"
SR = 44100

# **THE FALL IS A BED, NOT AN EVENT.** It plays under the whole drop and is asked for as one
# continuous rush rather than as a whoosh with a shape, because the shape comes from the car - the
# script fades it in as the fall starts and rides it. A clip with its own swell would fight that.
#
# The bounces are asked for DRY and CLOSE. They are heard from inside the cabin, which is the thing
# being struck, so the reverb of a large space would put the listener outside it.
CLIPS = [
    ("sfx_cable_car_fall", 9.0,
     "A heavy steel cabin falling fast through open air. Deep roaring wind rush, rumbling and "
     "buffeting, with the groan of straining metal underneath it. Continuous, no impact, no music, "
     "no voice."),
    ("sfx_cable_car_hit_1", 1.4,
     "A heavy steel cabin slamming into a concrete wall and glancing off it. One huge metallic "
     "crash with a low boom, debris and rattle after it. Close, dry, no reverb tail, no music."),
    ("sfx_cable_car_hit_2", 1.2,
     "A heavy steel box striking a concrete edge and scraping across it. A hard metallic bang "
     "followed by a short grinding scrape. Close, dry, no music, no voice."),
]


def peak(a):
    return max(abs(v) for v in a) if a else 0


for name, dur, prompt in CLIPS:
    raw = b"".join(api.text_to_sound_effects.convert(
        text=prompt, output_format="pcm_44100",
        duration_seconds=dur, prompt_influence=0.75,
        model_id="eleven_text_to_sound_v2"))

    # INTERLEAVED STEREO. Averaged, not decimated - see `generate_sfx_ai.py`.
    pcm = array.array("h"); pcm.frombytes(raw[:len(raw) // 4 * 4])
    mono = array.array("h", ((pcm[k] + pcm[k + 1]) // 2 for k in range(0, len(pcm) - 1, 2)))

    # **THE FALL LOOPS AND THEREFORE HAS TO JOIN ITSELF.** It is nine seconds under a drop that can
    # run longer, so the script loops it; an un-crossfaded loop clicks once a lap, which over a
    # silent fall is the only thing anybody would hear. A quarter-second equal-power crossfade of
    # the tail onto the head, and the tail is then discarded.
    if name.endswith("_fall"):
        n = int(0.25 * SR)
        if len(mono) > 4 * n:
            head, tail = mono[:n], mono[-n:]
            for k in range(n):
                x = k / float(n)
                mono[k] = int(head[k] * math.sqrt(x) + tail[k] * math.sqrt(1.0 - x))
            mono = mono[:len(mono) - n]

    # Peak normalised to -1 dBFS. These are the loudest things in the game by design and the mix
    # scaling that decides how loud they actually are lives in `SceneBuilder`, not here.
    p = peak(mono)
    if p:
        g = int(32767 * 0.891) / float(p)
        mono = array.array("h", (max(-32768, min(32767, int(v * g))) for v in mono))

    path = os.path.join(OUT, name + ".wav")
    with wave.open(path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes(mono.tobytes())

    got = len(mono) / SR
    ok = abs(got - dur) < 0.35
    print(f"  {name}: {got:.2f}s (asked {dur:.2f})" + ("" if ok else "   ! stereo handling?"))

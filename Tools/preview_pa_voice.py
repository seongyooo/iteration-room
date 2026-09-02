#!/usr/bin/env python3
"""Renders what the PA chain does to a narration line, without opening Unity.

The real treatment is a runtime filter chain built by SceneBuilder.AddTannoyFilters - these WAVs are
clean masters and stay that way. But that chain is about ten numbers, and retuning it by launching
the Editor, entering play mode and waiting for an iteration to start is slow enough that it does not
get done. This renders an offline approximation of the same chain so the numbers can be judged in
seconds.

It is an approximation, deliberately: Unity's reverb is FMOD's and this is a plain Schroeder
network. Trust it for "how much echo", "is it too muffled", "can I still make out the number" - not
for the exact tail.

    python Tools/preview_pa_voice.py                       # default line, default settings
    python Tools/preview_pa_voice.py voice_count_3         # a different line
    python Tools/preview_pa_voice.py voice_new_cycle out.wav

Keep the values below in step with SceneBuilder.AddTannoyFilters, or this stops predicting anything.
"""

import math
import os
import struct
import sys
import wave

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import generate_sfx as dsp

SR = dsp.SR
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
VOICE = os.path.join(ROOT, "Assets", "Audio", "Voice")

# --- mirrors SceneBuilder.AddTannoyFilters ---
HP_FREQ, HP_Q = 240.0, 1.0
LP_FREQ, LP_Q = 3400.0, 0.7
DISTORTION = 0.10
ECHO_DELAY_MS, ECHO_DECAY, ECHO_WET = 105.0, 0.22, 0.26
REVERB_DECAY = 2.3
REVERB_WET = 0.34          # stands in for reverbLevel 250 / room -350
REVERB_DAMPING = 0.55      # stands in for roomHF -900 / decayHFRatio 0.55


def read_wav(path):
    with wave.open(path, "rb") as w:
        assert w.getsampwidth() == 2, "expected 16-bit"
        n, sr, ch = w.getnframes(), w.getframerate(), w.getnchannels()
        raw = struct.unpack("<%dh" % (n * ch), w.readframes(n))
    x = [raw[i * ch] / 32768.0 for i in range(n)] if ch > 1 else [v / 32768.0 for v in raw]
    return x, sr


def resample(x, src_sr, dst_sr):
    """Linear interpolation. The narration is 22 kHz and everything else here runs at 44.1."""
    if src_sr == dst_sr:
        return list(x)
    ratio = dst_sr / src_sr
    out = [0.0] * int(len(x) * ratio)
    for i in range(len(out)):
        pos = i / ratio
        j = int(pos)
        frac = pos - j
        a = x[j] if j < len(x) else 0.0
        b = x[j + 1] if j + 1 < len(x) else a
        out[i] = a + (b - a) * frac
    return out


def echo(x, delay_ms, decay, wet):
    d = int(delay_ms / 1000.0 * SR)
    # Tail room for the repeats to die away in.
    buf = list(x) + [0.0] * (d * 6)
    out = list(buf)
    for i in range(d, len(buf)):
        out[i] += out[i - d] * decay * wet
    return out


def reverb(x, decay_time, wet, damping):
    """Schroeder: four parallel damped combs into two series allpasses. Prime-ish delays, so the
    combs do not line up and ring on one pitch."""
    combs = [1687, 1601, 2053, 2251]
    allpasses = [556, 341]

    n = len(x) + int(decay_time * SR)
    src = list(x) + [0.0] * (n - len(x))
    wet_sig = [0.0] * n

    for d in combs:
        # Feedback that reaches -60 dB after decay_time.
        g = 10 ** (-3.0 * d / (decay_time * SR))
        buf = [0.0] * d
        store = 0.0
        idx = 0
        for i in range(n):
            y = buf[idx]
            wet_sig[i] += y
            # One-pole lowpass inside the loop: each pass round the room loses the top first.
            store = y * (1 - damping) + store * damping
            buf[idx] = src[i] + store * g
            idx = idx + 1 if idx + 1 < d else 0

    wet_sig = [v / len(combs) for v in wet_sig]

    for d in allpasses:
        g = 0.5
        buf = [0.0] * d
        idx = 0
        for i in range(n):
            bufout = buf[idx]
            y = -wet_sig[i] + bufout
            buf[idx] = wet_sig[i] + bufout * g
            wet_sig[i] = y
            idx = idx + 1 if idx + 1 < d else 0

    dry = src
    return [dry[i] + wet_sig[i] * wet for i in range(n)]


def tannoy(x):
    x = dsp.highpass(x, HP_FREQ, HP_Q)
    x = dsp.lowpass(x, LP_FREQ, LP_Q)
    # Unity's distortionLevel is not a drive in dB; this is the curve that lands closest by ear.
    x = dsp.soft_clip(x, 1.0 + DISTORTION * 9.0)
    x = echo(x, ECHO_DELAY_MS, ECHO_DECAY, ECHO_WET)
    x = reverb(x, REVERB_DECAY, REVERB_WET, REVERB_DAMPING)
    return dsp.normalize(x, 0.85)


def main():
    name = sys.argv[1] if len(sys.argv) > 1 else "voice_iteration_01"
    out_path = sys.argv[2] if len(sys.argv) > 2 else os.path.join(ROOT, "%s_pa_preview.wav" % name)

    src = os.path.join(VOICE, name + ".wav")
    if not os.path.exists(src):
        sys.exit("no such line: " + os.path.normpath(src))

    x, sr = read_wav(src)
    processed = tannoy(resample(x, sr, SR))

    with wave.open(out_path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(b"".join(
            struct.pack("<h", int(max(-1.0, min(1.0, v)) * 32767)) for v in processed))

    print("in : %s (%.2fs @ %d Hz)" % (os.path.normpath(src), len(x) / sr, sr))
    print("out: %s (%.2fs @ %d Hz)" % (os.path.normpath(out_path), len(processed) / SR, SR))


if __name__ == "__main__":
    main()

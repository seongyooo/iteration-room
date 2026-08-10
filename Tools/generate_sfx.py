#!/usr/bin/env python3
"""Synthesises every sound effect the room needs, into Assets/Audio/SFX/.

Why generated rather than sourced: the same reason the narration is (Tools/generate_narration.ps1)
and the same reason the wall grain is (SceneBuilder.MakeNoiseNormalMap) - the whole project has to
rebuild from scripts, and a folder of downloaded CC0 files is the one part that could not. These
are synthetic and they sound it, which suits a facility that announces itself over a tannoy.

To replace any of them with a recorded take, drop the file in under the same name and delete
nothing else: SceneBuilder resolves clips by filename and does not care how they were made.

Standard library only - no numpy - so this runs wherever Python does.
Run:  python Tools/generate_sfx.py
"""

import math
import os
import random
import struct
import wave

SR = 44100
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Audio", "SFX")


# ---------------------------------------------------------------- primitives

def silence(seconds):
    return [0.0] * int(seconds * SR)


def noise(seconds, rng):
    return [rng.uniform(-1.0, 1.0) for _ in range(int(seconds * SR))]


def biquad(x, b0, b1, b2, a1, a2):
    """Direct form I. Coefficients are already normalised by a0."""
    y = [0.0] * len(x)
    x1 = x2 = y1 = y2 = 0.0
    for i, xn in enumerate(x):
        yn = b0 * xn + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
        y[i] = yn
        x2, x1 = x1, xn
        y2, y1 = y1, yn
    return y


def _rbj(freq, q):
    w0 = 2.0 * math.pi * max(1.0, min(freq, SR * 0.45)) / SR
    sw = math.sin(w0)
    return math.cos(w0), sw, sw / (2.0 * q)


def lowpass(x, freq, q=0.707):
    cw, sw, alpha = _rbj(freq, q)
    a0 = 1 + alpha
    return biquad(x, (1 - cw) / 2 / a0, (1 - cw) / a0, (1 - cw) / 2 / a0,
                  -2 * cw / a0, (1 - alpha) / a0)


def highpass(x, freq, q=0.707):
    cw, sw, alpha = _rbj(freq, q)
    a0 = 1 + alpha
    return biquad(x, (1 + cw) / 2 / a0, -(1 + cw) / a0, (1 + cw) / 2 / a0,
                  -2 * cw / a0, (1 - alpha) / a0)


def bandpass(x, freq, q=2.0):
    cw, sw, alpha = _rbj(freq, q)
    a0 = 1 + alpha
    return biquad(x, alpha / a0, 0.0, -alpha / a0, -2 * cw / a0, (1 - alpha) / a0)


def sweep_bandpass(x, f_start, f_end, q=3.0):
    """A bandpass whose centre frequency moves, recomputed per sample. Exponential rather than
    linear, so the sweep is even in pitch terms rather than crawling at the bottom."""
    y = [0.0] * len(x)
    x1 = x2 = y1 = y2 = 0.0
    n = max(1, len(x) - 1)
    ratio = f_end / f_start
    for i, xn in enumerate(x):
        cw, sw, alpha = _rbj(f_start * ratio ** (i / n), q)
        a0 = 1 + alpha
        yn = (alpha / a0) * xn - (-alpha / a0) * x2 - (-2 * cw / a0) * y1 - ((1 - alpha) / a0) * y2
        y[i] = yn
        x2, x1 = x1, xn
        y2, y1 = y1, yn
    return y


def sine(seconds, freq, amp=1.0):
    n = int(seconds * SR)
    w = 2.0 * math.pi * freq / SR
    return [amp * math.sin(w * i) for i in range(n)]


def env_decay(n, tau, attack=0.002):
    """Near-instant attack, exponential fall. tau is the time to fall to 1/e."""
    a = max(1, int(attack * SR))
    return [min(1.0, i / a) * math.exp(-(i / SR) / tau) for i in range(n)]


def env_ar(n, attack, release):
    """Linear up, cosine down - for swells rather than hits."""
    a = max(1, int(attack * SR))
    r = max(1, int(release * SR))
    out = [0.0] * n
    for i in range(n):
        if i < a:
            out[i] = i / a
        elif i > n - r:
            out[i] = 0.5 * (1 + math.cos(math.pi * min(1.0, (i - (n - r)) / r)))
        else:
            out[i] = 1.0
    return out


def apply_env(x, env):
    return [xi * ei for xi, ei in zip(x, env)]


def scale(x, k):
    return [v * k for v in x]


def mix(*layers):
    n = max(len(layer) for layer in layers)
    out = [0.0] * n
    for layer in layers:
        for i, v in enumerate(layer):
            out[i] += v
    return out


def at(base, layer, start_seconds):
    """Adds `layer` into `base` at an offset, growing base if it needs the room."""
    s = int(start_seconds * SR)
    if len(base) < s + len(layer):
        base = base + [0.0] * (s + len(layer) - len(base))
    for i, v in enumerate(layer):
        base[s + i] += v
    return base


def soft_clip(x, drive=1.0):
    return [math.tanh(v * drive) for v in x]


def normalize(x, peak=0.8):
    m = max(abs(v) for v in x) or 1.0
    return scale(x, peak / m)


def fade_edges(x, seconds=0.004):
    """Kills the click a hard start or stop leaves on a one-shot."""
    n = max(1, int(seconds * SR))
    for i in range(min(n, len(x) // 2)):
        k = i / n
        x[i] *= k
        x[-1 - i] *= k
    return x


def seamless(x, fade_seconds):
    """Wraps a tail back over the head so the clip loops without a seam. Feed it
    (length + fade) samples; it returns `length` of them."""
    f = int(fade_seconds * SR)
    n = len(x) - f
    out = x[:n]
    for i in range(f):
        k = i / f
        out[i] = out[i] * k + x[n + i] * (1 - k)
    return out


def write(name, samples):
    os.makedirs(OUT, exist_ok=True)
    path = os.path.normpath(os.path.join(OUT, name + ".wav"))
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        frames = bytearray()
        for v in samples:
            frames += struct.pack("<h", int(max(-1.0, min(1.0, v)) * 32767))
        w.writeframes(bytes(frames))
    print("  %-22s %5.2fs" % (name, len(samples) / SR))


# ---------------------------------------------------------------- the sounds

def button_tone(freq, length, click_level, peak):
    """A struck pitched tone - the pad answering rather than the pad being hit.

    The first version of this was a spring-loaded plate landing on its stop: a low thump with a
    detent click over it. Correct for the hardware, wrong for the game - at 96 Hz it read as a kick
    drum every time anyone stood on the pad, and the room already has low end in the room tone and
    the reset hit. This is the opposite choice: high, pitched, and left to ring.

    Partials are slightly inharmonic (2.01, 2.98, 5.9 rather than exact multiples) because exact
    ones read as an organ; the shorter decays on the upper ones are what make it metal."""
    rng = random.Random(int(freq))
    n = int(length * SR)

    # (ratio, level, how much of `length` it takes to decay). The fundamental rings on well past the
    # others - that tail is the "-----" of the sound.
    parts = [(1.00, 1.00, 0.55), (2.01, 0.38, 0.22), (2.98, 0.20, 0.11), (5.90, 0.07, 0.05)]

    out = [0.0] * n
    for ratio, level, decay in parts:
        env = env_decay(n, length * decay, attack=0.003)
        out = mix(out, apply_env(sine(length, freq * ratio), scale(env, level)))

    # A whisper of contact noise at the very front, so it still reads as something being pressed
    # rather than as a notification popping out of nowhere. Deliberately tiny.
    click = apply_env(bandpass(noise(0.02, rng), freq * 2.4, q=1.2),
                      env_decay(int(0.02 * SR), 0.004, attack=0.0003))
    out = at(out, scale(click, click_level), 0.0)

    return fade_edges(normalize(out, peak))


def floor_button_press():
    """The pad going down. C6, left to ring for most of a second."""
    return button_tone(1046.5, 1.0, click_level=0.12, peak=0.66)


def floor_button_release():
    """The pad coming back up. The same tone a fourth lower and half as long - the shape of a thing
    switching off, and deliberately a much smaller event than the press. The press is the one that
    means something."""
    return button_tone(783.99, 0.5, click_level=0.08, peak=0.42)


def door_open():
    """The pocket door: a servo takes up, the slab runs, and it settles into the wall. Timed against
    Door.openDuration (1.0s), with the settle overhanging the end of the motion."""
    rng = random.Random(11)
    dur = 1.25
    n = int(dur * SR)

    # Rail whine, pitching up as the slab gets going and back down as it arrives.
    ph = 0.0
    servo = [0.0] * n
    for i in range(n):
        f = 132 + 46 * math.sin(math.pi * min(1.0, (i / n) / 0.85))
        ph += 2.0 * math.pi * f / SR
        servo[i] = 0.6 * math.sin(ph) + 0.25 * math.sin(2 * ph) + 0.12 * math.sin(3 * ph)
    servo = apply_env(lowpass(servo, 900, q=0.9), env_ar(n, 0.06, 0.35))

    # Friction: the slab's edge running in the pocket.
    slide = apply_env(bandpass(noise(dur, rng), 1600, q=0.7), scale(env_ar(n, 0.09, 0.4), 0.55))

    out = mix(scale(servo, 0.5), slide)
    # The catch as it breaks loose, and the soft stop as it reaches the back of the pocket.
    out = at(out, apply_env(lowpass(noise(0.06, rng), 700), env_decay(int(0.06 * SR), 0.02)), 0.0)
    hn = int(0.35 * SR)
    thunk = mix(apply_env(sine(0.35, 74), env_decay(hn, 0.09)),
                apply_env(lowpass(noise(0.35, rng), 480), env_decay(hn, 0.05)))
    out = at(out, scale(thunk, 0.7), 0.95)
    return fade_edges(normalize(out, 0.72))


def gasp():
    """Waking up: one sharp inhale. The band opens as the breath goes in, and it is cut rather than
    faded - a breath stops, it does not decay."""
    rng = random.Random(23)
    dur = 0.5
    n = int(dur * SR)

    env = []
    for i in range(n):
        k = i / n
        env.append((k ** 1.6) * (1.0 if k < 0.86 else max(0.0, 1.0 - (k - 0.86) / 0.14)))

    breath = apply_env(sweep_bandpass(noise(dur, rng), 480, 1500, q=1.4), env)
    # A little voiced edge, so it reads as a person rather than as wind.
    voiced = apply_env(bandpass(noise(dur, rng), 240, q=3.0), scale(env, 0.35))
    return fade_edges(normalize(mix(breath, voiced), 0.5))


def sheet_rustle():
    """Bedding as the body sits up: a scatter of tiny crackles under a broad shush, which is what
    separates fabric from a plain noise burst."""
    rng = random.Random(37)
    dur = 1.1
    n = int(dur * SR)

    out = apply_env(highpass(noise(dur, rng), 2200), scale(env_ar(n, 0.15, 0.5), 0.35))

    t = 0.02
    while t < dur - 0.06:
        grain_len = rng.uniform(0.004, 0.016)
        g = bandpass(noise(grain_len, rng), rng.uniform(2500, 7000), q=1.6)
        g = apply_env(g, env_decay(len(g), grain_len * 0.35, attack=0.0006))
        # Loudest in the middle of the movement, tapering at both ends.
        out = at(out, scale(g, 0.9 * math.sin(math.pi * (t / dur)) ** 1.2), t)
        t += rng.uniform(0.012, 0.045)

    return fade_edges(normalize(out, 0.45))


def ominous_loop():
    """The room tone: a low drone that never resolves, plus air.

    8 seconds, and every partial is a multiple of 1/8 Hz so the tone wraps exactly; the noise layer
    is wrapped by crossfading its own tail back over its head. Both matter - RoomAmbience plays this
    on `loop`, so any seam is a click every 8 seconds for the whole session."""
    rng = random.Random(41)
    dur = 8.0
    n = int(dur * SR)
    fade = 1.2

    # 46.25 and 69.375 are a fifth apart, 92.5 doubles the root. 138.75 and 139.0 beat against each
    # other once every 4 seconds - two whole beats per loop, so the beating wraps too.
    tone = mix(sine(dur, 46.250, 0.55),
               sine(dur, 69.375, 0.30),
               sine(dur, 92.500, 0.18),
               sine(dur, 138.750, 0.10),
               sine(dur, 139.000, 0.10))

    # Slow breathing across the whole drone: 0.25 Hz, i.e. two cycles per loop.
    tone = [v * (0.82 + 0.18 * math.sin(2 * math.pi * 0.25 * i / SR)) for i, v in enumerate(tone)]

    air = scale(seamless(lowpass(highpass(noise(dur + fade, rng), 300), 2400), fade), 0.11)

    return normalize(mix(tone, air), 0.55)


def reset_sting():
    """Over the loop boundary, under the closed eyelids. A cluster swells, a riser runs up under it,
    and the whole thing lands on a low hit that decays away into the next iteration."""
    rng = random.Random(53)
    dur = 3.2
    n = int(dur * SR)

    # A minor second - the interval that refuses to settle.
    cluster = mix(apply_env(sine(dur, 110.0), env_ar(n, 1.4, 1.2)),
                  apply_env(sine(dur, 116.5), env_ar(n, 1.6, 1.2)),
                  apply_env(sine(dur, 220.0), scale(env_ar(n, 1.8, 1.0), 0.4)))

    riser = sweep_bandpass(noise(1.9, rng), 400, 5200, q=2.2)
    rn = len(riser)
    riser = apply_env(riser, [0.5 * (i / rn) ** 2.2 for i in range(rn)])

    hn = int(1.4 * SR)
    hit = mix(apply_env(sine(1.4, 55), env_decay(hn, 0.35)),
              apply_env(sine(1.4, 41.2), env_decay(hn, 0.5)),
              apply_env(lowpass(noise(1.4, rng), 900), scale(env_decay(hn, 0.12), 0.5)))

    out = at(scale(cluster, 0.7), riser, 0.0)
    out = at(out, scale(hit, 0.9), 1.85)
    return fade_edges(normalize(out, 0.78))


def machines_rev():
    """The facility spinning the cell back up. The loud part is the climb rather than the top, so it
    lands under the wake-up instead of arriving after it."""
    rng = random.Random(67)
    dur = 2.4
    n = int(dur * SR)

    ph = 0.0
    motor = [0.0] * n
    for i in range(n):
        # Fast climb, levelling off as the machine reaches speed.
        f = 48 + 145 * (1 - math.exp(-3.4 * (i / n)))
        ph += 2.0 * math.pi * f / SR
        motor[i] = (0.55 * math.sin(ph) + 0.3 * math.sin(2 * ph)
                    + 0.18 * math.sin(3 * ph) + 0.1 * math.sin(5 * ph))
    motor = lowpass(motor, 1400, q=1.1)

    # Turbine air, opening up as the motor climbs.
    air = bandpass(noise(dur, rng), 1800, q=0.6)
    air = [v * 0.3 * (i / n) for i, v in enumerate(air)]

    return fade_edges(normalize(soft_clip(apply_env(mix(motor, air), env_ar(n, 0.12, 0.55)), 1.4), 0.68))


def glass_rattle():
    """The lamp and the vase answering the machines. High-Q pings on a scatter of small impacts, so
    it reads as two objects knocking rather than as one struck bell."""
    rng = random.Random(71)
    out = silence(1.5)

    # Two objects, each with its own set of resonances.
    bodies = [[1180, 2360, 3510], [1640, 2960, 4820]]

    t = 0.03
    while t < 1.1:
        body = bodies[rng.randrange(len(bodies))]
        pn = int(0.22 * SR)
        click = noise(0.22, rng)
        ping = [0.0] * pn
        for j, f in enumerate(body):
            partial = bandpass(click, f * rng.uniform(0.99, 1.01), q=22 - j * 5)
            partial = apply_env(partial, env_decay(pn, 0.05 / (j + 1), attack=0.0004))
            ping = mix(ping, scale(partial, 1.0 / (j + 1)))
        out = at(out, scale(ping, (1.0 - t / 1.25) * rng.uniform(0.5, 1.0)), t)
        t += rng.uniform(0.035, 0.14)

    return fade_edges(normalize(out, 0.55))


def chime():
    """The two-note tannoy ding ahead of an announcement. Struck partials rather than pure sines - a
    bare sine reads as a test tone, and the inharmonic ones are what make it a bell."""
    out = silence(2.4)

    def bell(freq, length, level):
        n = int(length * SR)
        # Ratios off a struck bar. The 2.76 and 5.4 are what stop it sounding like an organ.
        parts = [(1.0, 1.0, 0.9), (2.0, 0.45, 0.55), (2.76, 0.28, 0.4), (5.40, 0.12, 0.22)]
        layer = [0.0] * n
        for ratio, amp, decay_scale in parts:
            env = env_decay(n, length * decay_scale * 0.5, attack=0.004)
            layer = mix(layer, apply_env(sine(length, freq * ratio), scale(env, amp)))
        return scale(layer, level)

    # A falling major third, the interval every institutional PA seems to have settled on.
    out = at(out, bell(987.77, 1.5, 0.85), 0.0)
    out = at(out, bell(659.25, 1.9, 0.9), 0.42)
    return fade_edges(normalize(out, 0.7))


def main():
    print("Writing SFX to", os.path.normpath(OUT))
    write("sfx_floor_button_press", floor_button_press())
    write("sfx_floor_button_release", floor_button_release())
    write("sfx_door_open", door_open())
    write("sfx_gasp", gasp())
    write("sfx_sheet_rustle", sheet_rustle())
    write("sfx_ominous_loop", ominous_loop())
    write("sfx_reset_sting", reset_sting())
    write("sfx_machines_rev", machines_rev())
    write("sfx_glass_rattle", glass_rattle())
    write("sfx_chime", chime())
    print("done")


if __name__ == "__main__":
    main()

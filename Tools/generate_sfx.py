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


def pull_in():
    """The loop taking you: something narrow opening underneath the room and everything being drawn
    down into it.

    Two motions against each other, which is what sells it. A noise band climbs and tightens while a
    tone falls away underneath - up and down at once reads as being pulled *through* something,
    where either alone is just a riser or just a drop. Then it is swallowed: a hard cut, a beat of
    almost-nothing, and the sub arriving late on the other side."""
    rng = random.Random(101)
    swell = 2.0

    # The climb. Exponential sweep and a squared amplitude ramp, so almost all of it happens in the
    # last half second - a linear riser reads as a slider being dragged, the same trap the collapse
    # ramp in LoopManager documents.
    air = sweep_bandpass(noise(swell, rng), 220, 5200, q=1.5)
    an = len(air)
    air = apply_env(air, [(i / an) ** 2.2 for i in range(an)])

    # The room falling away underneath. Pitch drops as the noise rises.
    n = int(swell * SR)
    ph = 0.0
    drop = [0.0] * n
    for i in range(n):
        f = 260.0 * (55.0 / 260.0) ** ((i / n) ** 0.7)
        ph += 2.0 * math.pi * f / SR
        drop[i] = 0.6 * math.sin(ph) + 0.25 * math.sin(2 * ph)
    drop = apply_env(lowpass(drop, 1200, q=1.2), [0.5 + 0.5 * (i / n) for i in range(n)])

    out = mix(scale(air, 0.75), scale(drop, 0.55))

    # Swallowed. The cut is the point - the sound does not decay, it stops, and the low end arrives
    # a beat later as if from the other side of it.
    cut = int(swell * SR)
    for i in range(cut - int(0.012 * SR), cut):
        out[i] *= (cut - i) / (0.012 * SR)

    sub_len = 1.1
    sn = int(sub_len * SR)
    sub = mix(apply_env(sine(sub_len, 38), env_decay(sn, 0.22, attack=0.006)),
              apply_env(lowpass(noise(sub_len, rng), 260), scale(env_decay(sn, 0.16), 0.5)))
    out = at(out, scale(sub, 0.8), swell + 0.09)

    return fade_edges(normalize(out, 0.8))


def power_down():
    """The facility switching the cell off. Plays under the closed eyelids, and the wall panels
    booting back up during the wake-up is the other half of it.

    A contactor drops out, then everything that was spinning runs down: the motor falls away in
    pitch while its filter closes, and a thin electrical whine slides down over the top and dies
    first. It ends in actual silence rather than a fade, because that is what switching off is."""
    rng = random.Random(103)
    dur = 2.6
    n = int(dur * SR)

    # The contactor. Dry and mechanical, no ring.
    clunk = mix(apply_env(sine(0.2, 96), env_decay(int(0.2 * SR), 0.035, attack=0.001)),
                apply_env(lowpass(noise(0.2, rng), 800), scale(env_decay(int(0.2 * SR), 0.02), 0.7)))

    # The motor running down. Pitch and amplitude fall together, and the harmonics go first.
    ph = 0.0
    motor = [0.0] * n
    for i in range(n):
        k = i / n
        f = 30.0 + 165.0 * math.exp(-3.2 * k)
        ph += 2.0 * math.pi * f / SR
        motor[i] = (0.55 * math.sin(ph) + 0.3 * math.sin(2 * ph) * (1 - k)
                    + 0.15 * math.sin(3 * ph) * (1 - k) ** 2)
    motor = apply_env(lowpass(motor, 900, q=1.1),
                      [math.exp(-2.1 * (i / n)) for i in range(n)])

    # The electrical whine over the top - the thing you notice stopping. Gone by a third of the way
    # in, well before the motor.
    whine_len = 1.0
    whine = sweep_bandpass(noise(whine_len, rng), 3400, 700, q=14)
    wn = len(whine)
    whine = apply_env(whine, [0.5 * math.exp(-3.4 * (i / wn)) for i in range(wn)])

    out = at(scale(motor, 0.9), scale(clunk, 0.8), 0.0)
    out = at(out, scale(whine, 0.35), 0.02)

    # One tick of something cooling, well after everything else has stopped. It is what makes the
    # silence afterwards read as "off" rather than as the clip ending.
    tick = apply_env(bandpass(noise(0.03, rng), 2600, q=3.0),
                     env_decay(int(0.03 * SR), 0.006, attack=0.0004))
    out = at(out, scale(tick, 0.12), 2.05)

    return fade_edges(normalize(out, 0.72))


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


def balloon_pop():
    """A balloon bursting. Three layers, and the order they arrive in is the whole sound.

    A burst is a pressure step, not a tone: the ear identifies it from a transient that is over
    almost before it starts. The band-passed crack is therefore very short (tau 4ms) and carries
    most of the level. Under it sits a brief low thump for body - without it the pop is thin and
    reads as a click on a mic rather than as something in the room. Last is a rubbery flap, the
    skin whipping back on itself, which is what stops it sounding like a gunshot.

    Kept under a fifth of a second on purpose: seventy of these can be in flight at once, and
    anything with a tail turns a roomful of balloons into a wash."""
    rng = random.Random(7717)

    crack = apply_env(bandpass(noise(0.05, rng), 1850.0, q=0.9),
                      env_decay(int(0.05 * SR), 0.004, attack=0.0004))

    body = apply_env(sine(0.06, 172.0), env_decay(int(0.06 * SR), 0.012, attack=0.0006))

    # The skin letting go. Low-passed and quiet - it is the part you hear only because the crack
    # has just got out of the way.
    flap = apply_env(lowpass(noise(0.14, rng), 900.0, q=0.7),
                     env_decay(int(0.14 * SR), 0.03, attack=0.004))

    out = mix(scale(crack, 1.0), scale(body, 0.45))
    out = at(out, scale(flap, 0.22), 0.006)
    return fade_edges(normalize(out, 0.85))


def drawer_open():
    """A drawer sliding out and stopping. Filtered noise that swells as it runs and a soft knock
    where it reaches its stop - the knock is what makes it read as a drawer rather than a sweep."""
    rng = random.Random(4242)
    n = int(0.42 * SR)

    slide = sweep_bandpass(noise(0.42, rng), 420.0, 1250.0, q=1.4)
    # Rises, then eases off as the hand slows near the end of the travel.
    env = [math.sin(math.pi * min(1.0, (i / n) ** 0.7)) for i in range(n)]
    slide = apply_env(slide, env)

    knock = apply_env(lowpass(noise(0.09, rng), 320.0, q=0.8),
                      env_decay(int(0.09 * SR), 0.02, attack=0.001))

    out = at(scale(slide, 0.5), scale(knock, 0.55), 0.36)
    return fade_edges(normalize(out, 0.5))


def item_pickup():
    """Taking something off a shelf. Short, bright and dry - it confirms a state change and then
    gets out of the way, because it fires in the middle of a sixty-second clock."""
    rng = random.Random(9091)
    tick = apply_env(bandpass(noise(0.03, rng), 3200.0, q=1.1),
                     env_decay(int(0.03 * SR), 0.005, attack=0.0004))
    ring = apply_env(sine(0.12, 1760.0), env_decay(int(0.12 * SR), 0.03, attack=0.001))
    out = mix(scale(tick, 1.0), scale(ring, 0.3))
    return fade_edges(normalize(out, 0.45))


def footstep(seed, weight=1.0, scuff=1.0):
    """One footfall on a hard smooth floor. Three variants are written and the controller cycles
    them, because a single clip repeated at three steps a second is instantly recognisable as one
    clip repeated at three steps a second - pitch-shifting one file does not fix that, it just makes
    the repetition sound detuned.

    Two layers and no third. A low thud, which is the shoe arriving and is nearly all of the weight;
    and a very short band of high noise for the scuff of a sole on a hard surface. The room is a
    sealed white box with a bare slab floor, so there is no carpet or grit to be heard - the scuff is
    what says the floor is hard.

    Under 0.13s TOTAL, and that is the constraint everything else bends to: at a sprint these fire
    every 0.3s, so anything with a tail overlaps its own next step and turns walking into a rumble.
    Same reasoning as balloon_pop, which has seventy of itself to worry about."""
    rng = random.Random(seed)

    # The arrival. Low-passed hard, because a footstep on a slab has almost nothing above 400Hz
    # except the scuff, and leaving that in makes it a slap.
    thud = apply_env(lowpass(noise(0.10, rng), 210.0, q=0.8),
                     env_decay(int(0.10 * SR), 0.020, attack=0.0012))

    # Body under the thud, for mass. Low enough to be felt rather than pitched - a footstep with an
    # audible NOTE in it reads as a drum.
    body = apply_env(sine(0.09, 88.0), env_decay(int(0.09 * SR), 0.016, attack=0.0008))

    # The sole. Brief and quiet: it is the only part above 1kHz, so it decides whether the floor
    # sounds hard or soft, and it is a fifth of the level of the thud.
    sole = apply_env(bandpass(noise(0.035, rng), 2400.0, q=0.8),
                     env_decay(int(0.035 * SR), 0.006, attack=0.0004))

    out = mix(scale(thud, 1.0), scale(body, 0.55 * weight))
    # Offset a hair, so the sole lands just after the heel rather than on top of it.
    out = at(out, scale(sole, 0.20 * scuff), 0.004)
    return fade_edges(normalize(out, 0.62))


def item_drop(seed=7701):
    """An object let go of, landing on a hard floor. A THUD, not a note.

    It replaces `sfx_floor_button_press` pitched down, which is what the fall borrowed while nothing
    better existed - and that clip is a struck C6 left to ring, so a dropped cube announced itself
    like a doorbell however far the pitch came down. Pitching a pitched sound down does not stop it
    being pitched.

    Three layers, in order of how much they matter. The IMPACT is a burst of noise pushed into the
    low-mids and cut off inside 15ms - almost the whole sound, and the reason it reads as an event
    rather than a tone. The BODY is a short sine at 104 Hz for mass; it is deliberately lower and
    shorter than a footstep's, because a dropped object is lighter than a person and stops sooner,
    and anything longer starts to read as a kick drum - the mistake `button_tone` documents having
    made in the other direction. The TAP is the only thing above 1 kHz and it is what says the floor
    is hard rather than carpeted, the same job the sole does in `footstep`.

    Then a much quieter second contact 55ms later: things dropped do not stop dead, they settle. It
    is 18% of the level, which is under conscious notice and is what stops the clip sounding like a
    sample rather than a thing happening.

    Under 0.15s in total, for the reason `footstep` and `balloon_pop` both are: several objects can
    land close together - a tower coming apart is two - and a tail turns that into a rumble."""
    rng = random.Random(seed)

    impact = apply_env(lowpass(noise(0.07, rng), 260.0, q=0.8),
                       env_decay(int(0.07 * SR), 0.013, attack=0.0006))
    body = apply_env(sine(0.06, 104.0), env_decay(int(0.06 * SR), 0.011, attack=0.0006))
    tap = apply_env(bandpass(noise(0.02, rng), 1900.0, q=0.9),
                    env_decay(int(0.02 * SR), 0.0035, attack=0.0003))

    out = mix(scale(impact, 1.0), scale(body, 0.50))
    # A hair after the impact rather than on top of it, so the surface is heard as a consequence of
    # the arrival - the same offset the footstep's sole uses.
    out = at(out, scale(tap, 0.28), 0.002)

    settle = mix(scale(apply_env(lowpass(noise(0.03, rng), 300.0, q=0.8),
                                 env_decay(int(0.03 * SR), 0.006, attack=0.0004)), 1.0),
                 scale(apply_env(bandpass(noise(0.012, rng), 2100.0, q=0.9),
                                 env_decay(int(0.012 * SR), 0.002, attack=0.0002)), 0.35))
    out = at(out, scale(settle, 0.18), 0.055)

    return fade_edges(normalize(out, 0.62))


def gas_hiss(seed=8803):
    """Sleeping gas released into a sealed room. The one sound in the game that arrives with no
    warning, so what it has to do is be UNDERSTOOD before it is located.

    A valve, not a spray can. The onset is the whole of the recognition: 8ms of attack and a bandpass
    swept DOWN from 5.2k to 2.4k, which is a nozzle opening under pressure and then the pressure
    equalising. Swept up instead it reads as something charging - the opposite of the meaning, and
    exactly the mistake `pull_in` warns about in the other direction.

    Broadband noise on its own is a shower. What makes this read as gas under pressure is that the
    hiss sits ON something: a low bed at 180 Hz for the volume of air actually moving, at only 22%,
    which is under conscious notice and is doing all the work of making the room feel small.

    2.8 seconds, and it does NOT decay to nothing - it settles to about a third and stays there.
    Gas that stops is gas somebody turned off; this is a room filling, and the clip has to still be
    going while the haze closes over. `SleepingGas` runs 0.35s of onset then 2.6s of fill, so the
    tail is sized to outlast the whole of it rather than to end tidily."""
    rng = random.Random(seed)
    length = 2.8
    n = int(length * SR)

    # The nozzle. Swept down, and wide (q=1.6) because a narrow band on noise whistles rather than
    # hisses - a kettle instead of a valve.
    jet = sweep_bandpass(noise(length, rng), 5200.0, 2400.0, q=1.6)

    # Air, not tone. Kept quiet enough to be felt rather than heard.
    bed = lowpass(noise(length, rng), 180.0, q=0.7)

    # Fast in, then held. Built by hand rather than with env_ar, because neither an attack/release
    # pair nor a decay can express "arrive, then persist at a level" - and persisting is the point.
    env = [0.0] * n
    attack = int(0.008 * SR)
    settle = int(0.55 * SR)
    for i in range(n):
        if i < attack:
            env[i] = i / attack
        elif i < settle:
            # Down off the initial crack to the sustained flow.
            k = (i - attack) / float(settle - attack)
            env[i] = 1.0 - 0.62 * k
        else:
            # Very slowly giving up, so the end of the clip is not a cliff.
            k = (i - settle) / float(max(1, n - settle))
            env[i] = 0.38 * (1.0 - 0.28 * k)

    out = mix(scale(apply_env(jet, env), 1.0),
              scale(apply_env(bed, env), 0.22))

    return fade_edges(normalize(out, 0.55), seconds=0.012)


def main():
    print("Writing SFX to", os.path.normpath(OUT))
    # Three footfalls, deliberately uneven: no two real steps weigh the same, and the variation is
    # what stops a walk cycle sounding like a metronome.
    write("sfx_footstep_1", footstep(5501, weight=1.00, scuff=1.00))
    write("sfx_footstep_2", footstep(5502, weight=0.88, scuff=1.25))
    write("sfx_footstep_3", footstep(5503, weight=1.10, scuff=0.80))
    write("sfx_floor_button_press", floor_button_press())
    write("sfx_floor_button_release", floor_button_release())
    write("sfx_door_open", door_open())
    write("sfx_gasp", gasp())
    write("sfx_sheet_rustle", sheet_rustle())
    write("sfx_ominous_loop", ominous_loop())
    write("sfx_pull_in", pull_in())
    write("sfx_power_down", power_down())
    write("sfx_chime", chime())
    write("sfx_balloon_pop", balloon_pop())
    write("sfx_drawer_open", drawer_open())
    write("sfx_item_pickup", item_pickup())
    write("sfx_item_drop", item_drop())
    write("sfx_gas_hiss", gas_hiss())
    print("done")


if __name__ == "__main__":
    main()

# Regenerates every PA-announcer voice line as 16-bit 22.05 kHz mono WAV.
#
# **ELEVENLABS** (2026-09-02). This is the third engine. Windows SAPI came first and is unshippable -
# Zira and Heami come with the operating system and carry no redistribution licence. MeloTTS replaced
# it because it is MIT and free, and was judged mushy and, once pitched down far enough to stop
# sounding sharp, male. Both are in `git log` if either is ever wanted back.
#
# **WHY PAYING WON.** The whole set is 255 clips and 4,857 characters - measured 2026-09-02, against
# a month's allowance of 37,935 on this account, so a complete re-voice is about an eighth of it.
# (It was written here as "115 clips and 1,700 characters" while the set was half its present size;
# the argument is unchanged, the numbers were not.) The cheapest paid tier is where the commercial
# licence is understood to start - understood, not checked: see `docs/asset-licences.md`. Against that: this is the
# most-replayed audio in the game (the iteration line alone fires thirty-odd times in a run), and two
# days went into open-source engines that did not get there. The reproducible-from-a-script property
# survives in the shape that matters - the generated WAVs are committed, so a fresh clone builds and
# plays with no key. A key is needed only to CHANGE what the PA says.
#
#   en -> Assets/Audio/Voice
#
# There is no Korean set and there will not be one: the PA speaks English in every language and the
# player reads a caption. See `PaSubtitle` and `docs/audio.md`.
#
# Install (once):
#
#   conda create -p C:\eleven python=3.11
#   C:\eleven\python -m pip install elevenlabs numpy
#
# **AT `C:\eleven`, NOT INSIDE THE REPO, AND THAT IS NOT A PREFERENCE.** The SDK's own package tree is
# deep, and under `<repo>\.venv-eleven\Lib\site-packages\elevenlabs\...` it overruns Windows' 260
# character path limit - pip reports success, silently drops files, and the first import dies on a
# module that was never written. A short prefix is the fix that needs no administrator.
#
# Run:
#
#   set ELEVENLABS_API_KEY=sk_...
#   C:\eleven\python Tools\generate_narration.py --find "Jessica Anne"  # search the library
#   C:\eleven\python Tools\generate_narration.py --list-voices    # what is on the account
#   C:\eleven\python Tools\generate_narration.py                  # only what is missing
#   C:\eleven\python Tools\generate_narration.py --force          # everything, spends credits
#   C:\eleven\python Tools\generate_narration.py --only voice_ride --force

import argparse
import math
import os
import re
import sys
import wave

import numpy as np

# ---------------------------------------------------------------------------------------------
# **WHICH VOICE.** `--list-voices` prints the id of everything on the account, which is how this gets
# filled in - not by copying a string out of a URL.
#
# A Voice Library voice has to be ADDED to the account before the API can reach it by id ("Add to my
# voices" on its page). One that is not added comes back as a 404 naming the id, which says it more
# clearly than this comment can.
VOICE_ID = "QByd5J8pzbnMMEP2G7eR"
VOICE_NAME = "Gwen - Warm, Alto, Professional"

# **AND A NOTE ABOUT WHICH PLAN THIS WAS GENERATED ON.** A stock voice like Sarah works on the free
# tier; a Voice Library voice returns 402 `paid_plan_required` until the account is on a paid plan.
# More importantly the COMMERCIAL LICENCE attaches at generation time and does not backdate, so
# anything baked on the free tier is a development set and has to be regenerated before release.
# That costs about 1,700 characters, which is six percent of one month on the cheapest paid tier.
#
# The one that was picked first and could not be auditioned:
#   flHkNRp1BlvT73UL6gyz   Jessica Anne Bogart - Eloquent Villain

# **`eleven_multilingual_v2`, NOT `eleven_v3`, AND THE REASON IS THE REPORT.**
#
# The evaluation the facility reads out at the end is assembled at RUNTIME from separately
# synthesised word clips - "cycle", "one", "nine", "iterations" - because the numbers are the
# player's own and cannot be known in advance. v3 is the expressive model: it acts, and it varies its
# reading between calls. Six words each acted separately do not add up to a sentence. v2 is the
# steady one.
#
# This is the same constraint that ruled out every voice-cloning engine before this (`docs/audio.md`).
# It is the hardest requirement on this asset and the easiest to forget, because nothing reveals it
# until the last five minutes of an hour-long game.
MODEL_ID = "eleven_multilingual_v2"

# **STABILITY HIGH, STYLE ZERO, for the same reason.** Stability trades expressiveness for
# consistency between calls, which is exactly the trade a tannoy wants: every line read the same way.
#
# `speed` below 1.0 stands in for SAPI's `Rate -2`, a value tuned because the default cadence read as
# a screen reader rather than an announcement. It is the only pacing knob in this file.
VOICE_SETTINGS = {
    "stability": 0.75,
    "similarity_boost": 0.75,
    "style": 0.0,
    "use_speaker_boost": True,
    # 0.92 -> 0.86 (2026-09-02, by request: it reads fast). A tannoy is not in a hurry, and this
    # line is heard thirty times in a run - the reading has to survive repetition, which a brisk one
    # does not.
    # 0.92 -> 0.86 -> 0.82, by ear each time. **0.78 IS THE FLOOR** - measured, 0.78 and 0.74
    # produce identical durations, so the API clamps somewhere in between and asking for less is
    # asking for nothing. Anything slower than that has to come from punctuation in the text.
    "speed": 0.82,
}

# **RAW PCM AT THE RATE THE GAME WANTS**, so nothing is decoded, resampled or re-encoded on the way
# in. The alternative is mp3 at 44.1 kHz and two lossy conversions to get back here. Every other clip
# in this project is 22.05 kHz 16-bit mono and so are these.
OUTPUT_FORMAT = "pcm_22050"
TARGET_RATE = 22050

# **HOW LOUD, AND IT IS RMS NOW RATHER THAN PEAK** (2026-09-02, by request: the PA is quieter than
# the game's own effects).
#
# `voiceSource` is already at volume 1.0, so there was nothing left to turn up at the Unity end - and
# peak normalising is why: one sharp consonant sets the level for a whole clip, and everything else
# sits well under it.
#
# So the set is levelled by its own loudness instead: scale until the RMS of the whole set hits
# `RMS_TARGET`, then soft-limit whatever that pushes past `PEAK_CEILING` rather than refusing the
# gain. Measured after the fact, that bought +3.3 dB on average across the set - not the 6 this note
# first claimed, because the clips were re-synthesised in the same pass and each came back at its
# own level (+0.4 to +5.5).
#
# **AND THE TANNOY CHAIN IS NOT WHERE THE VOICE LOSES LEVEL, which this note used to say it was.**
# Measured through the same filters: HP 240 Hz + LP 3400 Hz costs **0.1 dB**, because speech energy
# is already inside that band, and `AddTannoyFilters` leaves `echo.dryMix` at 1 and `verb.dryLevel`
# at 0 - the dry path is unity gain end to end. There is no makeup gain to add because there is
# nothing to make up. The gap against the game's effects was in the FILES (-17.2 dBFS RMS here
# against -14.8 there), and the rest of it is settled on the Unity side by `SceneBuilder.SfxLevel`,
# which brings the effects DOWN 4 dB. Do not "restore" a loss here that was never happening.
RMS_TARGET = 0.140
PEAK_CEILING = 0.97

# **AND A CLIP THAT ENDS A SENTENCE GETS SILENCE AFTER IT.**
#
# Play's report was that "remaining" drops away instead of finishing. It is not the reading: it is
# that Unity's filters stop when the source does, so the 1.8s reverb tail the chain would have put
# under the last syllable is cut off at the last sample of the clip. The room the PA is in vanishes
# the instant it stops talking.
#
# Padded here rather than lengthened at the far end, because `PlayInOrder` waits on `clip.length` and
# a clip that is honest about its own length keeps that timing correct for free.
#
# **ONLY WHERE THE SENTENCE ENDS.** The report's phrases end in commas and are followed immediately
# by the next one - padding those would put a second of silence in the middle of a sentence. The
# punctuation already says which is which.
TAIL_SILENCE = 0.75

# Must match `SceneBuilder.NarrationIterationLines`. Past this the announcer uses the generic line.
ITERATION_LINES = 30

# **THE BEAT AFTER THE NUMBER** (2026-09-02, by request), and it is a tag rather than a join.
#
# The alternative was two calls glued over exactly two seconds of silence, the way MeloTTS had to do
# it. Both were built and heard: the tag won because the model knows it is still inside one sentence,
# so "sixty seconds remaining" continues the line instead of starting a new one - and the 1.8s reverb
# tail rings THROUGH the gap rather than stopping dead at the edge of a spliced-in silence.
#
# Measured: 2.97s without it, 4.97s with. The tag is honoured exactly, not approximately.
#
# **KEEP IT UNDER THREE SECONDS.** ElevenLabs documents this tag and warns that long breaks
# destabilise the read - the model starts inventing what to do with the time.
ITERATION_BEAT = ' <break time="2.0s" />' 

# How many cycles the report can name, and how high its counts go. Must match
# `SceneBuilder.NarrationReportCycles` / `NarrationReportMax`. A run past the maximum is clamped to it
# rather than dropped: "ninety-nine iterations" is a better failure than silence.
REPORT_CYCLES = 4
REPORT_MAX = 99


def audio_dir():
    here = os.path.dirname(os.path.abspath(__file__))
    path = os.path.normpath(os.path.join(here, "..", "Assets", "Audio", "Voice"))
    os.makedirs(path, exist_ok=True)
    return path


# =============================================================================================
# THE LINES. Every string is carried over unchanged from the two engines before this, and so is the
# reasoning that chose it - a line that was argued for is a line somebody will otherwise "improve"
# back. The order is only for the log; the filenames are what the game reads.
def script():
    lines = []

    # "Iteration N. Sixty seconds remaining." as one clip per N rather than stitching a number onto a
    # shared tail - a synthesizer gets the sentence intonation right only when it sees the whole
    # sentence. Thirty covers far more iterations than a run reaches; past that the generic line
    # stands in.
    #
    # **AND IT IS ONE UTTERANCE NOW.** SAPI needed SSML to lift the number in pitch and put a 350 ms
    # break after it; MeloTTS, which has no markup at all, needed the line synthesised in two halves
    # and joined over a measured silence. Neither hack survives here - one call, no join.
    #
    # **A COMMA AFTER THE NUMBER, NOT A FULL STOP** (2026-09-02, by request). The stop was deliberate
    # once: it gave the number a terminal contour and made the tail its own sentence, which is what
    # SAPI needed to stop reading the whole line as one flat label. This model does not need helping,
    # and at 0.82 speed a full stop opens a gap wide enough to hear as two announcements. A comma
    # keeps the pitch up across the join, so it lands as one thing said once.
    #
    # **AND THEN A TWO-SECOND BEAT ON TOP OF IT** - see `ITERATION_BEAT`.
    for n in range(1, ITERATION_LINES + 1):
        lines.append((f"voice_iteration_{n:02d}",
                      f"Iteration {n},{ITERATION_BEAT} sixty seconds remaining."))
    lines.append(("voice_iteration_generic",
                  f"New iteration,{ITERATION_BEAT} sixty seconds remaining."))

    lines.append(("voice_new_cycle", "New cycle initialized."))
    # Spoken when the player ends a cycle themselves instead of running the clock out. The
    # distinction matters: a voluntary end skips the countdown entirely, which is otherwise the
    # loop's loudest beat.
    lines.append(("voice_cycle_terminated", "Cycle terminated."))
    # The ending, and the only line a run hears exactly once. Built out of the vocabulary the player
    # already has - cycles are started and terminated all game - so a cycle being *broken* reads as
    # the same voice admitting the machine failed.
    lines.append(("voice_cycle_broken", "Containment failure. Cycle broken."))
    lines.append(("voice_manual_termination",
                  "Manual termination available. Hold N to end the cycle."))
    # Room3-2N's cube. The one line in the game that speaks TO the player rather than about the
    # machine - see `NarrationDirector.AnnounceAllCyclesBroken` for why that is the point of it.
    lines.append(("voice_all_cycles_broken",
                  "All cycles have been destroyed. You will pay the price for destroying them."))
    lines.append(("voice_ten_seconds", "Ten seconds remaining."))

    # The countdown. Each digit has to finish inside its one-second slot before the next replaces it,
    # which is why they are bare words with a full stop and nothing else.
    for d, word in zip(range(9, 0, -1),
                       ["Nine.", "Eight.", "Seven.", "Six.", "Five.",
                        "Four.", "Three.", "Two.", "One."]):
        lines.append((f"voice_count_{d}", word))

    # **THE REPORT, AS THREE PHRASES RATHER THAN SIX WORDS** (2026-09-02, by request: the joins
    # sounded awkward).
    #
    # It was "Cycle" + "One" + "Nine" + "iterations" + "Four" + "minutes" - six clips, five joins, and
    # every one of those words synthesised ALONE. A word on its own gets a word's intonation: flat,
    # and falling at the end because as far as the model knows the utterance is over. Six of those in
    # a row is a list being read out, not a sentence, and no gap length fixes it - the problem is
    # inside each clip, not between them.
    #
    # So the pieces are bigger and each one is a PHRASE the model read as a phrase:
    #
    #     "Cycle one,"   "nine iterations,"   "four minutes."
    #
    # Two joins instead of five, and the commas matter as much as the size: a phrase ending in a
    # comma is read as continuing, so the three of them arrive as one sentence with two beats in it.
    # Only the last one gets a full stop.
    #
    # **THE COST IS CLIPS, AND THEY ARE CHEAP.** Every number the report can say needs its own
    # recording now - about two hundred of them against the old twenty-eight - because the number and
    # its noun are one utterance. That is roughly 2,900 characters, or ten percent of one month on
    # the cheapest paid tier, generated once. The alternative was assembling from words, and that is
    # what sounded wrong.
    for n in range(1, REPORT_CYCLES + 1):
        lines.append((f"voice_report_cycle_{n:02d}", f"Cycle {n},"))
    lines.append(("voice_report_total", "Total,"))
    for n in range(1, REPORT_MAX + 1):
        lines.append((f"voice_report_iterations_{n:02d}",
                      f"{n} iteration," if n == 1 else f"{n} iterations,"))
    for n in range(1, REPORT_MAX + 1):
        lines.append((f"voice_report_minutes_{n:02d}",
                      f"{n} minute." if n == 1 else f"{n} minutes."))

    # **THE ONE SPOKEN SENTENCE THE ENDING ADDS.** Said once the report has been read out, so a player
    # standing in a wrecked room with nothing to do knows something is coming and that waiting is the
    # right thing to be doing. Everything else this voice says at the end is a number.
    lines.append(("voice_transport_called", "Transport has been called. Please stand by."))

    # And what it says on the way out - five lines across the cable car's climb, fired where the car
    # IS rather than off a clock (`EndingDeparture.NarrateTheRide`). The arc is the facility signing
    # off: the experiment is over, its data has been taken, thanks, you are being removed, and someone
    # is still talking to you. It never once says you passed.
    for i, text in enumerate([
            "Your experiment is complete.",
            "Your data has been assimilated. Improved results have been obtained.",
            "Thank you for your participation.",
            "You are being removed from the test environment.",
            "Guidance will continue."]):
        lines.append((f"voice_ride_{i}", text))

    return lines


# =============================================================================================
def client():
    key = os.environ.get("ELEVENLABS_API_KEY")
    if not key:
        sys.exit("ELEVENLABS_API_KEY is not set - see the header of this file.")

    try:
        from elevenlabs.client import ElevenLabs
    except ImportError:
        sys.exit("The elevenlabs package is not installed - see the header of this file.")

    return ElevenLabs(api_key=key)


def list_voices(api):
    print(f"{'voice id':24}  name")
    for v in api.voices.get_all().voices:
        print(f"{v.voice_id:24}  {v.name}")
    print("\nA Voice Library voice only appears here once it has been added to the account. "
          "Use --find to search the library itself.")


def find_shared(api, query):
    """
    **SEARCHES THE PUBLIC VOICE LIBRARY, WITHOUT THE WEB APP.**

    Finding a voice by clicking is a hunt through a UI that gets redesigned, and the only thing
    actually needed at the end of it is a twenty-character id. This asks the API the same question,
    which is both faster and still correct after the next redesign.

    It also settles the step that usually follows: a shared voice can be addressed by id directly, so
    "add it to my voices" is in most cases not a step at all - take the id and generate.
    """
    page = api.voices.get_shared(search=query, page_size=30)
    voices = getattr(page, "voices", None) or []
    if not voices:
        print("Nothing in the shared library matches %r." % query)
        return

    print("%-24s  %-40s  %s" % ("voice id", "name", "category"))
    for v in voices:
        print("%-24s  %-40s  %s" % (v.voice_id, str(v.name)[:40],
                                    getattr(v, "category", "") or ""))


def synthesise(api, text):
    """One line as float samples at TARGET_RATE. Raw PCM in; nothing is decoded on the way."""
    stream = api.text_to_speech.convert(
        text=text,
        voice_id=VOICE_ID,
        model_id=MODEL_ID,
        output_format=OUTPUT_FORMAT,
        voice_settings=VOICE_SETTINGS,
    )
    raw = stream if isinstance(stream, (bytes, bytearray)) else b"".join(stream)
    # Signed 16-bit little-endian mono, which is what `pcm_22050` means.
    return np.frombuffer(raw, dtype="<i2").astype(np.float32) / 32768.0


# A silence at least this long is a deliberate beat rather than a breath, so what sits either side of
# it is a separate clause. Only the iteration lines have one (`ITERATION_BEAT`); every other clip goes
# through `level_clauses` untouched.
CLAUSE_SPLIT_SECONDS = 0.5

# How much a quiet clause may be lifted. A ceiling, not a target: without one, a clip whose second
# half is genuinely almost silent would be dragged up into noise.
CLAUSE_MAX_LIFT_DB = 9.0


def longest_silence(audio):
    """The longest run of near-silence, in seconds. Used to check a break actually happened."""
    if audio.size == 0:
        return 0.0

    win = int(0.02 * TARGET_RATE)
    if win < 1 or audio.size < win * 2:
        return 0.0

    frames = np.array([np.sqrt(np.mean(audio[i:i + win] ** 2) + 1e-12)
                       for i in range(0, audio.size - win, win)])
    if frames.size == 0 or frames.max() <= 0:
        return 0.0

    best = run = 0
    for f in frames:
        run = run + 1 if f < frames.max() * 0.02 else 0
        best = max(best, run)
    return best * win / TARGET_RATE


def level_clauses(audio):
    """
    **EVERY CLAUSE AT THE SAME LEVEL, BECAUSE A MACHINE HAS NO OPINION ABOUT WHICH ONE MATTERS.**

    Measured on the iteration line: the two halves come out 5.4 dB apart, and it is not the tail
    going quiet - it is the head going LOUD. "Iteration four" is new information and the model
    stresses it; "sixty seconds remaining" is the same six words every time and it throws them away.
    That is exactly what a person does and exactly what a tannoy does not.

    Chasing it through the text made it worse, and that is the useful part of the finding. Asking for
    a full stop and a capital took the gap to 7.8 dB; synthesising the two sentences separately and
    splicing them took it to 9.7 dB. Every one of those raises the HEAD. The tail sat at -24 dB in
    all three, which is the model's settled reading of a routine clause and not something punctuation
    is going to argue with.

    So it is fixed here instead: split on the beat, and bring the quieter side up to the louder one.
    A clip with no beat in it - which is every clip but these thirty-one - comes back untouched.
    """
    if audio.size == 0:
        return audio

    win = int(0.02 * TARGET_RATE)
    if win < 1 or audio.size < win * 4:
        return audio

    frames = np.array([np.sqrt(np.mean(audio[i:i + win] ** 2) + 1e-12)
                       for i in range(0, audio.size - win, win)])
    if frames.size == 0 or frames.max() <= 0:
        return audio

    speaking = frames >= frames.max() * 0.02
    need = max(1, int(CLAUSE_SPLIT_SECONDS * TARGET_RATE / win))

    # Segment boundaries: runs of speech separated by a long enough run of quiet.
    segments = []
    start = None
    quiet = 0
    for i, loud in enumerate(speaking):
        if loud:
            if start is None:
                start = i
            quiet = 0
        elif start is not None:
            quiet += 1
            if quiet >= need:
                segments.append((start * win, (i - quiet + 1) * win))
                start = None
    if start is not None:
        segments.append((start * win, audio.size))

    if len(segments) < 2:
        return audio

    levels = [float(np.sqrt(np.mean(audio[a:b] ** 2) + 1e-12)) for a, b in segments]
    target = max(levels)
    ceiling = 10.0 ** (CLAUSE_MAX_LIFT_DB / 20.0)

    out = audio.copy()
    for (a, b), level in zip(segments, levels):
        if level <= 0:
            continue
        out[a:b] *= min(target / level, ceiling)
    return out


def pad_tail(audio, text):
    """Room for the reverb to ring out, on the clips that finish a sentence - see `TAIL_SILENCE`."""
    if not text.rstrip().endswith((".", "!", "?")):
        return audio
    return np.concatenate([audio, np.zeros(int(TAIL_SILENCE * TARGET_RATE), dtype=np.float32)])


# **AND ONE CLIP CAME BACK IN THE WRONG REGISTER, WHICH IS A SYNTHESIS FAULT, NOT A PLAYBACK ONE.**
#
# Play reported `voice_cycle_terminated` as read too fast AND pitched higher than everything else.
# Measured: its median F0 is 250.6 Hz against 180 for the announcer's ordinary register - rank 2 of
# 255, +4.6 semitones, and nearly SIX above the lines it is actually heard beside (`cycle_broken`
# 165.8, `new_cycle` 162.1, `all_cycles_broken` 160.9). Every file is 22.05 kHz mono 16-bit and all
# 255 `.meta` files are identical, so nothing about playback explains it.
#
# **THE CAUSE IS THE CALL, AND IT IS SYSTEMATIC.** Every clip is synthesised alone, so a two-word
# line gives the model no context to settle into - and the eight next-highest clips in the set are
# all `report_*` number fragments, which are the other short ones. Short text reads high and quick.
#
# **SO THE CORRECTION IS A RESAMPLE, WHICH FIXES BOTH COMPLAINTS WITH ONE NUMBER.** Slowing the clip
# lowers its pitch by the same ratio - a tape played slower - so "too fast" and "too high" are one
# fault with one remedy. Pitch-shifting alone would fix the second and leave the first, and would
# need formant correction to avoid sounding like a different person.
#
# **THE TARGET EXCLUDES THE `report_*` FRAGMENTS ON PURPOSE.** The whole set's median is 191.7 Hz,
# but 203 of the 255 clips are short number fragments carrying the same defect, so they bias it
# upward. The remaining 52 sit at 180.4, and the 31 iteration lines - the voice the player hears
# most - independently agree at 180.7. That agreement is why 180 is the register and not a taste.
#
# **THE FACTOR IS MEASURED AT GENERATION TIME, NEVER WRITTEN DOWN.** A re-synthesis comes back at a
# different pitch every time (`stability` is 0.75, not 1.0), so a hardcoded number would be wrong
# the moment this clip is regenerated. Naming the clip is the decision; the arithmetic is not.
REGISTER_MATCHED = {"voice_cycle_terminated"}

# Anything past this is a measurement gone wrong rather than a clip in the wrong register.
REGISTER_MAX_STRETCH = 1.6


def median_f0(audio, lo=70.0, hi=320.0):
    """Median fundamental over the voiced frames, by autocorrelation. NaN if nothing is voiced."""
    win, hop = int(0.040 * TARGET_RATE), int(0.020 * TARGET_RATE)
    lag_lo, lag_hi = int(TARGET_RATE / hi), int(TARGET_RATE / lo)
    found = []
    for start in range(0, max(0, len(audio) - win), hop):
        frame = audio[start:start + win].astype(np.float64)
        if np.sqrt(np.mean(frame ** 2)) < 0.02:
            continue
        frame = frame - frame.mean()
        ac = np.correlate(frame, frame, "full")[win - 1:]
        if ac[0] <= 0:
            continue
        window = ac[lag_lo:lag_hi]
        if window.size == 0:
            continue
        lag = int(np.argmax(window)) + lag_lo
        # A weak peak is an unvoiced frame - a consonant, or room tone - and has no pitch to report.
        if ac[lag] / ac[0] < 0.30:
            continue
        found.append(TARGET_RATE / lag)
    return float(np.median(found)) if found else float("nan")


def stretch(audio, factor):
    """Longer by `factor`, and lower by the same ratio - a tape played slower.

    **BAND-LIMITED, NOT LINEARLY INTERPOLATED, AND THE DIFFERENCE IS AUDIBLE.** The first version of
    this used `np.interp` on the theory that stretching only ever moves energy DOWN the spectrum, so
    there is nothing to alias, and that the tannoy chain lowpasses at 3.4 kHz anyway. Both halves of
    that were wrong. Aliasing is not the failure mode - RECONSTRUCTION ERROR is: linear interpolation
    between two samples is a crude lowpass whose error grows with frequency, and it lands hardest on
    the consonants. Measured on this clip, the 3-6 kHz band went from 0.43% of the total energy to
    0.23% - half the band gone - and the error against a correct resample sat only 18.9 dB below the
    signal. Play heard it immediately, as the last consonant of "terminated" smearing. And the
    3.4 kHz lowpass is applied by Unity at RUNTIME; these WAVs are clean masters, and the error was
    under the cutoff in any case.

    Zero-padding the spectrum is exact band-limited interpolation (what `scipy.signal.resample`
    does, without the dependency). It assumes the clip wraps around, which is free here: every clip
    starts and ends in silence."""
    n = int(round(len(audio) * factor))
    if n <= 1 or n == len(audio):
        return np.asarray(audio, dtype=np.float32)

    spectrum = np.fft.rfft(np.asarray(audio, dtype=np.float64))
    out = np.zeros(n // 2 + 1, dtype=complex)
    keep = min(len(spectrum), len(out))
    out[:keep] = spectrum[:keep]
    # The source's Nyquist bin becomes an ordinary bin once there is room above it; left at full
    # value it is counted twice on the way back and rings by half a sample.
    if keep == len(spectrum) < len(out):
        out[keep - 1] *= 0.5
    return (np.fft.irfft(out, n) * (n / len(audio))).astype(np.float32)


def match_register(out, names):
    """Pull the clips in `REGISTER_MATCHED` back to the announcer's own register - see the note
    above. Reads what is on DISK, like `normalise`, so an `--only` run cannot set a target from
    three clips."""
    pitches = {}
    for name in names:
        path = os.path.join(out, name + ".wav")
        if not os.path.exists(path):
            continue
        f0 = median_f0(read_wav(path))
        if not np.isnan(f0):
            pitches[name] = f0

    ordinary = [f for n, f in pitches.items() if not n.startswith("voice_report_")]
    if not ordinary:
        return []

    target = float(np.median(ordinary))
    corrected = []
    for name in sorted(REGISTER_MATCHED):
        if name not in pitches:
            continue
        factor = pitches[name] / target
        if factor <= 1.02:                      # already in register, or below it
            continue
        if factor > REGISTER_MAX_STRETCH:
            print(f"  ! {name} measured {pitches[name]:.0f} Hz against {target:.0f} - "
                  f"x{factor:.2f} is past the cap, left alone")
            continue
        path = os.path.join(out, name + ".wav")
        body, padded = split_pad(read_wav(path))
        stretched = stretch(body, factor)
        if padded:
            stretched = np.concatenate(
                [stretched, np.zeros(int(TAIL_SILENCE * TARGET_RATE), dtype=np.float32)])
        write_wav(path, stretched)
        corrected.append((name, pitches[name], target, factor))
    return corrected


def split_pad(audio):
    """Separate the tail silence `pad_tail` appended from the recording in front of it.

    **THE PADDING IS A WALL-CLOCK AMOUNT AND MUST NOT BE STRETCHED.** It is a fixed 0.75s for the
    reverb to ring out; scaling it with a x1.39 register correction took a 2.33s clip to 3.24s and
    pushed the announcement that FOLLOWS it past the wake-up (`NarrationDirector.AnnounceNewCycle` -
    the two clips plus the blink have to fit inside 5.2s). The reverb needs the same three quarters
    of a second whatever register the line is read in.

    Split HERE, before the resample, because only here are the padded samples EXACTLY zero. Trimming
    afterwards was tried and is what a band-limited resample quietly breaks: it leaves ringing across
    the silence rather than zeros, so an exact-zero test finds nothing and the clip keeps its
    stretched padding. Any threshold picked to paper over that is a guess about how loud a recorded
    reverb tail gets, which is a different question."""
    end = len(audio)
    while end > 0 and audio[end - 1] == 0.0:
        end -= 1
    return audio[:end], len(audio) - end


def report_register(corrected):
    for name, was, target, factor in corrected:
        print(f"  {name:28} {was:5.1f} Hz -> {was / factor:5.1f} Hz  "
              f"(x{factor:0.3f} slower and lower, register {target:.0f} Hz)")


def write_wav(path, audio):
    audio = np.clip(np.asarray(audio, dtype=np.float32), -1.0, 1.0)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(TARGET_RATE)
        w.writeframes((audio * 32767.0).astype("<i2").tobytes())


def read_wav(path):
    with wave.open(path, "rb") as w:
        return np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(np.float32) / 32768.0


def normalise(out, names):
    """
    ONE GAIN FOR THE WHOLE SET, applied after everything is written.

    **PER SET, NOT PER CLIP**, and that distinction is the point. The relative loudness of the clips
    is part of the reading - a word in the middle of an assembled sentence should sit where the
    synthesizer put it - so every clip is multiplied by the SAME number, chosen so the loudest lands
    on `PEAK_TARGET`. Normalising each clip alone would flatten the report into a row of equally
    shouted words.

    Reads what is on DISK rather than what this run made, deliberately: a `--only` run must not level
    three new clips against each other and leave them at a different level from the other eighty.
    """
    total = 0.0
    samples = 0
    audio = {}
    for name in names:
        path = os.path.join(out, name + ".wav")
        if not os.path.exists(path):
            continue
        data = read_wav(path)
        audio[path] = data
        # Speech only. Counting the padded tails would drag the measured loudness down and the gain
        # up by however much silence happens to be in the set.
        voiced = data[np.abs(data) > 0.01]
        total += float(np.sum(voiced.astype(np.float64) ** 2))
        samples += voiced.size

    if samples == 0:
        return 1.0

    gain = RMS_TARGET / max(1e-9, (total / samples) ** 0.5)

    # **A GAIN NOBODY CAN HEAR IS NOT WORTH REWRITING 255 FILES FOR.** These WAVs are tracked source,
    # so every rewrite is a binary diff in the history whether or not the audio changed - and a run
    # that corrects ONE clip still moves the whole set's measured RMS a hair, which came out as
    # +0.08 dB across 254 files that nothing had touched. The threshold is well under the ~1 dB a
    # listener can pick up in an A/B, so anything it skips is inaudible by construction.
    if abs(20.0 * math.log10(max(gain, 1e-9))) < 0.10:
        return 1.0

    for path, data in audio.items():
        # Soft-limited rather than clipped, and rather than refusing the gain: a handful of
        # consonants are what would otherwise cap the level of the whole set.
        write_wav(path, soft_limit(data * gain))
    return gain


def soft_limit(x):
    """Anything over `PEAK_CEILING` is rounded off instead of squared off."""
    over = np.abs(x) > PEAK_CEILING
    if not over.any():
        return x

    y = x.copy()
    excess = (np.abs(y[over]) - PEAK_CEILING) / (1.0 - PEAK_CEILING + 1e-9)
    y[over] = np.sign(y[over]) * (PEAK_CEILING + (1.0 - PEAK_CEILING) * np.tanh(excess))
    return y


# **THE BREAK TAG IS HONOURED USUALLY, NOT ALWAYS - SO IT IS CHECKED.**
#
# Thirty of the thirty-one iteration lines came back with the two-second beat in them and one did
# not: `voice_iteration_30` was read straight through, 5.06s with a 0.36s gap where the others have
# 2.1s. Nothing errored. The clip is valid audio of the right words at the right speed, and the only
# way to know is to look for the silence.
#
# That is the shape of fault worth spending code on: it is invisible, it is per-clip, and it comes
# back differently on every run - so a human checking once proves nothing about the next generation.
#
# Retried rather than repaired. Splicing silence into the middle would give the beat but not the
# reading around it; asking again costs one call and usually lands.
BREAK_TRIES = 4


def break_seconds(text):
    """How long a beat this line asks for, or 0 if it asks for none."""
    match = re.search(r'<break\s+time="([0-9.]+)s"', text)
    return float(match.group(1)) if match else 0.0


def synthesise_checked(api, text, name):
    """One line, with the beat verified if it asked for one."""
    wanted = break_seconds(text)
    audio = synthesise(api, text)
    if wanted <= 0:
        return audio, 1

    # 70% of what was asked for. The model does not place it to the millisecond and does not need to;
    # what is being caught is a beat that is not there AT ALL.
    floor = wanted * 0.7
    for attempt in range(1, BREAK_TRIES + 1):
        if longest_silence(audio) >= floor:
            return audio, attempt
        if attempt == BREAK_TRIES:
            break
        audio = synthesise(api, text)

    print(f"    ! {name}: asked for a {wanted:0.1f}s beat and did not get one in "
          f"{BREAK_TRIES} tries (longest silence {longest_silence(audio):0.2f}s). Kept anyway.")
    return audio, BREAK_TRIES


def main():
    parser = argparse.ArgumentParser(description="Generate the PA narration through ElevenLabs.")
    parser.add_argument("--list-voices", action="store_true",
                        help="print every voice on the account with its id, then stop")
    parser.add_argument("--find", default=None, metavar="NAME",
                        help="search the public Voice Library by name and print ids, then stop")
    # **DEFAULTS TO SKIPPING WHAT EXISTS, because every call costs credits.** A re-run after adding
    # one line should cost one line, not eighty-four.
    parser.add_argument("--force", action="store_true",
                        help="regenerate clips that already exist (spends credits)")
    parser.add_argument("--only", default=None,
                        help="only clips whose filename starts with this")
    # Offline, and costs nothing: re-registers and re-levels what is already on disk. The register
    # pass needs no API, so after a hand edit or a partial run it is the cheapest way to put the
    # whole set back in agreement with itself.
    parser.add_argument("--register-only", action="store_true",
                        help="skip synthesis; just match register and re-level the clips on disk")
    args = parser.parse_args()

    if args.register_only:
        out = audio_dir()
        names = [n for n, _ in script()]
        report_register(match_register(out, names))
        gain = normalise(out, names)
        print(f"Levelled the whole set by x{gain:0.2f}.  ->  {out}")
        return

    api = client()
    if args.list_voices:
        list_voices(api)
        return
    if args.find:
        find_shared(api, args.find)
        return

    if not VOICE_ID:
        sys.exit("VOICE_ID is empty - run --list-voices and fill it in at the top of this file.")

    out = audio_dir()
    lines = script()
    if args.only:
        lines = [(n, t) for n, t in lines if n.startswith(args.only)]
        if not lines:
            sys.exit(f"--only {args.only} matched none of the {len(script())} clips.")

    made = skipped = characters = retries = 0
    for name, text in lines:
        path = os.path.join(out, name + ".wav")
        if os.path.exists(path) and not args.force:
            skipped += 1
            continue

        audio, tries = synthesise_checked(api, text, name)
        write_wav(path, pad_tail(level_clauses(audio), text))
        made += 1
        characters += len(text) * tries
        retries += tries - 1
        print(f"  {name:28} {len(text):4} chars  {text}"
              + (f"   (x{tries})" if tries > 1 else ""))

    # BEFORE the levelling, so the gain is measured on the audio that actually ships.
    report_register(match_register(out, [n for n, _ in script()]))

    gain = normalise(out, [n for n, _ in script()])
    print(f"\n{made} clip(s) written, {skipped} already present, {characters} characters spent"
          + (f" ({retries} retried for a missing beat)." if retries else "."))
    print(f"Levelled the whole set by x{gain:0.2f}.  ->  {out}")


if __name__ == "__main__":
    main()

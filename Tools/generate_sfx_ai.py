r"""The few sound effects that are GENERATED RATHER THAN SYNTHESISED.

`generate_sfx.py` builds everything else from the Python standard library, and that is still the
rule - every clip in this project that can be built from oscillators and filters is. These are the
exception, and the reason is recorded because it cost three attempts to reach:

**A CREAK COULD NOT BE SYNTHESISED CONVINCINGLY HERE.** Three passes were made. A swept sine sounded
like a synthesizer, which is what it was. A stick-slip impulse train was the right physics and
sounded like a hiss, because each release flipped sign every sample. Fixing that put 87% of the
energy in 200-1kHz with a clean 516Hz peak - measurably a metal ring, and still not what a hanger
under load sounds like. What a real one has is a texture that is neither tone nor noise, and this
project's toolkit has no way to make it.

**THE STEREO TRAP.** `pcm_44100` comes back INTERLEAVED STEREO. Written straight out as mono it is
exactly twice as long and an octave low - which is subtle enough to be mistaken for a bad take
rather than a format error. The channels are averaged back down here, and the check that catches it
is that the finished clip's duration equals `duration_seconds`.

    set ELEVENLABS_API_KEY=sk_...
    C:\eleven\python Tools\generate_sfx_ai.py

Licence: see `docs/asset-licences.md`. These are model output on a paid plan, which is a different
answer from the rest of the folder and is written down there rather than here.
"""
import os, sys, io, wave, struct
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
from elevenlabs.client import ElevenLabs
api = ElevenLabs(api_key=os.environ["ELEVENLABS_API_KEY"])
OUT = r"C:\Users\seonl\Desktop\c\2026\summer\Iteration\Assets\Audio\SFX"
SR = 44100

PROMPT = ("A single slow creak of a heavy steel cable-car hanger taking load. "
          "Metal grinding and rubbing against metal, a low groaning creak that rises and falls once. "
          "Close, dry, no reverb. No music, no voice, no wind, no machinery hum.")

for i, dur in ((1, 1.0), (2, 1.2), (3, 0.9)):
    raw = b"".join(api.text_to_sound_effects.convert(
        text=PROMPT, output_format="pcm_44100",
        duration_seconds=dur, prompt_influence=0.75,
        model_id="eleven_text_to_sound_v2"))
    # INTERLEAVED STEREO - see the header. Averaged down, not decimated: taking one channel throws
    # away half the take, and the two are correlated but not identical.
    import array
    pcm = array.array("h"); pcm.frombytes(raw[:len(raw) // 4 * 4])
    mono = array.array("h", ((pcm[k] + pcm[k + 1]) // 2 for k in range(0, len(pcm) - 1, 2)))

    path = os.path.join(OUT, f"sfx_cable_creak_{i}.wav")
    with wave.open(path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes(mono.tobytes())
    got = len(mono) / SR
    flag = "" if abs(got - dur) < 0.05 else "   ! not the length asked for - stereo handling?"
    print(f"  creak_{i}: {got:.2f}s (asked {dur:.2f}){flag}")

# Audio

The PA announcer and its schedule, the tannoy filter chain, how every clip is generated, and spatialisation.

---

## Audio

A diegetic PA announcer, taken from the reference film. The schedule is the whole system.

| Cue | When | Method |
| --- | --- | --- |
| "Iteration N, 60 seconds remaining." | after the wake-up, as the clock starts — **no chime** | `AnnounceIteration` |
| "10 seconds remaining." | `tenSecondCueAt` (T-12, not T-10) | `AnnounceTenSeconds` |
| "Nine." … "One." | one per whole second, T-9 to T-1 | `AnnounceCountdown` |
| "New cycle initialized." | top of the next iteration, under closed eyelids | `AnnounceNewCycle` |
| "Cycle terminated." | the player ends the cycle early | `AnnounceCycleTerminated` |
| "Containment failure. Cycle broken." | the player escapes — once per run, ever | `AnnounceCycleBroken` |
| "Manual termination available. Hold N…" | **iteration 2, 5s after waking in Room1** | `AnnounceManualTermination` |

- **The T-10 line is cued at T-12 on purpose.** It runs about two seconds, and from T-9 the digit countdown replaces it every second, so a literal T-10 clips it after one word. Retune `tenSecondCueAt` if the line is ever re-recorded.
- Cues are **pushed from `RunLoop`, not polled**. The loop owns `ElapsedTime`, and the announcer must stay silent through the wake-up. The counting loop guards on a *falling* whole second, so it fires once per second at any frame rate.
- Iteration 1 gets no "New cycle initialized" and no reset sting — it opens the run rather than resetting it.
- **The iteration line has no chime**, though T-10, "Cycle terminated." and the ending line do. It fires hundreds of times a run, and a two-note ding ahead of it made the loop's most repeated moment its most decorated. The ending line *is* chimed — the most important thing the facility ever says, and its wording is built from vocabulary the player already has, so a cycle being **broken** lands as the same voice admitting the machine failed.
- **Announcements replace each other** (`Stop()` + `Play()`), never `PlayOneShot` — the countdown fires once a second and one-shots would slur the digits together.
  - **That is why the manual-termination line carries a 5-second delay** (`PanelMessage.announceDelay`). Its sign moved into Room1, which is where the player *starts* an iteration, so an undelayed cue would fire in the same moment as "Iteration 2, 60 seconds remaining." and truncate it. **Anything new that announces itself from Room1, or from the first seconds of a cycle, has to clear that line or replace it.**

### The PA treatment

`AddTannoyFilters` puts the voice **and** the chime through a five-stage chain so they read as a horn on the wall of a hard, empty room rather than narration over the game.

- **A runtime chain on the `AudioSource`, not baked into the WAVs.** The clips stay clean masters, retuning is an Inspector drag, and a better-acted take dropped into `Assets/Audio/Voice/` inherits the treatment for free.
- **Component order on the GameObject *is* the signal chain** — Unity runs filters top to bottom. Reverb goes last: ahead of the distortion it grits up the tail as well as the voice, which reads as a broken speaker rather than a room.
- **Band-limiting does most of the work, not the reverb.** High-pass **340 Hz** / low-pass **3600 Hz** with the low-pass left resonant (`Q` 1.6) — a horn driver has no bottom and no top, and that peak is the nasal honk of every station announcement ever made. The ear identifies the *channel* long before the space.
- Distortion **0.17** — past ~0.3 the words stop being intelligible, and the announcer carries actual information.
- Echo **105 ms** at `decayRatio` 0.22 / `wetMix` 0.33, roughly the round trip across a room this size. Reverb is `User`, `decayTime` 2.1s with `roomHF` -900 and `decayHFRatio` 0.55 — a deliberately dark tail, since a bright one would undo the band-limiting.
- **`Tools/preview_pa_voice.py` renders the same chain offline** (`python Tools/preview_pa_voice.py voice_count_3`). An approximation — Unity's reverb is FMOD's, this is a Schroeder network — so trust it for "how much echo" and "can I make out the number", not the exact tail. **Its constants mirror `AddTannoyFilters`; change one and change the other.**

### Where the audio comes from

**Everything is generated from a script.** That is the same bargain the wall grain makes: the whole project must rebuild from scripts, and a folder of sourced CC0 clips was the one part that could not.

- `Tools/generate_narration.ps1` drives the Windows synthesizer to write **45 lines per language** as 22 kHz 16-bit mono WAV: English (`Microsoft Zira Desktop`) into `Assets/Audio/Voice/`, Korean (`Microsoft Heami Desktop`) into `Assets/Audio/Voice/ko/`. **Both folders use the same filenames**, which is what lets one loader serve either — `SceneBuilder.LoadVoiceSet(dir)` takes only the directory. English stayed at the root rather than moving to `Voice/en`, so nothing already pointing at those files had to be repointed.
  - **THE SCRIPT MUST KEEP ITS UTF-8 BOM.** Windows PowerShell 5.1 reads a `.ps1` without one as ANSI, and the failure is close to invisible: the mangled Hangul still *synthesises*, so the first Korean pass produced 38 clips of confident nonsense at plausible durations and only 7 zero-length files. Length alone does not diagnose it. See `docs/gotchas.md`.
  - **The countdown digits are Sino-Korean** (구·팔·칠), not native (아홉·여덟). A machine reading a clock counts that way, and it is the shorter of the two — which matters, because each digit has to finish inside its one-second slot. Measured: the longest Korean digit is **1.35s** against English's **1.63s**, so the constraint is looser than it already was.
  - **Both sets are wired into the scene and one is chosen at runtime** (`NarrationDirector.Lines`), rather than loaded on demand: 45 clips of 22 kHz mono is a couple of megabytes, less than the machinery to avoid holding it. Korean falls back to English clip-by-clip if the folder was never generated. To replace them with better-acted takes, keep the filenames — no C# refers to how they were made. `SceneBuilder.NarrationIterationLines` must match the range the script writes (30, then a generic line stands in).
  - **Getting a rising ending out of SAPI requires a question mark.** The iteration lines are written `Iteration <prosody pitch="+35%">N</prosody>? 60 seconds remaining.` — the terminal contour is chosen from sentence punctuation and overrides everything else. Measured on Zira at rate -2: `"1!"` 220→160 Hz (falls; the exclamation does nothing), `prosody contour="…"` 202→138 Hz (SAPI ignores the attribute), `"1?"` 182→232 Hz, `pitch +35%` **plus** `?` 179→259 Hz. The `?` is never spoken, and because it closes the sentence on the number the rise sits there.
  - **Rate is -2 for sentences, -1 for the countdown digits.** The default clip read as a screen reader rather than a PA. The digits cannot go slower than -1 — each must finish inside its one-second slot (verified: every digit's spoken part ends by 0.78s).
- `Tools/generate_sfx.py` writes twelve clips with the Python standard library — no numpy, no downloads, each seeded so re-running reproduces the identical set. See `Assets/Audio/SFX/README.md` for the filename table.
  - **`sfx_balloon_pop` is under a fifth of a second on purpose** — seventy can be in flight at once and anything with a tail turns a roomful of balloons into a wash. Each balloon carries a **fixed detune** from the field's seed: one clip across seventy reads as a machine gun, and a balloon keeping the same voice every iteration is one more thing about the room that stays put.
  - **The room-tone loop is the one with a real constraint.** It plays on `loop`, so a seam is a click every 8 seconds for the whole session. Every partial is a multiple of 1/8 Hz so the tone wraps exactly, and the noise layer is wrapped by crossfading its own tail over its head (`seamless()`). Verified numerically: the step across the wrap is smaller than the largest step inside the clip.
  - `sfx_pull_in` runs two motions against each other — a noise band climbing while a tone falls away underneath. Up and down at once reads as being pulled *through* something; either alone is just a riser or a drop. It ends on a hard cut with the sub a beat late.
  - **There is no tool-swing sound.** `BalloonTool.swingClip` is deliberately null rather than borrowing the sheet rustle; swinging at air gives no audible feedback, which is a known gap.
  - `LoadClip` is extension-agnostic and **a missing file resolves to `null` with every player guarding on it**, so an emptied SFX folder is still playable. Overwriting a generated clip with a recorded take under the same name is the entire swap.
- **Two clips are generated in C# rather than by the Python tool**, `SceneBuilder.MakeChopClip` and `SceneBuilder.MakeWadeClip`, both written straight to `Assets/Audio/SFX` as 44.1 kHz mono WAV and both cached (rebuilt only if the file is missing). They are in C# because they were written alongside the rooms that needed them; anything new is free to go in either place, as long as it is seeded and reproducible.
  - **`sfx_wade_1..3` are a step through waist-deep water, and are neither a splash nor a footstep.** `sfx_water_splash_*` is water hitting something from above — a hard front and a bright scatter — and a footstep is a transient off a hard floor. Water is heavy: the sound **swells** over ~50ms instead of starting, most of its energy is low (the mass being shoved sideways), and the bubbles arrive *after* the push on their own faster decay, because that is when the dragged-under air surfaces. A sharp attack is the one thing that makes a water sound read as a sample of a water sound.
  - They are **normalised**, not hand-balanced: the layers are noise, so their sum's level depends on the seed, and without it three clips come out at three volumes and the walk has a limp.
  - Played through the player's own footstep source, swapped in by `FirstPersonController.Wading` — see `puzzle-design.md` for why the wade sound is a swapped clip on the existing gait rather than a second sound system.
- Unity MCP's `generate_audio` is **not** an option for the voice — music/SFX only, no speech, and it needs an unconfigured fal.ai key.

### Sources and spatialisation

`MakeSource` builds every `AudioSource` with linear rolloff (`maxDistance` 14) rather than Unity's logarithmic default, which stays near full volume across a room this small.

- **2D**: the PA voice and chime (a room-wide tannoy has no position to walk away from), the music bed, the machines, and the player's own breath. **3D**: the doors and the floor pads.
- **There are no footsteps.** A distance-paced `FootstepPlayer` existed and was removed with its four clips — in a room this small with a 60-second clock, the player's own steps were noise over the announcer rather than presence.
- **The floor pads are audible, and audible for ghosts too.** `FloorButton` fires a clunk on the *edges* of `IsActive`, whoever caused it. This is a real affordance: `DoorIndicator` only reports to a player looking at the door, where the clunk reaches you facing the other way — and in Room3 it is how you count pads without turning round. Edges only, since a tick while held would be unbearable across a full iteration.
  - **Silent while `IterationRunning` is false** — the loop releases every ghost during the reset, and a rack of pads letting go behind closed eyelids is the machinery showing through.
- **The loop boundary is two sounds split across the blackout.** `PlayPullIn` fires the moment the iteration ends, while the lids are still falling and the panels flaring, because that is the moment the loop takes you; held until after the blackout it explains something that already happened. `PlayPowerDown` lands under the black. The panels booting during the wake-up are the third beat: **taken, switched off, switched back on.** Both fire at the *end* of an iteration, so neither needs an `IterationNumber > 1` guard.
  - A third cue, `sfx_glass_rattle`, was removed — high-Q pings off the nightstand props read as a doorbell going off every 60 seconds.
- `Door.Close()` is **deliberately silent** — the loop rewinding world state behind a black screen, not a door being shut. A sound there draws attention to the seam.
- Exactly **one `AudioListener`**, on the player camera. A second is a Unity warning and breaks positional audio.

## The title screen has the room's own tone (2026-08-20)

The menu plays `sfx_ominous_loop` — **the same clip that runs under every second of every iteration**,
at 0.22 against the room's 0.5.

**Not menu music, and the distinction is the point.** This game has no music anywhere; giving the
title screen some would make PLAY the moment a track stops rather than the moment a door opens. The
screen is a photograph of that room, so it sounds like that room, and what the player crosses when
they press PLAY is not silence into sound — it is a place they were already standing in.

- **Faded at both ends**, for the reason `RoomAmbience.FadeOutTone` gives: a cut reads as a sound
  failing, a fade reads as a room being switched on or off around you. The source is authored at
  volume 0 with `playOnAwake`, so `MainMenu.Start` rides it up rather than calling `Play()` — which is
  what stops the loop's first sample landing as a click.
- **It goes out across the load**, and it has to. The room scene starts its OWN copy of the same loop,
  so carrying the menu's through the switch would be heard as the tone restarting from its first
  sample under a room that is already toning.
- The menu scene already has the one `AudioListener` the game is allowed (on its camera), and
  `GameSettings.ApplyAudio` in `MainMenu.Awake` has put the saved master volume on it before the fade
  starts — so the slider on the SETTINGS page governs this like everything else.

**Level is a guess.** 0.22 is "present, not announced" on one pair of headphones and has not been
checked anywhere else. It is one field (`MainMenu.ambienceVolume`).

## The alarm (2026-08-30)

Room3-2N's cube being finished adds two sounds, and both are generated like everything else here.

**`voice_all_cycles_broken`** — "All cycles have been destroyed. You will pay the price for
destroying them." Chimed, like `voice_cycle_broken`, because it is heard once in a run if ever. It is
the only line in the game that addresses the player: everything else this voice says is procedure,
and breaking that is the point rather than an oversight. Generated in both languages.

**`sfx_alarm_siren`** — 3.2 seconds, two full wails, so it loops on a whole number of them and the
pitch is back where it started at the seam. A slow continuous sweep between two pitches rather than a
warble or a bell: a warble is a car alarm and reads as petty, a bell is a fire drill and reads as
orderly, and what this room has become is a BUILDING in trouble.

The horn is a sawtooth built from six partials rather than a sine — a sine siren is a theremin —
bandpassed at the top of the sweep so the harmonics thin as it climbs, which is what a real horn
does. The phase is ACCUMULATED rather than computed per sample: `sin(2*pi*f(t)*t)` with a moving `f`
is the classic way to get a sweep wrong, because the argument jumps whenever `f` changes and the wave
breaks into clicks. Under it, a low tone breathing in time with the wail — the sub a PA cabinet adds
to everything it plays, and what makes the siren feel like it is coming out of the walls.

`WallPanelDisplay.BeginAlarm` pulses on the same period, because a light that swells out of time with
the sound reads as two unrelated things.

## The voice stays Gwen, and eight alternatives were heard (2026-09-02)

Asked for something lower and calmer. Eight candidates were auditioned on two real lines
("Cycle terminated." and "Iteration one, sixty seconds remaining."), run through an offline copy of
the tannoy chain, and **the current voice was kept** — Gwen, Warm/Alto/Professional, median F0
184.5 Hz. The rejected set, by measured F0: Hope 188.5, Mona 175.0, Ivanna 173.6, Benjamin 121.2,
Jamie 104.5, Stillwater 103.5, Steven 89.6, Adrian 84.5. The audition renders are written to
`Build/VoiceAudition/` (git-ignored) by the script in this project's history; 504 characters of API
credit, if it is ever worth redoing.

**AND THE CHOICE WAS NOT MADE ON PITCH, WHICH IS THE MOST USEFUL THING HERE.** The request was for
something lower; the candidates spanned 84.5 to 188.5 Hz, four of them a full octave below Gwen,
and every one was turned down — including the two picked out by hand afterwards, which happened to
sit within 5 Hz of Gwen either side. Whatever is being judged, F0 is not it, and a re-run that
starts by sorting the library by depth will arrive back here. Try sorting by what the voice is FOR
instead: Gwen's own record says warm/professional/calming, and the rejected `informative_educational`
and IVR voices were the ones that sounded most like a machine reading a form.

**What the exercise settled that a re-run should not have to rediscover:**

- **The chain highpasses at 240 Hz, so every male candidate has its fundamental removed entirely.**
  That is not a fault — a real tannoy does exactly that and the ear rebuilds the missing fundamental
  from the harmonics — but it means a low F0 does not predict how deep the voice will *sound* in the
  room, and a candidate must be judged on the processed render, never on the library preview.
- **ANY MEASUREMENT TAKEN AFTER THE CHAIN REPORTS THE REVERB, NOT THE VOICE.** Three metrics were
  tried — energy below the cutoff, level lost through the chain, and the share of frames that stay
  periodic — and all three were confounded the same way, because the reverb adds correlated energy
  across the whole clip. One of them had a voice getting 12 dB *louder* for passing through a
  filter, and another had breathiness *improving* to 100% voiced. Do not revive them without
  solving that first. F0 measured on the dry master is the only number here worth trusting.

---

## The PA is English-only, and subtitled (2026-09-02)

There were two voice sets, `Voice` and `Voice/ko`, and `NarrationDirector` picked between them at
runtime. There is one now. What a Korean-language player gets instead is a **caption** — `PaSubtitle`,
bottom centre, drawn from `Loc`'s `pa.*` keys.

**An English player gets none** (decided 2026-09-02, after playing it). The caption first shipped for
everybody, which meant the English player read a written copy of a sentence they had just heard in
their own language, twice a minute, for the length of a run. It is a *subtitle*: it earns its place
exactly where the audio is in a language the player did not choose, which is one of the two. The gate
is `PaSubtitle.Wanted`, a single comparison against `Loc.Current`, asked per line so that changing
the language in the pause menu takes effect on the next one — and clearing anything already on screen.

The authoring rule is deliberately **not** gated with it: every `Announce*` still calls
`Caption("pa.<key>")` beside its clip. A key that only matters in Korean is still a key, and making
the call conditional would mean a new line silently having no Korean caption while looking correct to
whoever wrote it.

**Why the audio is not translated.** The PA is the facility talking to *itself*: the same category as
`ROOM 2` over a doorway, `ERROR` on room2-0's console, and the title. `Loc`'s own header has said
since 2026-08-20 that translating the facility's signage "changes where the game is set, not what
language it is played in". The announcer was the one thing on the wrong side of that line — a building
that switches to Korean because its subject speaks Korean is a building that knows who is in it and is
trying to be helpful, which this one is not.

**And the honest reason it changed now**: the Korean clips were generated, played, and judged awkward.
The design argument above is why the change is an improvement rather than a retreat, but it is not
what prompted it.

What it costs and what it buys: one set of clips to generate instead of two, one tannoy trim instead
of two (Korean needed half the slap and half the tail because it packs more syllables into a second —
that whole tuning is gone), and no possibility of a half-translated PA. What it costs is that a
caption can only be read while looking at the screen, where a voice reaches a player facing a wall.

### The engine changed too, and for a different reason

**THERE HAVE BEEN THREE. THE CURRENT ONE IS ELEVENLABS** (2026-09-02) — everything under this heading
about MeloTTS is the SECOND engine and is kept as the record of why the open-source route was taken
and why it was then left, not as a description of what generates the audio today.

| | engine | why it went |
|---|---|---|
| 1st | Windows SAPI (Zira, Heami), driven by `generate_narration.ps1` | ships with the OS, carries no redistribution licence |
| 2nd | MeloTTS (MIT) | mushy, and once pitched down far enough to stop sounding sharp, male |
| 3rd | **ElevenLabs**, `eleven_multilingual_v2`, voice "Gwen" | current |

`generate_narration.ps1` no longer exists; `Tools/generate_narration.py` is the whole of it, and its
header carries the argument for paying. The short version: the set is 255 clips and about 4,900
characters, a few percent of one month on the cheapest paid tier, against this being the most
replayed audio in the game — the iteration line alone fires thirty-odd times in a run. **The
reproducible-from-a-script property survives in the shape that matters**: the WAVs are committed, so
a fresh clone builds and plays with no API key. A key is needed only to CHANGE what the PA says.

Licence position, and what is still open: `docs/asset-licences.md`.

**Why MeloTTS and not the better-sounding models** — the second engine's reasoning, kept because it
is still the argument to re-read if the open-source route is ever wanted back:

- **Piper** (`OHF-Voice/piper1-gpl`) — no Korean voices at all, GPL-3.0, and its own docs say
  "intended for personal use and text to speech research only".
- **Kokoro-82M** — Apache-2.0 and good, but its `VOICES.md` lists eight languages and Korean is not
  one of them.
- **Coqui XTTS-v2** — CPML, which is non-commercial.
- **Chatterbox Multilingual** — MIT and does have Korean, but it is a voice-cloning model: timbre
  drifts between calls, and the evaluation report is **assembled at runtime from separately
  synthesised word clips**, so the same sentence would come out sounding like six people reading one
  word each. It also watermarks its output.
- **Qwen3-TTS** — Apache-2.0 on code *and* weights, a 2026 model, and audibly the best of them. Its
  `instruct` parameter takes a plain-language direction ("a calm, flat public-address announcement")
  and it works: the Korean voice measured 237 Hz without it and **148 Hz with it**, which is a thing
  no amount of pitch-shifting buys honestly. It was tried and its Korean was still judged awkward —
  and once the PA stopped being translated, the only question left was English, where MeloTTS's
  `EN-AU` won a blind-ish comparison against Qwen's `sohee` by ear.

If the voice is ever revisited, **Qwen3-TTS with `instruct` is where to start**, and the samples in
this decision were `docs/voice-samples/` (git-ignored; regenerate them).

**A fixed speaker is a hard requirement, not a preference.** `AnnounceCycleResult` plays "cycle",
"one", "nine", "iterations", "four", "minutes" as six clips back to back. Anything that re-derives the
voice per call breaks that sentence.

### The delivery

`SENTENCE_SPEED` / `DIGIT_SPEED` in the generator are translations of SAPI's `Rate -2` / `-1`, which
were tuned because the default cadence read as a screen reader rather than a PA. The clips are
**levelled per language** by one shared gain, not per clip — the relative loudness inside a language is
part of the reading, but the two packs came out nearly three times apart from each other.

**The echo is not in the clips and never was.** `SceneBuilder.AddTannoyFilters` is what makes this a
tannoy: high-pass 340, low-pass 3600, distortion 0.17, a 105 ms slap at 0.33 wet, and a 2.1 s tail. A
raw clip auditioned outside the game sounds dry because it *is* dry. Judge a voice with the chain on.

---

## The fall is three new clips and the only sources that ignore `SfxLevel` (2026-09-03)

The drop at the end of the ride was silent apart from one impact at the bottom, and that impact
arrived at the same level as a door opening. Asked for (2026-09-03): a loud landing, and a sound for
the falling itself.

**Three clips, generated rather than synthesised** — `Tools/generate_sfx_fall.py`, ElevenLabs, the
same exception `generate_sfx_ai.py` already argues for the creaks and for the same reason: this
project's toolkit builds everything it can from oscillators and filters, and a two-hundred-metre fall
is not one of the things it can build.

- `sfx_cable_car_fall` — 8.75s of wind rush and straining metal, **looped**, faded up over the first
  half second of the drop. It is asked for as a flat bed with no shape of its own, because the shape
  comes from the car: the script rides it, and a clip that swelled on its own schedule would fight
  that. It is the one clip in the project that has to **join itself**, so a quarter-second
  equal-power crossfade of its tail onto its head is baked in and the tail discarded — a loop point
  that clicks is, over a silent fall, the only thing anyone would hear.
- `sfx_cable_car_hit_1` / `_2` — the structures the cabin clips on the way down, alternating with the
  strike count rather than picked at random. They are seconds apart, and at that spacing a repeat is
  heard as a repeat rather than as a second impact.

**And two sources that are exempt from `SfxLevel`.** That constant (`MakeSource`'s `pa` flag is what
turns it off) exists to hold the effects under the announcer, and it is right everywhere else. It is
the wrong instrument here: the landing is the last thing this game says, the player is inside a steel
box being dropped, and nothing is competing with it. `FallAudio` and `ImpactAudio` are declared with
`pa: true` and are the only effect sources in the project that are.

They are also **2D** (`spatialBlend` 0). The listener is inside the object making the noise, so a
spatialised source would be one at zero distance — 2D with a rolloff curve doing nothing. Saying so
directly is honest and is one fewer thing to be wrong.

**Loudness is set in one place.** The alternative was to normalise the clips hotter, which would have
been silent, unfindable, and impossible to undo without regenerating them. The level lives in
`SceneBuilder`, where every other tuned number does.

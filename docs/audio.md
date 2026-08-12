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

- `Tools/generate_narration.ps1` drives the Windows synthesizer (`Microsoft Zira Desktop`) to write **45 lines** into `Assets/Audio/Voice/` as 22 kHz 16-bit mono WAV. To replace them with better-acted takes, keep the filenames — no C# refers to how they were made. `SceneBuilder.NarrationIterationLines` must match the range the script writes (30, then a generic line stands in).
  - **Getting a rising ending out of SAPI requires a question mark.** The iteration lines are written `Iteration <prosody pitch="+35%">N</prosody>? 60 seconds remaining.` — the terminal contour is chosen from sentence punctuation and overrides everything else. Measured on Zira at rate -2: `"1!"` 220→160 Hz (falls; the exclamation does nothing), `prosody contour="…"` 202→138 Hz (SAPI ignores the attribute), `"1?"` 182→232 Hz, `pitch +35%` **plus** `?` 179→259 Hz. The `?` is never spoken, and because it closes the sentence on the number the rise sits there.
  - **Rate is -2 for sentences, -1 for the countdown digits.** The default clip read as a screen reader rather than a PA. The digits cannot go slower than -1 — each must finish inside its one-second slot (verified: every digit's spoken part ends by 0.78s).
- `Tools/generate_sfx.py` writes twelve clips with the Python standard library — no numpy, no downloads, each seeded so re-running reproduces the identical set. See `Assets/Audio/SFX/README.md` for the filename table.
  - **`sfx_balloon_pop` is under a fifth of a second on purpose** — seventy can be in flight at once and anything with a tail turns a roomful of balloons into a wash. Each balloon carries a **fixed detune** from the field's seed: one clip across seventy reads as a machine gun, and a balloon keeping the same voice every iteration is one more thing about the room that stays put.
  - **The room-tone loop is the one with a real constraint.** It plays on `loop`, so a seam is a click every 8 seconds for the whole session. Every partial is a multiple of 1/8 Hz so the tone wraps exactly, and the noise layer is wrapped by crossfading its own tail over its head (`seamless()`). Verified numerically: the step across the wrap is smaller than the largest step inside the clip.
  - `sfx_pull_in` runs two motions against each other — a noise band climbing while a tone falls away underneath. Up and down at once reads as being pulled *through* something; either alone is just a riser or a drop. It ends on a hard cut with the sub a beat late.
  - **There is no tool-swing sound.** `BalloonTool.swingClip` is deliberately null rather than borrowing the sheet rustle; swinging at air gives no audible feedback, which is a known gap.
  - `LoadClip` is extension-agnostic and **a missing file resolves to `null` with every player guarding on it**, so an emptied SFX folder is still playable. Overwriting a generated clip with a recorded take under the same name is the entire swap.
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

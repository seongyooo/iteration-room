# SFX drop folder

`SceneBuilder.BuildAudio` resolves every sound effect from this folder **by filename**, so there is
no manual wiring: drop a file in with one of the names below, rebuild the scene
(`Iteration Room > Build Whitebox Scene`), and that cue becomes audible.

Any of `.wav` `.ogg` `.mp3` `.aif` `.aiff` works — the extension isn't part of the name it looks for.

A missing file resolves to `null` and every player guards on that, so an empty folder is a valid
state: the scene runs silent on the SFX channel while the narration still plays.

| Filename | Cue | Fired by |
|---|---|---|
| `sfx_footstep_1` … `sfx_footstep_4` | Footsteps. Multiple variants get picked at random with pitch jitter, never the same one twice running. Fewer than 4 is fine. | `FootstepPlayer` |
| `sfx_door_open` | The pocket door sliding open — the film's *(door swishes)*. | `Door.Open` |
| `sfx_gasp` | Waking up, on the eyes starting to open. | `WakeUpSequence` |
| `sfx_sheet_rustle` | Bedding, as the body sits up. | `WakeUpSequence` |
| `sfx_ominous_loop` | The room tone bed. Loops continuously — **make sure it's seamless.** | `RoomAmbience` |
| `sfx_reset_sting` | Music sting over the loop boundary — *(suspenseful music)*. | `RoomAmbience.PlayResetSting` |
| `sfx_machines_rev` | *(machines rev)* at the reset. | `RoomAmbience.PlayResetSting` |
| `sfx_glass_rattle` | *(glass/bottle rattles)*. Positional, plays from the nightstand props. | `RoomAmbience.PlayResetSting` |
| `sfx_chime` | *(bell dings)* / *(machine beeps)* ahead of an announcement. Plays before the iteration line and the "10 seconds remaining" line, but **not** before each countdown digit. | `NarrationDirector` |

## Narration is generated, not dropped here

The announcer's voice lines live in `../Voice/` and are produced by `Tools/generate_narration.ps1`
(Windows built-in TTS). Re-run that script to regenerate them; don't hand-edit the folder. To
replace them with better-acted takes, keep the same filenames — nothing in the C# cares how they
were made.

## Sourcing

CC0 sources that suit this room: [freesound.org](https://freesound.org) (filter by CC0),
Kenney's audio packs, and [Sonniss GDC bundles](https://sonniss.com/gameaudiogdc). Claude can't
download these — drop them in here yourself.

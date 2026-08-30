# Shooting the trailer

What to film, how to reach each shot, and what it costs to set up. The rig that makes it possible is
`CaptureRig` (`Assets/Scripts/Capture/CaptureRig.cs`); the encoder is already configured by
`Assets/Editor/RecorderSetup.cs`.

**Status: the rig is verified in code only.** It compiles and the scene builds with it wired. Nobody
has pressed F9 or F10 yet. Everything below about *how a shot will look* is a plan, not an
observation.

---

## 1. Before the first take

1. **Iteration Room / Set Up Recorder** — once per machine. Configures 1920×1080, 60 fps, H.264 with
   audio, manual start/stop, writing to `Recordings/`. The setting that matters is
   `FrameRatePlayback.Constant`: Recorder drives `Time.captureFramerate`, so the game advances a
   fixed 1/60s per rendered frame however long that frame took. **This is why a screen recorder is
   the wrong tool here** — the moment this game is most worth showing (nine ghosts, each with an
   animator and the afterimage shader) is the moment it runs slowest.
2. **Iteration Room / Build Whitebox Scene** — with the Editor open, drive it through MCP, not
   batchmode. Check the console for `error CS` before recording anything.
3. Open **Window ▸ General ▸ Recorder**. Leave it open; START and STOP are the only two controls used.
4. Press Play in `MainMenu`, not in `IterationRoom` — the title screen is where CYCLE SELECT lives.

## 2. The rig's controls

| Key | Does |
| --- | --- |
| **K** | Toggles the HUD: the clock, the carried slot, the E prompts, the end-cycle control and the touch layer. Straight on/off. |
| **J** | Toggles the **ITERATION / CYCLE** card on its own. Independent of the HUD, because the card is also a trailer asset. |
| **L** | Detaches the camera. The player freezes where they stand and the camera flies free from their eye position. Press again to hand it back. |
| **Hold O** (while detached) | Auto pull-back dolly: glides the camera backward and slightly up along wherever it was aimed the moment O went down, easing in on press and easing out on release. Mouse look and WASD are locked out for the whole glide — aim first, then hold O, don't steer mid-shot. Speed comes from the same scroll-wheel value flight uses, so tune it before pressing O. |

`F9` and `F10` still work as aliases for **K** and **L** if your keyboard has Fn-lock on.

**Why plain letters and not F-keys or chords** — both were tried and both failed before Unity saw the key. F9–F12 are media keys on most laptops and need **Fn** held, so the press goes to the system volume instead (the symptom is a volume overlay and a rig that looks dead). Alt+letter is a Windows menu accelerator and gets swallowed by the Editor's menu bar. A bare letter has nothing upstream to claim it.

**The console says whether the rig is alive.** On startup it logs `[CaptureRig] armed. K: HUD, L: camera, J: iteration card …`. If that line is missing the rig is not running; if it is there and a key does nothing, the key is being eaten before it arrives.

If you ever lose the HUD and cannot get it back, **stopping Play restores it** — the hiding is a runtime change to the scene, and the Editor reverts those on exit.
| WASD / Jump / Crouch / Sprint | Fly, while detached. These follow **your own bindings**, not hardcoded WASD. |
| Scroll wheel | Fly speed, ×1.15 per notch, clamped to 0.15–12 m/s. Starts at 1.6 — about two thirds of walking pace, because a camera moving at gameplay speed reads as a person rather than as a camera. |

Three things worth knowing before relying on it:

- **Detaching does not pause anything.** The clock runs, ghosts keep replaying, the recorder keeps
  writing. That is the point — you are filming past selves, and they have to still be moving.
- **Escape still pauses**, deliberately (it is the one verb that must never be lost). Don't press it
  mid-take: the pause overlay is not hidden by `CLEAN`, on purpose, because a game whose pause menu
  is invisible looks broken. If you *want* a frozen tableau, pause first and then fly — flight uses
  unscaled time and keeps working at `timeScale` 0.
- **You cannot click the end-cycle control while the HUD is hidden.** It is hidden by disabling its
  graphics, and a disabled graphic is not a raycast target. Hold the bound key (**N** by default)
  instead — that path is untouched.
- **Mirrors are wrong from a detached camera.** `MirrorReflection` renders for `PlayerLookup.Eye`,
  which is still the player's head. Every shot below avoids mirrors; cycle 3 is the mirror cycle and
  is not in this list anyway.

## 3. The order of the cut

The whole trailer is one argument, made in five beats. Nothing else needs to be in it.

1. **The problem** — you wake, the clock runs out, you have done nothing.
2. **The rule** — you wake again, and somebody who is you is already working.
3. **The scale** — six of you, at once.
4. **The payoff** — a room answering.
5. **The exit** — the console, the ERROR, the floor opening.

## 4. The shots

Cost is in *iterations you must play before the shot exists*, which is the real currency here: a
ghost is a recording, so a shot with five past selves in it is five laps of setup.

### A — The empty minute (beat 1)

| | |
| --- | --- |
| Where | Room1, from the bed |
| Route | CYCLE SELECT ▸ CYCLE 1 |
| HUD | **FULL** — the clock is the subject of this shot |
| Camera | First person |
| Cost | 0 iterations |

Wake, look around, walk as far as Room2, clock expires, pulled back to the bed. The whole shot is
that the minute is not enough. Let the reset land on camera — do not cut before the teleport.

### B — Somebody is already here (beat 2)

| | |
| --- | --- |
| Where | Room1 → Room2 |
| HUD | **FULL** |
| Camera | First person |
| Cost | 1 iteration (perform the take of a pin, then re-wake) |

Iteration 2, from the bed. The shot is the moment a past self walks past you carrying something you
watched yourself pick up. Frame it so the ghost enters from the side rather than being found — the
afterimage reads better in motion across the frame than head-on.

### C — Six of you, swinging (beat 3) — **the money shot**

| | |
| --- | --- |
| Where | Cycle 2, the tree hall |
| Route | CYCLE SELECT ▸ CYCLE 2, then walk room2-1 → room2-2 → tree hall |
| HUD | **BARE** |
| Camera | Both. First person for one pass, then detached for the wide |
| Cost | ~5 iterations, each spent taking an axe to the trunk |

This is the single frame that explains the game with no text, and it is the reason the rig has a
detached camera at all. Five past selves and the living player swinging at one trunk is the whole
premise as an image.

Shoot it twice. **First person** is the honest one — you are one of six. **Detached**, backed off and
slightly high, is the one that goes on the Steam page: fly out along the deck so the count of bodies
resolves as the camera pulls back. This is what the pull-back dolly (hold **O**) is for — fly into
position and aim by hand, then let go of the mouse and hold O so the actual pull-back is one smooth,
untouched glide instead of a hand-flown one.

Two known unknowns, both flagged in `TODO.md`: ghost crowding here has never been profiled (five
skinned, afterimaged bodies is the payoff and the load at once), and whether five ghosts swinging
actually *reads* as a work gang has never been looked at. **Watch the first take for both.** If it
reads as five people idling near a tree, the shot is not there yet and no camera move fixes it.

The chop and fall sounds are still placeholders — the item-pickup and door-open clips. Either cut the
audio for this shot or accept it.

### D — A room answering (beat 4)

| | |
| --- | --- |
| Where | Room2West, the chess room |
| Route | CYCLE SELECT ▸ CYCLE 1, red key → Room2West |
| HUD | **BARE** |
| Camera | First person, standing still |
| Cost | 11 iterations (twelve pieces, one per trip; the twelfth is the shot) |

The most expensive shot in the list and worth it: the room is **dark** until the last piece lands,
and then the lights come up, the board splits down the middle and a plinth rises out of the gap
carrying a red cube. It is the only place in the game where the reward is a change to the room rather
than an object appearing.

Do not move the camera. Stand where you can see the split and let it happen.

### E — A door somebody else is holding (beat 3, alternate)

| | |
| --- | --- |
| Where | Room3 |
| HUD | **CLEAN** — keep the E prompt |
| Camera | First person |
| Cost | 2 iterations |

The yellow triangle is only reachable while two past selves are standing on the pads. It is the one
moment in the game where **a prompt appears because somebody else is holding a condition open**, and
that is worth ten seconds of a trailer on its own. This is the shot that keeps `CLEAN` rather than
`BARE`: the disc appearing is the event.

### F — The console, the ERROR, the floor (beat 5)

| | |
| --- | --- |
| Where | Room4 (cycle 1) or room2-0 (cycle 2) |
| HUD | **BARE** |
| Camera | First person into the console, then detached for the break |
| Cost | A full cycle — see the note below |

Three objects into three shaped recesses, the panels glitching to ERROR, and the floor opening. Cycle
2's version is four billiard balls into four pedestals and is the better-looking of the two.

**There is currently no shortcut to this shot.** `DebugStart.AtCycleBoundary` does exactly what is
wanted — stands you in the last room with the three escape objects on the floor in front of you, with
the completion, the break and the hatch all on the real code path — but nothing sets it: the title
screen's TEST button was removed on request 2026-08-15 and was its only caller. So this shot costs a
played cycle unless a way to set that static is put back. See §6.

### G — Not in this trailer

- **Cycle 3, all of it.** The beam, the five mirrors and the receiver are verified in code and
  **nobody has seen them**. Planar reflection has at least three ways to look wrong that a clean
  build cannot rule out. Four of its rooms have no puzzle in them.
- **Room3-1's crushing corridor**, for now. A death that then replays every iteration afterwards is a
  genuinely striking image and belongs in a later trailer — but it is in cycle 3.
- **Room2-6's pool.** 210 floating balls and three rubber ducks is charming and says nothing about
  what the game is. Save it for a screenshot.

## 5. Stills for the Steam page

Five is the minimum and they should not all be from the same beat. Shoot at `BARE`, detached where
noted:

1. The tree hall, detached, wide — the count of bodies is the picture.
2. Room2West at the moment the lights come up, first person.
3. The white corridor with a past self at the far end, first person. This is the aesthetic shot: the
   black ceiling grid and the soft vanishing point already read as a deliberate facility rather than
   as a whitebox, and that impression is worth protecting.
4. Room2's balloon field, first person, several ghosts popping at once.
5. The console mid-ERROR, detached and low.

Capsule art is a separate job and is not a screenshot.

## 6. Open, before or during the shoot

- **The `AtCycleBoundary` shortcut has no caller.** Shot F needs it. Reinstating it means either
  putting the TEST button back (removed on request, so not something to do unasked) or hanging it off
  a modifier-click in the existing cycle picker. **Decide before shooting F**, or budget a played
  cycle for it.
- **The rig is unplayed.** F9 and F10 have never been pressed. Expected to be wrong on first contact:
  the fly sensitivity (0.6 is a guess), and whether the detached camera's culling mask really does
  show the whole body rather than the headless one.
- ~~**Room2-6's south door is blocked**~~ **IT NEVER WAS** (2026-08-26). `AssertWalkable` swept with
  `~0` and so counted the balloon layer, which `cc.excludeLayers` removes from the player outright —
  the `Solid` it named was one of the pool's 210 floating balls drifting into the doorway, a thing
  nobody can be stopped by. Both asserts now sweep `PlayerBlockingMask()`. Room2-6 is also downstream
  of the tree hall, not on the walk to it, so shot C was never affected either way.
- **CC-BY attribution is still missing** (`TODO.md` §7). A trailer is the point at which this stops
  being an internal problem: ten uncredited creators, in a video being pushed at an audience.

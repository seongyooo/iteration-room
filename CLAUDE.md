# Iteration Room

Unity prototype of a first-person time-loop puzzle game (ref: 2016 short film "Iteration 1").

**This file holds only what you must know before changing code.** Reasons, alternatives and past
bugs live in `docs/`. What changed when lives in `git log`. Outstanding work lives in `TODO.md`.
How to keep that split working is §6.

## Current state

Complete end to end — three puzzle rooms, a fourth room that is the ending, a title screen, a
sensitivity calibration step. The core loop, Rooms 1–2, Tab-to-switch and the ghost drop are
play-tested.

**The full run has never been played through by a human.** Room3's pads, Room4 and its plinth, the
wall messages, ghost possession and the insert-and-turn are built and verified in code only.
Shipped to itch.io once; several scene-affecting passes have landed since.

---

## 1. Invariants — breaking these breaks the project

### 1.1 Scenes are build output

`Assets/Scenes/*.unity` is git-ignored and regenerated; Unity re-randomises every fileID on rebuild.
**Never edit a scene by hand.** Change `Assets/Editor/SceneBuilder.cs` instead. A fresh clone has no
scenes until you build.

The scene `.meta` files **are** tracked — they pin the GUIDs `EditorBuildSettings.asset` stores by
value. Do not delete them. Git LFS is configured for `*.glb`; respect it.

### 1.2 One object: never duplicated, never lost

Every `CarryableItem` is in **exactly one** of these states at all times:

1. at origin · 2. held by the player · 3. held by exactly one ghost · 4. seated in a socket ·
5. loose in the world

A ghost's hold is **custody, not ownership**. Every exit from state 3 must land somewhere:
surrender → socket, timeline ends → dropped at last position, player takes it → player, loop reset →
origin, **ghost destroyed → released** (`GhostReplayer.OnDestroy`).

`ItemRegistry.ReturnAllToOrigin()` sweeps every registered item at the top of an iteration.
`PlayerHand.ReturnAll` alone is **not** sufficient — it only knows what the *player* picked up.

### 1.3 Record the attempt, re-evaluate the condition

A ghost never blindly replays. The condition re-evaluated must be **the one that actually enabled
the action** — not a weaker fact that correlates with it. Currently re-evaluated: item availability,
whether the ghost still holds an item, whether it is *equipped*, and `requiresOpenDrawer`.

### 1.4 Identity, never position

Pops carry a `balloonId`; carries carry an `itemId`. Replaying "the player acted *here*" against a
world that has moved makes a ghost's contribution luck.

### 1.5 Signals are levels; events are instants

`RecordedFrame.signals` is continuous state. `PopEvent` / `CarryEvent` are moments with an identity.
**Never convert an instant into a signal bit.** A ghost can skip several recorded frames in one tick,
so press-type interactables stretch their pulse and act on the rising edge.

### 1.6 `ghostInteractables` — append, never reorder

An interactable's index **is** its bit in `RecordedFrame.signals`. `PlayerRecorder.interactables` and
`GhostReplayer` must be the same array in the same order; `SceneBuilder` builds one and hands it to
both. `signals` is a `uint`, so **32 interactables is a hard cap** (4 used).

### 1.7 Ghosts have no colliders

A ghost is something you walk *through*. One that pushed the player would change the recording being
made against it.

### 1.8 `AcceptsInput` gates every `Update` that reads a key

`LoopManager.AcceptsInput` = `IterationRunning && !IsPaused && !RunOver`. A zero `timeScale` does not
stop `Update`, and `HandleLook` uses no `deltaTime` at all. **Any new interactable reading input must
use this gate.** The deliberate exceptions are the two `PressPlate`s, which are the only fixtures
OUTSIDE the loop: `CalibrationStartButton` runs before the first iteration and gates on
`SensitivityCalibration.Active`, `FinalRoomButton` runs after the last and gates on
`FinalRoomSequence.ButtonLive`. `ControlHintDisplay` has to be told about both, or their prompts
never appear.

### 1.9 Reset ordering

At the top of an iteration, in this order — each step must run before the thing that hides its
output: `ghost.ReleaseCarried()` → `PlayerHand.ReturnAll()` → `ItemRegistry.ReturnAllToOrigin()` →
drawers close → `BalloonField.ResetField()` → `ghost.ResetPlayback()`. Doors close after the
teleport, never before.

---

## 2. Architecture and responsibility boundaries

Do not merge these roles. Before adding a system, check whether one of them already owns the job.

| System | Owns |
| --- | --- |
| `SceneBuilder` (Editor) | Every object in both scenes, and every tuned constant |
| `LoopManager` | The iteration coroutine, the clock, reset ordering |
| `PlayerRecorder` / `RecordedTimeline` | What gets written into a timeline |
| `GhostReplayer` | Replaying one timeline, and re-evaluating it |
| `PlayerHand` | What the player carries and what is **in hand** |
| `CarryableItem` | One object's own state and where it is parented |
| `ItemRegistry` | id → object, id → socket, and the reset sweep |
| `IItemSocket` | Anything that accepts an item and keeps it |
| Room components | One puzzle's rule, nothing else |
| `FinalRoomSequence` | Room4: the plinth, the last press, and the break before the ending card |

**Values live in `SceneBuilder`, mechanisms live in components.** Shaders and scripts take the
number; they do not choose it.

Script-by-script detail: `docs/architecture.md`.

## 3. Implementation constraints

- **URP only.** Materials must use `Universal Render Pipeline/Lit`, never `Standard` — a `Standard`
  material renders magenta. Transparency needs the blend modes **and** the
  `_SURFACE_TYPE_TRANSPARENT` keyword; setting alpha alone does nothing.
- **This project cannot currently light a pure metal.** Anything imported at `metallicFactor` 1 comes
  in black. Drop it (~0.3) or it has no diffuse term at all.
- **Do not reach for a reflection probe as a preview of anything** — a probe of a white room is a
  blank white field.
- **UI: read `m_AnchoredPosition`, not `m_LocalPosition`** — the latter is stale for a
  `RectTransform`. Adding a `Canvas` or any UI component *replaces* `Transform` with `RectTransform`,
  discarding a position written before that call.
- **Check fixed-width HUD labels against `0.6 × fontSize × length`** — silent rewrapping has bitten
  twice.
- **`-nographics` cannot render.** Anything that renders must check
  `SystemInfo.graphicsDeviceType` or the headless build breaks.
- **Adding a renderer feature is not idempotent** — reconcile, don't append.

More: `docs/gotchas.md`, `docs/rendering-notes.md`.

## 4. Core game rules

One unbroken 60-second loop, one bed, ghosts accumulating forever. **Each iteration should leave the
player less to do.** Do not damage that when adding rooms.

- **Ghosts can carry things and use them**, including putting Room2's key in the lock — so a solved
  room stays solved without the loop keeping un-rewound state. Full argument:
  `docs/ghost-possession-design.md`.
- **The completed-errand rule**: a ghost replays a pickup only if that same recording also
  surrendered the item — and this applies **only to items that have a socket**. Unscoped it silently
  forbids ghosts to carry anything that is never delivered.
- **Tab cycles what is in the hand**; everything else carried is stowed and invisible, and the cycle
  ends on empty hands. Fixtures operated *with* an item gate on `hand.Holding(id)`, **not**
  `hand.Has(id)`. Equips are recorded, so a ghost swaps when the player did.
- **A ghost shows what it is holding** — otherwise "who has the key" and "why did the door stop
  opening" are unanswerable.
- **A TOOL-SHAPED ACTION REQUIRES THE TOOL, for ghosts as for the player.** An interaction performed
  *with* an object is gated on that object being **in the hand**; take the object away and the
  interaction stops. `KeyLock` does this for the key, `GhostReplayer.requirePopTool` for balloon
  pops. Check possession through `HoldingEquipped()`, never a cached id — an item can be taken out
  of a ghost's hands without asking.
  - **Known cost, accepted:** one pin means at most one entity can pop at a time, so Room2's
    accumulation does not survive. The mitigation is a *supply* of pins, not a change to the rule —
    `docs/decisions.md`, queued in `TODO.md`. Revert with `requirePopTool = false`.

- **`CarryKind` is `Take | Surrender | Equip`.** Equip is the Tab press — an empty `itemId` means
  empty hands.

Room-by-room reasoning: `docs/puzzle-design.md`.

## 5. Build and test

**Unity 6000.5.7f1, URP 17.5.0.** Pipeline assets in `Assets/Settings/` (`IterationURP` +
`IterationRenderer` + `IterationVolume`), wired into both `GraphicsSettings.defaultRenderPipeline`
and `QualitySettings.renderPipeline`. Do not change the Unity version.

`MainMenu` is build index 0 (where a standalone player opens); `IterationRoom` is index 1. Pressing
Play runs whichever scene is open. Prefer MCP for incremental visual tweaks, but **keep
`SceneBuilder.cs` authoritative for anything structural.**

Rebuild headlessly (only when the Editor does **not** have the project open):

```
"C:\Program Files\Unity\Hub\Editor\6000.5.7f1\Editor\Unity.exe" -batchmode -nographics \
  -projectPath "<repo>" -executeMethod IterationRoom.EditorTools.SceneBuilder.Build -quit -logFile <log>
```

**With the Editor open, a batchmode build fails outright.** Drive it through Unity MCP instead:

1. `read_console(clear)` — stale entries look exactly like fresh failures
2. `refresh_unity(compile)`, then **read the console before building** — otherwise the build runs on
   the previous assembly and a compile error passes unnoticed
3. `manage_editor(stop)` if in play mode — `NewScene` throws during play
4. `execute_menu_item("Iteration Room/Build Whitebox Scene")`
5. `read_console` — `[SceneBuilder] IterationRoom scene built at ...` and no `error CS`

MCP servers load only at session start; if the tools are missing, start a new session.
**Player Settings → Run In Background must stay ON** (`Build()` sets it).

After a change, confirm: no compile errors · no console exceptions · SceneBuilder still builds ·
the invariants in §1 still hold · state rewinds at the loop boundary · replay is deterministic.

**Code verification is not play-testing.** A passing runtime sweep says the code does what it was
told. Whether a wait is interesting, a prompt is findable or feedback is enough is only answerable by
playing it — say which of the two you did.

## 6. Working rules

### Before changing anything

1. Does this break an invariant in §1?
2. Does an existing system already own this job (§2)? Reuse before building.
3. Is there a decision recorded in `docs/` about it?
4. Scene change → is `SceneBuilder` the source of truth for it?
5. Ghost replay → identity-based, and both ends re-evaluated?
6. Item change → still exactly one object, never duplicated, never lost?
7. After implementing: **trace every path by which the state can escape.** Most of the real bugs
   found here were leaks, not logic — an object left in a socket, a flag left set, an item destroyed
   with its holder.

### Where a new fact belongs

| Kind of thing | Goes in |
| --- | --- |
| A short rule Claude must follow to avoid breaking something | **CLAUDE.md** |
| Why a decision was made, what was rejected, which bug it came from, why a number is that number | **`docs/`** |
| Work not yet done | **`TODO.md`** |
| What changed when | **`git log`** — never restated in prose |

**Do not grow CLAUDE.md into a history.** It holds rules, not the story of how they were found.
Equally, **do not delete a rule just to make it shorter** — move the reasoning to `docs/` and leave
the rule. Completed work is deleted from `TODO.md`, not annotated as done.

### When sources disagree

Trust in this order: **actual code behaviour → stated invariants → `docs/` → older prose.** But if
the code violates a stated invariant, that is a bug to scope, not a licence to assume the code is
right.

### Reporting

Say plainly which of *verified in code* and *played by a human* you did. They are not the same claim,
and only the second answers whether something is understandable or enjoyable.

## 7. Where things are

| Document | Contents |
| --- | --- |
| `TODO.md` | Outstanding work |
| `docs/architecture.md` | Script-by-script index |
| `docs/build-and-stack.md` | Engine/pipeline detail, both rebuild paths |
| `docs/ghost-possession-design.md` | Ghost custody: the full argument |
| `docs/puzzle-design.md` | Room-by-room design, and the three puzzle shapes |
| `docs/ghosts.md` | Ghost appearance, the afterimage shader, animation scrubbing |
| `docs/loop-and-ui.md` | Loop, wake-up, ending, sensitivity, pause, menu, HUD |
| `docs/room-geometry.md` | Shells, the wall grid, doors |
| `docs/rendering-notes.md` | Lighting, materials, probes |
| `docs/audio.md` | PA schedule, filter chain, clip generation |
| `docs/gotchas.md` | Costly one-time discoveries |
| `docs/decisions.md` | Rejected ideas and open questions |
| `iteration-game-spec.md` | Original spec |

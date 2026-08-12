# Iteration Room

Unity prototype of a first-person time-loop puzzle game (ref: 2016 short film "Iteration 1").

**This file holds only what you must know before changing code.** Reasons, alternatives and past
bugs live in `docs/`. What changed when lives in `git log`. Outstanding work lives in `TODO.md`.
How to keep that split working is §6.

## Current state

Complete end to end — three puzzle rooms, a fourth room that is the ending, a title screen, a
sensitivity calibration step. Behind Room2's red door is **Room2West, the chess board**: twelve of a
set's thirty-two pieces scattered across the floor, put back one at a time. The room is **dark**
until the last one lands; then its lights come up, the board splits down the middle and a plinth
rises out of the gap carrying a red cube. Behind the blue door is **Room2East, the cube room**: six
cubes carrying six symbols and six recesses in the walls that want them, paying out a blue sphere.
**Room3** pays out a yellow triangle on the same condition that opens its door. All three are
`CarryableItem`s and Room4's console has a shaped recess for each — see `docs/puzzle-design.md`.

**The three escape objects are valid within ONE iteration only, and putting all three into Room4's
console is the game's ONLY EXIT CONDITION.** Nothing exempts them from
`ItemRegistry.ReturnAllToOrigin`: the last run is a lap collecting all three from rooms the ghosts
have already finished and carrying them to the console. **The clock runs through Room4** — reaching
it ends nothing, and a run that gets there with two objects is pulled back to the bed like any
other. There is no button anywhere in the game that ends it.

**PLAYED THROUGH TO THE THREE-OBJECT ESCAPE, 2026-08-12: cleared on ITERATION 15, with 8 SECONDS
left on the final lap's clock.** The whole chain works in a human's hands — both accumulation rooms,
three collections and the walk to the console, inside one sixty-second iteration. What this measures:

- **Fifteen iterations, not four.** The earlier four-iteration figure was the game whose exit was
  crossing Room3's threshold; the two side rooms were optional then and are on the critical path now.
  Quote 15 as the run length, and expect it to come DOWN with practice — the player who measured it
  judged there was time to be found in tidier play, not that 15 was a floor.
- **The final lap fits, and 8 seconds is the whole margin.** Three pickups plus the walk to Room4 is
  the tightest thing in the game. It is also the one number that moves with *when past selves
  finished their rooms*, so treat 8s as an observation of one run, not a guarantee. Anything that
  lengthens that lap — a room past Room4, a fourth escape object, a slower walk — spends this margin
  first.
- **60 seconds is still not the binding constraint anywhere else.** Measured at `walkSpeed` 2.5 with
  sprint unused. A new room's cost is paid in ITERATIONS, never in metres — the exception being the
  final lap above.
- **Fifteen minutes is the whole game**, up from four. That is a direct answer to the itch.io "small"
  feedback, and it came from the two side rooms rather than from any new mechanic.

Room2West and Room2East have now both been played end to end (a clear requires them). What one
clear does **not** answer is whether twelve pieces is the right length or whether the lit square
teaches the left button. What play CONTRADICTED is recorded in `docs/puzzle-design.md` — Room2's
popping is not accumulative, and the three pins currently have no consumer.

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

**One press takes ONE item.** Every `CarryableItem` polls E for itself, so overlapping triggers used
to be taken by all of them at once — 32 chess pieces a square apart found it. Anything that answers a
key press over a takeable must defer to `ItemRegistry.NearestTakeable(eye)`: **nearest to the camera**,
which is also what the prompt disc is drawn over, so the press and the disc can never disagree.

**This is per OBJECT, not per id.** An id can name a supply — three pins share `"Tool"` — and each of
the three obeys the five states and returns to its own origin. What is forbidden is two *holders* of
one object, never two objects of one id. A ghost asks for a free instance
(`ItemRegistry.FindFreeForGhost`), and the player is limited to one per id by `PlayerHand`, whose
`carried` set is keyed by id — so anything gating on "E would pick this up" must check that too, or it
prompts for a press that cannot succeed.

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
`SensitivityCalibration.Active`. **It is the only one left**: Room4's plate is gone and the clock now
runs through that room, so its three recesses gate on `AcceptsInput` like every other fixture.
`ControlHintDisplay` still has to be told about the calibration plate, or its prompt never appears.

### 1.9 Reset ordering

At the top of an iteration, in this order — each step must run before the thing that hides its
output: `ghost.ReleaseCarried()` → `PlayerHand.ReturnAll()` → `ItemRegistry.ReturnAllToOrigin()` →
drawers close → `BalloonField.ResetField()` → `ChessBoard.ResetBoard()` → `ghost.ResetPlayback()`.
Doors close after the teleport, never before. `ResetBoard` must follow the sweep, not precede it: the
sweep is what puts the pieces back, and this is what forgets who was home.

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
| `ItemRegistry` | id → the **supply** wearing it, id → socket, and the reset sweep |
| `IItemSocket` | Anything that accepts an item and keeps it |
| Room components | One puzzle's rule, nothing else |
| `ChessBoard` | Room2West: the grid, which piece belongs where, and one `IItemSocket` per piece |
| `ChessPlacer` | The left button and the aim; every question about *where* is `ChessBoard`'s |
| `ChessReward` | Room2West's payoff: the lights, the board opening, the plinth. Not the rule |
| `CubeRoom` | Room2East: which cube belongs in which recess, and whether they are all home |
| `SymbolSlot` | One recess's range, E press and light; every *which* question is `CubeRoom`'s |
| `RewardPlinth` | A plinth that rises carrying an escape object, on its room's own condition |
| `FinalRoomSequence` | Room4: the console, the three-object exit condition, and the break |

**Values live in `SceneBuilder`, mechanisms live in components.** Shaders and scripts take the
number; they do not choose it.

Script-by-script detail: `docs/architecture.md`.

## 3. Implementation constraints

- **URP only.** Materials must use `Universal Render Pipeline/Lit`, never `Standard` — a `Standard`
  material renders magenta. Transparency needs the blend modes **and** the
  `_SURFACE_TYPE_TRANSPARENT` keyword; setting alpha alone does nothing.
- **~~This project cannot currently light a pure metal.~~ IT CAN, NOW THAT THE PROBES ACTUALLY
  REFLECT THE ROOM.** Every reflection probe was capturing an empty world and losing even that on
  save — four independent faults, listed in `docs/gotchas.md`. A metal is *entirely* reflection, so
  that was the whole of it. The three escape objects are metallic 0.9. **If a metal ever renders
  black again, check the probes before touching the material** — `BakeReflectionProbes` logs
  `wired/total`, and anything but `n/n` is the cause.
- **Anything generated by script is NOT static, and a baked probe only sees static renderers.** A new
  room's geometry is invisible to the bake unless `MarkReflectionProbeStatic` reaches it — it walks
  the scene, so it will, but a new *mover* has to be added to `MovesDuringPlay` or it bakes into the
  reflection in a pose it does not hold.
- **Do not reach for a reflection probe as a preview of anything** — a probe of a white room is a
  blank white field. That is about reading a probe as an *image*; it is still the right thing to
  reflect.
- **A near-mirror suits a small object; the walls' 0.85 does not.** At wall roughness a featureless
  white room blurs into a wash and a small object reads as plastic. At 0.97 it resolves the ceiling
  fixtures as distinct shapes, and that structure is what the eye reads as metal.
- **On a metal, base colour is REFLECTANCE, not paint.** A display colour fed straight in gives a
  dark, muddy mirror; real coloured metals sit high (gold ~1.0/0.77/0.34). Lift it toward white.
- **UI: read `m_AnchoredPosition`, not `m_LocalPosition`** — the latter is stale for a
  `RectTransform`. Adding a `Canvas` or any UI component *replaces* `Transform` with `RectTransform`,
  discarding a position written before that call.
- **Check fixed-width HUD labels against `0.6 × fontSize × length`** — silent rewrapping has bitten
  twice.
- **`-nographics` cannot render.** Anything that renders must check
  `SystemInfo.graphicsDeviceType` or the headless build breaks.
- **A MIRRORED import breaks three things at once, and `chess.glb` is one** — half its pieces carry a
  local scale of `(-1,-1,-1)`. (a) Anything sized through `InverseTransformVector` comes back
  **negative** and builds an inside-out collider; take an absolute value. (b) Unity decomposes a
  mirrored basis as a *different rotation* (pitch +90 against 270) that cancels against the negative
  scale — so a pose written as a fixed euler stands half the objects on their heads. **Measure the
  rotation off each object** instead of writing the number. (c) A `BoxCollider` on a negatively
  scaled transform warns on every load. The fix for all three is the same one: **push the −1 onto a
  wrapper INSIDE the object** (`SceneBuilder.MakeChessPiece`). The composite is untouched, because
  the wrapper sits after the object's own rotation and scale either way — and the object's own
  transform, which is what colliders and hand poses are read off, is positively scaled again.
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
  - **The rule needs a SUPPLY, and it has one.** One pin capped the whole room at one popper at a
    time, which killed Room2's accumulation. The fix was never a softer rule: `ItemRegistry` resolves
    an id to a **free instance**, so several objects share one id and the drawer holds three pins —
    the player and two past selves popping at once. A ghost that finds none simply does not pop.
    **An id can be a supply now**, so anything asking "the object with this id" must ask for a free
    one instead. Raising the count is one number in `SceneBuilder`. Revert the gate itself with
    `requirePopTool = false`.

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

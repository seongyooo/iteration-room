# Iteration Room

Unity prototype of a first-person time-loop puzzle game (ref: 2016 short film "Iteration 1").

**This file holds only what you must know before changing code.** Reasons, alternatives and past
bugs live in `docs/`. What changed when lives in `git log`. Outstanding work lives in `TODO.md`.
How to keep that split working is §6.

## Current state

Complete end to end — three puzzle rooms, a fourth room that is the ending, a title screen, a
sensitivity calibration step. **The building is ONE CORRIDOR**: Room1 → Room2 → Room2West → Room2East
→ Room3 → Room4, six shells in a line, each sharing a divider with the next — there is no branching
hub. Room2 is the balloon room and holds all three coloured keys; red opens the door onto Room2West,
blue the door onto Room2East, yellow the door onto Room3, in that order. **Room2West, the chess
board**: twelve of a set's thirty-two pieces scattered across the floor, put back one at a time. The
room is **dark** until the last one lands; then its lights come up, the board splits down the middle
and a plinth rises out of the gap carrying a red cube. **Room2East, the cube room**: six cubes
carrying six symbols and six recesses in the walls that want them (three per wall on the two walls
without a doorway, now that the room has doorways on both its south and north walls), paying out a
blue sphere. **Room3** pays out a yellow triangle on the same condition that opens its door. All
three are `CarryableItem`s and Room4's console has a shaped recess for each — see
`docs/puzzle-design.md`.

**Rooms are called `room<cycle>-<n>` in design discussion** — `Room1` is `room1-1`, `Room4` is
`room1-0`, and a cycle always ends in its `-0`. **The code names above are unchanged and are what
you grep for**; the mapping is in `docs/cycle-design.md` §4a.

**LAYOUT CHANGED 2026-08-13, from a hub (three coloured doors off Room2's own walls) to the linear
corridor above** — play-tested as tedious, backtracking to Room2 between the two side rooms rather
than making progress. Verified in code only (SceneBuilder compiles, the scene builds with no errors,
probe/room counts match): the iteration-count and final-lap-timing figures below were measured
2026-08-12 against the OLD hub layout and have **not** been re-measured against this one. Walk
distances changed with the topology, so treat every number below as needing a fresh playthrough
before it is trusted again.

**The three escape objects are valid within ONE iteration only, and putting all three into Room4's
console is the game's ONLY EXIT CONDITION.** Nothing exempts them from
`ItemRegistry.ReturnAllToOrigin`: the last run is a lap collecting all three from rooms the ghosts
have already finished and carrying them to the console. **The clock runs through Room4** — reaching
it ends nothing, and a run that gets there with two objects is pulled back to the bed like any
other. There is no button anywhere in the game that ends it.

**PLAYED THROUGH TO THE THREE-OBJECT ESCAPE ON THE ONE-OBJECT DESIGN. Three clears, 2026-08-14, all
on one build: 14 iterations in 8:29, then 12 in 7:30, then ITERATION 10 in 4:47.** The build is the
game as it now is: Tab and the pocket gone, one object in the hand, E to put down, held objects at
true size. What they settle:

- **Nothing changed across the three runs but the player knowing what to do, and the figure fell every
  time — and it has NOT converged.** Reading twelve as "a skill figure approaching a floor" was
  premature; it took one more run to find ten. Quote the count as **about ten for someone who knows the
  game**, expect a first-time player to need considerably more, and do not read a movement of one or
  two iterations as content having changed.
- **The last run nearly halved the clock while taking only two iterations off.** Time is falling much
  faster than the count, so what a practised player is saving is not errands — it is the walking
  inside each one, and ending the iteration the moment its errand lands.
- **10 iterations in 4:47 averages 29 seconds apiece** — under half the 60-second budget, down from
  37. That is `EndCycleControl` used aggressively, which is the loop working as intended: an
  iteration is worth exactly as long as it takes to add one thing.
- **A practised clear is now under five minutes.** That is a statement about how much game there is,
  not about pacing, and it is the strongest argument yet for the extra puzzles the Steam plan wants.
- **Removing the pocket did not lengthen the run. It shortened it.** The prediction on the way in was
  that one-object-at-a-time would cost iterations, because three escape objects can no longer reach
  Room4 in one trip. It came out at 14, 12, 10 against the old design's 15 — and 4:47 against its
  ~15 minutes. Much of that is a practised player, but the direction is the opposite of the one that
  was feared, and the design argument for the change no longer has to be paid for in length.
- **The endgame shape works in a human's hands.** Past selves deliver the escape objects they
  delivered while the living player brings the last one. The known sharp edge — a ghost's recorded
  delivery is refused outright if this run's console has not risen yet, and never retried — did not
  stop any of the three clears; whether it was ever hit is unknown.
- **60 seconds is still not the binding constraint.** Measured at `walkSpeed` 2.5 with sprint unused.
  A new room's cost is paid in ITERATIONS, never in metres.

**Superseded**: the old figures were 15 iterations, ~15 minutes, and a final lap with 8 seconds of
margin — that lap was three collections in one trip and cannot happen now. `git log` has the rest.

What three clears still do **not** answer: whether twelve chess pieces is the right length, whether a
metre of glass held at true size is pleasant or merely tolerable, and whether any of it is enjoyable
rather than merely finishable.

Room2West and Room2East have both been played end to end — a clear requires them. What play
CONTRADICTED is recorded in `docs/puzzle-design.md`: Room2's popping is not accumulative, and the
three pins currently have no consumer.

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

**E ONLY ACTS ON WHAT IS ON SCREEN.** Every fixture answers E on *proximity* — a polled volume — which
says nothing about whether the player can see it, so a press used to take an item behind you. Every
`WantsInteractHint` therefore ends in `PlayerLookup.InView(HintAnchor)`, and stating it against the
**prompt** is the point: an interaction is available exactly when its prompt disc would be on screen,
so a player never presses E on something the game gave them no mark for. Put it **last** in the
condition — the aim scan asks `WantsInteractHint` of every registered item, and the cheap
`playerInRange` test in front of it is what keeps that free. Frustum only, no occlusion test; see
`PlayerLookup.InView` for why a line-of-sight raycast is the wrong trade here.

**THE PRESS PATH IS ONE CALL: `PlayerLookup.PressGoesTo(this)`.** It is the fixture's own
`WantsInteractHint` (in reach · on screen · not behind anything · in a state where E would act) AND
the arbitration below, and **every fixture that polls E must end in it**. Stated as two separate
things it was got wrong three times out of eight: `Drawer`, `KeyLock` and `CarryableItem` polled E off
`playerInRange` alone and never asked their own hint property, so a press opened a drawer through a
wall or with your back to it. **The press goes exactly where the disc is — if there is no mark on
screen, E does nothing here.**

**One press takes ONE item, and it takes TWO mechanisms to hold that.** Every `CarryableItem` polls E
for itself, so overlapping triggers used to be taken by all of them at once — 32 chess pieces a
square apart found it, two stacked cubes found the half of it that was left.

1. **Which** — **whatever the player is LOOKING AT** (2026-08-17, by request; it was *nearest to the
   camera* until then). `PlayerLookup.AimedAnchor` ranks every candidate by how far its **hint anchor
   sits from the centre of the screen**, and within `AimTieBand` — things genuinely stacked, where aim
   cannot separate them — the nearer wins. **Every E fixture's press path ends in
   `PlayerLookup.IsAimedAt(HintAnchor)`**, and the prompt disc is drawn from the same value, so the
   press and the disc can never disagree.
   - **This is ONE scan for everything, and it has to be.** The old rule arbitrated takeable-vs-fixture
     only; two fixtures were left to script execution order, which is why cycle 2's chest answered
     every press with its top drawer and neither the lower bay nor the cube on top could be reached.
   - **`WantsInteractHint` is ELIGIBILITY ONLY and must never consult the arbitration** — the scan
     polls that property, so a fixture that asked back would recurse. A fixture states what it *could*
     do; which one the press is *for* is decided in one place.
   - The scan reads the fixture list from `ControlHintDisplay` (rebuilt per cycle by `CycleBinding`)
     and takeables from `ItemRegistry`. Anything in **neither** is judged on its own merits rather than
     silently losing a contest it was never entered in — see `IsAimedAt`.
2. **How many** — and aim cannot answer this, because each item recomputes it as its own `Update` runs
   and a take already made drops out of the running, promoting the next one down the pile. Gate on
   `PlayerHand.InteractedThisFrame` as well: one press, one action, whatever the script execution
   order happens to be.

**Every fixture that answers an E press must CHECK `PlayerLookup.InteractTaken` and CLAIM the press
with `PlayerLookup.ClaimInteract()` — and claim it only when it actually acts.** All three halves are
load-bearing:

- **Check**, or two fixtures answer one press and which pair fires is a coin toss on script execution
  order. Play found it as picking a key up and putting it in the lock with a single press.
- **Claim**, or `PlayerHand.LateUpdate` reads the press as *put down* and throws the held object on
  the floor — E means "put down" exactly when nothing else wanted it.
- **Only when acting.** A fixture that claims a press it then refuses is a deadlock: `KeyLock` used to
  flash red at an empty hand and eat the press, which made a key lying beside its own lock impossible
  to pick up. Refusing a press you were never offered is not a refusal — leave it for someone else.

**This is per OBJECT, not per id.** An id can name a supply — three pins share `"Tool"` — and each of
the three obeys the five states and returns to its own origin. What is forbidden is two *holders* of
one object, never two objects of one id. A ghost asks for a free instance
(`ItemRegistry.FindFreeForGhost`); **the player carries exactly one object, full stop** (§4), so
anything gating on "E would pick this up" must check `PlayerHand.HandsFull`, or it prompts for a press
that cannot succeed.

### 1.3 Record the attempt, re-evaluate the condition

A ghost never blindly replays. The condition re-evaluated must be **the one that actually enabled
the action** — not a weaker fact that correlates with it. Currently re-evaluated: item availability,
whether the ghost still holds an item, whether it is *equipped*, and `requiresOpenDrawer`.

### 1.4 Identity, never position

Pops carry a `balloonId`; a `Take` carries an `itemId` **and**, since 2026-08-13, an
`instanceName` — which physical object, not just which id. Replaying "the player acted *here*"
against a world that has moved makes a ghost's contribution luck, and for a SUPPLY id (three pins
share `"Tool"`) `itemId` alone has exactly the same problem one level up: "took a Tool" replays as
"took *a* free one", which loses which physical pin a take off a ghost actually meant and can leave
two pins out where the original iteration only ever had one taken. `GhostReplayer.TryTake` tries the
named object first (unless the living player holds it) and falls back to "any free one, or any
ghost-held one for a socketed item" only when it cannot.

**AN ID CAN NAME SEVERAL SOCKETS TOO, and a `Surrender` records WHICH ONE** (2026-08-15, for
room2-2's two bucket stands). `ItemRegistry` holds a **list** of sockets per id, registered by
GameObject name, and `CarryEvent.instanceName` carries the target of a surrender exactly as it
carries the object of a take. Anything placing an item must ask `FindSocket(itemId, targetName)`; the
one-argument form means "is there anywhere at all", which is the *eligibility* question and not the
placement one. A named socket that is occupied refuses and the ghost keeps carrying — the same
honest outcome a take of an unavailable object gets.

**A HAND-OVER THAT KEEPS NOTHING IS NOT A SURRENDER.** Pouring a bucket into the tank leaves the
bucket in the hand, so no custody changes and no `CarryEvent` describes it — a past self walked to
the tank with a full bucket and stood there. It is recorded as a **signal** instead (`PourPoint`,
§1.5), re-evaluated at the far end against the condition that enabled it. Any future "use the thing
you are holding *on* that fixture" belongs in the same shape.

### 1.5 Signals are levels; events are instants

`RecordedFrame.signals` is continuous state. `PopEvent` / `CarryEvent` are moments with an identity.
**Never convert an instant into a signal bit.** A ghost can skip several recorded frames in one tick,
so press-type interactables stretch their pulse and act on the rising edge.

### 1.6 `ghostInteractables` — append, never reorder

An interactable's index **is** its bit in `RecordedFrame.signals`. `PlayerRecorder.interactables` and
`GhostReplayer` must be the same array in the same order; `SceneBuilder` builds one and hands it to
both. `signals` is a `uint`, so **32 interactables is a hard cap** — **per cycle**, since the boundary
destroys every ghost (cycle 1 uses 4, cycle 2 uses 12).

**Overflowing it is silent** — `PlayerRecorder.SampleSignals` clamps to the mask width and drops the
rest, so the 33rd fixture simply never records and the room it is in looks fine. Both ends now say so
instead: `SceneBuilder.CheckGhostSignals` fails the build (and catches a **null** entry — a bit
nothing can ever set — and a **duplicate** — one fixture on two bits, whose second rising edge fires
an action the player performed once), and `PlayerRecorder` logs once if an array is swapped in at
runtime. If 32 is ever genuinely not enough, widen the mask to a `ulong`: `RecordedFrame`,
`PlayerRecorder`, `GhostReplayer.activeSignals`/`ApplySignals`, four edits and 4 bytes a frame.

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
| `FallingItem` | One carryable's fall when nothing holds it up — dropped, or its support taken |
| `HeldItemClearance` | Keeping whatever is in the hand out of the walls |
| `ItemRegistry` | id → the **supply** wearing it, id → socket, and the reset sweep |
| `IItemSocket` | Anything that accepts an item and keeps it |
| Room components | One puzzle's rule, nothing else |
| `ChessBoard` | Room2West: the grid, which piece belongs where, and one `IItemSocket` per piece |
| `ChessPlacer` | The left button and the aim; every question about *where* is `ChessBoard`'s |
| `ChessReward` | Room2West's payoff: the lights, the board opening, the plinth. Not the rule |
| `CubeRoom` | Room2East: which cube belongs in which recess, and whether they are all home |
| `SymbolSlot` | One recess's range, E press and light; every *which* question is `CubeRoom`'s |
| `WaterTank` | room2-2: how full the tank is, and the mark it has to reach. Not how it got there |
| `Bucket` | One pail: how full it is, and the pour that empties it — the tilt and the stream too |
| `BucketStand` | The spot under a tap that catches the water, and the socket a ghost places into |
| `PourPoint` | Where a pour is RECORDED. A signal, because pouring hands nothing over |
| `RewardPlinth` | A plinth that rises carrying an escape object, on its room's own condition |
| `FinalRoomSequence` | Room4: the console, the three-object exit condition, and the break |
| `Cycle` | One cycle's world: its bed, doors, rooms, signal array, panels — and **which particle systems are its gas**. Anything per-cycle a system outside needs is NAMED here, never gathered by type at runtime |

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
- **~~The completed-errand rule~~ REMOVED 2026-08-13, by explicit request**, along with the
  ghost-to-ghost close it used to backstop (next bullet). It used to require a recording to have also
  surrendered an item before a ghost would even attempt to retake it — the arbitration that stopped a
  recording that fetched a key and fumbled from robbing one that fetched it and delivered. Now a take
  is a fact this ghost always tries to reproduce, whether or not anything came of it. **The cost is
  real: whichever recording's Take fires earliest at replay wins a contested item, which can silently
  fail another ghost's OWN later delivery of that same item.** Accepted anyway, because it is not
  unrecoverable within a run — the ghost that lost the item still performs everything else in its
  recording (§1.3/§1.5, every action re-evaluates its own condition independently), and the living
  player can simply finish the one delivery that did not land. See
  `docs/ghost-possession-design.md` §4 and §5c for the full argument.
- **ONE OBJECT IN THE HAND, OR NONE. Tab is gone** (2026-08-13, by request), and with it the pocket,
  `hand.Has`, and `CarryKind.Equip`. Carried and in-hand are the same fact now, so every "operated
  *with* an item" gate is `hand.Holding(id)` and nothing has to keep two answers in agreement.
  - **E is the whole of handling an object**: press to take, press again to put down. `CarryKind` is
    `Take | Surrender | Drop`; a drop is recorded and a ghost reproduces it, or the world a recording
    leaves behind is not the world it was made in.
  - **Hands full REFUSES a take, it does not swap.** E has one meaning at a time, and a press that
    silently exchanged one object for another would be a third meaning with no prompt for it.
  - **The last lap changed shape as a direct consequence**: three escape objects can no longer reach
    Room4 in one trip, so finishing means past selves delivering what they delivered while the living
    player brings the last one. The 15-iteration and 8-second figures above were measured before this
    and are void.
- **Held objects are their TRUE SIZE.** The shrink-to-fit every carryable used to carry is gone;
  `handLocalScale` is now the object's own world scale, and where it sits comes from
  `SceneBuilder.HandPoseFor(size)` — bigger things are held further out and lower. A metre of glass is
  genuinely in the way, and that is the accepted trade. `handLocalScale` is still **required** for
  anything under a scaled parent (every chess piece), or it is handed over at 1/0.3039 of its size.
- **A ghost shows what it carries**, in its hand (`GhostReplayer.LayOutCarried`). It used to hide all
  but the equipped one, and since `CarryableItem.IsAvailable` reads `visible` that made them
  **untakeable as well as unseen**: a past self holding pin and key with the pin out put the key where
  the player could neither find nor reach it. The belt line the fix added is now mostly vestigial —
  recordings made after Tab's removal never hold more than one thing — but `HoldingEquipped` still
  gates every tool-shaped action, and `equippedId` is set by the take and cleared by the drop,
  surrender or steal, rather than replayed from an Equip event.
- **Ghost-to-ghost taking is OPEN again, reversed 2026-08-13 by explicit request.** `IsFreeForGhost`
  is still `!IsCarried && visible` and still shuts out `FindFreeForGhost`, so this runs through a
  second lookup, `ItemRegistry.FindHeldByGhost`, which `GhostReplayer.TryTake` falls back to when the
  free one comes back empty. It reproduces a take the living player genuinely made off a past self -
  `PlayerHand.Take` always records one, off a ghost or off the floor alike - at that take's own
  recorded timestamp. Still scoped to items with a socket, now purely as its own eligibility filter:
  a pin has no destination a hand-over could matter for.
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

- **Carryables have NO Rigidbody, and a fall is scripted** (`FallingItem`, on every carryable). The
  loop must put every object back exactly, and a simulated settle lands somewhere different every
  time. PhysX is reproducible in principle and cannot be here: it solves in islands, so where a
  dropped object ends up depends on every body near it — including a living player who moves
  differently every iteration, which would break "a past self does exactly what you did". One axis,
  real gravity, one known height. Nothing about a fall is recorded; it is *derived* from the release.

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
"C:\Program Files\Unity\Hub\Editor\6000.5.7f1\Editor\Unity.exe" -batchmode \
  -projectPath "<repo>" -executeMethod IterationRoom.EditorTools.SceneBuilder.Build -quit -logFile <log>
```

**Do NOT add `-nographics` unless you mean to skip the bakes.** With no graphics device
`BakeReflectionProbes` and the menu-background capture both bail by design - they cannot render - and
they say so in the log, so a room that MOVED keeps a cubemap of where it used to be and every metal
and every water surface in it reflects the old scene. Verified 2026-08-19: plain `-batchmode` on
Windows gets a real device and reports `14/14`. Watch the log for `probes NOT baked (-nographics)`.

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
| `docs/cycle-design.md` | The cycle paradigm and the `room<cycle>-<n>` naming (designed, not built) |
| `docs/ghosts.md` | Ghost appearance, the afterimage shader, animation scrubbing |
| `docs/loop-and-ui.md` | Loop, wake-up, ending, sensitivity, pause, menu, HUD |
| `docs/room-geometry.md` | Shells, the wall grid, doors |
| `docs/rendering-notes.md` | Lighting, materials, probes |
| `docs/audio.md` | PA schedule, filter chain, clip generation |
| `docs/asset-licences.md` | Every third-party file and what it is licensed under |
| `docs/gotchas.md` | Costly one-time discoveries |
| `docs/decisions.md` | Rejected ideas and open questions |
| `iteration-game-spec.md` | Original spec |

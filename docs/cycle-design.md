# The cycle paradigm

**How new puzzles get added from now on: not more rooms on the end of the corridor, but a new
CYCLE.** Finishing the game stops ending it. The three escape objects still go into the console, the
facility still says `CYCLE BROKEN` — and then an opening appears behind the console, and through it,
below, is another bed.

Design settled 2026-08-14, and **the boundary is built** — the loop runs over cycles, ghosts are torn
down at the seam, the floor opens, the gas fires, and `room2-1` exists one storey down with a bed in
it. **What does not exist is a puzzle in cycle 2**, so its `finalRoom` is null and nothing can finish
it yet. Verified in code only; nobody has played it.

This document is the argument and the constraints; `TODO.md` holds the work.

**Target platform is Steam.** The WebGL build is a prototype delivery vehicle and is not a design
constraint — where a decision below would have gone differently for a browser, it says so.

---

## 1. Why

A practised clear is **10 iterations in 4:47**. That is thin for a paid release, but length is not
the real deficiency. **The game has one verb.**

| Room | The action |
|---|---|
| Room2 | key → lock |
| Room2West | piece → square |
| Room2East | cube → recess |
| Room4 | escape object → recess |

Even the reward objects are fetch-and-place. A fourth room of the same shape adds two minutes and no
new experience — and `docs/future-ideas.md` §12 already asks that rooms not repeat a type.

A cycle boundary is a place to put **different verbs** without renegotiating anything inside the
existing rooms.

## 2. This is not the "chapters" that was rejected

`docs/decisions.md` § *Open question: one loop, or chapters?* weighed chapters and left them
unbuilt on two grounds. **One of them does not apply to this proposal, and one of them does.**

> *"One unbroken loop is the premise, and cutting it into chapters makes each one a small puzzle box
> rather than a place you are trapped in."*

**Does not apply.** The rejected shape put the boundary at a **section clear**. This one puts it at
the **win**. Inside a cycle nothing changes at all: one bed, sixty seconds, ghosts accumulating
forever, `EndCycleControl` on N. A cycle is not a chapter of a game — it is a whole game. The player
never experiences a puzzle box.

> *"It throws away accumulated ghosts, which are the visible record of the work; the ending's last
> image is two past selves holding pads, and that only lands because they were both earned in one
> run."*

**Applies, and is the real cost — and the consolation is not where it first looked.** The first
version of this argument said `CYCLE BROKEN` is the moment every earned ghost is working at once, so
walking out through them spends rather than deletes them. **That is false in this code, and the
correction matters.** A ghost whose recording runs out calls `SetVisible(false)` and leaves
(`GhostReplayer.cs:213-228`), and `EndCycleControl` truncates recordings — the average iteration is
**29 seconds**, so a large share of past selves have already retired long before the run is finished.
Completion is also structurally *later* than the last ghost delivery, since two of the three objects
arrive by ghost. By the time the console is full, most of them are gone. The code says so itself:
*"Short timelines make this ordinary rather than exotic."*

**The moment they are all working is the FINAL LAP, and it already exists.** Carrying the last object
to the console, past past selves running the errands they were given, *is* the climax
`docs/future-ideas.md` §1 and §14 asked for — and it needs nothing built. What follows completion is properly
a **stillness**: the facility broken, the panels gone to `ERROR`, the player walking out alone through
what they made. The ghosts are spent by the lap, not by the walk.

If a deliberate curtain call is ever wanted — every ghost replayed from t=0 at once while the player
crosses the room — that is a **staging feature in its own right**, not a prerequisite for any of this.

**And one open problem closes for free.** `TODO.md` §1 — the ghost reset trigger, spec §4.6, called
"the last real gap" — has never had a trigger anybody liked. `decisions.md` predicted that chapters
would supply one "for free, with a reason the player understands". This is that. Two more benefits
fall out: ghost count gets a **per-cycle ceiling** rather than growing forever, and Room1/Room2 get
somewhere to stop being the film's rooms — the divergence `docs/asset-licences.md` argues for.

## 3. The sequence

1. **Third object goes in.** `FinalSlot.Accept` → `FinalRoomSequence.Completed`. Unchanged.
2. **The clock stops.** Already free: `ElapsedTime += Time.deltaTime` lives only inside the inner
   `while` (`LoopManager.cs:238-240`), so leaving the loop freezes it where it stands.
3. **NO BREAK, and no timer of any kind.** `RunBreak()`'s dressing still fires — the door behind
   seals, "Containment failure. Cycle broken.", the panels glitch outward from the console, the shake
   ramps once (`FinalRoomSequence.cs:102-136`). But the 10-second `breakDuration` **stops gating
   anything**, and this costs no deletion: it only ever bounded the window before `RunEnding` took
   control away at `LoopManager.cs:383`, and a non-final cycle never routes into `RunEnding` at all.
   **The break dissolves as a consequence rather than being removed.**
4. **The opening appears** behind the console: one wall grid cell, bottom row, in Room0's north wall.
   *New.*
5. **The player stays as long as they like.** Clock frozen, ghosts still, door behind sealed, `ERROR`
   spreading across the panels. **No timer, no prompt and no failure state.** The room is theirs until
   they step into the opening — which is the only thing that can happen next, so nothing has to push
   them toward it.
6. **The drop.** Down a shaft into the bed room below. There is no kill plane, no fall damage and no
   Y bounds anywhere in the project, and `CharacterController` handles it as-is.
7. **The opening seals** above them — `Door.Seal()` reused (`Door.cs:149-169`, on
   `unscaledDeltaTime`).
8. **Gas, with no warning at all.** Not announced, not telegraphed. The player is not in the bed when
   it happens — they collapse wherever they stand, and wake in the bed, exactly as every iteration
   already works.
9. **The eyelids close** — `WakeUpSequence.CloseEyes()`, ~1.44 s. Unchanged.
10. **The boundary**, entirely behind the shut lids. See §5c.
11. **Waking.** `WakeUpSequence.WakeUp()` **works at a new bed for free** — it has no bed reference
    and poses the eye wherever the player already is.
12. **The HUD reads `CYCLE 2` on its own line, `ITERATION 1` beneath it.**

## 4. Settled

| | |
|---|---|
| **The clock** | **Stops** at `CYCLE BROKEN`. No time limit on reaching the opening, no failure state. |
| **The break** | **There is none.** No 10-second window, no timer, no prompt. The player stays in Room0 indefinitely. |
| **The gas** | Fires **without any warning** once the player is in the room below. |
| **The HUD** | `CYCLE 2 — ITERATION 1`. The count resets; the cycle number is shown beside it. |
| **The space** | **A floor below, running back the other way.** Cycle 2 switchbacks under cycle 1 along −Z. |
| **`EndCycleControl.UseCount`** | **Resets** at a boundary, like every other count. |
| **Saving** | **The boundary is an automatic checkpoint.** See §4b. |
| **Naming** | `room<cycle>-<n>`, the hinge room being `-0`. Document vocabulary. See §4a. |

**Why the count resets rather than continuing.** It follows from the fiction rather than from taste:
the `ERROR` panels mean *the cycle anchored to that bed is over*, iterations are counted against a
bed, and ghosts are recordings anchored to one. A new bed genuinely starts a new count.

### 4a. Room naming

Cycles make bare `Room1` ambiguous, so the vocabulary becomes **`room<cycle>-<n>`**, and the room a
cycle **ends** in is always **`-0`** — it is the hinge, belonging to the cycle it closes and opening
onto the next.

| Today's code name | Document name |
|---|---|
| `Room1` | `room1-1` |
| `Room2` | `room1-2` |
| `Room2West` | `room1-3` |
| `Room2East` | `room1-4` |
| `Room3` | `room1-5` |
| `Room4` | **`room1-0`** |

**`Room2West` and `Room2East` are already misnomers** — they were named when the layout was a hub and
they genuinely sat west and east of Room2. Since 2026-08-13 the building is one line and they are
simply the third and fourth rooms, so sequential numbering describes the game and the old names
describe a layout that no longer exists.

**This is document vocabulary. Code names do not change** — probe cubemaps are named off the
GameObject (`SceneBuilder.cs:2147-2148`), so renaming `Room4` renames the tracked
`Room4_Reflection.exr`, and that churn buys nothing today. **Cycle 2's rooms are new objects, so they
take the scheme natively in code** (`Room2_1`, `Room2_0`, …) at no cost. Cycle 1's code rename stays
available as its own commit if the split ever becomes annoying.

### 4b. Saving

**Players will not finish in one sitting**, so leaving and returning has to resume somewhere. The
**cycle boundary is that point, automatically** — no menu, no prompt, no save slots.

**It is also the only cheap place to put one.** A ghost is ~3,600 `RecordedFrame`s per 60-second
recording and the timelines live nowhere but on the ghost objects themselves (§5c), so saving
mid-cycle means serialising every accumulated timeline. **A boundary is the one moment in the whole
game with no ghost state at all** — the accumulation has just been discarded and the next has not
begun. What has to be written is tiny: which cycle, and nothing else.

**The cost, stated plainly:** quitting at iteration 9 of a cycle and coming back starts that cycle
again from iteration 1. At roughly five minutes a cycle that is an acceptable loss, and it is the
price of not serialising ghosts.

## 5. What the code forces

### 5a. Hard blockers — the design does not exist until these change

1. **`RunLoop` terminates permanently.** `yield break` at `LoopManager.cs:278`; `Start()` is the only
   `StartCoroutine(RunLoop())`, so nothing restarts it. → Restructure as **an outer `while` over
   cycles around the existing `while` over iterations.**

   The comment already at `LoopManager.cs:270-274` explains why the exit is taken there: *"Everything
   below this point is the cycle closing and re-opening… None of it should happen to a player who
   just got out, and the ending is largely defined by their absence."* **That reasoning stays true
   inside a cycle.** This design does not overturn it; it adds a **third path** — neither "an
   iteration ended" nor "the run ended" but "a cycle ended". The reset machinery does run — at a
   different bed.

2. **`RunOver` has no way back.** Set at `LoopManager.cs:333`, never cleared, and `AcceptsInput`
   (`:86`) is derived from it — so once true every interactable in the game is dead forever. →
   Keep `RunOver` for the *final* cycle's true ending and add **`CycleBreaking`** for the boundary:
   movement allowed, `AcceptsInput` false. That matches `docs/future-ideas.md` §14 — no new operations
   demanded right before the escape.

3. **`FinalRoomSequence.Completed` has no reset.** `ResetRoom` deliberately does not clear it and
   `Update` early-returns on it. → Cleared at the boundary.

4. **`EndingSequence.Play` always ends in `SceneManager.LoadScene(menuScene)`**
   (`EndingSequence.cs:79`). → Reached only on the last cycle — and **"last" must be derived, never
   hardcoded**: cycles are a list, and the last one is the one with no successor defined. More cycles
   are intended (§8), so a literal `2` anywhere here is a bug waiting for cycle 3.

5. ~~**`CarryableItem.floorY` is an ABSOLUTE WORLD Y.**~~ **DONE.** It was: compared straight against
   `transform.position.y`, with every assignment a half-height measured from y = 0, so an object on a
   lower storey fell through its floor to the height of the one above. Split in two — `floorY` is now
   explicitly the object's own half-thickness **above its own floor**, `floorBaseY` says which floor
   that is, and `RestingY` is the sum every height comparison reads. The ground storey needs no
   change, because `floorBaseY` defaults to zero.
   - **`SceneBuilder.SetFloorBase(root, y)` sets it for a whole subtree**, deliberately not per
     builder: a storey is a property of where a thing was placed, and asking each builder to remember
     it is asking for one of them to forget, silently.
   - **One read site keeps `floorY` and must**: `PlayerHand.VisibleAhead` builds a point relative to
     the *player*, who is standing on the floor being dropped onto, so `RestingY` there would add the
     storey in twice.

6. **`Door.sealing` is never cleared** (`Door.cs:76, 152`) and `Update` returns immediately while it
   is set. **Less pressing than it looks**: the door `RunBreak` seals is on the floor being abandoned,
   and the player is meant to stay in Room0 anyway. It only bites if the shaft cover reuses `Door` and
   something later wants it open again. → Give the cover its own component, and the problem does not
   arise.

### 5b. Two floors in one scene

7. **`BuildRoomShell` has no Y parameter at all** (`SceneBuilder.cs:2645`). Floor (−0.05), ceiling
   (5.4578) and walls (y 0 → `RoomHeight`) are absolute, and the room GameObject is left at identity
   so nothing propagates. `BuildDoorPocketFill`, `BuildDoorShell`, `BuildCeilingLights`,
   `BuildReflectionProbe` and `MakeFourWallFaces` are all the same. → Add a `yCenter`, or give the
   room root an offset and make every builder respect it.

8. **Two unconditional-`Awake` singletons are read on every ghost tick.** `BalloonField.Instance` and
   `EscapeTrigger.Instance` (`GhostReplayer.cs:243, 247`), assigned with no guard
   (`BalloonField.cs:37`, `EscapeTrigger.cs:45`). **A second instance silently steals the first**, and
   floor 1's balloons stop dropping for ghosts. `ChessBoard` already shows the better pattern — it at
   least clears `Instance` on destroy (`ChessBoard.cs:110`).

9. **`EscapeTrigger` deliberately ignores Y** — a documented decision, since a room has a floor and a
   ceiling. With a floor below, **a player at the same X/Z one storey down raises Room0's console.**

10. **`PanelMessage.PlayerInside` tests Z only** (`PanelMessage.cs:158`), and `BalloonField` uses the
    same world-space Z band. A room under Room1 at the same Z triggers Room1's sign.

11. **`ItemRegistry` maps one id to one socket, last writer wins** (`ItemRegistry.cs:87`), and a
    duplicate id is no longer an error — just a log that reads identically to a deliberate supply. →
    **Cycle 2 uses new ids.** Ids are wire values (`SceneBuilder.cs:22-30`).

12. **`LoopManager` names every room type by hand** — `balloonField`, `chessBoard`, `cubeRoom`,
    `finalRoom`, `bedSpawnPoint`, `doors`, `drawers`, all single fields wired once at
    `SceneBuilder.cs:630-651`. → Swap the block at the boundary, or generalise to an `IRoomReset` list.

13. **Wall panels are gathered by name** — anything whose parent ends in `_Panels`
    (`SceneBuilder.cs:307-317`). **Cycle 2's panels would automatically join cycle 1's `ERROR`
    glitch**, including rooms the player cannot see. The `ERROR` means *this bed's cycle is over*, so
    **the panel set must be per-cycle.** The exclusion hook already exists in the shape of
    `InCalibrationRoom`.

14. **Anything that moves during play must be excluded from the probe bake**, or it bakes in its
    authored pose. The list is `CarryableItem / Door / RewardPlinth / Balloon / Drawer /
    GhostReplayer / Player` (`SceneBuilder.cs:2189-2220`). Each room needs its own box-projected
    probe, and the `.exr` is named off the GameObject.

### 5c. The boundary itself — behind the eyelids, and the order matters

15. **Destroy the ghosts at or before the existing `ReleaseCarried` slot** (`LoopManager.cs:171`),
    never after `BalloonField.ResetField()` (`:185`). `GhostReplayer.OnDestroy` runs
    `ApplySignals(0u)` then `ReleaseCarried()` → `ReturnToOrigin()`, which ends in
    **`SetVisible(true)`** (`CarryableItem.cs:424`). Destroy late and a key a past self was holding
    reappears *outside* its balloon.

    `OnDestroy`'s own comment names this feature: *"the GHOST RESET (next steps §2) is precisely a
    feature that destroys ghosts, and it would have shipped straight into this."* The obligation in
    §1.2 is already met — the ordering is what is left to get right. Do not destroy inside a `foreach`
    over `ghosts`, and remember `Destroy` defers `OnDestroy` to end of frame.

16. **The ghost objects are the only owners of the recordings.** There is no `List<RecordedTimeline>`
    anywhere; `GhostReplayer` caches its own in `Init`. **Destroying the ghosts frees every byte of
    accumulated timeline** — the reset needs no separate bookkeeping.

17. **Replace the `ghostInteractables` array REFERENCE; never mutate it in place.** Every replayer
    holds the same reference. Mutating it changes what a bit *means*, and `activeSignals` edge
    tracking means a stale set bit is never cleared — a floor pad held down forever. Assign
    `PlayerRecorder.interactables` in the same step; it is a plain public field with no invalidation
    path.

18. **The 32-interactable cap becomes per-cycle rather than global.** No surviving timeline references
    the old bits, so cycle 2 may reuse 0–3. **A ceiling that constrained the whole project stops being
    global** — the paradigm's second free win.

19. **Counters to decide on**: `IterationNumber` and `totalElapsedTime` (reset),
    `EndCycleControl.UseCount` (currently and deliberately not loop-reset), and
    `PanelMessage.showFromIteration` — resetting the count makes the wall signs reappear, which may be
    right for a new cycle or may be noise.

### 5d. Free

- The clock already stops, and the player can already walk during the break.
- `WakeUpSequence` has no bed reference — it works at a new bed unchanged.
- `Teleport` zeroes `verticalVelocity`, so an iteration reset mid-fall is safe.
- Audio is one global 2D PA — nothing per-floor to duplicate.
- `AddFallingToEveryCarryable` and `MarkReflectionProbeStatic` are scene walks; new rooms join
  automatically.
- `GhostReplayer.OnDestroy` already discharges the §1.2 custody obligation.

## 6. The opening

**A HOLE IN THE FLOOR behind the console, one wall grid cell across** — 1.75 m square, using the wall
grid's cell width as the size reference so it reads as a piece of the building rather than a hatch
fitted to a person. It slides open the way a door does, and the player looks down through it at the
next bed before dropping in.

**Corrected 2026-08-14.** This section first recommended a cutout in the north WALL, on the grounds
that `SubtractRect` already cuts backing, collision and panels with one Rect while the floor is a
single slab with no cutout support. **That was implementation cost deciding a design question, which is
the wrong way round** — looking *down* through the opening at the bed you are about to wake in is the
whole image, and a wall opening cannot give it.

The cost turned out to be small anyway: `SubtractRect` is pure Rect arithmetic and works on any two
axes, so the floor reuses it with (x, z) in place of (along-wall, height). One helper emits the slab as
up to four pieces instead of one.

- The hole sits at room-local **z = +2.0**, clear of the console (its body ends at z = 0.575) and well
  clear of the north wall at 5.25. Behind the console from the player's approach, which is from −Z.
- **Both slabs are cut**: `room1-0`'s floor and `room2-1`'s ceiling, since the lower room sits directly
  beneath. One `Rect` per room, in room-local XZ.
- **There is a 1.6 m SERVICE VOID between the two storeys, and it is not decoration.** Every
  `RewardPlinth` retracts about a metre below its own floor, which was invisible until there was a room
  under it and then hung out of that room's ceiling. Housing the console did not help — the housing hung
  into the room too. Machinery needs somewhere to be that is in neither room, which is what a real
  building would give it. `BuildExitShaft` joins the two holes across the void, so the drop reads as
  going down a shaft rather than falling out of a roof space.
- **The cover slides sideways into the floor slab** and is invisible once retracted, which is what a
  door slab does in its pocket. No pocket is needed here because the surrounding floor is solid.
- **`CycleExit` drives it**, and "the player has gone through" is a drop test rather than a trigger
  volume: there is no kill plane, no fall damage and no Y bound anywhere in the project, so having
  fallen past the floor they were standing on is the plainest available statement of having left.

## 7. Scene strategy

**One scene, each cycle a storey below the last, switchbacking along −Z.** Looking down through the
opening and seeing the next bed requires both floors to exist at once, which is the whole point of the
beat and the reason this is not already a scene per cycle.

### 7a. Only one cycle is awake

**Culling does not stop `Update`, and that is where the cost was.** About two hundred components in
this scene poll every frame — fifty carryables, fifty `FallingItem`s, seventy balloons, the pads, the
doors — and until 2026-08-14 they ran whether or not anybody was in their cycle, in both directions:
cycle 2's pads were ticking through the whole of cycle 1.

`Cycle.SetAwake` deactivates the whole subtree. That stops the polling, drops the renderers and
lights, and unregisters the carryables under it through `CarryableItem.OnDisable` — the last of which
is wanted rather than tolerated, since `ItemRegistry.ReturnAllToOrigin` has no business sweeping a
cycle nobody can reach. `LoopManager` wakes the next cycle **before the hatch opens**, because the
player has to be able to look down at the bed, and puts the old one away behind the eyelids.

**It must happen AFTER the probe bake, and the first attempt did not.** A probe renders the scene from
its own position and a disabled renderer does not render, so `room2-1` and `room2-2` baked as empty
rooms — their `.exr` files came out at half the size of every other room's, which is what a cubemap of
nothing compresses to. Walls at 0.85 smoothness are almost entirely what they reflect.

### 7b. What actually forces a scene split, and when

**It will be needed. It is not needed for the reason it looked like.** Measured 2026-08-14 at two
cycles: the scene is **6.2 MB / 236,000 lines**, and nine probe cubemaps are **21 MB on disk** — about
14 MB of probes per six-room cycle.

- **Runtime memory is not the pressure.** Compressed, thirty probes is on the order of 50 MB, which is
  nothing on a desktop target. Runtime CPU was the real cost and §7a has taken it.
- **The pressures are both TIME, and both land on the developer first.** Every rebuild re-bakes every
  probe — six more per cycle — and the scene the Editor has to open and save grows with it. Player
  load time follows the same curve, later.
- **A cheaper step exists before splitting.** Cycles are one-way; the exit seals and nothing returns.
  So a spent cycle can be **destroyed** rather than deactivated, with `UnloadUnusedAssets` behind it,
  which frees the memory without touching scene structure. It does nothing for build time or load
  time, which are the pressures that eventually decide this.

**Watch the build, not the frame rate.** The trigger is a rebuild that has become intolerable or a
scene the Editor is slow to open — not a dropped frame, which §7a has already answered.

### 7c. The split is smaller than this document first claimed

An earlier version of this section said every reference is wired directly by `SceneBuilder`, so a
scene per cycle would mean finding everything at runtime — "a large refactor". **That was wrong, and
worth correcting because it was an argument for deferring.**

Almost every reference is **intra-cycle** — door to pad, slot to item, room to plinth — and all of it
serialises perfectly well inside a per-cycle scene. What crosses a boundary is three things:
`LoopManager.cycles`, `CycleExit.player`, and `SleepingGas.emitters`. Two more, `wallPanels` and
`ghostInteractables`, are **already rebound at runtime** as a consequence of being per-cycle.

**The `Cycle` component built for the boundary is already the seam a split would need**: one `Cycle`
root per scene, found after load, everything else inside it. That makes this a moderate change rather
than a structural one — and it gets *easier* with each cycle rather than harder, because nothing about
a boundary depends on both floors existing except looking through the shaft.

**And the escape route is in the fiction already**: a mock bed room visible through the shaft with the
real cycle loaded under the gas. The gas and the eyelids are a perfect loading mask, and §4b's
checkpoint means a boundary carries no state across.

## 8. Still open

**One item, and it is deferred on purpose.**

1. **Where does the last cycle end?** Undecided — the intent as of 2026-08-14 is to add cycle 3 and
   keep going, so there is no terminal cycle to design against yet. **The constraint that follows is
   firm even though the answer is not: never hardcode a last cycle.** Cycles are a list; "this is the
   last one" means "no next one is defined", and that is what routes into `EndingSequence`
   (§5a-4). Written that way, cycle 3 costs nothing to add and the question can stay open
   indefinitely.

### 8a. An idea, recorded only — the walk back through every cycle

**Raised 2026-08-14. Not decided, not planned, and nothing in this document depends on it.**

Once every cycle is cleared — say there are ten — the player returns to `room1-1` and simply **walks
forward through all ten cycles**, with every ghost from every cycle still there. No puzzle, no timer,
nothing asked. Sightseeing your own record: *so this is what I have been doing.*

**Why it is worth keeping.** It is §2's "spent, not deleted" argument applied to the whole game
instead of one lap, and it is a far better answer to §8's open question than a results screen. It also
gives the discarded ghosts a second life, which is the one cost of the cycle paradigm this document
could not pay off.

**Two things it collides with, so the price is known before anyone falls in love with it:**

- **Ghosts are discarded at every boundary, and timelines live nowhere but on the ghost objects
  themselves** (§5c-16). Bringing them all back means either never discarding — ten cycles at ten
  iterations is on the order of **a hundred simultaneous ghosts**, a different question from the
  thirteen already unprofiled — or **serialising every timeline**, which is exactly what §4b's
  checkpoint was designed to avoid.
- **The cheap version and the expensive version are very far apart, and they may deliver the same
  feeling.** "So this is what I did" does not obviously require live replay: posed figures, a subset
  of the ghosts, or an outright scripted diorama would read much the same walking past at speed.
  **Decide which one is actually wanted before costing it** — the expensive version is the only one
  that reopens serialisation.

**Closed, and recorded because they nearly cost work:**

- *"Do ghosts keep moving after completion?"* — answered by removing the break and by §2's
  correction. They stay still, most have already retired, and **no break-only time source is needed**.
  Ticking them on would have required one, since `Tick` takes the now-frozen `ElapsedTime` and
  reviving that would restart the HUD clock (`CountdownTimer.cs:11-19` writes unconditionally every
  frame).
- *HUD width* — `CYCLE N` goes on **its own line above** `ITERATION N`, which also settles the
  fixed-width check CLAUDE.md §3 demands: neither line is longer than what `IterationLabel` already
  renders, so the `0.6 × fontSize × length` budget that has bitten twice is untouched.
- *`EndCycleControl.UseCount`* — resets (§4).
- *Naming* — `room<cycle>-<n>`, document vocabulary only (§4a).
- *Saving* — the boundary is an automatic checkpoint (§4b).

## 9. What this does not answer

The paradigm is **a container, not a verb.** It creates somewhere to put new kinds of puzzle; it does
not supply one. If cycle 2 turns out to be another corridor of fetch-and-place rooms, this will have
been an elaborate way to ship the same game twice. The candidates that would actually add a verb —
accumulated workload (the tree and the axes) and simultaneous work — are in
`docs/future-ideas.md` §3 and §4, with the hard decision already taken in `docs/decisions.md`
(threshold as a **headcount**, nothing stored).

**Ghost crowding is still unprofiled.** It stops growing without bound under this design, but a
cycle's worth of skinned, afterimaged figures is a real cost on any target, and the tree room's entire
payoff is N of them working at once. Measure before committing to a headcount.

And none of this has been played. It is verified against the code only.

---

## 9. The ending: how the game stops at cycle 3 (2026-08-31)

### The decision

Cycle 4 was designed as the fourth act and is not going to be one yet. A full three-cycle run is
expected to take **about an hour for a player who has not counted anything** — the recorded figures
(31 iterations, 14:23) are a knows-everything run by the person who designed it — and a fourth act
would put the game past the point where a first-time player finishes it at all.

So cycle 3 is the last act, and cycle 4 is deferred rather than deleted.

### Why cycle 4 is still built, still loaded, and still in `CycleSceneNames`

The obvious way to end at cycle 3 is to drop `"Cycle4"` from `SceneBuilder.CycleSceneNames`. That
works and it is wrong, because it takes the room out of the WORLD as well as out of the game.

The ending flies past the outside of the building, and the last thing under the cable car as it
pulls away is cycle 4: a finished cell with a bed in it and nothing else, lit differently, that the
player is never let into. That is the game saying *there is more of this* in the only register it
has ever used, which is architecture — and it costs nothing, because the room was already built.

The switch is therefore `Cycle.playable`, asked by `LoopManager.HasNextCycle` alongside "does a next
cycle exist". **"Loaded" and "playable" stopped being the same question the moment the building had
to contain a room the game will not enter.**

### What replaces the fourth bed

`EndingDeparture` owns the order and delegates every part of it:

| Beat | Owner |
| --- | --- |
| The break, unchanged | `FacilityFailure` |
| The grade printed on room3-2N's north wall | `EvaluationBoard` / `RunEvaluation` |
| The east wall coming apart | `CycleExit` — the same component the floor hatches use |
| The car arriving, the doors, boarding, the climb | `CableCarRide` |
| Cycles woken, ceilings and walls off, past selves stood back up | `FacilityExterior` |
| The card | `EndingSequence`, unchanged |

**The climb is the reverse of the game.** The player spends an hour descending one hatch at a time;
they leave by going back up through all of it in one continuous move, past cycle 3, then 2, then 1,
to the surface — which is the only daylight in the game.

### Three things that were nearly built wrong

**The cutaway was a build-time list of renderers.** `FacilityExterior` held the walls and ceilings as
serialized `Renderer[]`, gathered by `SceneBuilder`. It built cleanly and logged *"46 walls and 27
ceilings taken off for the view"* — and 73 of those 74 renderers were in `Cycle1` and `Cycle2` while
the component was in `Cycle3`, so Unity would have nulled every one of them on save and the ride
would have been a tour of a solid white building. `cross-scene-report.txt` named all 74. It is a
`float` now (the cable's X) and the walls are found at runtime; a number crosses no boundary.

**The breach covers were scaled cubes.** Three boxes filling the hole in the east wall — which is
the thing CLAUDE.md §3 forbids in as many words, because a wall panel here is a generated mesh with a
6mm chamfer and a scaled box has neither chamfer nor grooves. They would have been three smooth
patches in a grid wall for the whole of cycle 3, visible long before anything opened. They are
`BuildPanelWall` segments now, one cell each.

**The board would have graded the player mid-puzzle.** Room3-2N is where the Bedlam cube is, so the
player is under that panel for most of cycle 3 — a proximity trigger alone starts the readout the
first time they walk in with a block. It is gated on `PowerOn` (the break) *and* on the player being
below the board's own centre, which separates this room from room3-0 twelve metres above it without
a tuned radius.

### The one thing the ride is actually for

`GhostArchive`. Every past self's pose is kept at the cycle boundary, before `LoopManager` destroys
them, and stood back up as the exterior is revealed — so the rooms the car climbs past have dozens of
people frozen mid-errand in them. Cycle 3's ghosts need no archive: they are still alive and still
standing, because the ending has never destroyed them (`EndingSequence`: *"the ghosts are left
standing... the people it took to get out"*).

An empty building would make this an architectural tour. The statues are the argument.

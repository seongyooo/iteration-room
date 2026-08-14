# The cycle paradigm

**How new puzzles get added from now on: not more rooms on the end of the corridor, but a new
CYCLE.** Finishing the game stops ending it. The three escape objects still go into the console, the
facility still says `CYCLE BROKEN` — and then an opening appears behind the console, and through it,
below, is another bed.

Design settled 2026-08-14. **Nothing here is implemented.** This document is the argument and the
constraints; `TODO.md` holds the work.

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
new experience — and `Iteration — Future Ideas.md` §12 already asks that rooms not repeat a type.

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
`Future Ideas` §1 and §14 asked for — and it needs nothing built. What follows completion is properly
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
   movement allowed, `AcceptsInput` false. That matches `Future Ideas` §14 — no new operations
   demanded right before the escape.

3. **`FinalRoomSequence.Completed` has no reset.** `ResetRoom` deliberately does not clear it and
   `Update` early-returns on it. → Cleared at the boundary.

4. **`EndingSequence.Play` always ends in `SceneManager.LoadScene(menuScene)`**
   (`EndingSequence.cs:79`). → Reached only on the last cycle — and **"last" must be derived, never
   hardcoded**: cycles are a list, and the last one is the one with no successor defined. More cycles
   are intended (§8), so a literal `2` anywhere here is a bug waiting for cycle 3.

5. **`CarryableItem.floorY` is an ABSOLUTE WORLD Y.** Default `0.06f` (`CarryableItem.cs:54`),
   compared straight against `transform.position.y` (`FallingItem.cs:104`), and every assignment is a
   half-height measured from y = 0: `keySize/2f`, `0f`, `cubeSize/2f` (`SceneBuilder.cs:3305, 3444,
   4011`). **This is the single largest cost of a second storey.** Anything carryable down there must
   have `floorY` set against that floor, or it falls through it — and anything carried *up* refuses to
   fall at all.

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

**One wall grid cell, bottom row, centre, in Room0's north wall.**

A cell is `1.75 × 1.3519492`; north and south walls are 5 columns by 4 rows. The cutout is
`Rect.MinMaxRect(-0.875f, 0f, 0.875f, GridCellHeight)`.

- **A wall cutout, not a floor hatch.** `SubtractRect` already cuts backing, collision and panels
  with one Rect. **The floor has no equivalent** — it is a single 9.0 × 0.1 × 10.85 collider cube, and
  a hatch there means building cutout support that does not exist.
- **It would be the first grid-aligned opening in the game.** Doorways are 1.3 × 2.5, deliberately
  off-grid. An opening that lands exactly on the grid reads as *the building coming apart*, not as a
  door — which is what the moment is.
- Room0's north wall is currently solid with **no pocket cavity and nothing behind it**. The shaft is
  new geometry and needs the equivalent of `BuildDoorPocketFill`'s `FarCap`, or it opens to the void.
- **Open it with the `RewardPlinth` pattern**, not `Door`: derived state (`blend` toward `Wanted`), so
  it needs no reset hook, and it is already excluded from the probe bake. Seal it with `Door.Seal()`.

## 7. Scene strategy

**One scene, second floor at −Y, switchbacking along −Z.**

Looking down through the opening and seeing the next bed requires both floors to exist at once. A
scene per cycle would dodge the absolute `floorY`, the singletons, the `ItemRegistry` collisions and
the panel gather — and could not produce that image, which is the whole point of the beat.

So the costs in §5b and §5c get paid.

**When one scene stops working.** A scene holding every cycle eventually costs load time and memory,
and **the intent is to keep adding cycles**, so this is a real future rather than a hypothetical one.
On a desktop target the ceiling is still far away — it was much nearer when WebGL was the delivery
platform — so it remains no reason to build scene streaming now.

**The escape route is already in the fiction**, which is what makes deferring safe: a **mock** bed
room visible through the shaft, with the real cycle loaded under the gas, since the gas and the
eyelids are a perfect loading mask. §4b's checkpoint already means a boundary carries no state
across. **Build it when a cycle's build time or memory actually hurts, not before** — and note the
switch gets *easier* with each cycle rather than harder, because nothing about a boundary depends on
both floors existing except looking through the shaft.

## 8. Still open

**One item, and it is deferred on purpose.**

1. **Where does the last cycle end?** Undecided — the intent as of 2026-08-14 is to add cycle 3 and
   keep going, so there is no terminal cycle to design against yet. **The constraint that follows is
   firm even though the answer is not: never hardcode a last cycle.** Cycles are a list; "this is the
   last one" means "no next one is defined", and that is what routes into `EndingSequence`
   (§5a-4). Written that way, cycle 3 costs nothing to add and the question can stay open
   indefinitely.

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
`Iteration — Future Ideas.md` §3 and §4, with the hard decision already taken in `docs/decisions.md`
(threshold as a **headcount**, nothing stored).

**Ghost crowding is still unprofiled.** It stops growing without bound under this design, but a
cycle's worth of skinned, afterimaged figures is a real cost on any target, and the tree room's entire
payoff is N of them working at once. Measure before committing to a headcount.

And none of this has been played. It is verified against the code only.

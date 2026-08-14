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

**Applies, and is the real cost.** But this structure has an answer the rejected one did not: the
moment `CYCLE BROKEN` fires is **the only moment in the game when every ghost the player earned is
working at once**. If the opening appears behind the console and the player walks *through* that to
reach it, the ghosts are **spent, not deleted** — which is exactly the climax walk-through
`Future Ideas` §1 and §14 have been asking for since the beginning, finally with a reason to exist.

**And one open problem closes for free.** `TODO.md` §1 — the ghost reset trigger, spec §4.6, called
"the last real gap" — has never had a trigger anybody liked. `decisions.md` predicted that chapters
would supply one "for free, with a reason the player understands". This is that. Two more benefits
fall out: ghost count gets a **per-cycle ceiling** rather than growing forever, and Room1/Room2 get
somewhere to stop being the film's rooms — the divergence `docs/asset-licences.md` argues for.

## 3. The sequence

1. **Third object goes in.** `FinalSlot.Accept` → `FinalRoomSequence.Completed`. Unchanged.
2. **The clock stops.** Already free: `ElapsedTime += Time.deltaTime` lives only inside the inner
   `while` (`LoopManager.cs:238-240`), so leaving the loop freezes it where it stands.
3. **The break, 10 seconds.** `RunBreak()` — the door behind seals, "Containment failure. Cycle
   broken.", the panels glitch outward from the console, the shake ramps
   (`FinalRoomSequence.cs:102-136`). Unchanged.
4. **The opening appears** behind the console: one wall grid cell, bottom row, in Room0's north wall.
   *New.* The player can already walk during this — `ControlEnabled = false` is not set until the
   scrim (`LoopManager.cs:383`).
5. **The climax walk.** Past selves still working their remaining timeline. **Nothing forces the
   player.** There is no timer and no failure state; they may stand and watch as long as they like.
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
12. **`CYCLE 2 — ITERATION 1`.**

## 4. Settled

| | |
|---|---|
| **The clock** | **Stops** at `CYCLE BROKEN`. No time limit on reaching the opening, no failure state. |
| **The gas** | Fires **without any warning** once the player is in the room below. |
| **The HUD** | `CYCLE 2 — ITERATION 1`. The count resets; the cycle number is shown beside it. |
| **The space** | **A floor below, running back the other way.** Cycle 2 switchbacks under cycle 1 along −Z. |

**Why the count resets rather than continuing.** It follows from the fiction rather than from taste:
the `ERROR` panels mean *the cycle anchored to that bed is over*, iterations are counted against a
bed, and ghosts are recordings anchored to one. A new bed genuinely starts a new count.

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
   (`EndingSequence.cs:79`). → Reached only on the last cycle.

5. **`CarryableItem.floorY` is an ABSOLUTE WORLD Y.** Default `0.06f` (`CarryableItem.cs:54`),
   compared straight against `transform.position.y` (`FallingItem.cs:104`), and every assignment is a
   half-height measured from y = 0: `keySize/2f`, `0f`, `cubeSize/2f` (`SceneBuilder.cs:3305, 3444,
   4011`). **This is the single largest cost of a second storey.** Anything carryable down there must
   have `floorY` set against that floor, or it falls through it — and anything carried *up* refuses to
   fall at all.

6. **`Door.sealing` is never cleared** (`Door.cs:76, 152`) and `Update` returns immediately while it
   is set. A sealed opening is frozen forever. → Clear it at the boundary, or give the shaft cover its
   own component rather than reusing `Door`.

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

**When one scene stops working.** Eventually a scene holding every cycle costs load time and memory.
**On a desktop target that ceiling is far away** — this was a much nearer problem when WebGL was the
delivery platform, and it is not a reason to build scene streaming now. If it ever binds, the escape
route is already in the fiction: a **mock** bed room visible through the shaft with the real cycle
loaded under the gas, since the gas and the eyelids are a perfect loading mask. **Not for cycle 2, and
probably not for cycle 3.**

## 8. Still open

1. **Do ghosts keep moving during the break?** Today they stop: `ghost.Tick(ElapsedTime)` is inside
   the inner `while` (`LoopManager.cs:242-243`). Letting them run means they work out whatever is left
   of their timeline and fall still one by one, which is the better image and is what §2's argument
   about spending rather than deleting them depends on. **Recommended.**
   *The trap*: `Tick` takes `ElapsedTime` as its argument and that value is frozen. Ticking on
   requires a **break-only time source** — reviving `ElapsedTime` itself would restart the HUD clock,
   which writes unconditionally every frame (`CountdownTimer.cs:11-19`) and would contradict "the
   clock stops".
2. **Does "room0" become a code name?** Probe `.exr` files are named off the GameObject, so
   `Room4_Reflection.exr` follows — a tracked asset. **Recommended: keep `room0` as document
   vocabulary and make the rename its own commit, if at all.**
3. **HUD width.** `IterationLabel` spaces every character, so `CYCLE 2 — ITERATION 1` is ~41
   characters after spacing. CLAUDE.md §3 requires checking `0.6 × fontSize × length`, and it has
   bitten twice. **Recommended: two lines.**
4. **Does `EndCycleControl.UseCount` reset at a boundary?**
5. **What is the last cycle's real ending?** If cycle 2 is cleared and there is no cycle 3, something
   must decide that and route to `EndingSequence`.
6. **Saving.** Two cycles is ~10 minutes and there is no save. A boundary is the natural checkpoint.
   Not a blocker; recorded as a consequence — and it becomes a requirement, not a nicety, once this is
   a Steam release.

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

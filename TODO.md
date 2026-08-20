# TODO

Work not yet done. Completed work lives in `git log`; the reasoning behind decisions lives in
`docs/`. Nothing in this file should accumulate as history - when an item is done, delete it.

---

## Next steps

1. **Design cycle 2's first puzzle, and give it a NEW VERB.** The boundary is built (`docs/cycle-design.md`); `room2-1` is a bed in an empty room one storey down, and its `finalRoom` is null so nothing can finish it. **This is now the whole of the work, and it is a design question rather than an engineering one.** Cycle 1's four puzzles are all fetch-and-place; another corridor of the same shape makes this an elaborate way to ship the same game twice. The candidates that add a verb are accumulated workload (the tree and the axes) and simultaneous work — `Iteration — Future Ideas.md` §3 and §4, with the threshold decided as a **headcount** in `docs/decisions.md`.
   - **Nothing has been played.** The boundary is verified in code only: it compiles, both scenes build, probes are 8/8, and the slabs cut correctly. Whether dropping through a hole into a room that gasses you reads as intended is unanswered.
   - **Ghost crowding is still unprofiled**, and it stays worth measuring even though the boundary stops the count growing without bound: the tree room's whole payoff is N past selves working at once. Profile against a desktop target — WebGL is a prototype vehicle, not the release.
   - Cycle 2's rooms are sealed on both walls. The switchback runs −Z, so the next room goes at `4 * RoomPitch` and `room2-1` grows a SOUTH doorway when it does.
2. **Expand Room2: several keys, several locks.** The 2026-08-12 play-through found the thing this was speculative about: **Room2's popping does not accumulate** (the key is visible through its balloon, so it is spot-and-pop-one), and the three pins therefore have no consumer. More keys than one iteration has time to deliver is what converts popping into an errand iterations divide, and it is the same shape as the key mechanic that already works rather than a new one.
   - **Still not first, but for a different reason than before.** The old note here said content length was "answered by the 15-iteration clear" — the opposite is true now: a practised clear is ten iterations in 4:47, and length is the open problem. It is not first because **the answer to length is cycle 2, not more locks on Room2** — another key-and-lock is the same verb the game already has four of. What is left of this item is the narrow and still-true point: the pin supply is infrastructure with nothing consuming it, and its real consumer is a tool-shaped action that is not a key.
   - Rides on systems that exist: `ItemRegistry` is already id→supply and id→socket, `KeyLock` gates on `hand.Holding(id)`, item ids are wire values, pops carry a `balloonId`, and `signals` has 28 of its 32 bits free.
   - **ROOM2'S WALLS ARE SPENT, so "the side doors to hang them on" no longer exists as written.** South is Room1, north is Room3 behind the GOLD key, west is the chess room behind the RED, east is the cube room behind the BLUE. More locks on Room2 means either restructuring its shell or hanging the extra keys on doors that are already there — several keys for ONE door (all of them turned before it opens) is the version that costs no geometry at all.
   - **The one real cost is the geometry**, if a new wall opening is what it comes to. `BuildRoomShell(..., Rect southCutout, Rect northCutout)` takes openings on Z only, and `BuildDoorPocketFill` is Z-oriented too, so doors in the X walls need the shell builder extended. Contained, but not a parameter change.
3. **Re-deploy to itch.io.** A build is ready at `Build/iteration-webgl.zip` and a new project page is being set up; the upload itself is manual (no butler on this machine).
4. **New puzzles go in CYCLE 2, not on the end of the corridor.** Superseded by `docs/cycle-design.md`: the old plan here was to insert a room before Room4 and push Room4 out behind it. **Do not do that.** Cycle 1 is a finished game at ten iterations; lengthening its corridor adds walking, and the corridor is already the one place the clock has no slack. New rooms go on the floor below, behind the cycle boundary, where they cost iterations of their own and where their verbs can differ from cycle 1's.
   - **Distance is still not the constraint** at `walkSpeed` 2.5 with sprint unused. The old "8 seconds to spare on the final lap" figure that argued against extending the corridor is **void** — it was measured on the hub layout and on a final lap that collected three objects in one trip, which one-object-in-the-hand made impossible. It is not evidence for anything now.
   - `RecordedFrame.signals` is a `uint`, so **32 recorded interactables is a hard cap**; four are used. **Per cycle**, once the boundary exists — discarding the ghosts frees every bit for reuse (`cycle-design.md` §5c).
   - The tree room (`Iteration — Future Ideas.md` §3) is the strongest candidate and is now unblocked: multiple axes needed the item pool, which shipped. Its threshold is a HEADCOUNT under the decision in `docs/decisions.md` — N simultaneous choppers cannot be met before iteration N — so pick that number against the run the game actually has — which as of 2026-08-14 is **ten iterations for a practised player**, not the fifteen this line used to say. A tree wanting N simultaneous axes cannot fall before iteration N+1, so N=5 spends half a practised run on one room. It also now has somewhere better to live than the end of the corridor: **cycle 2** (`docs/cycle-design.md`).

## Cycle 2 cannot be finished - it has no room0 and no shards

Superseded 2026-08-19 by the section below: `Room2_0` and its console are DELETED, so the cycle no
longer declares three shard slots nothing can fill - it declares no exit at all, and `finalRoom` is
null. `BuildRingShard` is still a builder with no call site, and `ShardA/B/C` are still ids nothing
wears.

**What is left is a DESIGN decision, not the wiring**: which room ENDS cycle 2, and which three rooms
pay a shard out and on what condition. Room2-1 (four switches), room2-2 (the tank), the tree hall (the
felled tree) and room2-6 (three valves) all have rules and doors but no rewards. `RewardPlinth`
already does "an escape object rises on this room's own condition" and takes a `RoomCondition`, so
whichever three are chosen cost one call each.

## Cycle 2 lost its third leg, and its console with it (2026-08-19)

The old `room2-6`, `room2-7` and `room2-0` are deleted by request, and the pool at the bottom of the
slide is **room2-6** now. What that leaves open:

- **There is no `room2-0`, so cycle 2 cannot be finished** - `Cycle.finalRoom` is null, which every
  consumer guards. It could not be finished before either (its console declared three shard slots and
  nothing wore those ids), so this removes three build errors per run rather than adding a gap. To put
  it back: a room at the end of the walk, `BuildFinalRoom` and its `BuildPlinthHousing`, the
  `CheckCycleFinishable` call, and three carryables wearing `ShardA/B/C`.
- **Room2-7 is behind room2-6's door now** - the weighing room - and ITS door is the capped one. That
  is the honest end of the walk.
- **The tree hall's north-west exit is sealed**, because the room it led to is gone. `TreeFelled` is
  still built and still reset as a `RoomCondition` - what gates the far ledge now is the PIT, since
  the tree is the only bridge, so felling it is still the price of leaving that room.
- **`BuildBallPit` and `BuildBallFill` are now uncalled**, along with `ball_pit.glb`. Kept rather than
  deleted: the pit is a good prop and the next room that wants one gets it for a line. If cycle 2 is
  ever declared final, they and the model are a clean deletion.
- **The core is still measured off `legThreeX`/`legThreeZ`.** Those two numbers now describe where the
  building's west side was rather than where any room is; the core sits inside a ring with two sides
  missing and is not visible from anywhere the player can stand.

## Room2-7's weighing puzzle is unplayed, and three things about it are guesses (2026-08-19)

The room, the scale and the weights are in (`docs/puzzle-design.md` has the table and the reasoning).
**Verified in code and in the built scene; not played.** What that leaves open:

- **Whether five deliveries is too many.** The rule is now "bring all five", and the weights are
  solved so that nothing else in the cycle reaches 26.7 - which makes this the largest headcount any
  room in the game has asked for. One hand, sixty seconds, and a sweep home at every boundary means
  five on the pan at once is five past selves each carrying one. Whether that is the good kind of long
  is the biggest open question in the room.
- **A PARTLY FILLED BUCKET IS A CONTINUOUS WEIGHT**, and it is the hole in the uniqueness proof. The
  proof covers empty and full; `Weighable` reads `Bucket.Level`, which is anything in between while a
  bucket sits under a tap. A player watching the readout could dial one in to reach the target another
  way. Left alone deliberately - it takes knowing the weights, it is fiddly, and it reads as a clever
  route rather than an exploit - but it is why the room is not single-solution without the caveat.
- **Whether 26.7 is reachable inside the loop at all.** Five objects from four rooms, one hand each -
  so the last delivery needs four past selves to have already made theirs. That is the intended shape
  and it has never been walked, and the walk now includes a one-way slide.
- **THE OBJECTS COME FROM ROOMS THE PLAYER HAS LEFT BEHIND.** The axe is in the tree hall, the bucket
  and its water in room2-2, the cube on room2-1's dresser - and the slide is one-way. Carrying an axe
  down the chute is possible; carrying one BACK is not. This is the first puzzle in the game whose
  ingredients are upstream of a one-way passage, and whether that reads as clever or as cruel is the
  single biggest open question here.
- **A carryable ridden down the slide is untested.** `SlideRide` teleports the player and the hand
  anchor is parented to the camera, so it should simply come along - but nothing has ever been carried
  through a scripted ride.

## Room2-6 - the flooded room and its valves - is unplayed (2026-08-19)

Room2-5's slide lands in 1.2m of water with 210 plastic balls, three rubber ducks and two beach balls
floating on it (`WaterPool`, `FloatingBalls`, `SceneBuilder.BuildWaterPool`). **Verified in code
only** - it builds, the ride path measures the room's floor at -3.71 rather than the waterline, and
nothing about it touches the loop, the signal array or the reset.

- **It is scenery, like room2-6 and room2-7.** No collider, no carryable, no interactable, no
  condition. Falling in is not fatal; see `docs/puzzle-design.md` for why drowning and swimming were
  both turned down. Wading costs speed (0.45x) and changes the footstep clips, and that is all.
- **The room still has no way out**, and now it is a room somebody will want to stay in. Its doorway
  went when the room dropped below the hall and has not come back at the new height.
- **The slide's exit and the room's opening are one hole now**, so the depth of the room below the
  hall is derived from the mouth (`SlideRoomFloorY`) rather than chosen. If that band ever changes,
  both walls and the tunnel between them move together - do not pin one of them to a number.
- Play questions: whether the arc off the chute reads as the slide finishing or as being thrown off
  it, whether 0.45 speed in water is heavy or annoying, whether the wade clips sit right at walking
  cadence, and whether the hole shows enough of the room from the hall to earn its geometry.
- **THE CHEST'S DRAWERS ARE A TOGGLE AGAIN** (`canClose = true`, 2026-08-20, by request), so a
  ghost's take of a billiard ball works on odd numbers of past-self pulls and silently fails on even
  ones. It was chosen over the alternative fault - a drawer that can never be shut blocks the bay
  below it - and both are written up in `docs/gotchas.md`. **The third option, if this turns out to be
  the worse half: make the player's press a toggle and a ghost's replay open-only.** One line in
  `Drawer.SetGhostSignal`; it costs a little of "a past self does exactly what you did".
- **`room2-0` IS CLEARED** - iteration 22, 10:04, 2026-08-20 - so the machinery is answered and only
  the judgement questions are left. What a clear cannot say: whether 22 iterations is enjoyable or a
  slog, whether the pictograms read as *how many of these are there* rather than *bring one of these*
  to somebody who was not told the answer, and whether the refusal flash reads as an answer rather
  than as a fixture that did nothing. **All three need a player who did not build it.**
- **The standing cost of room2-7 is the single biggest driver of cycle 2's length**, and it has never
  been weighed against anything. Its scale re-locks every iteration, so five past selves must re-load
  it before the door to room2-0 will open - every trip, for the rest of the run. If cycle 2 wants to
  be shorter, that latch is the lever, not the number of balls.
- **THE CYCLE PICKER IS UNLOCKED ON PURPOSE, AND A SHIPPED BUILD WANTS IT GATED.** CYCLE SELECT
  currently lists every cycle whether or not the player has reached it, because the shortcut into a
  cycle under construction is the whole value of the page while cycle 3 is being built. For release:
  show only cycles up to `GameSettings.SavedCycle`, and hide the button entirely at 1. That is one
  condition in `MainMenu.Awake` and one in `Start`; leaving it as it is ships a title screen that
  spoils cycle 2 and lets a new player start there knowing nothing.
- **CYCLE 3 IS PLANNED AND NOT STARTED.** What already handles an arbitrary number of cycles and needs
  nothing doing: `LoopManager.CycleRecords` and the ending's breakdown table (a third cycle is a third
  row), `CycleBinding` (gathers per cycle), the gas repointing, and the signal-bit reset at the
  boundary. What does NOT: the three things listed below, all of which assume cycle 2 is last.
- **Cycle 2's hatch is capped and cycle 3 does not exist.** Completing room2-0 currently runs
  `LoopManager.RunEnding` - the break, ten seconds, then the game's ending scrim - because there is no
  cycle after it. Three things change the day there is one: `FinalRoomSequence.opensWayOutOnBreak`
  goes to false, `BuildCycleTwoExit`'s `Shaft_Cap` comes out and its `shaftDepth` becomes
  `ServiceVoid`, and cycle 2's `CycleExit` moves out of room2-0 onto the join between the storeys
  where cycle 1's already sits.
- **The valve puzzle's pacing is a guess.** Two turns of 1.4s each on three wheels, in a room 8.75 x
  10.5 crossed at 1.125 m/s (walking, slowed by the water). That is roughly 30 seconds of work for a
  player who already knows where the wheels are - so whether "six turns is more than one iteration
  holds" is TRUE depends entirely on how much of the minute is spent getting down here at all, which
  has never been measured. If one iteration turns out to be enough, the lever is `turnsToOpen` or
  `Valve.turnDuration`, not the room.
- **The floats are 215 rigidbodies** (210 balls, 3 ducks, 2 beach balls) on the balloon layer, and the
  balls are meant to SLEEP when undisturbed - `FloatingBalls` skips a body it would not move, which is
  the whole reason the count is affordable. If that room ever costs frames, check that they are
  actually asleep before blaming the count: anything that writes to them every frame (a bob, a drift,
  a stray `AddForce`) silently keeps all 210 awake.
- **THE SECOND STOREY INSIDE CYCLE 2 IS NOW ANSWERED IN THREE PLACES**, and all three had to be:
  room2-6, room2-7 and room2-0 sit `SlideRoomFloorY` (3.7m) below the rest of the cycle.
  1. `FallingItem` RAYCASTS for the floor instead of trusting `floorBaseY` - anything dropped down
     there used to stop 3.7m in the air, and anything put on the scale used to end up inside it. The
     probe reaches 32m, deep enough for the tree hall's pit.
  2. `CarryableItem.DropAt` clamps to that same raycast rather than to `RestingY` (2026-08-20). The
     clamp is what stops a drop landing *below* a surface, and against a cycle-wide height it was
     lifting every drop in those three rooms 2.4m into the air first.
  3. `SceneBuilder` sets `floorBaseY` for those rooms in a **second pass**, so the value in the scene
     is true rather than 3.7m out for the five props that live down there.

  **A fourth storey-sensitive reader would be a fourth bug.** `RestingY` is still consulted by
  `BalloonField` and `LoopManager`'s test jump; both are cycle-1-only today, and both would be wrong
  if a lowered room ever grew a balloon or a console.
- `FirstPersonController.PushOverlapping` collects at most **24** colliders. Standing in the pool with
  the balls packed around the player is the first place in the game that can plausibly exceed it; the
  overflow is silent and simply means some balls are not pushed.

## Queued fixes

1. **Decide the mirrored `ERROR`, and the panel UVs behind it.** Wall panels are `PrimitiveType.Cube`,
   and a Unity cube's +X and -X faces carry opposite U directions - so the test card that reads
   correctly on the west wall reads MIRRORED on the east, and the same holds north against south.
   **Seen 2026-08-11 and liked**: a display showing its picture backwards is a display that has
   stopped working, which is what that room is for. So this is a decision to make deliberately, not
   a defect to clear - and it is only a decision because nothing had ever put a TEXTURE on a panel
   before; flat colour has no handedness. Flipping it is a per-panel `_BaseMap_ST` U scale of -1
   chosen from which wall the panel is on, computed once in `BeginGlitch`.
   The shipped 2026-08-11 itch build has the mirroring in it.

2. **`nightstand.glb` is no longer referenced by anything** — the nightstand is built from primitives now. 8.8MB of Git LFS that nothing loads. Delete it once it is clear nothing else wants it.
3. **Crouch, and which key is left for it.** Sprint shipped on **Shift**, the conventional key — the old plan paired Ctrl-sprint with Shift-crouch, and that inversion only existed to free Shift, so it went with it. Crouch therefore needs its own key (Ctrl, or C) rather than the swap. It also still interacts with something tuned: crouch changes the eye height the near-clip corner analysis assumed.

## Open, from the future-ideas review

1. **How many pins is the right number?** Three is the player plus two past selves popping at once,
   picked to prove the supply rather than tuned. Room2 holds ~70 balloons and a 60-second loop, so
   whether three hands clear it at a rate worth playing is a play-test question. One number in
   `SceneBuilder.BuildNightstand`.
2. **`Iteration — Future Ideas.md` sits at the repo root and is untracked.** Every other design
   document is in `docs/`, which `CLAUDE.md` §7 indexes. Two things in it need reconciling with what
   is already true: §10's ghost-tool rule is **shipped**, not future work, and §13 restates this
   file's Room-extension plan, so the two will drift.
3. **A number lock (future-ideas §7) is knowledge, not accumulation.** Once the player knows the
   combination, no iteration reduces the work and ghosts cannot help — the one puzzle shape where the
   core rule stops applying. §7 also contradicts §14, which asks for no new operations before the
   final escape. Decide before it is built. (§8's chess board is **settled and built** — the way out
   was to tell the player the arrangement and charge them the walk, which moves it into accumulation.
   See `docs/puzzle-design.md`.)
4. **Room2West's four picked numbers are playable but still unmeasured.** The 15-iteration clear
   proves the room works end to end; it does not say these are the right values, and each is one
   constant: `ScatteredPieceCount` (12 — how many iterations the room is), `HeldPieceScale` (0.28 — a
   king fills 0.31m of the view), `ChessReward.openTravel` (1.7 each way, which leaves both halves
   about a metre off the walls), and the room starting at intensity **0**. Twelve is the one worth
   attacking first: it and the cube room's six are what took the run from four iterations to fifteen,
   so **this constant is the game's length dial**. Same status for the ghost carry pose: a clear means
   past selves carrying pieces have now been on screen and nothing was reported wrong, which is not
   the same as having looked at whether a ghost reads as *carrying a rook*. `ghostLocalEuler` was
   tuned for the key; one number if it does not.
5. **Timed chains (future-ideas §4) fight `signals` semantics.** A ghost advances by elapsed time and
   can skip several recorded frames in one tick, which is why press-type interactables stretch their
   pulse. Narrow timing windows are exactly where that bites. Plain simultaneity is safe; sequencing
   with delays needs its own design pass.


## The tree hall (cycle 2, built 2026-08-16 - verified in code, NOT played)

- **Is 40 chops the right number?** It is a guess shaped to be out of one player's reach inside sixty
  seconds and comfortable for five. Only play answers it. One field: `trunk.chopsToFell`.
- **Five axes is the earliest clearable iteration**, so it is the room's real difficulty knob and it
  has never been felt. Raising or lowering it is one line in `BuildTreeHall`.
- **Ghost crowding is still unprofiled**, and this room is where it peaks: five skinned, afterimaged
  past selves swinging at once is the payoff and the load at the same time. `docs/cycle-design.md` §9
  has been asking for this measurement since the paradigm was written.
- **Chop and fall sounds are placeholders** - the item-pickup and door-open clips.
- ~~Ghosts still do not swing.~~ **DONE** - `GhostReplayer.Swing`, ticked per frame while a swing is
  live. Whether five of them at once actually reads as a work gang is still unplayed.
- **The old note, kept for what it says about where this lives:** The LIVING player's axe now arcs on each chop (`PlayerHand.Swing`),
  but a past self holding one stands still while the notch deepens - the recording only ever carried
  the player's own body, and a ghost's carried item is posed by `GhostReplayer.LayOutCarried` rather
  than animated. The room's whole image is five people swinging at once, so this is the largest
  remaining gap between what it does and what it is for. The hook exists: the chop bit is already
  per-ghost, so `SetGhostSignal` is where a ghost-side swing would start.
- **Whether the crossing is now comfortable is unmeasured.** Leaves are non-solid, the deck is 3.3m
  wide and there is a ramp at each lip - see `docs/puzzle-design.md`. If it is still awkward the next
  lever is the pit's width, which is 10.5m only because room2-4 was.
- `Room2_3/4/5_Reflection.exr` are orphaned - the rooms that owned them are gone.


## Tree hall, still open after the 2026-08-16 revisions

- **The pour lift is inferred, not observed.** `Bucket.PosePourHold` raises the pail to chest height
  because `HandPoseFor` holds a big object low and a 104-degree tilt from there swings it below the
  camera. That is a reading of the code, not of the game - if the real symptom was the bucket
  JUMPING to the floor, the cause is somewhere else entirely.
- **The tree roots are placed by measurement** (scaled to 3.1x the trunk's half-width, sunk to 62% of
  their own height) and have never been looked at in motion.
- ~~`tree_roots.glb` is 1.33 million triangles~~ **GONE ENTIRELY 2026-08-17, by request** - the hall
  has no separate stump model at all now. What remains after the cut is the tree's OWN lower half,
  carved by the same wedge that severed it (`Felled_Stump`), so the stump and the log are two halves
  of one object instead of two different trees' geometry meeting at the floor. `tree_roots.glb` is
  still in `Assets/ArtAssets/Nature` and is now referenced by nothing - delete it with
  `nightstand.glb` when the orphans are swept.

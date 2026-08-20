# TODO

Work not yet done. Completed work lives in `git log`; the reasoning behind decisions lives in
`docs/`. Nothing in this file should accumulate as history - when an item is done, delete it.

---

## Next steps

1. **CYCLE 3: FOUR EMPTY ROOMS AND NO PUZZLE IN ANY OF THEM.** `room3-1` is the bed room under
   room2-0's hatch, and it now has a **gate in each of its four walls** onto `Room3_2N/S/E/W` - see
   `docs/room-geometry.md`. The way in is a floor pad that a PAST SELF has to hold, so the room
   already states the cycle's premise; what it does not have is anything to do once you are through.
   The shape is in and the puzzles are not. `docs/Room3 구조 변경 및 레버 기반 퍼즐 기믹 구현
   프롬프트.md` is the spec; §1 and §2 of it are built and §3-§9 are not.
   - **§3-§7: THE COLOURED STAIRS.** Red, blue and yellow panels in room3-2N, each colour driven by a
     hold-to-activate lever in one of the other three rooms (S=red, E=blue, W=yellow). The step
     geometry is settled - half a grid row, 0.651 to the top, walked up on `stepOffset` - and nothing
     else is. **The lever must be a `GhostInteractable` with hold semantics**, exactly like
     `FloorButton`, or a past self cannot hold it and the room cannot be solved by one person.
     Signal budget is fine: three levers takes cycle 3 from 6 of 32 to 9, and the panels themselves
     are driven by the levers so they cost nothing.
   - **§7 is the actual puzzle and is undesigned.** "Raising one colour blocks a route the others
     opened" is what stops the room being one lever held down. Panel count and placement are
     deliberately left open in the spec for play to settle, so build them as data.
   - **§8: CCTV.** Models are in (`cctv_camera.glb`, `hanging_monitor.glb`). Each monitor is a camera
     rendering to a target, so it is a whole extra scene render per feed - decide whether they run
     always or only while looked at.
   - **A ghost with no panel under it should fall** (by request). Ghosts have no colliders and replay
     recorded positions exactly, so this is a real change to `GhostReplayer`: a downward probe, and a
     rule for what a ghost does once it has fallen off its own recording.
   - **The room names are placeholders.** `Room3_2N/S/E/W` does not fit the `room<cycle>-<n>` scheme
     in `docs/cycle-design.md` §4a, and cannot until the puzzle order is decided.
   - **The pads are unlabelled**, and whether that reads as discovery or as fumbling is a play
     question. So is whether 22.75m of corridor is the right length to be caught in.
   - **Room3-2N's lighting is derived, not seen** - a 3x3 grid with intensity scaled by the square of
     a 3x height jump. Expect to retune. See `docs/room-geometry.md`.
2. **CYCLE 2'S ROOM2-6 SOUTH DOOR IS BLOCKED**, and the build has been saying so on every run:
   `room2-6 south door: 'Solid' blocks the way through (swept at 21.70, -9.97, 89.15)`. `AssertWalkable`
   caught it; nobody was reading the output. A wall collision block is standing in the doorway.
3. **THE ON-SCREEN KEY PROMPTS DO NOT FOLLOW THE BINDINGS** (deferred 2026-08-20, by request). Keys
   are rebindable from the settings page now, but the six places the game NAMES a key are still
   strings authored into `SceneBuilder`: the interact disc's "E", the "[E] — PUT DOWN" hint, "HOLD [N]
   — END CYCLE", and the calibration room's WASD/SPACE/E keycaps plus its SHIFT and CTRL side walls.
   Rebind INTERACT and the disc still says E, which is the game giving an instruction that does not
   work. The fix is a small component holding a `GameAction` and a format string, plus a change event
   on `InputBindings`; it also needs a SHORT form of each key name, because "LEFT MOUSE" does not fit
   in a disc or on an 84px keycap. **The calibration room is the worst of the six** - it is the first
   thing a player sees and it is entirely an explanation of the controls.
4. **THE CHEST'S DRAWERS ARE A TOGGLE, AND IT IS THE WORSE OF TWO FAULTS** (`canClose = true`,
   2026-08-20, by request). A ghost's ball take fails on even numbers of past-self pulls. It was
   chosen over the alternative - a drawer that can never be shut blocks the bay below it - and the
   third option is still unbuilt: make the PLAYER's press a toggle and a GHOST's replay open-only,
   one line in `Drawer.SetGhostSignal`. It costs a little of "a past self does exactly what you did".
5. **Gate the cycle picker before release.** CYCLE SELECT lists every cycle whether or not the player
   has reached it, which is a shortcut worth having while cycle 3 is under construction and a spoiler
   in a shipped build. One condition in `MainMenu.Awake` and one in `Start`.
6. **CC-BY attribution is mandatory and the credits line does not do it.** Every model is CC-BY, which
   permits commercial use - so nothing is blocked - but `SceneBuilder.CreditsLine` reads "FURNITURE
   MODELS: CREATIVE COMMONS", which names no author, no title, no licence version and no link. Ten
   creators are uncredited in a build that is already public. See `docs/asset-licences.md`, which has
   every row resolved and the exact form each entry wants.
7. **Room2-7's latch is the length lever.** Its scale re-locks every iteration, so five past-self
   deliveries are the standing price of every trip to room2-0 - which is why two independent clears
   came out at 22 iterations 1.2 seconds apart. If cycle 2 should be shorter, that is what to change,
   not the number of balls.

## Cycle 2 lost its third leg and got a new console (2026-08-19, closed 2026-08-20)

The old `room2-6`, `room2-7` and `room2-0` are deleted, and the walk is room2-1, room2-2, the tree
hall, then down the slide into the pool (room2-6), the weighing room (room2-7) and the console room
(room2-0). What that leaves open:

- **`BuildBallPit` and `BuildBallFill` are uncalled**, along with `ball_pit.glb`. Kept rather than
  deleted: the pit is a good prop and the next room that wants one gets it for a line. If cycle 2 is
  ever declared final, they and the model are a clean deletion.
- **The core is still measured off `legThreeX`/`legThreeZ`.** Those two numbers now describe where the
  building's west side was rather than where any room is; the core sits inside a ring with two sides
  missing and is not visible from anywhere the player can stand.

## Room2-7's weighing puzzle: what two clears did and did not settle (2026-08-19)

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
- ~~**Whether 26.7 is reachable inside the loop at all.**~~ **ANSWERED by two clears** (2026-08-20):
  it is, and the room is the single biggest driver of cycle 2's length - see below. The original
  worry, kept because the reasoning still holds for the next room like it: Five objects from four rooms, one hand each -
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

## Room2-6 - the flooded room and its valves (2026-08-19, played 2026-08-20)

Room2-5's slide lands in 1.2m of water with 210 plastic balls, three rubber ducks and two beach balls
floating on it (`WaterPool`, `FloatingBalls`, `SceneBuilder.BuildWaterPool`). **Two clears have now
gone through it**, so the room works; what is below is what a clear cannot answer.

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


## The tree hall (cycle 2, built 2026-08-16, played 2026-08-20)

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

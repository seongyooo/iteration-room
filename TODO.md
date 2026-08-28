# TODO

Work not yet done. Completed work lives in `git log`; the reasoning behind decisions lives in
`docs/`. Nothing in this file should accumulate as history - when an item is done, delete it.

---

## Before this can be sold

One hard blocker, and it is not graphics.

- **Cycle 3 is not a third act yet** — the beam now drives a lift and the lift reaches deck A, so the
  cycle has a mechanism and a destination for the first time. What it does not have is a REASON:
  there is no `finalRoom`, no exit condition and nothing to carry anywhere, so the cycle cannot be
  finished. Detail at item 1.

~~`cctv_camera.glb` is CC-BY-NC-4.0~~ **RESOLVED 2026-08-28** by deleting the CCTV system it was in —
see item 7 for what is left, which is a decision rather than a blocker.

Everything else below is quality, not permission.

---

## Next steps

1. **CYCLE 3: FOUR EMPTY ROOMS AND NO PUZZLE IN ANY OF THEM.** `room3-1` is the bed room under
   room2-0's hatch, and it now has a **gate in each of its four walls** onto `Room3_2N/S/E/W` - see
   `docs/room-geometry.md`. The way in is a floor pad that a PAST SELF has to hold, so the room
   already states the cycle's premise; what it does not have is anything to do once you are through.
   The shape is in and the puzzles are not. `docs/Room3 구조 변경 및 레버 기반 퍼즐 기믹 구현
   프롬프트.md` is the spec; §1 and §2 of it are built and §3-§9 are not.
   - **THE BEAM DRIVES THE LIFT NOW** (2026-08-28), so `LaserReceiver.Lit` has a consumer for the
     first time and the west-wall plate is that lift's low call. What is still open is everything
     ABOVE that: the lift reaches deck A and deck A has nothing on it. A way up is not a puzzle.
   - **The riser pane is built and nobody has bounced it.** One mirror in room3-2S is tilted 45deg, so
     it turns a level beam through 90 and sends it to the ceiling, where the lift's high call is. It
     is carried like any other pane. **Verified in code only**, and it is the one piece of the optics
     whose geometry cannot be checked from a plan - the beam has to actually land inside a 0.7m target
     sixteen metres up.
   - **THE BEAM'S PLANE FOLLOWS ITS HOLDER NOW, AND NOBODY HAS PLAYED WITH THAT.** A held pane sits
     1.2m above the HOLDER'S FEET rather than at one world Y, so the light climbs the building with
     the people carrying it. Two things that only play can answer: whether losing the guarantee that
     every pane is on one plane makes a failed route hard to READ - the beam now misses for a reason
     you cannot see from the floor plan - and how much a small step under a holder costs. **That
     second one is nearly a bug already**: the lift at rest is a 0.20m step, so a pane held while
     standing on it sits 0.20 above the beam, against a glass RADIUS of 0.236. It still catches, by
     3.6cm. Any step taller than 0.236m breaks a route silently and looks like nothing at all, and
     there is no assert anywhere that would say so.
   - **The route is two mirrors long and there are five.** The shortest solution is one turn in
     room3-1 and one inside the big room; the other three mirrors have nothing to do. Lengthening it
     is level design - move the receiver, or put something in the way - and it wants play first,
     because "how many bounces is fun" is not answerable from a plan.
   - **Deck A is reachable now; DECK B IS NOT.** `BeamLift` runs a 2m deck between the floor and deck
     A's surface, raised at rest and pulled down by the beam - so the ride up is what happens when a
     past self lets go. Deck B is another storey above that with nothing serving it, and the five
     columns are still unreachable architecture.
   - **How much realism the body is worth is still open.** The model is 7,932 triangles with no UVs
     and no textures, so every surface is one flat colour - `SceneBuilder` gives it six tuned ones
     (skin, hair, shirt, pants, socks, eyes) and that is the ceiling. Anything more is a different
     asset, not a different number. The mirror is the only place it will ever be seen properly, and
     the glass is now known to be good - so this is a look-and-decide, not a wait.
   - **The gates no longer stand off for the player** (`Door.standsOffForPlayer = false`) and shut in
     0.3s. Two things to watch: whether a leaf closing THROUGH the player reads as a bug rather than
     as your own fault, and whether the room still lets a determined player through their own east or
     west gate - that run is only 1.18m.
   - **A ghost with no floor under it should fall** (by request). Ghosts have no colliders and replay
     recorded positions exactly, so this is a real change to `GhostReplayer`: a downward probe, and a
     rule for what a ghost does once it has fallen off its own recording. It was asked for when the
     room had panels that came and went under people; with those gone it is no longer urgent, and it
     is still the right behaviour.
   - **The room names are placeholders.** `Room3_2N/S/E/W` does not fit the `room<cycle>-<n>` scheme
     in `docs/cycle-design.md` §4a, and cannot until the puzzle order is decided.
   - **The pads are unlabelled**, and whether that reads as discovery or as fumbling is a play
     question. So is whether 22.75m of corridor is the right length to be caught in.
   - **Room3-2N's lighting is derived, not seen** - a 3x3 grid with intensity scaled by the square of
     a 3x height jump, plus four spots under the two decks at a flat 6 because a deck is a ceiling for
     whatever is beneath it. None of the seven numbers has been looked at. See `docs/room-geometry.md`.
   - ~~The lift has never been ridden~~ **PLAYED AND IT CARRIES YOU** (2026-08-28). `BeamLift` moving
     the player itself through `FirstPersonController.Carry` is the right mechanism and none of the
     three failure modes it was flagged for showed up in a ride.
2. **OCCLUSION IS LIVE IN EVERY CYCLE NOW, AND NOBODY HAS PLAYED WITH IT ON.** `PlayerLookup.Occluded`
   forgave any hit sharing `transform.root` with the thing being looked at, which switched the test
   off entirely inside cycles 2 and 3 - one root per cycle there, so every wall shared a root with
   every fixture. It forgives the FIXTURE now (`OwnerObject`: the nearest ancestor implementing
   `IInteractHintTarget`), which is exact and bounded. **This tightens prompts in all three cycles,
   not just the two that were blind** - cycle 1 forgave a whole ROOM at a time and now forgives one
   object.
   - **PLAY HAS ALREADY FOUND ONE REGRESSION AND IT IS FIXED**: the chess pieces went unpickable,
     because a piece's anchor is its own origin and its `floorY` is ZERO - the anchor rests exactly on
     the floor plane, so the ray to it ended on the floor collider. `PlayerLookup.SurfaceClearance`
     drops the last 5cm of the cast; the surface a thing stands on is not hiding it. **The lesson
     generalises past chess: any anchor sitting ON a surface was in the same position**, and the chess
     piece is the only carryable in the game with `floorY = 0` (every other one is 0.06 or measured).
   - What is still unplayed is the rest of it: a prompt that has stopped appearing where it used to,
     anywhere a fixture is read past its own room's furniture. Every drawer front, rim and tray in the
     game is built `removeCollider: true`, so the only solid things left to occlude with are walls,
     floors and doors - but that is an argument, not a playthrough.
4. **THE ON-SCREEN KEY PROMPTS DO NOT FOLLOW THE BINDINGS** (deferred 2026-08-20, by request). Keys
   are rebindable from the settings page now, but the six places the game NAMES a key are still
   strings authored into `SceneBuilder`: the interact disc's "E", the "[E] — PUT DOWN" hint, "HOLD [N]
   — END CYCLE", and the calibration room's WASD/SPACE/E keycaps plus its SHIFT and CTRL side walls.
   Rebind INTERACT and the disc still says E, which is the game giving an instruction that does not
   work. The fix is a small component holding a `GameAction` and a format string, plus a change event
   on `InputBindings`; it also needs a SHORT form of each key name, because "LEFT MOUSE" does not fit
   in a disc or on an 84px keycap. **The calibration room is the worst of the six** - it is the first
   thing a player sees and it is entirely an explanation of the controls.
5. **THE CHEST'S DRAWERS ARE A TOGGLE, AND IT IS THE WORSE OF TWO FAULTS** (`canClose = true`,
   2026-08-20, by request). A ghost's ball take fails on even numbers of past-self pulls. It was
   chosen over the alternative - a drawer that can never be shut blocks the bay below it - and the
   third option is still unbuilt: make the PLAYER's press a toggle and a GHOST's replay open-only,
   one line in `Drawer.SetGhostSignal`. It costs a little of "a past self does exactly what you did".
6. **Gate the cycle picker before release.** CYCLE SELECT lists every cycle whether or not the player
   has reached it, which is a shortcut worth having while cycle 3 is under construction and a spoiler
   in a shipped build. One condition in `MainMenu.Awake` and one in `Start`.
7. **THE ONE NON-COMMERCIAL ASSET IS GONE** (2026-08-28). `cctv_camera.glb` was CC-BY-NC-4.0 and
   could not ship in a paid build; the CCTV system it belonged to was deleted by request, so the file,
   `hanging_monitor.glb`, `CctvFeed.cs` and the three feed materials went with it. **The blocker was
   retired by the room changing rather than by sourcing a replacement**, which is worth noticing: the
   cameras had outlived their subject by a week - they were watching a staircase deleted 2026-08-21.
   What is left is a decision, not a blocker:
   - **`wooden_bucket.glb` is CC-BY-SA-4.0** (room2-2's buckets). Sellable, but the model and any
     modification stay under the same licence. A decision to take, not to discover. Replacing it is
     cheap - a bucket is not a hard model to source.
   - ~~Attribution is missing~~ **DONE**: `ReadModelCredits` reads title/author/licence/source out of
     each glTF's own `asset.extras`, `BuildCreditsPage` puts it on a CREDITS page on the title screen,
     and `WriteAttributionFile` writes `ATTRIBUTION.md` with the URLs. 26 models, generated on every
     build, so it cannot go stale again.

8. **Room2-7's latch is the length lever.** Its scale re-locks every iteration, so five past-self
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

## The capture rig, built 2026-08-23, unplayed

`CaptureRig` hides the HUD (F9, three steps) and takes the camera off the player's head (F10).
Verified in code only: **the two keys have never been pressed.** `docs/trailer-shotlist.md` is what it
is for.

- **Shot F has no shortcut.** `DebugStart.AtCycleBoundary` does exactly what filming the console
  needs - stands the player in the last room with the three escape objects on the floor, everything
  after that on the real path - and nothing sets it, because the title screen's TEST button was
  removed on request 2026-08-15. Either it goes back in some form (a modifier-click on the existing
  cycle picker would not add a visible control), or that shot costs a played cycle. **A decision, not
  a defect** - it was removed deliberately.
- **Three numbers are guesses**: `lookSensitivity` 0.6, `flySpeed` 1.6 and the 1.15-per-notch scroll
  step. All three are one field in `SceneBuilder.BuildCaptureRig`.
- **The pull-back dolly (hold O) is new and unpressed.** `dollyRiseRatio` (0.25) and `dollyEaseTime`
  (1.1s) are guesses in the same place as the three above. What has never been watched: whether the
  ease-in/out reads as smooth or as two visible speed changes, whether a quarter rise is enough to
  read as "slightly high" in a real room, and whether locking out the mouse for the whole glide is
  comfortable or feels like losing control of the shot.
- **The detached camera's culling mask is unverified.** It is set to the mirror camera's - full body,
  no headless one - so a flown shot should show a whole person. Nobody has looked.
- **Mirrors are wrong from a detached camera** and will stay wrong: `MirrorReflection` renders for
  `PlayerLookup.Eye`, which is still the player's head. Fixable only by letting something else name
  the eye, which is a change to a cycle-3 system for a trailer's benefit. Not worth it yet.

## Two graphics passes are in and unseen; the third is blocked (2026-08-25)

Asked for after play: lift the graphics. The diagnosis was that nothing was textured badly - it was
that **every surface in the building was mathematically perfect**. Three passes; the reasoning and
every dead end are in `docs/rendering-notes.md`.

**1 and 2 are built and verified in code only.** Nobody has looked at either.

- **Chamfered panel rims.** Every wall panel is a generated mesh with a 6mm chamfer on its front rim
  instead of a scaled cube, so each of the ~3,400 raised panels has a lit line inside the shadow line
  it already had. 20 distinct meshes for the whole game, so batching is unaffected - the build logs
  the count and hundreds would mean the sizes had stopped repeating.
  - **What to look at**: whether the rim reads at all at the far end of a room, or whether 6mm is too
    fine to survive at distance. `PanelChamfer` is the number.
  - Watch the doorway edges specifically - partial panels are what mint the odd mesh sizes.
- **Roughness variation.** `MakeSmoothnessMap` breaks up how sharply light comes back, in broad soft
  patches, on everything that already gets a normal map. It can only ever dull: the walls' 0.85
  becomes a range of about 0.61-0.83.
  - **What to look at**: whether it reads as "used" or as "dirty". `WearFloor` (0.72) is the range and
    turning it toward 1.0 disables the effect without removing the texture.
  - The metallics are the risk - the map carries metallic in its red channel, so check something
    polished (the three escape objects) has not lost its shine.

- **Bounce light. THE BAKE WORKS NOW (2026-08-25) AND NOBODY HAS LOOKED AT IT.** All four scenes bake
  clean; what remains is entirely a judgement made by eye, and it is the one thing that decides
  whether any of this was worth it.
  - **Take the ambient DOWN, and do it before judging the result.** `SetupLighting` carries almost all
    of the room's light on flat Trilight ambient precisely because there was no bounce. That constant
    is now a second helping, so left alone a successful bake makes the building brighter and
    **FLATTER** - the opposite of the point. Start by halving the Trilight values
    (0.155 / 0.644 / 0.719) and compare a corner against a wall centre: the corner should now be
    visibly darker. If it is not, the ambient is still winning.
  - **The dial is back for this.** `Iteration Room` → play the calibration room → **TAB**
    (`Assets/Scripts/Dev/LightingTuner.cs`): ambient x3, fixture intensity/cone, both shadow biases,
    and the six surface numbers, all seeded from the live scene. Read the numbers off it and type
    them into `SceneBuilder` - it saves nothing on purpose. **It cannot show you bounce**: the
    fixtures are Mixed, so anything feeding the bake is live in its direct half and stale in its
    indirect one. Rebuild and RE-BAKE before believing a result.
  - **APV IS OFF AND THE INFRASTRUCTURE IS KEPT.** If it is retried, in this order: (1) make
    `CaptureMenuBackground` able to see probe data, or accept that the title screen will not match
    the game - everything else is unmeasurable until this is true; (2) answer why the walls take +2
    while the floor takes +101; (3) only then tune. `docs/gotchas.md` lists seven things already
    tried against (2), none of which worked.
  - **THE WALLS ARE LIT BY A CONSTANT, NOT BY THE LIGHTS.** Ambient at zero leaves them at 13 of 255;
    the four fixtures are downlights and give a wall only the grazing tail of the cone. Two answers
    have now been tried and reverted: **baked GI** (bounce follows the direct light, so it went to the
    floor) and **wall washers** (a point spot makes a blob, and a row of them is barred by the
    8-lights-per-object limit). Both are written up in `docs/gotchas.md`.
    **What has NOT been tried**: a light shaped like the thing it stands for. URP has no realtime area
    light, but a long thin *row* of very weak points along a cove, or an emissive strip with the
    ceiling fixtures thinned out to make room under the 8-light limit, would attack the evenness
    problem rather than the brightness one. **Evenness is the requirement** - the ambient constant is
    doing art direction, not just filling a hole.
  - **WHY DOES THE BOUNCE NOT REACH THE WALLS AND CEILING?** This is the open question the whole
    lighting pass ran into. With ambient at zero the walls measure **4-8 out of 255** and the ceiling
    the same, while the floor sits at 67 - so the bake lights what the downlights already light and
    almost nothing else. Ambient has been put back because a black building is unusable, but it is
    carrying a job that a working bake should be doing. Three suspects, none tested:
    - **Probe validity against 25mm panels.** Dilation is on and Virtual Offset moves a probe 1cm;
      neither may be enough for a wall built of thin plates with grooves between them.
    - **Reflection probes captured in an unlit room.** The walls are smoothness 0.85 and the notes
      already say gloss only works because of the probes - if `BakeReflectionProbes` renders before
      APV data applies, they are mirroring black.
    - **There may simply be little wall bounce to find.** Four downlights in a 8.75 x 10.5m room aim
      at the floor; the walls get a grazing cosine and the ceiling nothing at all.
    **Measure before changing anything** - `MenuBackground.png` is re-rendered by every build and can
    be sampled with a few lines of Python, which is how all of the numbers above were got.
  - **THE SHADOW ATLAS IS AT 4096 AND NOBODY HAS MEASURED THE COST** (2026-08-25). Four casters at
    2048 each was chosen for sharpness plus stability, and it is four times the shadow pixels the
    game drew the day before. This exact number was reduced to 2048 once already, after play reported
    "laggy everywhere" - the cause was believed to be full-screen passes, but nothing was measured
    then either. **Watch the frame rate; if it moved, this is the first thing to halve.**
  - **Shadows are the one thing it is honest about**, and there is a real complaint waiting for it:
    the player's shadow was removed after four attempts because it read as detached, and
    `docs/rendering-notes.md` names `m_ShadowDepthBias`/`m_ShadowNormalBias` as an untested suspect
    that "needs an eye, not a guess". This is that eye.
  - **Then decide whether the bake earns its cost**: ~4 minutes and about 15MB of cell data per full
    run, against a building whose whole complaint was that it reads as a whitebox.
  - Cycle1's baking set sits in `Assets/Settings/ProbeVolumes/` while the other three are in
    `Assets/Scenes/<Scene>/`. Cosmetic - Unity finds them either way - but worth tidying if the
    baked data is ever committed.

Also from the same pass, and unlooked-at:

- **MSAA is 2x, was 4x.** The player camera already runs SMAA, so 4x was buying the second half of a
  job already being done, at four samples of bandwidth per pixel. Watch the panel grooves - long
  straight dark edges are exactly what MSAA is for, and if they crawl in motion this is why.
- **Shadow resolution is back to 1024 per caster** (it was briefly 2048, for the player's shadow that
  no longer exists) and the atlas to 2048. Four casters, an exact fit.
- **`PlayerLookup.Occluded` no longer allocates.** It was `Physics.RaycastAll` - a fresh array per
  call, per candidate, per frame - which in a room like the chess board is a dozen allocations a frame
  for the collector to charge for later as a hitch.


## The ROOMS cast shadows now, and nobody has looked at those either (2026-08-24)

**The player's own shadow is gone** - removed after play, because four attempts in one day all came
out below the bar. `SceneBuilder.BuildPlayerBody` records what each one ruled out and what to fix
FIRST if it is ever restored; the short version is that the remaining suspects are not the caster and
not the silhouette, they are point lights standing in for area panels, and URP's default shadow bias.

What survived that removal is the half of the change that was never about the player. Until this day
exactly ONE light in the building cast anything - Room1's, and inside Room1 a fixed CORNER fixture -
so thirteen of fourteen rooms had no shadows at all and the fourteenth threw them sideways. Every
room now has a caster and `ShadowBudget` keeps it to the one nearest the player, which is very nearly
the one overhead, at the same cost the game already paid.

Nobody has looked at what that does for the ROOMS - the chess pieces, the cubes, the buckets, the
tree. Open:

- **Do room shadows help or just add noise?** This is the first time twelve chess pieces on a board,
  or a stack of cubes, have thrown anything. If they read badly the whole thing can go back to
  `castShadows: false` and cost nothing.
- **`maxCasters` is 1 and no longer has to be.** One was forced by the player's shadow multiplying
  into limbs; ordinary objects multiply into a soft pool instead. Raising it costs one shadow-map
  render per fixture.
- **`switchMargin` 0.8 is a guess** - it holds the current caster until a rival is clearly closer, so
  walking between two fixtures does not flick the room's shadows back and forth. Watch for a swing.
- **`m_ShadowDepthBias`/`m_ShadowNormalBias` are both at URP's default 1**, which peter-pans a contact
  point away from whatever is casting. Lowering it blind trades that for acne on the floor.
- **Nothing has been profiled**, though one caster is what the game had before any of this.

Also worth knowing: the probes bake with **nothing** casting, because `WireShadowBudget` switches
every fixture off before `BakeReflectionProbes` runs. That is what the bake effectively did before,
so it is not a regression - but a probe cubemap will never contain a shadow while it stays that way.


## The first-person body is GONE (2026-08-23, after play, by request)

Looking down shows the floor. `PlayerBody_View` is not built, and `FirstPersonHiddenBones`,
`PlayerBody.hideBones`, `PlayerBody.onlyWhileDriving` and `PlayerBodyViewSetback` went with it - all
four existed only to make that instance tolerable. The world and shadow bodies stay, so mirrors, CCTV
and past selves are unchanged.

- **This closes the knee question.** The hip-down cut it briefly had could never have been knee-down:
  hiding is a zeroed `localScale`, scale is inherited, and the shin is a child of the thigh - so there
  is no arrangement that keeps a child and drops its parent, on this rig or any other. A true cut
  needed a clip plane in the shader. Nothing needs one now.
- **If a first-person body ever comes back**, the thing that will bite again is the rig: it is
  IK-built and `Foot.L/R` hang off the skeleton root beside `Body`, NOT off the shins. The obvious
  edit - hide `LowerLeg` - leaves two feet standing on the floor by themselves.
- **The shadow body now stands at the true position** rather than 20cm back, because the legs it used
  to line up against are gone. It is the only evidence in the player's own view that they have a body,
  which makes it worth more attention than it has had - nobody has looked at it since it moved.

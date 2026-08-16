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
- ~~`tree_roots.glb` is 1.33 million triangles~~ **RESOLVED 2026-08-17**: swapped for
  `stylized_tree_stump`, which is **1,072 triangles** and 0.85MB. The fit is derived rather than
  written down, so the new asset sized and seated itself with no numbers changed.

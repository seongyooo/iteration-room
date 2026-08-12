# TODO

Work not yet done. Completed work lives in `git log`; the reasoning behind decisions lives in
`docs/`. Nothing in this file should accumulate as history - when an item is done, delete it.

---

## Next steps

1. **Ghost reset trigger** (spec §4.6, which leaves the trigger undecided). **Promoted to first by the 15-iteration clear: the player finishes this game sharing the building with FOURTEEN past selves** — not the "at least six" this item was written against, and not a number anybody has looked at. **This is the last real gap**, and ghost possession has made it heavier: a reset now takes the key back and **re-locks Room2**, and it un-tidies twelve chess pieces and six cubes, so the reset has a real cost for the first time. That may be exactly right (resetting should hurt) or fatal (nobody can afford it). Either way the two can no longer be designed apart.
   - Crowding is now a **performance** question as well as a legibility one — fourteen skinned, afterimaged figures is a number to profile, not to assume, and the WebGL build is the case that matters.
2. **Expand Room2: several keys, several locks.** The 2026-08-12 play-through found the thing this was speculative about: **Room2's popping does not accumulate** (the key is visible through its balloon, so it is spot-and-pop-one), and the three pins therefore have no consumer. More keys than one iteration has time to deliver is what converts popping into an errand iterations divide, and it is the same shape as the key mechanic that already works rather than a new one.
   - **Demoted from first**, because the argument that carried it there was content length and the 15-iteration clear has answered that. What is left is the narrower and still-true point: the pin supply is infrastructure with nothing consuming it.
   - Rides on systems that exist: `ItemRegistry` is already id→supply and id→socket, `KeyLock` gates on `hand.Holding(id)`, item ids are wire values, pops carry a `balloonId`, and `signals` has 28 of its 32 bits free.
   - **ROOM2'S WALLS ARE SPENT, so "the side doors to hang them on" no longer exists as written.** South is Room1, north is Room3 behind the GOLD key, west is the chess room behind the RED, east is the cube room behind the BLUE. More locks on Room2 means either restructuring its shell or hanging the extra keys on doors that are already there — several keys for ONE door (all of them turned before it opens) is the version that costs no geometry at all.
   - **The one real cost is the geometry**, if a new wall opening is what it comes to. `BuildRoomShell(..., Rect southCutout, Rect northCutout)` takes openings on Z only, and `BuildDoorPocketFill` is Z-oriented too, so doors in the X walls need the shell builder extended. Contained, but not a parameter change.
3. **Re-deploy to itch.io.** A build is ready at `Build/iteration-webgl.zip` and a new project page is being set up; the upload itself is manual (no butler on this machine).
4. **Rooms beyond Room4** — requested now, by itch.io players, so no longer speculative. `3 * RoomPitch` is taken by the ending room, so a new puzzle room goes at `4 * RoomPitch` and **Room4 moves out behind it**: Room4's south doorway stays, its north stays sealed, `DoorPocketFill_4` appears, and `EscapeTrigger` moves to the new last puzzle room's north threshold.
   - **Distance is not the constraint, EXCEPT on the final lap.** The 2026-08-12 clear ran at `walkSpeed` 2.5 with sprint never used, so the clock has slack in every ordinary iteration. But the last lap — three collections plus the walk to the console — came in with **8 seconds to spare**, and moving Room4 out behind a new room spends that margin directly. Measure the new lap before committing to the move; a `RewardPlinth` closer to the player's route, or sprint being worth using, are the levers if it does not fit.
   - `RecordedFrame.signals` is a `uint`, so **32 recorded interactables is a hard cap**; four are used.
   - The tree room (`Iteration — Future Ideas.md` §3) is the strongest candidate and is now unblocked: multiple axes needed the item pool, which shipped. Its threshold is a HEADCOUNT under the decision in `docs/decisions.md` — N simultaneous choppers cannot be met before iteration N — so pick that number against the **fifteen-iteration** run the game actually has, not against a guess.

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

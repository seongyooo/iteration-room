# TODO

Work not yet done. Completed work lives in `git log`; the reasoning behind decisions lives in
`docs/`. Nothing in this file should accumulate as history - when an item is done, delete it.

---

## Next steps

1. **Build the cycle boundary.** Design is settled and written up in `docs/cycle-design.md`; nothing is implemented. Finishing the game stops ending it — `CYCLE BROKEN`, then a grid-cell opening behind Room0's console onto a bed one floor down, gas without warning, and a fresh accumulation. **This ABSORBS the old "ghost reset trigger" item** (spec §4.6, which left the trigger undecided): a cycle boundary is the reset, with a reason the player understands, which is exactly what `docs/decisions.md` predicted chapters would buy.
   - Six hard blockers before it exists at all, listed in `cycle-design.md` §5a. The two that will cost most: `RunLoop` ends with `yield break` and has to become a loop over cycles, and **`CarryableItem.floorY` is an absolute world Y**, so nothing can be carried on a second storey until that is per-floor.
   - **Ghost crowding is still unprofiled**, and it stays worth measuring even though this design stops the count growing without bound: the tree room's whole payoff is N past selves working at once. Profile against a desktop target — WebGL is a prototype vehicle, not the release.
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

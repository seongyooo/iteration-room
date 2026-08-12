# TODO

Work not yet done. Completed work lives in `git log`; the reasoning behind decisions lives in
`docs/`. Nothing in this file should accumulate as history - when an item is done, delete it.

---

## Next steps

1. **Play-test the whole run, start to finish.** This is the first build that can be *finished* and nothing in it has been played. Three things code cannot settle: whether two facing pads read as "both of them, at once" without text; whether the ghosts converge in a way that makes the final walk-through feel earned rather than like waiting; and whether the ~5-iteration floor is satisfying or a grind. Known pressure point: whichever ghost bursts the key balloon does so at the same timestamp forever, and every Room3 setup iteration sits behind that.
2. **Ghost reset trigger** (spec §4.6, which leaves the trigger undecided). Ghosts accumulate forever and the run now needs at least six, so the room is genuinely crowded by the time it is solved. **This is the last real gap**, and ghost possession has just made it heavier: a reset now takes the key back and **re-locks Room2**, so the reset has a real cost for the first time. That may be exactly right (resetting should hurt) or fatal (nobody can afford it). Either way the two can no longer be designed apart.
3. **Re-deploy to itch.io.** A build is ready at `Build/iteration-webgl.zip` and a new project page is being set up; the upload itself is manual (no butler on this machine).
4. **Rooms beyond Room4** — added on request, not speculatively. `3 * RoomPitch` is taken by the ending room now, so a new puzzle room goes at `4 * RoomPitch` and **Room4 moves out behind it**: Room4's south doorway stays, its north stays sealed, `DoorPocketFill_4` appears, and `EscapeTrigger` moves to the new last puzzle room's north threshold. Two constraints first: an empty room costs only ~2.4s of the clock to cross, so **distance is not the binding constraint** — Room2's key toll is paid every iteration, so anything further out must be cheap in seconds and expensive in iterations, the way Room3 is. And `RecordedFrame.signals` is a `uint`, so **32 recorded interactables is a hard cap**; four are used.

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
3. **Number-lock and chess puzzles (future-ideas §7, §8) are knowledge, not accumulation.** Once the
   player knows the combination or the layout, no iteration reduces the work and ghosts cannot help —
   the one puzzle shape where the core rule stops applying. §7 also contradicts §14, which asks for no
   new operations before the final escape. Decide before either is built.
4. **Timed chains (future-ideas §4) fight `signals` semantics.** A ghost advances by elapsed time and
   can skip several recorded frames in one tick, which is why press-type interactables stretch their
   pulse. Narrow timing windows are exactly where that bites. Plain simultaneity is safe; sequencing
   with delays needs its own design pass.

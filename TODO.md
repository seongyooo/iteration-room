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

2. **The drawer reads as a slab sliding out of a solid box.** `nightstand.glb` bakes its whole body into one mesh, so `BuildNightstandDrawer` bolts a generated drawer onto the front face — there is no recess for it to come out of. Fix either way: build the nightstand procedurally (it is a box with a drawer; the room is already procedural everywhere else), or source a model with a real drawer node. **Sourcing is the smaller job but adds a dependency on someone else's topology; building it is more work and matches how the rest of the room is made.**
3. **The pin's left-click prompt should retire after N swings**, not persist forever. Note this is a *partial* return of the retiring that play-testing removed, and the distinction is the point: "shown once" did not teach, but "shown until the player has actually done it several times" both teaches and stops nagging. Count the action, not the display — same rule as the old `interactRetired`.
4. **Shift to crouch, Ctrl to sprint.** ⚠️ This is **inverted from the near-universal convention** (shift=sprint, ctrl=crouch), so expect testers to fight it — flagging it now so the decision is deliberate rather than discovered in a play-test. Both also interact with things already tuned: sprint changes the 7.7m/1.7s margin the pad-to-door close is checked against (see Room1), and crouch changes the eye height the near-clip corner analysis assumed.

## Pin supply — mitigation for the pop gate

Popping now requires holding the pin, for ghosts as well as the player (shipped 2026-08-11, costs
accepted — see `docs/decisions.md`). With **one** pin that means at most one entity in the room can
pop at a time, and Room2's accumulation does not survive it.

The fix is a supply, not a rule change:

1. Turn the pin from one object into a small supply — `ItemRegistry` resolving `"Tool"` to a free
   instance from a pool, and the drawer visibly holding several.
2. Nothing else. The gate is already in place and becomes free once every ghost can have one.

Reach for this if play-testing says the room clears too slowly. Revert instead with
`GhostReplayer.requirePopTool = false`.

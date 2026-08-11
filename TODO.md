# TODO

Work not yet done. Completed work lives in `git log`; the reasoning behind decisions lives in
`docs/`. Nothing in this file should accumulate as history - when an item is done, delete it.

---

## Next steps

1. **Play-test the whole run, start to finish.** This is the first build that can be *finished* and nothing in it has been played. Three things code cannot settle: whether four pads read as "all of them, at once" without text; whether the ghosts converge in a way that makes the final walk-through feel earned rather than like waiting; and whether the ~7-iteration floor is satisfying or a grind. Known pressure point: whichever ghost bursts the key balloon does so at the same timestamp forever, and every Room3 setup iteration sits behind that.
2. **Ghost reset trigger** (spec §4.6, which leaves the trigger undecided). Ghosts accumulate forever and the run now needs at least six, so the room is genuinely crowded by the time it is solved. **This is the last real gap**, and ghost possession has just made it heavier: a reset now takes the key back and **re-locks Room2**, so the reset has a real cost for the first time. That may be exactly right (resetting should hurt) or fatal (nobody can afford it). Either way the two can no longer be designed apart.
3. **Re-deploy to itch.io.** Three scene-affecting passes have landed since the shipped build.
4. **Rooms beyond Room3** — added on request, not speculatively. Mechanically: `BuildRoomShell`/`BuildCeilingLights`/`BuildReflectionProbe` at `3 * RoomPitch`, Room3's `capFarSide` going false, **and `EscapeTrigger` moving to whatever the new last room is**. Two constraints first: an empty room costs only ~2.4s of the clock to cross, so **distance is not the binding constraint** — Room2's key toll is paid every iteration, so anything further out must be cheap in seconds and expensive in iterations, the way Room3 is. And `RecordedFrame.signals` is a `uint`, so **32 recorded interactables is a hard cap**; six are used.

## Queued fixes

1. **The drawer reads as a slab sliding out of a solid box.** `nightstand.glb` bakes its whole body into one mesh, so `BuildNightstandDrawer` bolts a generated drawer onto the front face — there is no recess for it to come out of. Fix either way: build the nightstand procedurally (it is a box with a drawer; the room is already procedural everywhere else), or source a model with a real drawer node. **Sourcing is the smaller job but adds a dependency on someone else's topology; building it is more work and matches how the rest of the room is made.**
2. **The pin's left-click prompt should retire after N swings**, not persist forever. Note this is a *partial* return of the retiring that play-testing removed, and the distinction is the point: "shown once" did not teach, but "shown until the player has actually done it several times" both teaches and stops nagging. Count the action, not the display — same rule as the old `interactRetired`.
3. **Teach Tab.** Tab now cycles what is in the hand and nothing in the game mentions it - which is the `DoorButton` failure in waiting (testers could not find a control the room never explained). Decide between a prompt and a wall message.
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

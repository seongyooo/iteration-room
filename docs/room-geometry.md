# Room shells and geometry

Shell construction, the wall grid, and doors. Every constant here was measured or derived; `SceneBuilder` is the source of truth for all of it.

---

## Room shells and geometry

**Seven shells**, all from `BuildRoomShell` at different Z centres, one straight corridor: `Room1`
(Z=0), `Room2` (Z=`RoomPitch`=10.85), `Room2West` (Z=`2·RoomPitch`=21.7), `Room2East`
(Z=`3·RoomPitch`=32.55), `Room3` (Z=`4·RoomPitch`=43.4), `Room4` (Z=`5·RoomPitch`=54.25), and a
sealed `CalibrationRoom` at `-2·RoomPitch` used only for the sensitivity step. Room2West and
Room2East used to be a pair of side rooms turned ninety degrees off Room2's east and west walls,
each reached through its own coloured door on Room2 itself; they are on the chain now, same
orientation as every other room, and the coloured doors sit at the three joins between Room2 and
Room3 instead (see `docs/puzzle-design.md` for why). **Room4 is the ending** — it has a doorway
south and none north, so it is the end of the building, and every door pocket between it and Room1
is uncapped, one per join, five in total. `RoomPitch` = `RoomDepth 10.5 + 2×WallDepth 0.125 +
DoorPocketDepth 0.1`.

- Adjacent rooms share a divider: one room's north wall and its neighbour's south wall sit back to back with the same doorway cut out of the panelling, the backing **and** the collision. Scene paths are `Room/Room1/...`; furniture and pads hang off `Room/` directly.
- **Floor/ceiling slabs span the full `RoomPitch`**, not the interior, so adjacent floors meet exactly under the divider. Sized to the interior they leave a gap at the threshold and the player drops through it. They are also pushed **out by half their thickness** so their inner faces sit on the room bounds.
- Room bounds: X ±4.375 (width 8.75), Z ±5.25 about centre (depth 10.5), height **5.408**. Door, bed and spawn share the X=0 axis. `BuildRoomShell` derives minZ/maxZ as `zCenter ± depth/2` — don't hardcode them.
- **`GridCellHeight` is derived from the height** (`RoomHeight / GridRows`), not the other way round. At a hardcoded 1m cell the top row was a half cell and the panelling ran off cut in half at the ceiling. To change cell height, change **`GridRows`**.
- The height is an odd number because a **golden-ratio pass** left it there. Kept rather than rounded because the fixture intensity and every reflection probe are tuned against it.

### The wall grid

**Cells are 1.75 × 1.3519m**, 5 columns on end walls, 6 on sides, **4 rows** — 88 panels per room before doorway cutouts, **458 in the scene**, of which **370** (Rooms 1–4, not the calibration room) are driven by `WallPanelDisplay`.

- Two hard constraints: a cell's *width* must divide both 8.75 and 10.5 exactly or the last column overshoots (`BuildPanelWall` lays fixed steps after rounding the column count); a cell's *height* must divide `RoomHeight` exactly, now enforced by construction.
- **The cells were briefly exactly φ:1**, which is where `RoomHeight`'s 5.4078 comes from. An exact φ cell and a fixed room *cannot* both hold — both constraints demand a rational ratio and φ is irrational. If φ is ever wanted back: rebuild the floor plan around it (8.75 × 10.5 → **8.09 × 9.71**, moving all furniture), or approximate with **0.583 × 0.357** (+0.95% off, indistinguishable) — but that takes the panel count from 110 to 924 per room, and `WallPanelDisplay` drives every panel through its own property block.
- Groove width `GridLineThickness` **0.05**, visible face 1.70 × 1.3019m. Taller panels need more groove to hold the same visual weight; shrink the cells much and it must come back down or the panels turn to slivers (pieces under 0.02m are skipped).
- **There is no grid texture.** The grid is geometry: white panel cubes over a near-black backing (`GrooveDark`), so a groove shows the dark backing. The old textured materials went when the backing had to be split to cut the doorway — a split slab can't carry a tiled texture, since each piece's UVs run 0..1 across *itself*.
- **Neither the panels nor the backing carry colliders.** Collision is a separate set of invisible boxes built by subtracting the doorway from the wall rect. A collider on the backing would be `GrooveDepth` too far back (the camera's near plane then clips through the wall) and would seal the doorway shut.
- **Backing and collision overrun both ends of a wall by `WallDepth`** (`structureRect`), so adjacent walls interpenetrate at the corners. Sized to the interior exactly, two perpendicular walls meet along a *line* and leave a `WallDepth`-square column, full room height, that neither covers — invisible to raycasts but producing pinhole cracks at grazing angles. **This is why thickening the walls is the wrong fix for see-through: the void is `WallDepth` squared, so a thicker wall makes it bigger.** The panel grid stays at interior width, so the visible layout is unchanged.
- `GrooveDepth` 0.025 is a balance point: deeper catches more occlusion, but the panels' white side faces then wash the seam out at grazing angles.

### Doors

- The door is a **natural size (`DoorWidth` 1.3 × `DoorHeight` 2.5) and deliberately NOT grid-snapped** — forced to exactly one cell wide it read as a missing panel rather than a doorway. `BuildShell` passes a doorway `Rect` to `BuildPanelWall` and `SubtractRect` splits any overlapping panel into up to four surrounding pieces.
- The doorway is cut out of the **backing slabs as well as the panels**. With the backing solid the opening was a dead-end recess — you could walk "through" but you were looking at wall.
- The door **slides sideways into a pocket** (`openLocalOffset` = +X by `DoorWidth`), tucking inside the wall build-up. Room2's `KeyLock` plate is on the **left** — on the right it ends up buried behind the open slab.
- **`BuildDoorPocketFill` caps the pocket** everywhere except the volume the slab sweeps (plus 0.01 clearance so faces aren't coincident). Left open, the pocket ran the full width of the building and **exited to the sky at both ends**. It carries no colliders — the walls either side hold the collision.
  - `capFarSide: true` adds `FarCap`, which **keeps its collider**: it is the end of the world and the player must not walk out. Exactly one exists at a time, on the last room.
- `BuildDoorShell` (slab, pocket, lamp) is shared; `BuildPadDoor` / `BuildKeyDoor` differ only in what unlocks them. The shell's root carries the room's Z offset so measurements inside stay in the frame they were tuned in.
- **The player camera's near clip is 0.05, not Unity's 0.3** (far 100, not 1000). At 0.3 you could stand against a wall, turn sideways and see through to the far side. What pokes through is the near plane's **corner**: at fov 60 it reaches **0.463m** (0.532m ultrawide) while the CharacterController only stops the camera 0.220m away, against a total wall build-up of 0.125m. At 0.05 the corner reaches 0.077m — 2.8× margin at 16:9, 1.9× at 32:9. **Re-check this if the fov is ever raised**; the corner scales with `tan(fov/2)`.

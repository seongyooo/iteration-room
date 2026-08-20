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

## Cycle 3's first room: three gates and a corridor (2026-08-21)

Room3-1 has **no door in it**. Three of its walls carry a **gate** — a piece of the wall that leaves
while a floor pad is held — and the fourth, north, is the mouth of a corridor whose entire length is
solid until a pad lifts it.

**The pad is in room3-1 and the opening is in its wall, so nobody goes through their own.** Step off
to walk through and it shuts behind you. Every room beyond is reachable only while a PAST SELF stands
on the pad — the cycle's premise stated in its first room, the way Room3's two pads state cycle 1's.
That is also why the pads are in the cycle's `signals` array: a `FloorButton` outside it is a pad no
ghost ever stands on, which would make every opening in the room dead.

### Shut, it is wall

This is the specification, not a finish. Leaves and the corridor's face alike are built the way
`BuildPanelWall` builds panels — same material, same 0.025 thickness, same depth off the face, same
groove inset — and are **split into one quad per grid cell** so the row line across an opening lands
where the wall's own does. No frame, no reveal, no indicator lamp.

Four things had to be got right, and three of them were got wrong first:

- **A leaf carries its own backing.** The cutout takes the wall's backing away along with its panels,
  so the 0.05 groove around a leaf had nothing behind it and the sightline ran *groove, cavity, the
  neighbour's own cutout, the room next door*. Play saw straight through a shut gate. **A panel is
  not a wall; a panel plus the dark plate behind its grooves is.**
- **Both sides of a shared wall get leaves.** A gate built on one face only leaves the OTHER room
  looking at a bare hole. The two calls take the same pads, and the leaves fit because each retracts
  to its own side of the 0.1m cavity — near leaf 0.129 to 0.169 back from its face, far leaf the
  mirror of that, 12mm between them. `withCavityLiner` belongs to the first call only.
- **The leaves are baked into the reflection probes** (`WallWhileShut`), against the general rule that
  movers are not. Left out, a rectangle is missing from the wall in every reflection, and at 0.85
  smoothness a wall is almost entirely what it reflects — a permanent mark saying *the door is here*.
- **It pushes in before it slides.** A panel flush with its neighbours has nowhere to go sideways.
  `Door.pushInOffset` runs that first phase (zero on every ordinary door). **A single diagonal move
  does not work**: the gap between panels is one groove, 0.05, and a linear diagonal has receded only
  0.002 by the time it has used that up — it clips its neighbour on the way out.

### Whole cells, always centred

`GateSpan` places every opening and both rooms sharing a wall call it, so their cutouts cannot
disagree. How many cells depends on whether the wall's count is even:

| wall | width | cells | opening | leaves |
| --- | --- | --- | --- | --- |
| east / west | 10.5 | 6 | the middle **pair**, 3.5 wide | 2, opening to their own sides |
| south | 8.75 | 5 | the middle **cell**, 1.75 wide | 1, sliding one cell to one side |
| north | 8.75 | 5 | the middle **cell** — the corridor mouth | none; the corridor's own block |

**A leaf is never part of a cell.** A two-cell gate on the odd wall was built first and could only sit
half a cell off centre, which read as a mistake; splitting a single cell into two half-width leaves
would put a vertical groove down the middle of a cell where the wall has none, which is precisely
what gives a shut gate away.

`RoomPitchX` is the east-west counterpart to `RoomPitch` — the term that is not the room's own size is
identical, so two walls meet with the same build-up and the same cavity on any side.

### The north corridor, and the block that fills it

22.75m long and one cell wide, from room3-1's north face to room3-2N's. **The whole of it is one solid
block**, and holding the pad lifts the entire thing straight up into a shaft of its own height above.
What the player walks through is the hole it leaves; what is over their head the whole way is the
block. Letting go does not close a door — it fills the corridor back in, everywhere at once, and there
is no safe corner inside it. See `CrushingBarrier`.

Its south end carries the wall's grid on the wall's plane, so with the pad untouched room3-1 has four
walls and no corridor.

**Every dimension is an exact multiple of the grid, and the first version's were not.**
`BuildPanelWall` divides a wall by its ROUNDED cell count, so a 22.05m corridor came out in cells of
1.696 against the building's 1.75 — a different rhythm, visible down its length — and corridor plus
shaft came to 5.5076, which is four rows plus a 0.1m sliver. Thirteen cells and four rows now, with
the room beyond placed FROM the corridor rather than the corridor measured between the rooms, because
only one of the two can be the multiple.

**Room3-2N is offset half a cell.** Its south wall has ten cells where room3-1's north has five, so
the building's axis falls on a cell boundary there and a centred mouth straddled two cells. Moving the
ROOM by half a cell puts that axis on a cell's middle; a full cell would have changed nothing, because
the parity is what matters and not the distance.

### Nothing may be exactly the size of the hole it sits in

Two flickers came from coplanar faces and one black artefact from an overrun, and all three are the
same lesson:

- The block was exactly 1.75 wide, so its sides shared a plane with the corridor's wall panels, and
  its base shared one with the floor. It is 2mm narrower now and sunk 20mm.
- Its face backing is only 1mm narrower — wider clearance leaves a slot the lit wall shows through,
  which reads as a black border with a bright edge.
- **The corridor's side walls overran into the room.** `BuildPanelWall` extends its backing and
  collision `WallDepth` past each end so perpendicular walls interpenetrate at corners. The corridor
  runs along Z, so that overrun went 12.5cm INTO room3-1: two near-black slabs, full height, standing
  proud of the north wall and reading on camera as thick black pillars with visible side faces. The
  walls are inset by `WallDepth` at each end now, so each overrun lands inside a room's own wall
  build-up. See `docs/gotchas.md`.

Every opening is covered by `AssertWalkable`, probing through the **opening's** centre — not the
wall's, which on the five-cell walls is not the same point. `CrushingBarrier` is skipped by that check
the way `Door` is: the corridor being solid is the room, not a fault.

### Room3-2N

Twice as wide, twice as deep and **three times as tall** as a standard shell — 17.5 x 21 x 16.2234,
which is ten cells, twelve cells and twelve rows. The grid cell does not change, only the count, so the
panelling reads as the same wall continued. `BuildBigRoom` rather than parameters on `BuildEmptyRoom`,
because that one lays its floor across a whole `RoomPitch` so neighbours meet under their shared
divider — a rule about the chain, not about this room.

**Its lighting is derived and has not been looked at.** `BuildCeilingLights` is tuned for a 5.4m
ceiling; at 16.2 its range does not reach the floor and inverse square says the floor gets a ninth of
the light. `BuildTallRoomLights` spreads a 3x3 grid, sizes the range to the diagonal and squares the
intensity by the height ratio — but 10.5 was found by eye in the first place, and this is a far bigger
jump than the one it was re-derived across. Expect to retune.

### Climbing it: steps are walked, not jumped

A grid row is 1.3519 and a panel's top lands at 1.327; the jump clears 0.90. Raising the jump to 7.9
(1.56m) was tried and play called it awkward — a person who jumps a metre and a half floats, and every
room in the game would inherit it to solve a problem in one of them.

So the step came down instead. **Half a row, 0.651 to its top, walked up on the controller's
`stepOffset`** (`SceneBuilder.PlayerStepOffset`, 0.72). Stairs you walk up read as stairs. And the
number is deliberately kept **under the 0.90m jump**, so nothing anywhere becomes reachable that a jump
could not already reach — the guarantee a taller jump could not make. The two existing things that lean
on `stepOffset` both still hold: the chess board is thinner than this and is still walked over, and
balloons are excluded from the controller entirely.

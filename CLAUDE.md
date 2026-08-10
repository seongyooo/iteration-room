# Iteration Room

Unity prototype of a first-person time-loop puzzle game (ref: 2016 short film "Iteration 1"). Spec: `iteration-game-spec.md`. `room_layout_sample.png` is a film still used as the furniture-placement reference; `iteration_room_layout.png` is a layout sketch.

**Current state**: complete end to end — three rooms, a win state, an ending, a title screen and a sensitivity calibration step. The core loop and Rooms 1–2 are play-tested. **Room3, the ending and the wall messages are built clean but the full run has never been played through** (next steps §1). Shipped to itch.io once; three scene-affecting passes have landed since.

**This file records decisions and their reasons.** For *what changed when*, read `git log` — it is not repeated here.

**Scenes are build output.** `Assets/Scenes/*.unity` is git-ignored; `SceneBuilder` is the source of truth and Unity re-randomises every fileID on rebuild. A fresh clone has no scenes until you run the build. (The `.meta` files *are* tracked: they pin the scene GUIDs that `EditorBuildSettings.asset` stores by value.)

## Stack and build

- **Unity 6000.5.7f1, URP** (`com.unity.render-pipelines.universal` 17.5.0). Pipeline assets in `Assets/Settings/` (`IterationURP` + `IterationRenderer` + `IterationVolume`), wired into both `GraphicsSettings.defaultRenderPipeline` and `QualitySettings.renderPipeline`. It started on Built-in and moved because Built-in with no post-processing reads flat no matter what the materials do.
  - Materials must use `Universal Render Pipeline/Lit`, **not** `Standard` — URP has no Standard shader and a material left on it renders magenta. `SceneBuilder.OpaqueShader()` resolves whichever exists; `SetSmoothness()` handles `_Glossiness` vs `_Smoothness`.
- **Unity MCP** ([CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp)), needs `uv`/`uvx`. **MCP servers only load at session start** — if its tools are missing, start a new session rather than re-registering.
- **There is no manual scene editing.** Both scenes are assembled by `Assets/Editor/SceneBuilder.cs`. `MainMenu` is build index 0 (where a standalone player opens); `IterationRoom` is index 1. Pressing Play in the Editor runs whichever is open.
- Rebuild headlessly:
  ```
  "C:\Program Files\Unity\Hub\Editor\6000.5.7f1\Editor\Unity.exe" -batchmode -nographics -projectPath "C:\Users\seonl\Desktop\c\2026\summer\Iteration" -executeMethod IterationRoom.EditorTools.SceneBuilder.Build -quit -logFile <log>
  ```
  Check the log for `error CS` and exceptions; `[SceneBuilder] IterationRoom scene built at ...` plus exit code 0 means it worked. Ignore `[Licensing::Client] Error: HandshakeResponse...` retries.
- To play it, launch the Editor binary directly (`Unity.exe -projectPath ...`) — Unity Hub's project picker is flaky on this machine ("프로젝트를 찾을 수 없습니다" on a valid project).
- **A batchmode build fails outright if the Editor has the project open.** Drive the build through MCP instead: `execute_menu_item("Iteration Room/Build Whitebox Scene")`, then `read_console`. **Clear the console first** — stale entries look exactly like fresh failures. If the Editor is in Play mode, `manage_editor(action:"stop")` first; `NewScene` throws during play.
- **Player Settings → Run In Background must stay ON.** Without it play mode stops ticking when the Editor loses focus, so any MCP-driven test captures a stale frame and looks like the change did nothing. `Build()` sets it, since setting it during play mode does not persist.
- Prefer MCP for incremental visual tweaks; keep `SceneBuilder.cs` authoritative for anything structural.

## Room shells and geometry

**Four shells**, all from `BuildRoomShell` at different Z centres: `Room1` (Z=0), `Room2` (Z=`RoomPitch`=10.85), `Room3` (Z=`2·RoomPitch`=21.7), and a sealed `CalibrationRoom` at `-2·RoomPitch` used only for the sensitivity step. `RoomPitch` = `RoomDepth 10.5 + 2×WallDepth 0.125 + DoorPocketDepth 0.1`.

- Adjacent rooms share a divider: one room's north wall and its neighbour's south wall sit back to back with the same doorway cut out of the panelling, the backing **and** the collision. Scene paths are `Room/Room1/...`; furniture and pads hang off `Room/` directly.
- **Floor/ceiling slabs span the full `RoomPitch`**, not the interior, so adjacent floors meet exactly under the divider. Sized to the interior they leave a gap at the threshold and the player drops through it. They are also pushed **out by half their thickness** so their inner faces sit on the room bounds.
- Room bounds: X ±4.375 (width 8.75), Z ±5.25 about centre (depth 10.5), height **5.408**. Door, bed and spawn share the X=0 axis. `BuildRoomShell` derives minZ/maxZ as `zCenter ± depth/2` — don't hardcode them.
- **`GridCellHeight` is derived from the height** (`RoomHeight / GridRows`), not the other way round. At a hardcoded 1m cell the top row was a half cell and the panelling ran off cut in half at the ceiling. To change cell height, change **`GridRows`**.
- The height is an odd number because a **golden-ratio pass** left it there. Kept rather than rounded because the fixture intensity and every reflection probe are tuned against it.

### The wall grid

**Cells are 1.75 × 1.3519m**, 5 columns on end walls, 6 on sides, **4 rows** — 88 panels per room, **367 in the scene**.

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

## The puzzle

### Room1 — the pad and the door

Bed at (0, 0.7) (`messy_bed.glb`, head toward the door); nightstand at (-0.95, 1.35); floor pad at (2.8, -1.75); door centred on the north wall. Positions follow `room_layout_sample.png`.

- **Bed spawn is at (0, -0.7) facing 180°** — away from the bed, down the open room, as if you have just got up. It used to be at Z=-1.75, which left the bed nowhere near the player and failed to read as waking up *in* it. **The player cannot be spawned in the bed**: `PlaceModel` gives it a full-volume `BoxCollider` (X ±0.63, Y 0..0.89, Z -0.33..1.73) and a CharacterController placed inside gets shoved out on frame one. With the foot at Z=-0.33 and radius 0.3, **Z=-0.7 is as close as it can stand**.
  - Measured landmarks for re-tuning: mattress top Y=0.59, pillow centre (0, 0.59, 1.47), nightstand X -1.23..-0.67 / Z 1.16..1.54 — so the bed's **west** side is blocked, east is free.
  - The bed ships with **two pillows**, both off centre; `UseSinglePillow` hides one and slides the survivor onto X=0. They are separate glb nodes, so this is a transform tweak — a bed that baked its pillows into the bedding mesh could not be fixed this way.
- **The door has no control beside it and tracks its pad instead.** Step on, it opens across the room; step off, it shuts.
  - **Tracking rather than latching is load-bearing.** A door that latched open when the pad was first touched is solvable in one iteration — step on, stroll through — which deletes the premise the whole game rests on. Tracking also *teaches* the rule: the player watches the door open and shut with their own feet in the first seconds, and learns at that moment why one person cannot do both jobs at once.
  - **`DoorButton` is gone** (in git). A play-test found nobody could locate it: testers held the pad for an iteration, walked to the door in the next and expected it to open. They were right — the button was a second gesture the room never explained and it bought the puzzle nothing. Removing it also makes a ghost's contribution **visible from anywhere in the room**: the plate clunks, the lamp goes green and the door slides open in front of you. The wall beside the door is now bare, which is part of the point.
  - **The door refuses to shut on the player** (`doorwayClearance` 1.2m, tested against the *closed* slab's world position cached in `Awake`, since the door root sits at the room centre). A `CharacterController` is not pushed by a moving transform, so a slab closing through one leaves the player inside it. The **`openAmount > 0f` guard is not a shortcut**: without it, walking up to a shut door would hold it open forever. With it the reprieve only extends an already-open door, and cannot be used to reach one — pad to doorway is **7.7m (1.7s at `moveSpeed` 4.5) against a 1.0s close**. Re-check that margin if the pad moves or `openDuration` drops. It doubles as anti-frustration: the intended solve no longer fails by half a second when a ghost's timing is short.
  - The open sound fires on the transition out of fully-shut, not from `Open()`. Closing is silent — that is the pad being released, which `FloorButton` already announces.

### Room2 — drawer, pin, balloons, key

**Deliberately not another pad-and-button.** The chain: the nightstand **drawer** in Room1 (E) holds a **pin tool** (E, held in hand) → walking into Room2 drops **70 translucent balloons** → swinging the tool (left mouse) bursts them → one holds the **key** → the key opens Room2's north **key door** (E at the lock).

- **A pop is recorded by *identity*, never by position** (`RecordedTimeline.PopEvent` carries a balloon id). This is the decision the whole thing rests on. Recording "the player swung here" and replaying it against physics that has moved everything makes a ghost's contribution luck; recording "burst balloon 17" means the ghost bursts balloon 17 wherever it has ended up, so progress accumulates exactly.
  - Accepted cost: a ghost can swing at air while a balloon bursts across the room. Tolerable because **ghosts have no colliders**, so only the living player scatters the field differently, and nobody eye-tracks one balloon among seventy.
  - Pops are an **event list, not more bits in `RecordedFrame.signals`** — there are more balloons than the mask is wide, and a signal is a *level* where a pop is an instant with an identity. `GhostReplayer.DrainPops` fires every event whose time has passed, so it survives a ghost skipping frames. **It drains before the end-of-timeline check** — after it, one long frame would retire a ghost with its closing pops unfired and those balloons would stay up for good.
- **The balloons are translucent and the key is visible inside the one that holds it.** This is what makes the search a matter of looking rather than of bursting seventy and hoping — luck in the first version, skill now. `MakeTranslucentMaterial` is the URP transparent set-up on **Lit**, not Unlit, because unlike a ghost these take the ceiling lights; the blend modes **and** the `_SURFACE_TYPE_TRANSPARENT` keyword are both required or the alpha does nothing. The in-balloon key is a **child of the balloon**, so it vanishes the instant the balloon bursts, and is a **darker** material than the loose key — at brass it washes out through the pink.
- **Balloon fall is hand-rolled, not gravity.** `useGravity` off, `fallAcceleration` 2.2 m/s² applied in `FixedUpdate`, damping 1.1, bouncy `PhysicsMaterial` (0.55, `bounceCombine` **Maximum** since floor and walls have none). Unity has no per-body gravity scale, and damping 9.81 down to a drift takes so much damping the balloons stop bouncing and read as underwater. **Low damping plus a small pull is light *and* lively; heavy damping is only slow.** Terminal velocity 2.0 m/s.
- **The player never collides with a balloon.** They are on their own layer (`EnsureLayer("Balloon")`, index 8) which the `CharacterController` excludes via `excludeLayers`; they stay solid to each other, the floor and the walls. Pushing is an explicit `OverlapCapsule` in `FirstPersonController.PushOverlapping`.
  - **This is the fix for "standing on a balloon lifts you into the air", chosen over the one-line version on purpose.** The old push took its direction from `hit.moveDirection`; standing still the controller's only motion is gravity `(0,-1,0)`, so both horizontal components were zero and the token lift normalised into a **pure upward shove**, re-applied every frame — the balloon rose, `stepOffset` walked the player onto it, and you rode it to the ceiling. Guarding the lift fixes that instance; removing the contact removes the class.
  - **Pushed by speed, not force, and horizontally only.** A capped `VelocityChange` at the centre of mass (off-centre mostly spins a sphere), along the horizontal offset from the controller's axis — geometry, not direction of travel, so it is well defined however the player moves. An early 0.55 impulse on a 0.04kg balloon was an 11 m/s kick off a brushed shin. The cap also means walking through a crowd nudges each balloon once rather than accelerating it every frame.
  - The sweep is skipped with no horizontal input, and `horizontalMove` is **cleared when `ControlEnabled` goes false** — the wake-up takes control mid-stride and a stale vector would blow balloons apart with the player's eyes shut.
- **The balloons are a pool, never instantiated at runtime**, which is what makes an id mean the same thing sixty seconds later. Spawn points and the key balloon (**index 52**) come from one fixed seed (`fieldSeed` 20260810) — reseed per iteration and the room is a different room each time, with nothing to learn.
  - `BalloonField` **parks the pool during `Build()`** (`ComputeSpawnPoints` is public for this). Built objects sit at their parent's origin, so left to `Awake` the saved scene stores seventy balloons heaped beside the bed.
  - `Balloon` resolves components through **`EnsureRefs()` rather than `Awake`**: Awake order is undefined and `BalloonField` resets the pool from its own `Awake`, so references would still be null and the reset would silently do nothing.
- **The drop triggers on anyone walking in — player *or* ghost** (`TriggerIfInside`). Ghosts are deterministic, so once one has been through, the balloons come down at the same instant every iteration, which keeps later recordings aligned with the field they were made against.
- **Carrying is world state and the loop rewinds it** (`PlayerHand.ReturnAll`, called *before* `BalloonField.ResetField` so the key is handed back and then hidden, not the other way round). Leave the tool in hand across a reset and the trip to the drawer stops costing anything.
- **The drawer is generated geometry.** `nightstand.glb` bakes its body into one mesh, so there is no drawer node. `BuildNightstandDrawer` measures that renderer's bounds (**min (-1.23, 0, 1.16), max (-0.67, 0.59, 1.52)**, logged every build) and sizes a drawer onto the **-Z** face, toward the foot of the bed where the player wakes up looking.
- The tool gates on `Drawer.**IsFullyOpen**`, not `IsOpen` — otherwise one E press both opens the drawer and empties it, with script execution order deciding whether it worked, which reads as the item being unobtainable half the time.
- **The key lock is deliberately NOT a `GhostInteractable`, and this is the most important rule in the project.** The room runs on *record the attempt, re-evaluate the condition* — which only holds while the condition re-evaluated is **the one that actually enabled the action**. For the key door that was the player **holding the key**. A version shipped briefly that re-evaluated a ghost's unlock against `BalloonField.KeyRevealed` (the key's balloon having burst) — a different and much weaker fact — and it quietly let a ghost do the one thing a ghost cannot: carry something. `KeyRevealed` was removed with it.
- **Inserting the key is literal.** E at the lock calls `PlayerHand.Surrender`, which drops it from the carried set (so the HUD loses the icon) but **keeps it in `taken`** so `ReturnAll` still rewinds it; `CarryableItem.InsertInto` parents it to a `KeySocket` and makes it visible. "A ghost cannot do this" becomes something you watch happen rather than a rule in a file.
- **The key does not stay in the lock across the loop.** Leaving it as un-rewound world state was considered and rejected. The consequence is live and constrains everything downstream: **Room2's exit costs the *player* a key retrieval in every iteration that goes past it, and that toll never accumulates the way Room1's pad does.**
- **The lock's face is a keyhole, not a key** (`BuildKeyholeSlot` — a round seat and a slot, flat decals rather than a bored hole, which would show the wall behind). An etched key was right while nothing could be put in it; with a real key in the socket, an inserted key over an engraved one reads as two keys.
- `DoorIndicator` takes an optional `keyLock` so this lamp reports "you are carrying the key" the way Room1's reports "the pad is held".

### Room3 — four pads, and the way out

**Four floor pads at (±3.2, 18.7) and (±3.2, 24.7); `Door3` needs ALL FOUR held at once.** One person cannot stand in four places, so it takes **four past selves overlapping in time** — the thing Room1 does not ask for. Room1 proves a past self can do a job; Room3 asks you to make four of them do it *simultaneously*.

- **It is the plainest room of the three — no items, nothing to search or carry — and that follows from Room2, not from laziness.** Room2's key toll is paid every iteration, so a second expensive room behind it would be unreachable rather than hard. What Room3 costs is **iterations**, not seconds: one per pad, plus one to walk through. Each visit is "walk in, stand on a pad".
- The pads are a **rectangle, not a scatter**. Four things in a regular grid read as one set at a glance — the player must never hunt for the fourth — and the symmetry says *all of these*, where an irregular arrangement invites guessing that a subset might do. All four sit off the centre line, so the straight walk between the doors steps on none.
- **`FloorButton.AllActive(pads)` is the rule and lives in one place**, called by both `Door` and `DoorIndicator`, so the lamp can never say "go" on a rule the door does not use. A null or **empty** array is deliberately *not* active — a door wired to no pads should stay shut, where "all zero are held" would vacuously mean open.
- The lamp does more work here than anywhere: at Room1's door red means nobody is on the pad; here it is the only way to tell **three** past selves from **four**.
- Expect the player to reach the door **before** the ghosts reach their pads — each iteration gets faster as the rooms ahead get solved, so the recordings are slower than the run replaying them. Standing at the door watching four past selves converge is the intended beat. It is also why ghosts' `EndCycleControl` timing matters: a ghost that quit at t=40 releases its pad at t=40.
- **Four pads sets the run's floor at about seven iterations** (Room1's pad, Room2's key balloon, four Room3 pads, the escape). That is the number to weigh if the pad count changes, and it is what makes the missing ghost reset press harder than it did.

#### The wall message

`Room/PanelMessage.cs` puts `H O L D   [ N ]` / `TO SKIP TO THE NEXT ITERATION` on **all four walls at once**, with the PA saying "Manual termination available."

- **On the wall rather than the HUD, because the walls are displays** — established long before there was anything to display. A fifth grey disc on the HUD would have said it in the game's voice instead of the facility's.
- **Room3 rather than Room1**, because that is where the information becomes worth having: by then every iteration costs a walk through two solved rooms and the dead time at the end is the most expensive thing in the run.
- **Four walls, and that is not decoration.** The player enters with their back to the south wall, walks a diagonal to a pad, and turns to face the door — there is no wall they reliably look at, and a message on the wrong one is a message nobody reads.
- **The walls light up every visit; the announcement fires once per session.** A sign can sit there being true; a voice repeating an instruction the player has already followed, on every iteration they spend crossing this room, would become the most irritating thing in the prototype.
- **Not the panels themselves spelling it out** — that was the first idea and the grid kills it. A wall is 5–6 cells across by 4 tall, so panel-as-pixel is a **6×4 display** and nothing legible fits. What still makes it read as the wall is the **dark plate behind the type**: white panelling switching to near-black with red text is what a display doing something looks like.

## Ghosts (`Assets/Scripts/Ghost/`)

`PlayerRecorder` records position, yaw and a **bitmask of interaction signals** at a fixed interval; `GhostReplayer` scrubs the timeline and reports those signals back. **Ghosts have no collider at all**, so they can never trigger anything by touching it.

- `GhostInteractable` is the abstraction: `PlayerSignal` (sampled into one bit) and `SetGhostSignal` (fed back an iteration later). `FloorButton` and `Drawer` are the implementations.
- **`ghostInteractables` is `{ floorButton, drawer, pad3A, pad3B, pad3C, pad3D }`.** Bit position is the index, and `PlayerRecorder.interactables` must be the same array in the same order — `SceneBuilder` builds one and hands it to both. **Append, never reorder**: reordering invalidates every timeline recorded so far. (It is only safe to have removed the door button because timelines live for one session and are never persisted.)
- **Signals are levels, not events.** A ghost advances by elapsed time and can skip several recorded frames in one tick, so a one-frame pulse would eventually be missed. Press-type interactables stretch their pulse (`Drawer.openPulseDuration` 0.15s) and act on the rising edge at replay.
- **Only the real player's actions may raise a signal.** `Drawer.RegisterPlayerOpen` sets the pulse; a ghost's replayed pull goes straight to `Open()`. Route a ghost through the recording path and each iteration inherits the last one's actions — the drawer would open earlier and earlier until it opened on frame one.
- **A ghost releases every signal and hides itself once its timeline runs out** (`GhostReplayer.Tick`, guarded by `finished`). Clamping at the last frame — what the scrub loop does on its own — keeps applying that frame's signals forever. Harmless while every timeline was 60 seconds, decisive once `EndCycleControl` made short ones possible: **quit at t=5 standing on the pad and that signal would hold from t=5 to t=60 of every future iteration**, five seconds buying a 55-second hold. With the release in place, deciding when to quit *is* deciding how long your past self keeps standing there. **`EndCycleControl` only works because of this.**
- **Ghosts are hidden for the whole wake-up** (`SetVisible`, driven from `RunLoop`). Every timeline's frame 0 is the bed spawn, so `ResetPlayback` parks *every ghost ever made* on the spot the player is about to open their eyes on. They return exactly as the clock starts, which is also the frame they start moving, so they appear already walking.
- The ghost is a **rough human silhouette from primitives** (head/torso/2 arms/2 legs), at alpha 0.16 near-black on an **unlit** transparent material (`GhostFaint`) — low enough that the crudeness never reads, only the outline does. Unlit matters: a lit ghost picks up shading and specular that give the primitives away. It casts no shadow (the material's ShadowCaster pass is off).
- Limbs hang off **empty pivots at shoulders and hips** — a primitive's own pivot is at its centre, so rotating the capsule directly spins it about its middle.
- Only position and yaw are recorded, so the walk is **inferred from distance travelled** (`SwingLimbs`). Phase advances with distance rather than time, keeping stride length constant; amplitude scales with speed so a stationary ghost stands still rather than marching on the spot.

## The loop (`Loop/LoopManager.cs`)

The whole loop is **one coroutine (`RunLoop`), not `Update`** — an iteration opens and closes with the wake-up sequence, and the clock and the ghosts stay frozen while that plays. **Recording starts only once the player has control**, so every timeline covers the same window; start it before the wake-up and you get a pile of frames all stamped t=0.

- **The `while (true)` has exactly one exit, and where it sits is load-bearing.** The escape check is taken immediately after `IterationRunning = false` and *before* the collapse held at full, the pull-in, the blink, the panels going out and the recording becoming another ghost. All of that is the cycle closing and re-opening.
- The calibration step is yielded on **outside** the `while`, before `IterationNumber` is first incremented.
- **`AcceptsInput` (`IterationRunning && !IsPaused && !RunOver`) is the gate every `Update` that reads a key must use.** `BalloonTool`, `Drawer`, `CarryableItem`, `KeyLock`, `EndCycleControl` and `ControlHintDisplay` all use it. **A new interactable that reads input must too.**

### `Loop/WakeUpSequence.cs`

Eyelids (two black UI panels driven through their *anchors*, so they cover any resolution) fall shut as time runs out, then open on the ceiling before the player sits up. It borrows the camera by clearing `ControlEnabled` and posing the eye via `SetEyePose(height, pitch, roll)`; negative pitch looks up.

- **The lids blink rather than sweep** (`blinkShutKeys` / `blinkOpenKeys`, arrays of `x = lid position, y = seconds`). Eyes fall, snap part of the way back, fall further, and only then give out. That flutter is what makes the loop taking you read as involuntary rather than as a fade-to-black.
- **The rise decouples body from head.** The body eases out quick off the pillow and settles; the head lags a quarter of the way in, levels only near the top, and carries a slight `riseRoll` that leaves and returns to level. Driving height and pitch from one shared `SmoothStep` is exactly what made it read as a camera on rails.

### `EndCycleControl` — 60 seconds is a ceiling, not a quota

Started as a test aid and was promoted to a real mechanic: once you have done what you came to do the remainder is dead time, and spending it was the one thing the loop never let the player decide. Available from iteration 1. (A deliberate departure from spec §4.3–4.4.)

- **Hold to commit** (`holdDuration` 0.6s with a fill gauge), not press — it is irreversible and it truncates the recording the next ghost is made from. Dims to `unavailableAlpha` when `IterationRunning` is false so it reads as unavailable rather than broken.
- Answers to **`N`** as well as a click, and the key is what gets used: the cursor is locked, so clicking means freeing it and watching the view spin. The scene also needs an **`EventSystem`** — uGUI is inert without one.
- `EndCycleEarly` sets a flag rather than shoving `ElapsedTime` to `loopDuration`, because `ElapsedTime` is the timestamp written into every recorded frame and forcing it would corrupt the tail of the timeline. The flag is armed just before the timed window so an input during the wake-up can't end the next cycle instantly, and re-arms only on release so holding the key through the blackout doesn't chain.
- **Its label was silently wrapping onto two lines**, which is probably why testers asked for a skip button it has had since the first build: `HOLD [N] — END CYCLE` is 20 monospace cells (~192px at `fontSize` 16) inside a rect that was **168** wide. Now 224 with `Overflow` set. **Check any fixed-width HUD label against `0.6 × fontSize × length`** — this is the second time a label has quietly rewrapped.

### The ending (`Loop/EscapeTrigger.cs`, `Loop/EndingSequence.cs`)

Walking out of Room3's north door ends the run — the **only** exit condition in the game.

- **`EscapeTrigger` sits ON the threshold, not past it, and that is forced.** The `FarCap` is directly behind the doorway and the pocket in front of it is 0.1m against a controller of radius 0.3 — **there is physically no "through" to stand in**. The volume is centred on Room3's north wall face (Z 26.95, `halfDepth` 0.5), which a player reaches to Z≈26.875 before the cap stops them. The ending takes control on the frame it fires, so they never reach the wall.
  - Two gates, both needed: **`door.IsOpen`**, so walking up to a shut door ends nothing (it reads as *you got out*, not *you arrived*); and **`halfWidth` = `DoorWidth/2 + 0.05`**, because without an X bound anyone anywhere along the north wall is at the same Z as the opening and the run would end from across the room the moment the fourth pad went down.
  - Y is deliberately **not** tested — the room has a floor and a ceiling, and a jump must not be a way to miss the end of the game.
- **The ending is defined by what it does NOT do**, because the reset is the only vocabulary the game has built and the player has seen it dozens of times:
  - **The collapse lets go instead of peaking** — `RunEnding` ramps flare and shake down from `WallPanelDisplay.Flare`, so escaping inside `collapseLeadTime` means watching the thing that takes you every sixty seconds try, and stop.
  - **The panels do not power down.** That is the loop's signature; using it would say the cycle continued.
  - **The lids do not blink.** The blink is involuntary and belongs to the thing that just failed. `EndingSequence` fades its own opaque scrim instead — reusing the eyelids would have been free and wrong.
  - **The ghosts are left standing**, visible under the scrim, holding their pads: the last image of the run is the people it took to get out.
  - **The room tone fades and stops** (`RoomAmbience.FadeOutTone`), the only thing that ever stops it. It fades rather than cuts — a cut reads as a sound failing.
- Timing: scrim 2.4s → 0.9s black → card 1.4s → hold 6.5s → `MainMenu`, **automatically, not on a keypress**. The card is `C Y C L E   B R O K E N` over `ESCAPED ON ITERATION N` — the iteration number is the one score the game keeps, and it is diegetic.
- **Everything in `EndingSequence` runs on `Time.unscaledDeltaTime`.** Pausing is locked out (`RunOver`, read by `PauseMenu`), but a coroutine that can be frozen with no loop left to unfreeze it is a soft lock at the one moment the game must not have one.
- `RunEnding` **discards the final recording** — there is no next iteration for it to haunt.

## Mouse sensitivity and calibration

The first itch.io play-test came back with "far too sensitive", from a build that felt fine in the Editor. **The same constant genuinely means different things on the web and on the desktop, and on two testers' machines.**

- **Unity's WebGL build hands the engine the browser's pointer-lock delta completely unscaled.** Verified by decompressing the shipped `WebGL.framework.js.unityweb`: `fillMouseEventData` does literally `HEAP32[idx+8] = e["movementX"]` — no `devicePixelRatio`, no canvas correction. That value has already been through the OS pointer-speed slider and its acceleration curve, and Chrome, Firefox and Safari do not agree on it.
- **It is not frame rate**, the usual first guess. `Input.GetAxis("Mouse X")` is already the delta accumulated *since the last frame*, so total rotation over a sweep is the same at any rate. That is why `HandleLook` correctly uses **no `deltaTime`** — don't "fix" it.
- So the fix is not a better number. `GameSettings.MouseSensitivity`, default **1.1** (was a serialized 2.0), backed by `PlayerPrefs`.
- **`GameSettings` applies live but writes on the way out.** A `Slider` raises `onValueChanged` every frame of a drag and each `PlayerPrefs.Save` is a storage flush on WebGL. Nothing relies on `OnApplicationQuit` — a browser tab is *closed*, not quit.
- Still available if complaints continue: request pointer lock with `{ unadjustedMovement: true }` via a `.jslib`, bypassing the OS acceleration curve. Chrome/Edge support it; Firefox/Safari fall back silently. Not done — the slider makes it optional.

### The calibration step (`Menu/SensitivityCalibration.cs`)

Before iteration 1 the run stops and asks the player to look around and set the value, because the pause slider can only be found by someone who has *already* lost an iteration to a default that did not suit them.

- **It runs in a room of its own, and that is the third attempt.** Both failures looked fine until played:
  1. On the title screen, panning the menu's background still within ~16° of headroom. Not a test — sensitivity *is* the relationship between hand travel and how far the world goes round, and that cannot be judged inside a window narrower than one flick.
  2. Still on the title screen, turning a camera inside **Room1's baked reflection cubemap** hung as a skybox. It turns a full circle and shows *nothing*: sampled, the cubemap's floor face runs **0.940–0.944** and its ceiling **0.690–0.734**. The room is a white box, so its reflection probe is a blank field. **Do not reach for a reflection probe as a preview of anything.**
- **`CalibrationRoom` is sealed** — `Rect.zero` for both cutouts, no doorways at all, sitting behind Room1 with a room's worth of nothing between. **Empty on purpose**: what is judged is how fast the room goes round, and furniture is something to look *at* rather than something that shows motion. The panel grid does it better — a regular grid whose grooves sweeping past give an unambiguous read on speed.
- **`CalibrationSpawn` holds the SAME offset and facing as `BedSpawnPoint`** — (0, centre − 0.7) at yaw 180, so the wall ahead is 4.55m and the sides 4.38m in both rooms. Turning in place has the same angular rate whatever is in front of you, so the *number* is identical either way — but what the eye counts is detail crossing the view, and a far wall puts many more panel edges in a degree. An earlier version stood the player 3m from the south wall looking down the length: **8.25m of wall ahead against 4.55m**, same sensitivity, noticeably quicker. **If either spawn moves, move the other.** The residual difference is the furniture, and it is accepted.
- Its ~88 panels are **excluded from `WallPanelDisplay`** (`InCalibrationRoom`) — seen once and never again, so booting and flaring them would write a per-frame property block to renderers nobody can look at. It gets its own reflection probe, or the 0.85-smoothness walls mirror the sky and come out blue.
- **The menu background capture poses the player at the bed first**, since they now start in an empty white box. Safe because the room scene is already saved by then.
- **The rest of the HUD is switched off** (`hideWhileActive`) — a countdown reading 1:00 describes a loop that has not started. `SceneBuilder` fills the list as **"every canvas child except this page and `PauseMenu`"** rather than by name, so a HUD element added later is covered without anyone remembering. PauseMenu stays live so a settings screen cannot trap someone.
- **Control is left fully on, walking included.** Iteration 1 teleports everyone back to the bed regardless, and every other control is already inert via `AcceptsInput`.
- **The wheel is the only way to change the value.** Arrow keys and A/D were offered and had to go — both are bound to the `Horizontal` axis, so every press that nudged the number also strafed the player, which reads as the setting having moved the room.
- **The pointer stays captured, which is what makes the number honest** — a browser reports different deltas for a free pointer than a locked one, and the game runs locked. That is also why the gauge is a readout rather than a slider. `FirstPersonController` owns the lock and the click-to-retry; this page only reports the state. `[ENTER]` is gated on the capture, so nobody confirms a number they could not test.
- The readout sits on **its own dark plate, low in the frame** — dimming the room behind a full-screen scrim would be dimming the subject.

## Pausing (`Loop/PauseMenu.cs`)

**Escape** freezes the room and puts `RESUME` / `MAIN MENU` / `QUIT` over it, plus the sensitivity slider.

- **The freeze is one line — `Time.timeScale = 0` — and it works because every moving part already runs on scaled time**: the loop's clock, the wake-up's `WaitForSeconds`, the collapse ramp, the balloons, `CameraShaker`'s Perlin sampling. Verified by reading for `Time.unscaled*` before building it.
- **What a zero time scale does not stop is `Update`** — every raw key read still lands, and `HandleLook` uses no `deltaTime` at all, so the view would swing with the very mouse movement made to reach Resume. Hence the `AcceptsInput` gate above.
- `ControlEnabled` is forced false **every frame** while paused, not once on open — the loop hands control back at the end of every wake-up regardless. On resume it is restored to *what it was*, so pausing during a wake-up cannot hand back a camera the loop had taken away.
- `AudioListener.pause = true` as well: the announcer and room tone are not on scaled time and would talk over a frozen room.
- **`Time.timeScale` and `AudioListener.pause` are global**, so both are restored in `OnDestroy` as well as through the buttons — that covers the scene change, the path easiest to miss. `ToMainMenu` unfreezes *before* the load, since a scene arriving at `timeScale 0` cannot start itself.
- Built **last on the canvas** so it covers the HUD, the prompts and the eyelids. Its scrim keeps `raycastTarget` on deliberately — that is what stops a click reaching the end-cycle control underneath. The `CanvasGroup` clears **`blocksRaycasts` as well as alpha** when closed; an alpha-0 graphic still receives clicks.
- **The slider re-reads `GameSettings` on every open (`SyncSensitivity`), not once in `Awake`** — a bug this shipped with. `Awake` runs at scene load; the calibration step runs from `LoopManager.Start()` *afterwards*; so a slider seeded in `Awake` held the load-time value and snapped away from the real one when dragged. **Anything that can change a value behind a panel's back makes a single seed stale.**
- It is seeded with `SetValueWithoutNotify` **before** the listener is attached — a `Slider` raises the event on assignment, so seeding afterwards writes its own starting value back over the saved one.
- `MakeSlider` assembles the `Slider` by hand: Unity drives the fill's and handle's anchors itself every frame and finds their containers **through the hierarchy, not through fields**, so each must be a child of its own container rect with offsets left at zero.

## The title screen (`MainMenu.unity`)

A separate, near-empty scene: camera, canvas, a still of the room, `PLAY` and `QUIT`.

- **Separate scene rather than a state of the room, and that is what makes the loading bar real.** `IterationRoom` is a 306,000-triangle bed, 367 wall panels, seventy balloons, four baked probes and a pile of shaders. Held in one scene there would be nothing to load and the bar would be theatre.
- **The background is a still of the game's own first frame**, `Assets/Textures/MenuBackground.png`, rendered by `CaptureMenuBackground` **on every build** so the menu can never advertise a room that no longer exists.
  - Captured through `RenderPipeline.SubmitRenderRequest` with a `SingleCameraRequest`, **not** `Camera.Render()` — URP does not support a bare `Render()` from arbitrary code, and the request is also what runs the volume stack, so the still carries the same tonemapping, bloom and vignette.
  - `targetTexture` is restored in a `finally`; left assigned, the game renders into a `RenderTexture` instead of the screen and the saved scene carries it.
  - **Skipped under `-nographics`** — the previous PNG stands and the build logs a warning. A stale background beats a failed build.
  - The view is the south wall's panelling and nothing else, because the spawn faces away from the bed. That was chosen over a dedicated framing showing the furniture: it is the frame the game genuinely opens on, and a grid of blank panels says what this place is more plainly. **If revisited, add a menu-only camera — do not move the spawn to improve the menu.**
- **Buttons are white with the dark look coming entirely from the `ColorBlock`** — a `Button` tints by multiplying, so an already-near-black background has nothing left to brighten with on hover.
- `onClick` is wired in `Awake` from serialized references, not as persistent listeners (which would need `UnityEventTools` to serialize at all).
- The menu **puts the cursor back** (`Cursor.lockState = None`) in `Start` — Unity does not reset that across a scene load.
- The progress bar shows the **lesser** of real progress and elapsed fraction of `minimumLoadingTime`, so a fast machine does not flash the load past in two frames while the bar never claims more than has happened. Unity reports `0..0.9` while `allowSceneActivation` is withheld and never reaches 1, so it is rescaled — a bar stopping dead at 90% reads as a failed load.

## HUD

- **The HUD is monospace** — `Assets/Fonts/JetBrains_Mono/static/JetBrainsMono-Regular.ttf` via `SceneBuilder.UIFont()`, used by every text object in both scenes.
  - Diegetic reason: everything on screen belongs to the *facility*, and a terminal face says so where a proportional one reads as the game's own UI. Practical reason: the countdown changes every second, and proportional digits reflow and twitch every tick.
  - **Licensing: JetBrains Mono is SIL OFL and `OFL.txt` ships beside it.** Keep that file where it is. This replaced Consolas, which was Microsoft's and fine only for a prototype that never left this machine — embedding a font in a distributed build is redistribution.
  - Point `UIFont()` at the **static Regular**, never the variable font beside it — uGUI's legacy `Font` has no axis control, so a variable face is a coin toss on which weight renders.
  - `UIFont()` falls back to the built-in font rather than throwing. **Check it did not fall back** after a font change: `grep -o "m_Font: {fileID: [0-9]*, guid: [a-f0-9]*" Assets/Scenes/*.unity | sort | uniq -c` should show one guid.
- **The "Iteration N" label sits dead centre**, upper case and spaced out (`IterationLabel.spacedCaps`). It used to sit 150px high, which read as a subtitle floating over the room; the label is the iteration announcing itself, so it gets the middle of the screen with nothing else on it. Spacing is done **in the string** because uGUI's `Text` has no tracking control; in a monospace face a space is exactly one cell, and the single space between words becomes three. `horizontalOverflow` is `Overflow` so a narrow rect can't break it as "ITERATIO / N 12".
- `Loop/CarriedItemsDisplay.cs` — top-left readout of what you are carrying, **as icons**. It exists because carrying is the one rewound state the player otherwise cannot see: the pin is visible in hand, but the key is **pocketed** (`showInHand` false, so taking it does not knock the pin out of the hand you needed to get it with).
  - Icons rather than words for the same reason the HUD is monospace — the facility's readout, not the game's subtitle. They also survive being glanced at, which is all this gets.
  - A **fixed pool of four `Image` slots**, enabled and disabled rather than created and destroyed, rebuilt only when `PlayerHand.Version` changes. `itemId` is the wire value other scripts match on (`KeyLock` asks for `"Key"`).
- `Loop/ControlHintDisplay.cs` — a grey disc with `E` over whatever interactable the player has walked up to, and a mouse glyph on the pin once it is in hand.
  - **These used to retire on first use and play-testing overruled it.** The reasoning was real — the loop's texture is repetition, so an instruction replaying every sixty seconds becomes the most repeated thing in the prototype — but testers never found the door button, and those who did could not find it again an iteration later. **A prompt shown once has not been taught, and a control the player cannot find is worse than one they are reminded of.**
  - What survives is the restraint: the disc appears only when E would actually *do* something (`WantsInteractHint`, not proximity), only over the nearest such thing, and at `maxAlpha` **0.75** — a label stencilled on the fixture rather than the game talking. That is the dial if it starts to grate.
  - `IInteractHintTarget` means "E right now would do something here". A shut drawer with the pin inside is one place with two answers, and prompting over the object that would *ignore* the press teaches the wrong thing. Implemented by `Drawer`, `CarryableItem` and `KeyLock`.
  - `interactTargets` is typed `MonoBehaviour[]`, not the interface, because **Unity does not serialize interface fields** — the cast happens once in `Awake`.
  - Positions come from `WorldToScreenPoint` into a full-screen rect via `ScreenPointToLocalPointInRectangle` with a **null camera** (the canvas is ScreenSpaceOverlay; passing one skews it). The `z <= 0` test is load-bearing — `WorldToScreenPoint` returns a mirrored on-screen position for anything behind the eye, so without it a prompt for something at your back appears in front of you.
  - Its fade uses `Time.unscaledDeltaTime`, or a prompt caught mid-fade sits frozen under the pause overlay.

## Lighting

**Four recessed ceiling fixtures per room** (`BuildCeilingLights`) — emissive panels plus real spot lights in a 2×2 grid (X = ±`GridCellWidth`, Z = centre ±2.6).

- **This replaced an all-ambient setup, and that swap is why the room stopped reading as a whitebox.** Flat ambient adds the same amount to every surface regardless of orientation, distance or occlusion, so wall, ceiling and floor came out identical — no falloff, no direction, nothing to read depth from. **If the room ever looks flat again, check whether something pushed ambient back up to carry the lighting.**
- **Spots, not points**, aimed straight down: a downlight's cone gives the floor bright pools and lets the walls fall off toward the corners, and that gradient is most of the effect. `spotAngle` 130 / `innerSpotAngle` 45 so it behaves like a panel rather than a torch.
- `intensity` **10.5**, higher than it looks because a 130° cone from over 5m up spreads over most of the room. **Derived rather than tuned**: `9 × (5.408/5.0)² = 10.5` holds the floor at the brightness signed off at the lower ceiling. Inverse square is an approximation here (the cone also widens from higher up), so check it by eye. If the fixture count, ceiling height or ambient bands move, this moves with them.
- **Only Room1's fixtures cast shadows.** Every additional light's shadow shares one atlas, and the rooms past the first hold nothing worth the map.
- **The default Directional Light is deleted, not dimmed** — sealed boxes with a ceiling slab, so a sun contributed nothing while still costing a shadow pass.
- **Ambient is `Trilight`, not `Flat`**, demoted to standing in for the bounce URP is not computing. Trilight lights a surface by which way it *faces*, which maps onto the one thing downlights get wrong — they hammer the floor and never touch the ceiling.
  - `ambientGroundColor` **0.719** lights **downward** faces, i.e. the ceiling — nothing else in the room lights an upward-facing surface at all. `ambientEquatorColor` **0.644** → the walls. `ambientSkyColor` **0.155** lights the floor, which the spots already cover; raise it and the floor blows out.
  - Tuned by eye over two passes from 0.22 / 0.40 / 0.88. The shape that came out: floor lowest by a long way, walls and ceiling high and close together. The walls went *down* to 0.138 and back **up** to 0.644 — at 0.138 the falloff toward the corners read as gloom rather than shape and the room lost its clinical evenness. Fixtures came 15 → 9 across the same passes: the fill had been carrying more than it looked.
  - Expect the ground colour to bleed onto the walls — that is spherical harmonics doing what bounce would, and it is why the walls read lit rather than painted.
  - **`DynamicGI.UpdateEnvironment()` must be called after assigning these**, or the values never reach a shader and every tweak looks like it did nothing. This cost a full tuning pass.
- **`ConfigureLightingPipeline()` sets URP enums by name, never by index.** `m_AdditionalLightsRenderingMode` serialises as `[Disabled, PerPixel, PerVertex]`, so PerPixel is index **1**, not the 2 you would guess — picking 2 silently gives per-vertex lighting, which washes the room into flat blocks. `m_AdditionalLightsShadowmapResolution` is likewise an enum (`_256`…`_8192`), so assigning `2048` as an int throws. Set to `_4096`, since six shadowed fixtures at `_2048` make URP quietly drop each map to 512.
- Post-processing (`BuildPostProcessing`) is a global `Volume` pointing at `IterationVolume.asset`: **Neutral** tonemapping (ACES crushes the near-white walls to grey and warms them), a subtle vignette, contrast +8, and a **bloom threshold of 1.2** — the walls sit near 1.0 luminance, so a default threshold blooms the whole room. The player camera opts in via `renderPostProcessing`; URP cameras ignore the volume stack otherwise.
- **Ambient occlusion is a renderer feature on `IterationRenderer.asset`, not a volume override**, so it lives outside the scene. `ConfigureAmbientOcclusion()` reconciles it every build because adding is not idempotent — a retried add silently stacks duplicates (this produced six SSAO features once).
  - **`Samples`, `NormalSamples` and `BlurQuality` run `High=0, Medium=1, Low=2` — 0 is best, which reads backwards.** Setting `Samples` to 2 drops to 4 samples and sprays visible noise.
  - `Radius` is the other half of the noise problem. The stock 0.035m reads as no occlusion; 0.12 throws a dithered halo around small objects which on white walls reads as *dirt*. **0.045 with `Intensity` 0.7 and `AOMethod = InterleavedGradient`** is the clean point.
  - If the scene ever looks "dirty/noisy around objects" again, it is this feature, not the lighting.
- **A tuning panel (`Dev/LightingTuner.cs`) found these numbers in play mode and has been deleted** — the values live in `SetupLighting` / `BuildCeilingLights`, which were always the source of truth. `git log` has it. Two lessons it left: a panel must **seed its controls from the live scene** (hardcoded constants meant opening it silently overwrote the room's real lighting, which reads exactly like "my change never went in"), and it must hold `ControlEnabled` false **every frame**. Touch `volume.profile`, **not** `volume.sharedProfile` — the former hands back a runtime clone, so dragging exposure cannot dirty the asset on disk.

## Materials and art

**Albedo is flat colour everywhere and should stay that way**: the reference room is plain white panels, so realism comes from geometry and lighting, not painted-on detail.

- **Surface relief is a generated normal map**, `Assets/Textures/SurfaceGrain.png` (`MakeNoiseNormalMap`) — multi-octave value noise made **tileable by wrapping the lattice** at each octave's period (`Mathf.PerlinNoise` is not tileable and seams at every repeat).
  - **`_BumpScale` is much larger than the sub-1 that looks sane.** Central differences across a smooth field give tiny gradients, so the map is genuinely shallow: at 0.7 the surface renders *perfectly flat* even with the camera against it; at 10 it is stucco. Floor **1.8**, walls **0.2**.
  - **Walls are a smooth glazed panel, not plaster**: `_Smoothness` **0.85** with relief kept to a whisper purely so the specular isn't a uniform sheet, which is what makes a flat surface look CG. The floor stays matte at **0.18**.
  - **Tiling must be set on `_BaseMap`, not `_BumpMap`** — URP/Lit drives the normal map's UVs from `_BaseMap`'s transform, so a scale on `_BumpMap` does nothing. Per face: (5,3) for a 1.7 × 0.9m wall panel vs (26,30) for a 9 × 10.9m slab, for the same ~0.35m grain.
  - It imports as `TextureImporterType.NormalMap`; without that the shader reads raw RGB as a normal and tilts every surface. It also means you **cannot measure it by sampling `.r`/`.g`** — Unity re-encodes to DXT5nm. Judge it from a render.
- **Gloss only works because of the reflection probes.** Raising smoothness without them paints the blue procedural sky over every white panel — the exact failure the old "0.03 smoothness everywhere" rule existed to dodge. Note that **a mirror of a white room is still white**: the gloss reads as sheen and falloff rather than visible reflected objects. If a harder "switched-off display" look is wanted, the panels need a *darker* albedo.
- **One baked reflection probe per room** (`BuildReflectionProbe`), box-projected and sized to the room so a reflected wall stays on the wall instead of sliding with the camera. `reflectionIntensity` is **1.0**.
  - **Baked during `Build()`** via `Lightmapping.BakeReflectionProbe`, writing `Assets/Textures/*_Reflection.exr`. A Realtime probe was tried first, reasoning that rebuilds would invalidate baked data — but **a realtime probe renders nothing until play mode**, so `probe.texture` came back EMPTY and the glossy walls had nothing to reflect. Baking in the build gets a real asset regenerated in lockstep with the geometry it captures.
  - `resolution` **512**: at 256 the ceiling fixtures reflect as vague smears.
- **The wall panels are displays and they boot at the start of every iteration** (`WallPanelDisplay`, driven from `WakeUpSequence`): dark (`WallPanelColor` 0.13) when you wake, sweeping to white as you sit up. This is the visual half of "New cycle initialized", and it retroactively makes the wall grid *diegetic* — the panels aren't tiles, they're screens, which is why the room is built out of them.
  - Panels come up **from the floor rows first with per-panel scatter** (`onsetJitter`). A uniform fade reads as a dimmer; a staggered one reads as separate screens waking. Onsets are computed once in `Awake` from world Y with a **fixed seed**, so the boot is identical every iteration — a loop should be.
  - Driven through a **`MaterialPropertyBlock` on `_BaseColor`** (`_Color` would silently do nothing — URP/Lit's albedo is `_BaseColor`), which is what allows panels to differ mid-sweep. This **breaks SRP batching** across the array; fine at this scale, worth knowing if the room grows.
  - `PowerDown()` is called with the eyelids already shut, so the player only ever witnesses the room coming *back*.
  - **The last `collapseLeadTime` (10s) of a cycle is a collapse**: `SetFlare` blows the panels past white with heavy emission while `CameraShaker` ramps up, both held at full through the blink before being cleared behind the black. The ramp is **squared** — a linear one reads as a slider being dragged. `PanelWhite` has `_EMISSION` enabled with a *black* colour purely so the keyword is compiled in; a property block cannot turn a shader keyword on. Escaping is the one case where it does not peak.
  - The material itself stays **white** — the resting state for all but the first seconds of an iteration, and what the probes bake against.
- `Assets/ArtAssets/Furniture/` holds `messy_bed.glb` and `nightstand.glb`, both **Creative Commons** (confirmed 2026-08-10). If either is BY rather than 0 the build needs an attribution line — **record the source URL here the next time either is touched.** `messy_bed` won a five-way in-engine comparison because its bedding is rumpled and slept-in; the loop opens every iteration by waking up here, so a neatly-made bed undercuts the premise. Cost: **306,907 triangles**, 20-60× the alternatives.
  - **`.glb` needs `com.unity.cloud.gltfast`** (6.19.0). Without it Unity imports a `.glb` as an opaque `DefaultAsset`.
  - Keep the glTF materials these ship with; a hand-rolled URP/Lit stand-in drops the metallic/roughness maps.
  - **These models bake contact shadows into their occlusion maps**, which only looks right in the authored pose. Moving the pillow left those shadows painted on the bedding as dirt. `DisableBakedOcclusion` clones the material into a project asset with `occlusionTexture_strength = 0`; SSAO then supplies contact shadows that follow the actual geometry. The clone must be its own asset because glb materials are sub-assets regenerated on every reimport. **If furniture shows a shadow that doesn't move with the light, this is why.**
  - They are authored **Z-up** (`Quaternion.Euler(-90, …)`) and their **units differ wildly** with bounds not centred on the origin. Don't assume centimetres. Measure bounds at scale 1 and derive `uniformScale = 2.05f / max(horizontal extent)` — that is how `messy_bed`'s 0.00941 was found.
- `PlaceModel(...)` instantiates via `PrefabUtility.InstantiatePrefab` and repositions so *rendered bounds* (not the mesh pivot, which is at an arbitrary corner) centre on a target X/Z with the base on a given floor Y. Its optional `rotation` is applied **before** bounds are measured, and the generated `BoxCollider` un-rotates world-axis-aligned bounds back into local axes so a rotated model doesn't get height and depth swapped. **Reuse this for any future model placement.**
- **The HUD's glyphs are drawn from code** — `Assets/Textures/Icons/` written by `IconCanvas`, a ~60-line software rasteriser. Shapes are predicates over a 0..1 square composed with `&&` and cut back with a `sign` of -1, at **4×4 supersampling**, which is the only reason a 128px disc has a clean edge. Origin is **bottom-left**, matching Unity's texture coordinates. They are **white with the shape entirely in alpha** (the HUD tints via `Image.color`) and import **uncompressed** — block compression frays something that is nothing but alpha. The pin's needle is drawn deliberately fatter than the real pin: at 58px displayed, a true needle lands on one screen pixel and vanishes.
- Ghost material `GhostFaint.mat` — URP Unlit, transparent, near-black at alpha 0.16. The URP transparent set-up needs all of `_Surface`/`_SrcBlend`/`_DstBlend`/`_ZWrite` **and** the `_SURFACE_TYPE_TRANSPARENT` keyword; set the colour alone and the shader stays opaque, rendering a solid black mannequin.
- **A marble floor was built and dropped** — the veining pulled the room from "cell" toward "hotel lobby". If it comes up again: turbulence marble needs `turbulenceScale` above **one full cycle** (0.55 wobbles the bands half a cycle and reads as wood grain; 2.2 makes neighbours cross and merge into something stone-like). A textured floor also **cannot carry the grain normal as well**, since URP/Lit drives every secondary map from `_BaseMap`'s single UV transform.
- **Furniture is the remaining art gap.** Recommended CC0 sources: Sketchfab's CC0 filter, Poly Haven, ambientCG. Claude can't download these — drop them in `Assets/ArtAssets/Furniture/` and `PlaceModel` handles placement.

## Audio

A diegetic PA announcer, taken from the reference film. The schedule is the whole system.

| Cue | When | Method |
| --- | --- | --- |
| "Iteration N, 60 seconds remaining." | after the wake-up, as the clock starts — **no chime** | `AnnounceIteration` |
| "10 seconds remaining." | `tenSecondCueAt` (T-12, not T-10) | `AnnounceTenSeconds` |
| "Nine." … "One." | one per whole second, T-9 to T-1 | `AnnounceCountdown` |
| "New cycle initialized." | top of the next iteration, under closed eyelids | `AnnounceNewCycle` |
| "Cycle terminated." | the player ends the cycle early | `AnnounceCycleTerminated` |
| "Containment failure. Cycle broken." | the player escapes — once per run, ever | `AnnounceCycleBroken` |
| "Manual termination available. Hold N…" | first time the player reaches Room3 | `AnnounceManualTermination` |

- **The T-10 line is cued at T-12 on purpose.** It runs about two seconds, and from T-9 the digit countdown replaces it every second, so a literal T-10 clips it after one word. Retune `tenSecondCueAt` if the line is ever re-recorded.
- Cues are **pushed from `RunLoop`, not polled**. The loop owns `ElapsedTime`, and the announcer must stay silent through the wake-up. The counting loop guards on a *falling* whole second, so it fires once per second at any frame rate.
- Iteration 1 gets no "New cycle initialized" and no reset sting — it opens the run rather than resetting it.
- **The iteration line has no chime**, though T-10, "Cycle terminated." and the ending line do. It fires hundreds of times a run, and a two-note ding ahead of it made the loop's most repeated moment its most decorated. The ending line *is* chimed — the most important thing the facility ever says, and its wording is built from vocabulary the player already has, so a cycle being **broken** lands as the same voice admitting the machine failed.
- **Announcements replace each other** (`Stop()` + `Play()`), never `PlayOneShot` — the countdown fires once a second and one-shots would slur the digits together.

### The PA treatment

`AddTannoyFilters` puts the voice **and** the chime through a five-stage chain so they read as a horn on the wall of a hard, empty room rather than narration over the game.

- **A runtime chain on the `AudioSource`, not baked into the WAVs.** The clips stay clean masters, retuning is an Inspector drag, and a better-acted take dropped into `Assets/Audio/Voice/` inherits the treatment for free.
- **Component order on the GameObject *is* the signal chain** — Unity runs filters top to bottom. Reverb goes last: ahead of the distortion it grits up the tail as well as the voice, which reads as a broken speaker rather than a room.
- **Band-limiting does most of the work, not the reverb.** High-pass **340 Hz** / low-pass **3600 Hz** with the low-pass left resonant (`Q` 1.6) — a horn driver has no bottom and no top, and that peak is the nasal honk of every station announcement ever made. The ear identifies the *channel* long before the space.
- Distortion **0.17** — past ~0.3 the words stop being intelligible, and the announcer carries actual information.
- Echo **105 ms** at `decayRatio` 0.22 / `wetMix` 0.33, roughly the round trip across a room this size. Reverb is `User`, `decayTime` 2.1s with `roomHF` -900 and `decayHFRatio` 0.55 — a deliberately dark tail, since a bright one would undo the band-limiting.
- **`Tools/preview_pa_voice.py` renders the same chain offline** (`python Tools/preview_pa_voice.py voice_count_3`). An approximation — Unity's reverb is FMOD's, this is a Schroeder network — so trust it for "how much echo" and "can I make out the number", not the exact tail. **Its constants mirror `AddTannoyFilters`; change one and change the other.**

### Where the audio comes from

**Everything is generated from a script.** That is the same bargain the wall grain makes: the whole project must rebuild from scripts, and a folder of sourced CC0 clips was the one part that could not.

- `Tools/generate_narration.ps1` drives the Windows synthesizer (`Microsoft Zira Desktop`) to write **45 lines** into `Assets/Audio/Voice/` as 22 kHz 16-bit mono WAV. To replace them with better-acted takes, keep the filenames — no C# refers to how they were made. `SceneBuilder.NarrationIterationLines` must match the range the script writes (30, then a generic line stands in).
  - **Getting a rising ending out of SAPI requires a question mark.** The iteration lines are written `Iteration <prosody pitch="+35%">N</prosody>? 60 seconds remaining.` — the terminal contour is chosen from sentence punctuation and overrides everything else. Measured on Zira at rate -2: `"1!"` 220→160 Hz (falls; the exclamation does nothing), `prosody contour="…"` 202→138 Hz (SAPI ignores the attribute), `"1?"` 182→232 Hz, `pitch +35%` **plus** `?` 179→259 Hz. The `?` is never spoken, and because it closes the sentence on the number the rise sits there.
  - **Rate is -2 for sentences, -1 for the countdown digits.** The default clip read as a screen reader rather than a PA. The digits cannot go slower than -1 — each must finish inside its one-second slot (verified: every digit's spoken part ends by 0.78s).
- `Tools/generate_sfx.py` writes twelve clips with the Python standard library — no numpy, no downloads, each seeded so re-running reproduces the identical set. See `Assets/Audio/SFX/README.md` for the filename table.
  - **`sfx_balloon_pop` is under a fifth of a second on purpose** — seventy can be in flight at once and anything with a tail turns a roomful of balloons into a wash. Each balloon carries a **fixed detune** from the field's seed: one clip across seventy reads as a machine gun, and a balloon keeping the same voice every iteration is one more thing about the room that stays put.
  - **The room-tone loop is the one with a real constraint.** It plays on `loop`, so a seam is a click every 8 seconds for the whole session. Every partial is a multiple of 1/8 Hz so the tone wraps exactly, and the noise layer is wrapped by crossfading its own tail over its head (`seamless()`). Verified numerically: the step across the wrap is smaller than the largest step inside the clip.
  - `sfx_pull_in` runs two motions against each other — a noise band climbing while a tone falls away underneath. Up and down at once reads as being pulled *through* something; either alone is just a riser or a drop. It ends on a hard cut with the sub a beat late.
  - **There is no tool-swing sound.** `BalloonTool.swingClip` is deliberately null rather than borrowing the sheet rustle; swinging at air gives no audible feedback, which is a known gap.
  - `LoadClip` is extension-agnostic and **a missing file resolves to `null` with every player guarding on it**, so an emptied SFX folder is still playable. Overwriting a generated clip with a recorded take under the same name is the entire swap.
- Unity MCP's `generate_audio` is **not** an option for the voice — music/SFX only, no speech, and it needs an unconfigured fal.ai key.

### Sources and spatialisation

`MakeSource` builds every `AudioSource` with linear rolloff (`maxDistance` 14) rather than Unity's logarithmic default, which stays near full volume across a room this small.

- **2D**: the PA voice and chime (a room-wide tannoy has no position to walk away from), the music bed, the machines, and the player's own breath. **3D**: the doors and the floor pads.
- **There are no footsteps.** A distance-paced `FootstepPlayer` existed and was removed with its four clips — in a room this small with a 60-second clock, the player's own steps were noise over the announcer rather than presence.
- **The floor pads are audible, and audible for ghosts too.** `FloorButton` fires a clunk on the *edges* of `IsActive`, whoever caused it. This is a real affordance: `DoorIndicator` only reports to a player looking at the door, where the clunk reaches you facing the other way — and in Room3 it is how you count pads without turning round. Edges only, since a tick while held would be unbearable across a full iteration.
  - **Silent while `IterationRunning` is false** — the loop releases every ghost during the reset, and a rack of pads letting go behind closed eyelids is the machinery showing through.
- **The loop boundary is two sounds split across the blackout.** `PlayPullIn` fires the moment the iteration ends, while the lids are still falling and the panels flaring, because that is the moment the loop takes you; held until after the blackout it explains something that already happened. `PlayPowerDown` lands under the black. The panels booting during the wake-up are the third beat: **taken, switched off, switched back on.** Both fire at the *end* of an iteration, so neither needs an `IterationNumber > 1` guard.
  - A third cue, `sfx_glass_rattle`, was removed — high-Q pings off the nightstand props read as a doorbell going off every 60 seconds.
- `Door.Close()` is **deliberately silent** — the loop rewinding world state behind a black screen, not a door being shut. A sound there draws attention to the seam.
- Exactly **one `AudioListener`**, on the player camera. A second is a Unity warning and breaks positional audio.

## Script index (`Assets/Scripts/`, namespace `IterationRoom`)

Only what is not covered above.

- `Player/FirstPersonController.cs` — CharacterController movement, mouse look, jump. Also owns the pointer lock and the click-to-retry (browsers only grant capture inside a user gesture, so the lock requested in `Start` is routinely refused on WebGL). **Looking is skipped entirely while the cursor is loose**, or a pointer travelling across the page swings the view across the room.
- `Player/CameraShaker.cs` — sits on a **`CameraRig` between the player and the camera**, and that placement is the point: `FirstPersonController` rewrites the camera's own `localPosition` and euler angles every frame, so a shake applied to the camera itself is wiped instantly. Uses Perlin rather than `Random` — continuous noise reads as a building judder, per-frame random values as a broken frame rate.
- `Interactables/FloorButton.cs` — hold-type, active if the player OR any ghost is on it. The player side is **polled in `FixedUpdate`, deliberately not `OnTriggerEnter/Exit`**: `Teleport` disables and re-enables the CharacterController inside one frame, so a player standing on it when the iteration ended never generated the exit callback — the pad stayed lit forever and every ghost recorded afterwards held it forever too. **Don't "simplify" this back to trigger events.** `Drawer`, `CarryableItem`, `PlayerHand`, `KeyLock` and `EscapeTrigger` all poll for the same reason.
  - The pad has **no collider at all** — a logical volume testing the player's centre against `activationRadius` (derived from the visual radius so the two can't drift), plus a foot-height check so jumping off releases. Its old trigger was `radius 0.5, height 0.5`, which Unity silently clamped to a *sphere*, and an AABB test added the player's own 0.3 radius: a ±0.8m square around a disc of radius 0.35.
- `Interactables/DoorIndicator.cs` — the lamp: one block split red/green, only ever one lit (albedo *and* emission — albedo alone looks like coloured plastic). **It tracks the condition, not the door.**
- `Interactables/BalloonTool.cs` — uses an **overlap sphere, not a raycast**: in a room packed with balloons, having to line one up in the crosshair turns a physical act into a shooting gallery.
- `Loop/IterationLabel.cs` — the eyelids are built first in `BuildUI` so they sit at the back of the canvas and the label draws on top of the black rather than under it.

## Gotchas (don't rediscover these)

- **A world-space Canvas is legible when its forward (+Z) matches the direction the viewer is LOOKING** — not when it points at the viewer. Unity's default scene is the proof: camera at z=-10 looking toward +Z, canvas unrotated, text the right way round. So a wall message must face **away from the room, into its wall**. Getting it backwards renders the text mirrored, which is what happened to all four of Room3's messages. "Face the normal inwards" is the intuition to distrust — it is what you would do for a physical sign.
- **A `RectTransform`'s serialized `m_LocalPosition` is stale, and reading it will convince you a correct build is broken.** Its x and y come from `m_AnchoredPosition`; Room3's wall messages all serialize as `{0,0,0}` while sitting exactly where they should. **Inspect `m_AnchoredPosition`.** Related: `AddComponent<Canvas>()` (or any UI component) *replaces* a plain `Transform` with a `RectTransform`, so a position written before that call is discarded.
- **There is no scripting API that creates a layer.** `EnsureLayer` edits `ProjectSettings/TagManager.asset` through a `SerializedObject`, so the build has a side effect **outside the scene** — expect that file in a diff after a fresh clone's first build. It is idempotent by name. Indices **0-7 are Unity's own**; three look blank and are not, and writing into one is silently dropped, so the search starts at 8.
- **`-nographics` cannot render anything**, which is why the menu capture checks `SystemInfo.graphicsDeviceType`. Anything else needing a real render must make the same check or the canonical headless build stops working.
- A fresh Unity project via `-createProject` does **not** include uGUI — `"com.unity.ugui": "2.0.0"` had to go into `Packages/manifest.json` before any `Text`/`Canvas` script would compile.

## Next steps

1. **Play-test the whole run, start to finish.** This is the first build that can be *finished* and nothing in it has been played. Three things code cannot settle: whether four pads read as "all of them, at once" without text; whether the ghosts converge in a way that makes the final walk-through feel earned rather than like waiting; and whether the ~7-iteration floor is satisfying or a grind. Known pressure point: whichever ghost bursts the key balloon does so at the same timestamp forever, and every Room3 setup iteration sits behind that.
2. **Ghost reset trigger** (spec §4.6, which leaves the trigger undecided). Ghosts accumulate forever and the run now needs at least six, so the room is genuinely crowded by the time it is solved. **This is the last real gap.** Needs both a trigger condition and a presentation.
3. **Re-deploy to itch.io.** Three scene-affecting passes have landed since the shipped build.
4. **Rooms beyond Room3** — added on request, not speculatively. Mechanically: `BuildRoomShell`/`BuildCeilingLights`/`BuildReflectionProbe` at `3 * RoomPitch`, Room3's `capFarSide` going false, **and `EscapeTrigger` moving to whatever the new last room is**. Two constraints first: an empty room costs only ~2.4s of the clock to cross, so **distance is not the binding constraint** — Room2's key toll is paid every iteration, so anything further out must be cheap in seconds and expensive in iterations, the way Room3 is. And `RecordedFrame.signals` is a `uint`, so **32 recorded interactables is a hard cap**; six are used.

## Backlog (recorded 2026-08-11, nothing implemented)

### Queued fixes

1. **The drawer reads as a slab sliding out of a solid box.** `nightstand.glb` bakes its whole body into one mesh, so `BuildNightstandDrawer` bolts a generated drawer onto the front face — there is no recess for it to come out of. Fix either way: build the nightstand procedurally (it is a box with a drawer; the room is already procedural everywhere else), or source a model with a real drawer node. **Sourcing is the smaller job but adds a dependency on someone else's topology; building it is more work and matches how the rest of the room is made.**
2. **The pin's left-click prompt should retire after N swings**, not persist forever. Note this is a *partial* return of the retiring that play-testing removed, and the distinction is the point: "shown once" did not teach, but "shown until the player has actually done it several times" both teaches and stops nagging. Count the action, not the display — same rule as the old `interactRetired`.
3. **Tab to switch tools / hold nothing.** `PlayerHand` already tracks a carried set with one `showInHand` item; this is a cycle plus an empty slot. Worth checking against `CarriedItemsDisplay`, which currently reads "what you have", not "what is in your hand".
4. **Shift to crouch, Ctrl to sprint.** ⚠️ This is **inverted from the near-universal convention** (shift=sprint, ctrl=crouch), so expect testers to fight it — flagging it now so the decision is deliberate rather than discovered in a play-test. Both also interact with things already tuned: sprint changes the 7.7m/1.7s margin the pad-to-door close is checked against (see Room1), and crouch changes the eye height the near-clip corner analysis assumed.

### Puzzle mechanics: the three shapes

Worth naming, because the game currently only uses one of them and the other two are where the unexplored ideas live.

1. **Simultaneity** — several conditions held at the same moment. Room1's pad, Room3's four pads. *Implemented, and arguably finished*: more pads is more of the same.
2. **Accumulation** — a quota filled across iterations, where each visit adds to a running total. **Unexplored, and the most valuable direction.** This is the reference film's tree-and-axe (several people chopping before it starts to give), and the same shape as filling an aquarium, cranking a winch, or charging something. It is valuable because it is the only shape where **an iteration spent badly still counts for something** — where today a wasted iteration is wasted entirely.
3. **Knowledge** — a combination, an arrangement, a sequence. **Unexplored, and the cheapest per iteration**: knowledge is the one piece of state the loop **cannot** rewind, because the player carries it in their head. Once learned it costs zero iterations forever. Chess-piece placement and a numeric lock both live here.

**The constraint that governs all of them: a ghost can replay an *action*, never a *possession*.** Ghosts have no colliders and no inventory, by design (see the key lock). So:

- **Accumulation works only if the tool belongs to the station, not the hand.** An axe the player carries makes chopping conditional on holding it, and re-evaluating that condition for a ghost fails exactly the way `KeyRevealed` did. An axe (or bucket, or crank handle) **fixed at the tree** turns chopping into an action performed in place, which a ghost can repeat.
- **A water/aquarium quota must be a valve or a pump, not carried buckets** — same reason.
- **A chessboard cannot be ghost-assisted at all**, since placing a piece is carrying one. That is not a reason to drop it: make the hard part the *arrangement* (knowledge, category 3) and the execution 20 seconds once known. Not every room needs ghosts — but a room that needs them and cannot have them is broken.

### Open question: one loop, or chapters?

Today every puzzle sits inside one 60-second loop, from one bed, with ghosts accumulating forever. The alternative is **chapters**: clear a section, wake in a *new* bed, start a fresh set of iterations.

Chapters would answer three live problems at once, which is why this needs deciding before more rooms go in rather than after:

- **The ghost reset** (next steps §2) gets its trigger for free — the chapter boundary *is* the reset, with a reason the player understands.
- **Room2's key toll** stops compounding. Today every iteration re-crosses every solved room; a chapter boundary retires them.
- **The ~7-iteration floor stops growing.** Each new room currently adds setup iterations that all sit behind every earlier room's cost.

What it costs is the thing the prototype is actually about: **one unbroken loop is the premise**, and cutting it into chapters makes each one a small puzzle box rather than a place you are trapped in. It also throws away accumulated ghosts, which are the visible record of the work — the ending's last image is four past selves holding pads, and that only lands because they were all earned in one run.

### Rejected, on grounds that could change

- **Etching an `E` glyph on every fixture** to teach interaction — fully diegetic, always true, free at runtime. Lost to the prompt disc because a permanent label is *permanently* on screen in a game whose whole texture is repetition, and because the prompt generalises to controls with no fixture to etch (the mouse button belongs to the thing in your hand). If the discs ever read as too game-like, this is the swap.
- **Letting the key stay in the lock** as world state the loop does not rewind, which would make Room2 cheap to re-cross. Rejected — but see the toll it leaves, which constrains every room behind it.
- **Spec deviations still open**: the floor pad sits on the bed's left where spec §3 says right (it follows `room_layout_sample.png` instead), and the ghost reset of §4.6 is not implemented.

---

**Under git**, with **Git LFS configured for `*.glb`** (`.gitattributes`) — both furniture models are committed as pointers. Every tuning value in this file was found by eye over long sessions, so the editor's undo is no longer the only safety net.

# Iteration Room

Unity prototype of a 1-room, first-person time-loop puzzle game (ref: 2016 short film "Iteration 1"). Full design spec: `iteration-game-spec.md`. Layout reference sketch: `iteration_room_layout.png`. `room_layout_sample.png` is an actual film still used as the furniture-placement reference (bed/nightstand/floor-button positions).

**Status**: core loop implemented and manually play-tested as working (hold floor button for a full iteration → next iteration the ghost holds it while the player opens the door). A polish pass (centered furniture, bigger wall grid, real furniture models, shadow-like ghost, jump) has also been applied and rebuilt cleanly. A QA pass (2026-08-09) then found and fixed three defects that a play-test doesn't surface on its own — see **QA findings** below — and an audio/narration layer has been added.

## Stack

- Unity 6000.5.7f1, **Universal Render Pipeline** (`com.unity.render-pipelines.universal` 17.5.0). It started on Built-in deliberately, but the user asked for a realistic look and Built-in with no post-processing was the actual bottleneck — everything reads flat no matter what the materials do. Pipeline assets live in `Assets/Settings/` (`IterationURP` + `IterationRenderer` + `IterationVolume`) and are wired into both `GraphicsSettings.defaultRenderPipeline` and `QualitySettings.renderPipeline`.
  - Materials must use `Universal Render Pipeline/Lit`, **not** `Standard` — URP has no Standard shader, so a material left on it renders magenta. `SceneBuilder.OpaqueShader()` resolves whichever exists, and `SetSmoothness()` handles Standard's `_Glossiness` vs URP's `_Smoothness`.
- Unity MCP: [CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp), registered with Claude Code via `claude mcp add --scope local --transport stdio UnityMCP -- <uvx path> --from "mcpforunityserver==<version>" mcp-for-unity`. **MCP servers only load at session start** — if you're reading this in a session where Unity MCP tools aren't showing up in ToolSearch, that's expected if the session predates registration; the fix is simply to start a new Claude Code session in this directory, not to re-register anything.
- `uv`/`uvx` (installed via winget `astral-sh.uv`) is required for the MCP server to run.

## How this project gets built

There is **no manual scene editing history** — the entire scene is assembled by one script, run headlessly. This is the pattern to keep using:

1. Edit C# under `Assets/Scripts/` and/or `Assets/Editor/SceneBuilder.cs`.
2. Rebuild headlessly:
   ```
   "C:\Program Files\Unity\Hub\Editor\6000.5.7f1\Editor\Unity.exe" -batchmode -nographics -projectPath "C:\Users\seonl\Desktop\c\2026\summer\Iteration" -executeMethod IterationRoom.EditorTools.SceneBuilder.Build -quit -logFile <path-to-log>
   ```
3. Check the log for `error CS` (compile errors) or exceptions before assuming success. A `[SceneBuilder] IterationRoom scene built at ...` line near the end plus exit code 0 means it worked. Benign noise to ignore: `[Licensing::Client] Error: HandshakeResponse...` retries at startup that self-resolve.
4. To actually see/play it, launch the Editor GUI directly (Unity Hub's "Open" project picker has been flaky on this machine — "프로젝트를 찾을 수 없습니다" even on a valid project — so prefer launching the Editor binary directly):
   ```
   "C:\Program Files\Unity\Hub\Editor\6000.5.7f1\Editor\Unity.exe" -projectPath "C:\Users\seonl\Desktop\c\2026\summer\Iteration"
   ```
   Then open `Assets/Scenes/IterationRoom.unity` and hit Play. Check `Get-Process Unity` before launching a second instance — batchmode against a project already open in the GUI will conflict.

**Player Settings → Run In Background is ON**, and needs to stay on. Without it, play mode stops ticking the moment the Editor loses focus, so any MCP-driven test (teleport the player, open the door, screenshot) captures a stale frame and looks like the change did nothing — `Time.time` sits at ~0.04 while wall-clock seconds go by. `SceneBuilder.Build()` sets it, because setting it *during* play mode does not persist.

Once Unity MCP is connected in a session, prefer it for incremental/visual tweaks (moving objects, tweaking materials interactively) over round-tripping through `SceneBuilder.cs` + batchmode. Keep `SceneBuilder.cs` as the source of truth for anything structural, though — it's what makes the whole scene reproducible from scratch.

## Scene layout (current coordinates, all under namespace `IterationRoom`, room shell built in `Assets/Editor/SceneBuilder.cs`)

- **Two rooms**, built by the same `BuildRoomShell` at different Z centres: `Room1` (the loop room, centre Z=0) and `Room2` (identical, empty, centre Z=`RoomPitch`=10.85, i.e. `RoomDepth 10.5 + 2×WallDepth 0.125 + DoorPocketDepth 0.1`). They share a divider — Room1's north wall and Room2's south wall sit back to back with the same doorway cut out of both the panelling and the collision, making a walk-through passage. Scene paths are `Room/Room1/...` and `Room/Room2/...`; furniture and buttons still hang off `Room/` directly.
  - Floor/ceiling slabs span the full `RoomPitch`, not just the interior, so adjacent rooms' floors meet exactly under the divider. Sized to the interior they leave an open gap at the threshold and the player drops through it.
- Room bounds (each room): X -4.375..4.375 (width 8.75), Z ±5.25 about its centre (depth 10.5), height **5.0**. Door, bed, and spawn all share the X=0 axis (centered layout). `BuildRoomShell`'s minZ/maxZ are derived as `zCenter ± depth/2` — don't hardcode them, it'll decenter the room.
  - The height is a **whole number of grid cells (5 x 1m) on purpose**. At 4.5m the top row was a half cell, so the panelling ran off cut in half at the ceiling. Keep `RoomHeight` an exact multiple of `GridCellHeight`.
- Bed centered at (0, 0.7) (X/Z) — head/pillow end at high Z (near the door), foot at low Z (near spawn). It is `messy_bed.glb`. The model ships with **two pillows**, both off the centre line; `UseSinglePillow` hides one and slides the survivor onto X=0. They're separate nodes in the glb, so that's a transform tweak — a future bed model that bakes its pillows into the bedding mesh couldn't be fixed this way.
- Nightstand (`nightstand.glb`, with a lamp and vase on it) sits at (-0.95, 1.35), tight against the bed's west side and level with the pillow. It replaces the bedside table that came bundled with the previous bed model.
- Floor button sits in the open floor area past the foot of the bed, east side, around (2.8, -1.75) — also per `room_layout_sample.png`.
- Door + door button are centered on the north wall (Z=5.25), X=0. The door is a **natural size (`DoorWidth` 1.3 x `DoorHeight` 2.5) and deliberately NOT snapped to the grid** — an earlier version forced it to exactly one cell wide and two tall, which read as a missing panel rather than a doorway. Instead the panelling is cut around it: `BuildShell` passes a doorway `Rect` to `BuildPanelWall`, and `SubtractRect` splits any panel that overlaps it into up to four surrounding pieces, so panels frame the door at whatever size it is.
  - The door **slides right into a pocket** (`Door.openLocalOffset` = +X by `DoorWidth`). The slab sits in the `DoorPocketDepth` cavity between the two rooms' walls, so sliding it sideways tucks it inside the wall build-up and both walls hide it. The door button is deliberately on the **left** (`-GridCellWidth`) — on the right it ends up buried behind the open slab.
  - The doorway is cut out of the **backing slabs as well as the panels** (both are built from `SubtractRect` pieces). With the backing left as one solid slab the opening was a dead-end recess — collision was already cut so you could walk "through", but you were looking at wall.
- Bed spawn point (loop reset location) sits south of the bed's foot, at (0, -1.75).
- No point light in the room (removed — read as too harsh/blown-out). Lighting (`SceneBuilder.SetupLighting`) is carried almost entirely by **flat ambient** (`RenderSettings.ambientMode = Flat`, color ≈ (0.95, 0.95, 0.97)), because ambient hits every surface equally — including the ceiling, which a directional light never reaches. The default Directional Light is left weak and steeply angled (Euler (72, 200, 0), intensity 0.25, soft shadows at strength 0.5) purely for enough shading to read geometry. A strong low-angle directional blew out one wall while the opposite wall read grey — **don't raise its intensity to brighten the room, raise ambient instead**; pointed this steeply down it blows out the floor long before it does anything for the walls.
  - These numbers are URP-specific and were retuned once already: URP responds to ambient very differently from Built-in. The old Built-in value of 1.05 blows the entire room to featureless white under URP, while 0.72 leaves the walls grey. Re-tune by eye if the pipeline or tonemapper changes.
- Post-processing (`SceneBuilder.BuildPostProcessing`) is a global `Volume` in the scene pointing at `Assets/Settings/IterationVolume.asset`: Neutral tonemapping (ACES crushes the near-white walls to grey and warms them), a subtle vignette, contrast +8, and a deliberately high bloom threshold of 1.2 — the walls sit near 1.0 luminance, so a default threshold blooms the whole room. The player camera opts in via `UniversalAdditionalCameraData.renderPostProcessing`; URP cameras ignore the volume stack otherwise.
- Ambient occlusion is a **renderer feature on `IterationRenderer.asset`, not a volume override**, so it lives outside the scene. `SceneBuilder.ConfigureAmbientOcclusion()` reconciles it on every build because adding it is not idempotent — a retried add silently stacks duplicates (this already happened once, producing six SSAO features). Settings that matter:
  - **`Samples`, `NormalSamples` and `BlurQuality` run `High=0, Medium=1, Low=2`, i.e. 0 is the best quality, which reads backwards.** Setting `Samples` to 2 drops it to 4 samples and sprays visible occlusion noise around every object.
  - `Radius` is the other half of the noise problem, and the two were fixed in separate passes. The stock 0.035m reads as no occlusion at all here, but 0.12 throws a broad dithered halo around small objects (the door button, door edges) which on flat white walls reads as *dirt*. 0.045 with `Intensity` 0.7 and `AOMethod = InterleavedGradient` is the clean point: tight contact shadows only.
  - If the scene ever looks "dirty/noisy around objects" again, it's this feature, not the lighting.
- All room materials are matte (`_Glossiness` 0.03) and `RenderSettings.reflectionIntensity` is 0.1. Standard's default 0.5 smoothness mirrors the blue procedural skybox onto the walls/floor, which reads as a weird blue tint.
- Wall grid cells are 1.75m wide x 1m tall everywhere. **There is no grid texture any more.** The grid is entirely geometry: white panel cubes over a near-black backing (`GrooveDark`), so what shows at the bottom of each groove is the dark backing. The old `WallGrid`/`WallGridSide` textured materials, their 2048px baked texture and all the tiling/offset maths were deleted when the backing had to be split into pieces to cut the doorway — a split slab can't carry a tiled texture correctly, since each piece's UVs run 0..1 across *itself*. Cell size is driven by `GridCellWidth`/`GridCellHeight` and groove width by `GridLineThickness`.
- The floor/ceiling slabs are pushed **out by half their thickness** so their inner faces sit exactly on the room bounds, rather than sinking half the slab into the room.
- **Walls are real panel geometry, not just a textured plane** (`SceneBuilder.BuildPanelWall`). Each wall is a recessed backing slab plus a grid of raised panel cubes, so the seams are actual grooves (`GrooveDepth`) that catch shadow and ambient occlusion. Two things to know before touching it:
  - Neither the panels nor the backing slab carry colliders. Collision is a **separate set of invisible boxes** built by subtracting the doorway from the wall rect (same `SubtractRect` used for the panels). A collider on the backing slab would be `GrooveDepth` too far back — the player walks past the panels and the camera's near plane clips through the wall — and it would also seal the doorway shut.
  - `GrooveDepth` is a tradeoff, not a free parameter: deeper catches more occlusion, but the panels' white side faces then wash the seam out when you view a wall at a grazing angle. 0.025 is the balance point found by eye.
- Countdown timer text (`CountdownTimer`, top-right) is red (`Color.red`).

## Scripts (`Assets/Scripts/`, namespace `IterationRoom`)

- `Player/FirstPersonController.cs` — CharacterController-based Portal-style movement + mouse look + jump (`jumpForce`, Space/`Jump` axis).
- `Loop/LoopManager.cs` — singleton driving the 60s loop. The whole loop is **one coroutine (`RunLoop`), not `Update`**: an iteration now opens and closes with the wake-up sequence, and the clock and the ghosts have to stay frozen while that plays. Recording deliberately starts only once the player has control, so every ghost's timeline covers the same window — start it before the wake-up and you get a pile of frames all stamped t=0.
- `Loop/WakeUpSequence.cs` — the transition between iterations: eyelids (two black UI panels driven through their *anchors*, so they cover any resolution) fall shut as time runs out, then open on the ceiling before the player sits up. It borrows the camera by clearing `FirstPersonController.ControlEnabled` and posing the eye via `SetEyePose(height, pitch)`. Negative pitch looks up.
- `Loop/IterationLabel.cs` — fades the center-screen "Iteration N" text in/out. The eyelids are built first in `BuildUI` so they sit at the back of the canvas and the label/timer draw on top of the black rather than being covered by it.
- `Ghost/RecordedFrame.cs`, `Ghost/PlayerRecorder.cs`, `Ghost/GhostReplayer.cs`, `Ghost/GhostInteractable.cs` — records player position/yaw plus a **bitmask of interaction signals** at a fixed sample interval; ghosts scrub the timeline and report those signals back (not via physics — **ghosts have no collider at all**, so they can never trigger anything by touching it).
  - `GhostInteractable` is the abstraction: anything a ghost can operate exposes `PlayerSignal` (sampled into one bit) and `SetGhostSignal` (fed that bit back an iteration later). `FloorButton` and `DoorButton` are the two implementations. **This was originally hardcoded to one `FloorButton`**, which meant a ghost's only possible contribution was standing on that one pad — a play-test found the door never re-opened by itself because the door-button press was never recorded in the first place.
  - **Bit position is the object's index in `LoopManager.ghostInteractables`**, and `PlayerRecorder.interactables` must be the same array in the same order. `SceneBuilder` builds one array and hands it to both. Reordering it invalidates every timeline recorded so far.
  - **Signals are levels, not events.** A ghost advances its cursor by elapsed time and can skip several recorded frames in one tick, so a one-frame pulse would eventually be missed. Press-type interactables therefore stretch their pulse (`DoorButton.pressPulseDuration`, 0.15s) instead of recording an edge, and act on the rising edge at replay.
  - **Only the real player's actions may raise a signal.** `DoorButton.RegisterPlayerPress` sets the pulse; a ghost's replayed press goes straight to `TryPress` and deliberately bypasses it. Route a ghost through the recording path and each iteration inherits every press of the one before — the door would open earlier and earlier until it opened on frame one.
  - The ghost is a **rough human silhouette assembled from primitives** (head/torso/2 arms/2 legs), not a capsule. It's held at alpha 0.16 near-black on an unlit transparent material (`GhostFaint`), which is low enough that the crudeness of the primitives never reads — only the outline does. Unlit matters: a lit ghost picks up shading and specular that give the primitives away.
  - Limbs hang off **empty pivots at the shoulders and hips**, because a primitive's own pivot is at its centre — rotating the capsule directly would spin it about its middle instead of swinging from the joint.
  - Only position and yaw are recorded, so the walk is **inferred from distance travelled** (`GhostReplayer.SwingLimbs`). Phase advances with distance rather than time, which keeps stride length constant instead of the legs spinning faster the quicker the ghost moves, and the amplitude scales with speed so a stationary ghost stands still rather than marching on the spot.
- `Interactables/FloorButton.cs` — hold-type; active if the real player OR any ghost is currently holding it. The player side is **polled in `FixedUpdate` (bounds overlap), deliberately not `OnTriggerEnter/Exit`**. `Teleport` disables and re-enables the CharacterController inside a single frame, so a player standing on the button when the iteration ended never generated the exit callback: the button stayed lit forever with nobody on it, and since `PlayerRecorder` samples `PlayerHolding`, every ghost recorded after that held it forever too. Don't "simplify" this back to trigger events.
- `Interactables/DoorButton.cs` — one-touch (trigger enter, or `E` while in range); only opens the door if the linked `FloorButton.IsActive`.
- `Interactables/Door.cs` — slides the door panel by `openLocalOffset` and swaps the indicator light colour when opened. `SceneBuilder` sets that offset to +X, i.e. sideways into the wall pocket — the field's own default is still a vertical slide, so read the built value, not the declaration. `Close()` snaps it shut again and is called by the loop every iteration.
- `Audio/NarrationDirector.cs`, `Audio/RoomAmbience.cs`, `Audio/FootstepPlayer.cs` — the PA announcer, the room tone/machinery, and distance-paced footsteps. See the **Audio** section for the cue schedule and why the clip slots are allowed to be null.

## Audio

The room has a diegetic PA announcer, taken from the reference film: every iteration is announced on a fixed schedule. That schedule is the whole system — four cue types per loop.

| Cue | When | Method |
| --- | --- | --- |
| "Iteration N, 60 seconds remaining." | after the wake-up, as the clock starts | `NarrationDirector.AnnounceIteration` |
| "10 seconds remaining." | `LoopManager.tenSecondCueAt` (T-12, not T-10) | `AnnounceTenSeconds` |
| "Nine." … "One." | one per whole second, T-9 to T-1 | `AnnounceCountdown` |
| "New cycle initialized." | top of the next iteration, under the closed eyelids | `AnnounceNewCycle` |

- **The T-10 line is cued at T-12 on purpose.** It runs about two seconds, and from T-9 the digit countdown replaces whatever the announcer is saying every second, so cueing it at a literal T-10 clips it after one word. `tenSecondCueAt` is serialized — retune it if the line is ever re-recorded at a different length.
- Cues are **pushed from `LoopManager.RunLoop`, not polled** by the director. The loop already owns `ElapsedTime`, and the announcer has to stay silent through the wake-up, which sits outside the timed window. The counting loop guards on a *falling* whole second, so it fires exactly once per second at any frame rate.
- Iteration 1 gets no "New cycle initialized" and no reset sting — it opens the run rather than resetting it.
- **Announcements replace each other** (`Stop()` + `Play()`), never `PlayOneShot`. The countdown fires once a second and one-shots would leave the digits slurring over each other.

### Where the audio comes from

- **Narration is generated, not sourced.** `Tools/generate_narration.ps1` drives the Windows built-in synthesizer (`Microsoft Zira Desktop`, en-US female) to write all 42 lines into `Assets/Audio/Voice/` as 22 kHz 16-bit mono WAV. It costs nothing, runs offline, and keeps the voice track in the same "reproducible from a script" category as the rest of the project. The flat synthetic delivery happens to suit a facility tannoy. To replace them with better-acted takes, keep the filenames — no C# refers to how they were made. `SceneBuilder.NarrationIterationLines` must match the range the script writes (currently 30, then a generic line stands in).
- **SFX are dropped in by hand and resolved by filename** — see `Assets/Audio/SFX/README.md` for the table of names. `SceneBuilder.BuildAudio` looks each one up via `LoadClip`, which is extension-agnostic (`.wav/.ogg/.mp3/.aif/.aiff`). **A missing file resolves to `null` and every player guards on it**, so an empty SFX folder is a valid, playable state — narration plays and the SFX channel is simply silent. Dropping a correctly-named file in and rebuilding is the entire wiring step.
- Unity MCP's `generate_audio` is **not** an option for the voice: it does music/SFX only, no speech. It also needs a fal.ai key that is not configured on this machine.

### Sources and spatialisation

`SceneBuilder.MakeSource` builds every `AudioSource` with linear rolloff (`maxDistance` 14) rather than Unity's logarithmic default, which stays near full volume across a room this small.

- **2D** (`spatialBlend` 0): the PA voice and chime (a room-wide tannoy has no position to walk away from), the music bed, the machines, the player's own footsteps and breath.
- **3D**: the door, and the prop rattle — which is parented to the **nightstand** so it comes from the lamp and vase standing on it.
- `FootstepPlayer` paces steps by **distance travelled**, not by a timer, for the same reason `GhostReplayer.SwingLimbs` does. It reads nothing but the transform, which means the identical component works on a ghost — deliberately left off the Ghost prefab, since ghosts accumulate without limit and so would their footsteps. It also ignores any frame faster than `teleportSpeed`, so the loop's reset teleport doesn't fire a burst of steps.
- `Door.Close()` is **deliberately silent** — that's the loop rewinding world state behind a black screen, not the door being shut. A sound there draws attention to the seam.
- Exactly **one `AudioListener`** exists, on the player camera. Adding a second is a Unity warning and breaks positional audio.

## QA findings

A full QA pass on 2026-08-09 (build verification + code review + scene-file inspection + spec conformance). Fixed:

1. **The closed door had no collider.** `SceneBuilder` built `DoorPanel` with `removeCollider: true`, and `BuildPanelWall` cuts the doorway out of *both* walls' collision — so the two rooms were permanently connected and the player could walk straight through the closed door, bypassing the puzzle entirely and violating spec §2's "단일 iteration만으로는 절대 풀 수 없다". Invisible while Room2 was still a dead-end recess. The slab now keeps its collider; once open it sits at x 0.65..1.95, entirely behind the wall's own collision, so it never blocks the opening it just cleared.
2. **The door was world state the loop never rewound.** `Door.IsOpen` stayed true forever and `Open()` early-outs on it, so every iteration after the first solve began with the door already open. `Door.Close()` now snaps it back, called from `RunLoop` *after* the teleport so a player standing in the doorway is already back at the bed.
3. **`DoorButton.playerInRange` got stuck true**, the same defect already documented on `FloorButton`: `Teleport` disables and re-enables the CharacterController inside one frame, so `OnTriggerExit` never arrives. Standing at the button when an iteration ended left `E` opening the door from anywhere in the room, including from the bed. Now polled in `FixedUpdate` via bounds overlap, pressing on the rising edge.

Still open (found, not fixed):

- **Floor button's activation zone is much larger than it looks.** Its trigger is `radius 0.5, height 0.5` — height < 2×radius, so Unity clamps it to a *sphere* — and `FloorButton` tests AABB overlap, so with the player's 0.3 radius the real condition is a ±0.8 m box against a visible disc of radius 0.35. You can stand ~0.5 m clear of it and it lights.
- **Control and recording stay live through `CloseEyes()`** (~1.6 s of blind walking), and those frames all stamp `ElapsedTime ≈ 60.0`, so the ghost skips them in one jump on its final tick.
- **`EditorBuildSettings.m_Scenes` is empty** — a standalone build would ship no scenes.
- **Every rebuild regenerates all fileIDs**, so `IterationRoom.unity` shows ~6,675 changed lines (26% of the file) with zero semantic change. Since `SceneBuilder` is the source of truth, `.gitignore`-ing the generated scene would be the consistent move.
- Spec deviations: the door button flashes red on denial where spec §5 says "아무 반응 없음"; the floor button sits on the bed's left where spec §3 says right (it follows `room_layout_sample.png` instead); the ghost reset of spec §4.6 is not implemented at all.

## Art assets

- `Assets/ArtAssets/Furniture/` holds exactly what the scene uses: `messy_bed.glb` and `nightstand.glb`. Five bed models were placed side by side and compared in-engine; `messy_bed` won because its bedding is rumpled and slept-in — the loop opens every iteration by waking up here, so a neatly-made bed undercuts the whole premise. Cost of that choice: **306,907 triangles**, 20-60x the alternatives (fine for one object, worth knowing before adding more). The four rejects were deleted afterwards (~78MB); if a different bed is ever wanted, they have to be re-downloaded.
  - **`.glb` needs `com.unity.cloud.gltfast`** (6.19.0, installed). Without it Unity imports a `.glb` as an opaque `DefaultAsset` and it can't be instantiated at all.
  - Keep the glTF materials these ship with; a hand-rolled URP/Lit stand-in drops the metallic/roughness maps.
  - **These models bake contact shadows into their occlusion maps**, which only looks right in the exact pose they were authored in. Moving the pillow and hiding its neighbour left those shadows painted on the bedding — a dark smear beside the pillow and a hard black band across the pillow itself, which read as dirt rather than shading. `DisableBakedOcclusion` fixes it by cloning the material into a project asset with `occlusionTexture_strength = 0` (applied to `Sheet` and `Pillow_2`); the scene's SSAO then supplies contact shadows that follow the actual geometry. The clone has to be its own asset because the glb's materials are sub-assets that get regenerated on every reimport. If furniture ever shows a shadow that doesn't move with the light, this is the cause.
  - **All five are authored Z-up**, so every one needs `rotation: Quaternion.Euler(-90, …)` to stand up — but their **units differ wildly**, and their bounds are not centred on the origin. Don't assume centimetres: `old_bed` is already in metres, `lowpoly_bed` sits 2.8m off-origin. The reliable way to place a new one is to measure its bounds at scale 1 and derive `uniformScale = 2.05f / max(horizontal extent)`, which is how `messy_bed`'s 0.00941 was found. `PlaceModel` then handles centring and floor-seating.
- `SceneBuilder.PlaceModel(...)` instantiates these via `PrefabUtility.InstantiatePrefab` and repositions them so their *rendered bounds* (not their raw mesh pivot, which is at an arbitrary corner) are centered on a target X/Z with their base resting on a given floor Y. Reuse this helper for any future model placement instead of hand-computing offsets. It takes an optional `rotation`, applied **before** bounds are measured, so a model authored Z-up or facing the wrong way still lands centred and on the floor; the generated `BoxCollider` un-rotates the world-axis-aligned bounds back into local axes so a rotated model doesn't get its height and depth swapped.
- Room shell stays procedural — walls are generated panel geometry (`BuildPanelWall`), floor/ceiling are plain white. That is on purpose: the reference room's walls are plain white panels, so realism there comes from geometry and lighting, not from albedo textures. Don't add wall/floor textures without being asked.
- **Furniture is the remaining gap.** The Kenney models are deliberately low-poly with flat per-face colours, so no texture work makes them look real — they have to be replaced with better models. Recommended CC0 sources: Sketchfab's CC0 filter (best furniture selection), Poly Haven (props/textures/HDRIs), ambientCG (textures). Claude can't download these, so the user drops them in `Assets/ArtAssets/Furniture/` and `SceneBuilder.PlaceModel` handles placement.
- Ghost material is `GhostFaint.mat` — URP Unlit, transparent, near-black at alpha 0.16, so it reads as a faint afterimage rather than a solid lit shape. The URP transparent set-up needs all of `_Surface`/`_SrcBlend`/`_DstBlend`/`_ZWrite` **and** the `_SURFACE_TYPE_TRANSPARENT` keyword; set the colour alone and the shader stays opaque, rendering a solid black mannequin.

## Gotchas already hit (don't rediscover these)

- A fresh Unity project via `-createProject` does **not** include `UnityEngine.UI` (uGUI) by default — needed `"com.unity.ugui": "2.0.0"` added to `Packages/manifest.json` before any `Text`/`Canvas` script compiles.
- Unity Hub's folder-picker "프로젝트를 찾을 수 없습니다" error on this machine is a Hub UI issue, not a project problem — launch `Unity.exe -projectPath ...` directly instead.

## Scope boundaries

Multi-room expansion has **started**: a second identical room shell now sits beyond the door (see Scene layout). It is whitebox only — no furniture, no puzzle logic, no second loop. Adding further rooms is just more `BuildRoomShell` calls at multiples of `RoomPitch`.

## Next steps (spec section 7, in priority order)

The prototype's core loop (spec section 5) is **done and play-tested**. What's left:

1. **Escape detection + ending — nothing happens when you solve the puzzle.** `LoopManager.RunLoop` is an unconditional `while (true)`: it never checks whether the player got out, so opening the door and walking into Room2 still yanks them back to the bed at 60s. The puzzle currently has no win state at all. Smallest scope, biggest payoff — the prototype can't be "finished" by a player until this exists. Spec section 7 leaves the ending undefined, so the shape of it is a design decision to make first.
2. **Ghost reset trigger.** Spec section 4.6 says a reset exists but deliberately leaves the trigger undecided. Ghosts accumulate forever right now, so after ~10 iterations the room fills with replaying figures. Needs both a trigger condition and a presentation.
3. **Room2 puzzle content.** Spec section 7's multi-step accumulation idea (investigate drawer/cup/lamp → learn something → act on it in a later iteration). Depends on 1 and 2 being settled.

**This project is under git** as of the initial commit, with **Git LFS already configured for `*.glb`** (`.gitattributes`) — verified working, both furniture models are committed as pointers. `.git` is ~90MB. Every tuning value in this file was found by eye over long sessions, so the editor's undo is no longer the only safety net.

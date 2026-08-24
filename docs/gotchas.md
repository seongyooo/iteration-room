# Gotchas

Things that cost a session to discover once. Do not rediscover them.

---

## Gotchas (don't rediscover these)

- **Windows cannot make a valid zip with the obvious tools.** Both `Compress-Archive` and
  `[IO.Compression.ZipFile]::CreateFromDirectory` write `Path.DirectorySeparatorChar` into the entry
  names, so every path in the archive comes out as `Build\WebGL.loader.js`. The ZIP spec requires
  forward slashes, and **itch.io takes the backslash literally** - it creates one file whose NAME
  contains a backslash rather than a `Build/` directory. The symptom is the cruel one: `index.html`
  loads perfectly and everything it references 404s, so the page looks like it is nearly working.
  `Tools/make_webgl_zip.ps1` builds the entry names by hand and refuses to finish if any of them
  still holds a backslash.
  - **Verify the ARCHIVE, never the folder.** This shipped because the folder was listed and found
    correct - which it was, and always had been. The archive was the thing that was wrong.
- **EVERY REFLECTION PROBE IN THIS SCENE REFLECTED NOTHING, THROUGH FOUR INDEPENDENT FAULTS ON ONE FEATURE — and that, not any material setting, is what "this project cannot light a pure metal" actually was.** All seven probes existed, were configured, were baked, and all seven `_Reflection.exr` files sat on disk looking perfectly correct. Each fault below hid the next, so each fix appeared to change nothing:
  1. **Baked too early.** `BuildReflectionProbe` baked inside `BuildShell`, and `SetupLighting()` runs after it — changing scene lighting after a bake drops the reference. → bake last, in `BakeReflectionProbes()`, just before the scene is saved.
  2. **A Baked-mode probe does not carry its own cubemap.** The reference lives in the scene's **lighting data asset**, which this build never generates, so `bakedTexture` was set in memory and came back `NULL` after save-and-reopen. → `ReflectionProbeMode.Custom` + `customBakedTexture`, which is a field on the component and serialises with the scene.
  3. **URP gates box projection and probe blending on the pipeline ASSET**, and both were off. `probe.boxProjection = true` on the component — which this project has always set, with a comment explaining why the room shape needs it — does nothing while that is false. → set in `ConfigureLightingPipeline`.
  4. **`Lightmapping.BakeReflectionProbe` renders ONLY renderers flagged `ReflectionProbeStatic`, and SceneBuilder builds everything from script and flagged nothing.** So all seven probes were faithfully capturing an empty world and baking the skybox. → `MarkReflectionProbeStatic()` flags 966 renderers before the bake, excluding movers by component (`CarryableItem`, `Door`, `RewardPlinth`, `Balloon`, `Drawer`, `GhostReplayer`, the player) so nothing bakes in a pose it does not hold.
  - **The test that finally located it, and the one to reach for again:** assign the baked cubemap as the **global** reflection (`RenderSettings.customReflectionTexture`) and look at a pure white mirror (metallic 1, smoothness 1, no emission). Still brown default-skybox ground → the cubemap's *contents* are wrong, not its wiring. That one step separated "the probe is not being applied" from "the probe contains sky", which is where three of these four faults were hiding.
  - **What it cost, in two places at once.** The walls are deliberately glossy at smoothness 0.85, and that decision is written down as only working "because the reflection probes give it the room to mirror" — they never did. And a metal is *entirely* reflection, so a metal with an empty probe has nothing to be: that is the whole of the black gold key, and of the rule drawn from it. With real probes the red cube visibly mirrors the chess board in its own face.
  - **The failure mode is why it survived this long.** An empty probe does not error, does not render magenta, and does not look broken. It looks like a room that is very slightly flat. **`BakeReflectionProbes` logs `wired/total` and the static count** for exactly that reason — if it ever says anything but `n/n`, metal goes black again.
- **A GENERATED MESH'S CAPS CAN BE WOUND INSIDE OUT AND NOBODY WILL SEE IT — until the material changes.** `TriangularPrismMesh` had correct sides and both caps facing *into* the solid, so they were backface-culled: looking down at the yellow triangle you saw through its top face to the inside of the far wall. It survived because the material was emissive at 2.4 and saturated, which makes the inside of a shape very nearly the same image as the outside. A polished metal exposed it immediately.
  - **The test, which is cheap and settles it:** Unity's front face is the triangle whose `cross(v1-v0, v2-v0)` points ALONG the outward normal. Verify the convention against the built-in **Quad**, whose vertices, winding and normals are all known: verts `(-.5,-.5,0) (.5,.5,0) (.5,-.5,0)`, tris `0,1,2`, normal `(0,0,-1)` — and the cross product comes out `(0,0,-1)`. Then check every generated triangle against its own centroid.
  - **A fan following a counter-clockwise ring produces a DOWNWARD normal**, which is the specific trap here: the top cap has to be wound backwards against the ring and the bottom cap with it, not the reverse of what reads naturally.
- **AN INTERACTION VOLUME BELONGS WHERE A PLAYER CAN STAND, NOT WHERE THE FIXTURE IS — and only standing in front of it finds this.** Room4's console has three recesses in the top face of a block **1.15m deep**. Boxes centred on the recesses put their near edge **0.45m beyond** a player pressed against the console's front face: the fixture was visible, prompted correctly in code, and could not be reached. Every number was measured off real geometry and every one of them was right. The fix is a wide box offset toward the side the player approaches from, and **overlapping volumes are fine** when what disambiguates them is not position — the three overlap at the middle of the console and the object in hand decides which answers.
  - Generalises past this console: any fixture set into the **top** of a waist-high solid, or behind a lip, or above a plinth, has a reach volume that is nowhere near its own centre. Check by standing there, in play mode, not by reading the transform.
- **A procedurally drawn glyph has to be LOOKED AT, and three of six were wrong on the first pass.** All six of the cube room's symbols built, imported and measured plausibly (20–40% ink coverage) while being unrecognisable. What the numbers cannot catch: a five-point star whose radius is **lerped between tip and valley across each sector draws a FLOWER** — a star's edges are straight lines, so the test has to be which side of the tip→valley *segment* the sample falls on. Four clover leaves spaced 0.34 apart across and 0.24 up merge into two lobes, and four discs that overlap in pairs but not at the centre leave a **diamond-shaped hole** where none of them reaches it. A heart's triangle wider than its lobes pokes out as two spurs at the shoulders — it has to stop exactly at their span. **Render them and open the PNG.**
- **A Unity cube's opposite faces carry opposite U directions**, so the same texture on the east wall
  is the mirror of the one on the west - and north against south likewise. Wall panels are cubes, and
  this went unnoticed for the whole project because nothing had ever put a texture on one: flat
  colour has no handedness. It surfaced the moment the ending gave every panel a test card, and it
  surfaced **in a screenshot** - no amount of reading the code was going to show it. Fix is a
  per-panel `_BaseMap_ST` U scale of -1 chosen from which wall the panel sits on.
- **A world-space Canvas is legible when its forward (+Z) matches the direction the viewer is LOOKING** — not when it points at the viewer. Unity's default scene is the proof: camera at z=-10 looking toward +Z, canvas unrotated, text the right way round. So a wall message must face **away from the room, into its wall**. Getting it backwards renders the text mirrored, which is what happened to all four of Room3's messages. "Face the normal inwards" is the intuition to distrust — it is what you would do for a physical sign.
- **A `RectTransform`'s serialized `m_LocalPosition` is stale, and reading it will convince you a correct build is broken.** Its x and y come from `m_AnchoredPosition`; Room3's wall messages all serialize as `{0,0,0}` while sitting exactly where they should. **Inspect `m_AnchoredPosition`.** Related: `AddComponent<Canvas>()` (or any UI component) *replaces* a plain `Transform` with a `RectTransform`, so a position written before that call is discarded.
- **There is no scripting API that creates a layer.** `EnsureLayer` edits `ProjectSettings/TagManager.asset` through a `SerializedObject`, so the build has a side effect **outside the scene** — expect that file in a diff after a fresh clone's first build. It is idempotent by name. Indices **0-7 are Unity's own**; three look blank and are not, and writing into one is silently dropped, so the search starts at 8.
- **Driving the open scene to take a screenshot LEAVES IT DRIVEN — rebuild afterwards, always.**
  Posing the player and camera through the editor to render a view of something is the fastest way to
  check work, and the pose does not go away. Worse, it can reach disk: entering and leaving play mode
  left the scene reporting `isDirty == false` with the player still parked where a screenshot had put
  them, so the saved scene had it too.
  - **It presents as two unrelated bugs, and neither one names the cause.** Left in Room2, the report
    was "the game starts in Room2 **and the walls are black**" — the walls because panels are authored
    at the switched-off value and `WakeUpSequence` boots them with `PowerDown()`/`PowerUp()`, and the
    calibration room is deliberately excluded from that array. So starting anywhere but the
    calibration room means starting before the boot, and the room looks broken rather than misplaced.
  - The fix is one build: `SceneBuilder` is the source of truth for the player's start pose. The habit
    is to rebuild after any inspection pass that moved something, rather than trying to restore each
    field by hand - a `finally` that puts back `targetTexture` and `clearFlags` will not save you,
    because the thing that mattered was the player's transform.
- **Rewriting the URP asset during a build can cost that build's very next render.** The menu
  background is captured mid-`Build()`, and the one build that also rewrote `IterationURP` (main
  light shadows off, shadow atlas 4096→2048, shadow distance, SSAO downsample) captured a frame with
  **the skybox and nothing else** — every lit surface missing, and the unlit `BEGIN` label two rooms
  away showing through the wall that should have hidden it. URP drops and rebuilds its pipeline
  instance when its asset changes, and the render request went in during that window. It does not
  reproduce once the pipeline is warm: the identical code, rebuilt, captured correctly.
  - **The camera was never in the wrong place.** The mirrored `BEGIN` read like a camera fault and
    is not one — the label faces the calibration spawn, so seeing it *through* Room1's missing south
    wall shows it from behind. Confirm placement before blaming it: `bedSpawnPoint` is
    `Room/BedSpawnPoint`, world `(0, 0.05, -0.7)`, euler `(0, 180, 0)`, scale 1.
  - **What made this shippable was that nothing failed.** The build logged success and a skybox went
    to itch.io behind the title screen. `CaptureMenuBackground` now clears to magenta instead of the
    skybox and rejects any frame more than 1% clear colour, keeping the previous PNG and logging an
    **error** — the rooms are sealed boxes with the camera inside one, so a correct frame measures
    zero stray pixels out of 1920×1080. Retries once first, which is all the pipeline window needs.
  - Generalises: **anything that renders inside a build must check what came back.** A render request
    that returns garbage returns it silently.
- **`-nographics` cannot render anything**, which is why the menu capture checks `SystemInfo.graphicsDeviceType`. Anything else needing a real render must make the same check or the canonical headless build stops working.
- A fresh Unity project via `-createProject` does **not** include uGUI — `"com.unity.ugui": "2.0.0"` had to go into `Packages/manifest.json` before any `Text`/`Canvas` script would compile.


## A Z-UP IMPORT ANSWERS A ROTATION WITH A TRANSLATION (`realistic_tree.glb`, 2026-08-16)

The tree model is authored **Z-up**, and glTFast carries the correction *inside the prefab*. Writing
the instance's rotation - **even to `Quaternion.identity`** - destroys that correction, and the object
then responds to being rotated by sliding sideways instead of turning. Several attempts at "lay the
tree down" moved it around the room without ever tipping it.

**The fix is to never touch the imported instance's own transform rotation.** Put a plain `GameObject`
of your own above it and rotate that. `SceneBuilder.BuildTree` does exactly this - `FallPivot` is the
thing that turns, and the tree hangs off it untouched.

The same shape of bug is already recorded above for `chess.glb`'s mirrored pieces. The rule
generalises: **measure a model's pose off the model, never write a number into it.**

## `RecalculateBounds` MEASURES VERTICES, NOT TRIANGLES (2026-08-16)

Splitting a mesh by keeping a subset of its **triangles** while carrying the whole **vertex** array
over is tempting - it is three lines shorter and renders identically. It is wrong, and nothing about
the picture says so: `Mesh.RecalculateBounds` walks every vertex whether or not any triangle
references it, so both halves of a cut tree came back claiming the *whole* tree's bounds. The visible
costs are that neither half ever culls, and that reading the bounds back to check the cut reports
that the cut did not happen. **Remap the vertices** - `SceneBuilder.FilterTriangles` does.

## A CYLINDER CANNOT STAND IN FOR A TAPERED, LEANING TRUNK (2026-08-16)

The notch needed something behind it, because a hole cut into a mesh shows the inside of a shell and
back-faces are not drawn - so the cut looked straight through the tree. A dark cylinder inside the
bark was tried and failed twice: the trunk loses a third of its width over the notch's height, and it
also leans off the axis the cylinder is centred on, so it stood proud of the bark as a black ring.
Sampling the radius harder only moved the failure around.

**What worked was having no shape to get wrong**: the carved trunk is drawn with a `Cull Off` copy of
its own bark material, so the inside of the trunk is the inside of the trunk.


## A WALL WHOSE WIDTH IS NOT A MULTIPLE OF THE CELL (2026-08-16)

`BuildPanelWall` tiled whole `GridCellWidth` cells from the wall's centre, which is exact for every
room in cycle 1 (8.75 and 10.5 are 5 and 6 cells) and wrong for anything else. The tree hall's 30.45m
wall went through three versions before it was right, and the middle one is the instructive failure:

1. **Round the count** - leaves 0.35m of bare `GrooveDark` backing at each end, which on a 17.5m wall
   reads as a **black border**, not as a groove.
2. **Clamp the last cell** - covers the backing and leaves a **sliver panel** jammed against the
   corner, which play called out just as fast.
3. **Fit the cell width**: round the count, then divide the width by it. 17 cells of 1.791m instead of
   17 of 1.75 plus a gap. Nobody can see 4cm; everybody can see a sliver.

The rule generalises: **when a repeating unit has to fill a fixed span, stretch the unit, do not leave
a remainder.**

## COPLANAR FACES AT A HOLE'S EDGE (2026-08-16)

The pit's lip flickered all the way round. The shaft walls ran from `y = 0` downward, so their top
0.1m occupied exactly the volume of the floor slab, and their outer faces sat on exactly the plane of
the slab's cut edge - two coplanar surfaces fighting for the same pixels. **Hanging the shaft from the
slab's UNDERSIDE removes the shared plane** rather than biasing it, which is the fix to reach for
first: a depth offset only moves a z-fight somewhere else.

## SPLICING BY `str.index` FINDS THE WRONG COPY (2026-08-16)

Three separate times this session, a scripted edit that located its target with a first-match search
spliced a block into the wrong occurrence and left `SceneBuilder.cs` with **1,400 duplicated lines**
and two definitions of `BuildTreeHall`, `BuildTree` and `BuildPanelWall`. It cost a full build cycle
each time and the last one needed the duplicate span found by diffing the file against itself.

**Anchor a scripted edit on something that occurs once**, assert the match count before replacing, and
re-scan for duplicate member definitions afterwards - `grep -c 'private static <Name>('` is enough.

**And the same trap has a second form: an UNBOUNDED replace.** Laying the fire axes flat meant changing
`root.transform.localRotation = Quaternion.Euler(tipDegrees, yaw, 0f);` in `BuildFireAxe` - a line
`BuildBucket` has verbatim. The replace hit both, so two of room2-2's buckets lost their tip while
KEEPING the lift that only a tipped bucket needs, and shipped hovering a quarter of a metre above the
floor. Nothing failed; the build was clean and the log said nothing.

**Assert the count on every scripted replace, not just the ones that look ambiguous.** A line that
reads as specific to one builder is exactly the line a sibling builder also has.


## AN ADDITIVELY LOADED SCENE IS VISIBLE BEFORE ANYTHING DECIDES IT SHOULD NOT BE (2026-08-16)

Cycle 2's root shipped inactive; cycle 1's shipped active, because cycle 1 is the one the game opens
in. That is the bug rather than the shortcut: `CycleSceneLoader` brings every cycle in additively and
asynchronously, and `LoopManager` only decides which one should be awake *after* the load returns. In
between there are frames rendering whatever each scene happened to be SAVED with - so starting the
game at cycle 2 opened on a flash of cycle 1's rooms.

**Every cycle root now ships asleep and `LoopManager` wakes exactly one, unconditionally.** The
general rule: when something's visibility is decided at runtime, its authored state must be the
INVISIBLE one, or there is a window where the authored state is the answer.

## A `WallPanelDisplay` PANEL CANNOT BE RECOLOURED BY ITS MATERIAL (2026-08-16)

The tree hall's upper walls are faded toward black to hide the ceiling. Setting `sharedMaterial` on
them is not enough: every panel handed to a `WallPanelDisplay` is driven through a
`MaterialPropertyBlock`, which overrides the material outright, so the faded panels came back white
the moment the wake-up powered the walls up. They are excluded from the display's list at build time
instead - identified by the material they were given rather than by re-deriving their height, so the
two cannot drift apart.


## A `SetActive(false)` ROOT IS ALSO INVISIBLE TO A SCREENSHOT (2026-08-16)

Making cycle 1's root ship asleep (above) silently broke the title screen: `CaptureMenuBackground`
runs *after* that in `Build()`, so the menu's background photograph became a picture of an empty
scene, and the title screen went black. The room is woken for the capture and put back afterwards.

**Anything that reads the scene - a probe bake, a screenshot, a bounds measurement - has to run while
the thing it reads is awake**, and "ships asleep" quietly moves every one of those.

## A `uGUI` IMAGE WITH A NULL SPRITE IS A SOLID RECTANGLE (2026-08-16)

The menu's gradient scrim is a generated PNG. A freshly written PNG imports as a plain `Texture2D`,
so `LoadAssetAtPath<Sprite>` returned **null** - and an `Image` with a null sprite draws a filled rect
at its colour, which for `Color.white` is a white sheet over the entire title screen. Set
`textureType = Sprite` on the importer *before* asking for the asset as one, and keep a fallback
colour for the case where it still fails: a menu that loses its gradient should still be a menu.


## AN IMPORTED MODEL'S TEXTURES ARE UNCOMPRESSED AND NOBODY TELLS YOU (2026-08-17)

`realistic_tree.glb` is a 56MB file. Its textures at runtime were **629MB**: four 4096x4096 maps that
glTFast imports as uncompressed ARGB32. That was the in-game stutter, and it did not look like a
texture problem - the same room also has a 1.3M-triangle stump, which is the thing anybody would
blame first. Measured, the triangles were affordable and the textures were not.

**They cannot be fixed with import settings.** They are SUB-ASSETS of a custom importer, so there is
no `TextureImporter` to configure. `SceneBuilder.ShrinkModelTextures` blits each one into a render
target at the size actually wanted, reads it back, writes a real PNG asset with compression on, and
repoints the material at the copy. 629MB -> 3MB, once per build, idempotent.

**The lesson is to measure before optimising**: `Profiler.GetRuntimeMemorySizeLong` on every texture
in an imported model takes five lines and would have found this the day the tree went in.

**And the same measurement settled the other half of it.** The stump in the same room was 1.33M
triangles - the number everyone would have optimised first - and replacing it with a 1,072-triangle
one cost nothing but the swap, because everything about its placement is DERIVED from the model's own
bounds. A builder that measures instead of hardcoding is a builder whose assets are replaceable.

## GATHERING BY TYPE SWEEPS UP MORE THAN YOU MEANT (2026-08-17)

`CycleBinding.PointGasAt` handed `SleepingGas` every `ParticleSystem` under the cycle root. It was
written when the only particle systems in a cycle WERE the four wall emitters, and it read as a
faithful re-derivation of what `SceneBuilder` had already wired by hand. Room2-2's taps then added
four water sprays, and the boundary started playing them - `Fill()` plays what it is given - and
`Clear()` stopped and cleared systems belonging to the taps.

**The fault is not the sweep, it is that the sweep replaced a correct wiring with a guessed one.**
`SceneBuilder` builds the four emitters and knows exactly which they are; the runtime rebinding threw
that away and asked a question ("what particle systems are in here") whose answer only happened to be
right. The fix names them on `Cycle`, which is where every other per-cycle list already lives.

**The rule this generalises to**: a runtime rebind may re-establish a reference the scene split drops,
but it must ask for the SAME thing the builder assigned. `GetComponentsInChildren<T>` is only that when
T can mean one thing, and "a cycle's particle systems" stopped meaning one thing the moment a room had
water in it. The same trap is live for anything gathered by type or by name suffix - the wall panels
(`*_Panels`) are the other one, and they are already scoped per cycle for exactly this reason.

## SUPERSAMPLE ANYTHING THAT PHOTOGRAPHS THIS BUILDING (2026-08-17)

The menu background is a picture of a room made of thin black grooves on white - the worst case for
aliasing there is. Captured at 1:1 the lines crawl and break up, and the title screen advertised the
game as a jaggy mess. It is rendered at 2x into the RenderTexture and box-filtered down
(`SceneBuilder.Downsample`), which does not depend on what MSAA the pipeline asset is set to.

The downscale is done in C# rather than with a bilinear blit on purpose: a blit at exactly 2:1 samples
pixel CENTRES and misses half the detail it is meant to be averaging.

## `Physics.Raycast` MEASURES THE HIGHEST SURFACE, NOT THE ONE YOU MEANT (2026-08-19)

The slide's ride path is built by dropping a ray at every step along the line the rider takes and
keeping whatever is underneath - the chute's surface while there is chute (`SampleRidePath`). A single
`Raycast` returns the CLOSEST hit, and from a ray that starts above the world that is the HIGHEST
surface. That is the same thing right up until something is above the chute.

Moving the landing room up so its ceiling met the slide's mouth put a ceiling slab exactly there. The
last two samples of the chute measured that slab's TOP, so the recorded path climbed from 0.53m to
1.80m and the ride carried the player over the roof of the level and out of it, then dropped them
five metres. **It played precisely as it was built, and the build reported success.**

Two lessons, and the second is the expensive one:

- Cast with `RaycastAll` and pick by what the surface IS when "under the line" is not the same
  question as "the first thing hit". Leg 0 prefers a child of the slide and holds its last height
  otherwise.
- **The assert that was meant to catch this had a hole in it.** It compared every sample against the
  height the ride STARTS at (2.73m) - and 1.80m is below that, so a path that climbed 1.3m in one step
  passed. An assert on the absolute range cannot see a local climb; check each sample against the one
  before it if that is what you mean.

## A SLAB RUNS TO THE ROOM PITCH, NOT THE ROOM DEPTH (2026-08-19)

`BuildSlab` builds floors and ceilings out to `RoomPitch`, which is the room's depth PLUS a door
pocket - deliberately, so neighbouring rooms' slabs meet under the shared divider instead of leaving a
seam. The consequence is easy to miss: **a room's ceiling already sticks half a pocket out past its
own north wall.**

Anything built into that pocket afterwards is therefore building on top of something. Lining the
slide's mouth with a tunnel across the full pocket put two of its four pieces exactly level with the
room's ceiling slab and the hall's floor slab - coplanar faces, and the flicker play reported at the
mouth. The walls have bodies too (a `BuildPanelWall` hangs a `WallDepth` off the face it is given), so
the genuinely open span between two rooms is `pocket - WallDepth`, not `pocket`.

Derive such pieces from `RoomPitch`, `WallDepth` and `DoorPocketDepth` rather than measuring them off
the scene: a change to any of the three otherwise re-opens the slot, silently, one build later.

## A WALL'S BACKING SLAB IS OUTSIDE ITS FACE (2026-08-19)

The second half of the flicker at the slide's mouth, and the half that reasoning about it got wrong
twice. `BuildPanelWall` is given the plane of the wall's VISIBLE face and puts its backing slab
`GrooveDepth + WallThickness/2` BEHIND that - away from the room. So a wall's body does not occupy the
space you would guess from its position: it occupies the pocket on the far side of it.

Between two rooms a door pocket apart, that leaves far less open space than the pocket suggests:

    pocket                     0.35
    - this room's wall body    0.125
    - the other room's body    0.125
    = genuinely empty          0.10

Lining that pocket across the whole 0.35 - which is what "fill the gap between the two wall faces"
produces - lays the lining straight through both backings, and the shared faces flicker with the
camera. It survived one round of fixes because the pieces were sized against the wall FACES, which is
the number that is written down, rather than against the backings, which is where the geometry is.

**Measured off the built scene in the end, not reasoned about.** Parsing `Cycle2.unity` for every box
near the mouth and printing the pairs whose faces are coplanar and overlapping found it in one pass,
after two rounds of deriving the wrong number from the constants. When a z-fight will not die, dump
the boxes.

## A MOVER IS FOUND BY WALKING UP, SO IT HAS TO BE AN ANCESTOR (2026-08-19)

`MarkReflectionProbeStatic` decides what may be baked into a probe by walking UP from every renderer
looking for a component that moves it - `Door`, `Drawer`, `RewardPlinth`, and so on. That is the right
shape (the thing that renders is usually a child of the thing that moves), and it has one failure mode
worth naming: **a component that moves a SIBLING'S subtree is invisible to it.**

Room2-6's valve was built that way at first - the wheel model under the holder, the `Valve` component
on a reach-trigger object beside it. Walking up from the wheel finds the holder and then the room, and
never meets a `Valve` at all, so a wheel that spins a full turn on every press would have been baked
into the room's reflection standing still.

Two fixes, and the structural one is better: put the moving geometry UNDER the component that moves
it. Here that meant giving the trigger a `center` offset instead of offsetting its transform, so the
`Valve` object could sit at the holder's origin and be the wheel's parent. The alternative - adding
`Valve` to `MovesDuringPlay` - would have been a lie about where it sits, and the next fixture built
to the same pattern would need its own entry.

**When adding a fixture with a moving part, check the part is a descendant of the component.**

## `capFarSide` PUTS A WALL IN THE DOORWAY (2026-08-19)

`BuildDoorPocketFill(capFarSide: true)` seals the far mouth of a door pocket. It exists for the end of
a walk, where the pocket would otherwise open onto the outside of the level once the door slides
clear, and it KEEPS its collider on purpose.

Room2-6's door was built with it while there was nothing beyond, and room2-7 was built behind that
door on the next pass without the flag being revisited. The room existed, was lit, was probed, was
walkable and was entirely invisible: a solid wall stood in the doorway between the two.

**The flag is a statement about the WALK, not about the door**: true only where the walk ends, false
at every join with a room through it. It is worth grepping when adding a room behind an existing
door - nothing else in the build reports it, because a capped pocket is exactly what a correct
end-of-walk looks like.

## THE PIT-LIP Z-FIGHT, AGAIN, IN A DRAIN (2026-08-19)

Room2-6's drain shaft was built running from `y = 0` downward - so its top 0.1m sat inside the floor
slab and its outer faces lay on the plane of the slab's cut edge. That is the SAME fault this file
already records for the tree pit's lip, arrived at independently four rooms later, and the fix is the
same one: hang the shaft from the slab's UNDERSIDE (`-WallThickness`) so there is no shared plane to
fight over.

Worth noting because the first version looked right in every static view. The cover hid it until the
valves opened, so it shipped as "the drain flickers after it opens" rather than as a hole in the
floor that was always wrong.

## A DYNAMIC RIGIDBODY IGNORES ITS PARENT (2026-08-19)

Room2-6's ducks and beach balls are the only things in this game that are both physics props and
carryables, and picking one up did not work: the prompt appeared, `PlayerHand.Take` ran, the object
was parented under the camera - and it did not come. **PhysX overwrites a dynamic body's transform
every fixed step from its own simulated position**, so the object was in the hand for a fraction of a
frame and then back wherever the water had it.

Two things were wrong, and the second is the one worth remembering:

- the body has to go **kinematic**, with `detectCollisions` off so a held duck cannot shove the pool's
  balls around from inside the player's head;
- it has to happen **in `CarryableItem.AttachTo`**, not in the component that owns the floating. The
  first version left it to `FloatingBalls.FixedUpdate` to notice `IsCarried` - one physics step later,
  which is exactly the step that throws the object away. Custody is `CarryableItem`'s job (CLAUDE.md
  §2), so the freeze belongs beside the reparenting.

**Also clear `interpolation`.** An interpolated kinematic body smooths its transform toward its
physics pose a frame behind, so an object parented to a moving camera visibly lags and swims.

## A FALLBACK FLOOR HEIGHT IS A FLOOR WHERE THERE IS NO FLOOR (2026-08-19)

`FallingItem.SurfaceUnder` raycasts for whatever is under a dropped object and fell back to
`item.RestingY` - the one floor height the cycle was told about - when the ray found nothing. Over the
tree hall's pit that is a description of a floor that is not there, so an object dropped into the hole
**stopped in mid-air at the lip**, at the height the floor would have been.

Two fixes, both needed: the probe has to be long enough to reach the bottom of the deepest hole in the
building (the pit is 26m and the probe was 9m), and "nothing underneath" has to return
`float.NegativeInfinity` rather than a guess - there is no surface, so the fall has no end. Nothing is
lost by letting it fall: `ItemRegistry.ReturnAllToOrigin` sweeps every object home at the top of the
next iteration.

## A DOOR AND ITS OPENING ARE TWO SEPARATE CALLS (2026-08-19)

Room2-7 appeared to have no door and no sign over it. Both were built, both were exactly where they
were meant to be, and both were **inside a solid wall**: `BuildWeighRoom` cut a doorway in the room's
NORTH wall and left the south one `Rect.zero`, while `BuildPadDoor` and the target sign were placed
against the south wall.

`BuildPadDoor` places a slab and `BuildPanelWall` cuts the hole, and **nothing in the build checks
that a door has one.** A door with no opening has no symptom other than being invisible - it still
opens, still answers its condition, still appears in `Cycle.doors`.

**If a door is invisible, look for the `Rect` before looking at the door.** The same pairing exists for
every room in the game; this is the first time the two halves disagreed.

## AN ATLAS-MAPPED PATCH CANNOT CARRY A TEXTURE (2026-08-19)

The scale's display was to show its number on the model's own green panel rather than on anything laid
over it. It cannot, as imported: `weigh_scale.glb` maps that patch into a 0.06-wide window of an
atlas, and - measured off the file - **U does not run across the panel at all.** The same `u` appears
at both ends of it, because the patch was UV'd as repeated strips of a flat colour rather than as a
surface anything is drawn on. There are ten distinct UVs for 45 vertices.

No amount of tiling or offset fixes that. What does is reissuing the submesh with UVs computed from
its own footprint (`SceneBuilder.FlatUvMesh`) - same vertices, same triangles, same place, mapped so a
texture lands on it square.

**Check a model's UVs before planning to draw on it.** Reading `TEXCOORD_0`'s min/max from the glb is
one script and answers it; correlating `u` against vertex position answers whether it is usable, which
the bounding box alone does not.

## `ModelBounds` MEASURES A BOX, SO ROTATING A ROUND THING RESIZES IT (2026-08-20)

Nine billiard balls, scattered through nine different orientations, came out of the first build at
**nine different sizes** - radii spread 23% apart in one drawer.

`SceneBuilder.ModelBounds` transforms the **eight corners of each mesh's own AABB** into the target
frame and takes the extent of those. That is exact for a box and an over-estimate for anything else -
and *the size of the over-estimate depends on the rotation*, up to sqrt(3) for a cube corner-on. Every
model in this project had been sized by it and none had noticed, because every one of them is either
box-ish or placed at a single fixed rotation. A ball at a random yaw is the first case where both
halves of that stopped being true at once.

**Anything scattered through rotations has to be measured by its vertices, not its box.** For a
sphere that is one pass: transform the vertices into the frame, take the midpoint of min/max as the
centre and the largest distance from it as the radius. Both are rotation-invariant by construction.
See `SceneBuilder.OrientBilliardBall`, which reads the mesh once and answers "which way does the
number face" and "how big is it" together.

**And the symptom is not obviously a measuring bug.** Mismatched balls read as a bad model or a bad
import long before they read as an AABB of a rotated box - which is why this is written down.

## THE TEXTURE ON A SPHERE IS MIRRORED, AND SO ARE THE DIGITS ON IT (2026-08-20)

Every number in `billiard_balls.glb` is drawn **backwards** in its own texture file. That is correct:
the sphere's UVs run the other way, so the two mirrors cancel and the ball reads right way round.
Checked before use rather than after, by taking the triangle nearest UV (0.5, 0.5) and testing the
sign of `cross(dU, dV) . normal` - positive means the texture appears mirrored on the surface.

Worth the check because the same session had already shipped a mirrored readout on the weigh scale
and fixed it by flipping the source; doing that here would have flipped a number that was already
correct, and a texture that looks wrong when opened is exactly the invitation to do it.

## A GATE ON WHERE A THING CAME FROM MUST STOP APPLYING ONCE IT LEAVES (2026-08-20)

`CarryableItem.requiresOpenDrawer` says "this cannot be taken unless that drawer is open", which is
right for exactly as long as the object is still in the drawer. It was asked unconditionally, so an
object carried out of the room and PUT DOWN became **permanently unreachable**: the gate went on
asking about a drawer a hundred metres away, and E over an object lying in plain sight did nothing,
for the rest of the run.

Play found it on the billiard balls, which are the first thing in this game carried the length of the
building and set down somewhere else. **The pins have always had the same hole** and it never showed,
because a pin is used in the room its drawer is in - which is the shape of this class of bug: a
condition that is true by accident for every existing caller, until one caller stops being local.

The fix is `Released` - "let go of in the world", set by `DropAt` and by nothing else. It is the one
flag that already means "this is not where it started".

## `DropAt` RE-PARENTS TO THE ORIGIN, AND SOME ORIGINS MOVE (2026-08-20)

Dropping an object parents it back to `originParent` so it stays inside its own cycle. For everything
that starts on a floor that is free; for anything that starts **in a drawer** it is not, because the
tray slides. A ball dropped at the far end of the building jumped 0.22m sideways every time a past
self opened that drawer in room2-1.

`CarryableItem.dropParent` is the fix - the drop's parent, defaulting to `originParent`, set by
`SceneBuilder` to the CHEST rather than the tray. `ReturnToOrigin` still uses `originParent`, because
the top of an iteration is exactly when the object belongs back in the drawer.

**Worth checking whenever a new carryable starts life on something that moves**: a plinth, a drawer, a
door, a felled tree.

## A CLAMP IS A FLOOR, AND "THE FLOOR" IS NOT ONE NUMBER PER CYCLE (2026-08-20)

`CarryableItem.DropAt` clamped a drop to `RestingY` - `floorBaseY + floorY`, where `floorBaseY` is ONE
height for a whole cycle. The clamp is there because the callers are not all above the floor:
`ChessPlacer` passes a raycast hit *on* the board and `SymbolSlot` passes a recess in a wall, and an
object left exactly at a surface is an object sunk into it.

Room2-6, room2-7 and room2-0 hang 3.7m below the rest of cycle 2, so down there the clamp lifted every
drop to the floor height of the storey ABOVE. The object then fell back and landed correctly - which
is why it read as a cosmetic "why did that duck jump" rather than as a bug in a clamp.

**`FallingItem` had already learned this exact lesson a day earlier**, for the fall itself, and the
fix is the same one: ask what is actually under the point (`FallingItem.SurfaceUnder`, now public and
positional) instead of remembering one height. The pattern worth taking away is that **the second
fix was needed because the first one only covered one of the two readers.** When a remembered value
turns out to be a lie, grep for every reader of it before calling it fixed - `RestingY` had four.

## A TOGGLE REPLAYED BY GHOSTS IS A PARITY, NOT A STATE (2026-08-20)

Cycle 2's chest of drawers shipped with `Drawer.canClose = true`, and the comment that turned it on
said why it was safe: *"these bays are EMPTY, so a past self's replayed pull closing one costs
nothing"*. Nine billiard balls went into them the day room2-0 was built, and nobody turned it back off.

`Drawer.SetGhostSignal` reproduces the **pull**, not the resulting state - which is the correct
reading of "record the attempt, re-evaluate the condition" for a toggle. So the drawer's openness at
any moment is the **parity of how many past selves have reached for it**: one ghost opens it, two
leave it shut, three open it again. Everything inside gates on `IsFullyOpen`, so a ghost's take - and
with it that ghost's entire delivery - worked on odd iterations and silently did nothing on even ones.

Play reported it as *"a past self brought the ball last time and did not this time, and I did
nothing"*, which is a very hard symptom to trace back to a drawer.

**An idempotent action is the only kind a crowd of ghosts can all perform.** `Open()` is idempotent;
`Toggle()` is not. That is the whole of why cycle 1's nightstand has always been open-only.

**And the comment had already said so.** It named the exact precondition ("these bays are empty") and
the exact consequence of breaking it ("a ghost can close it on another ghost's errand"). What was
missing was anyone re-reading it while putting something in the drawer - so when a comment states a
precondition, changing the thing it is about is the moment to go and find it.

**IT WAS PUT BACK THE SAME DAY, BY REQUEST, AND THE OTHER HALF IS WHY.** A drawer that cannot be shut
is open forever, and an open one stands between the player and the other bay: `WantsInteractHint`
keeps an open *closable* drawer in the aim contest, and its front has slid 0.23m nearer the eye, so it
wins the press from in front of the bay below it. Play reported the lower bay as unopenable.

So the chest has both faults available and one of them chosen. **The trade taken: a drawer found shut
is an errand that fails, which the player can see and undo by pulling it; a bay that can never be
opened is neither.** If the parity failures become the worse of the two, the third option is to make
the PLAYER's press a toggle and a GHOST's replay open-only - which costs a little of "a past self does
exactly what you did" and buys back both.

**`ColourLever` IS THE SAME SHAPE AND DOES NOT HAVE THE FAULT, which is worth saying out loud.** It
became a two-position latch on 2026-08-21 - a toggle in every sense the drawer is one - and a crowd of
ghosts throwing it cannot produce a parity, because what is recorded is `PlayerSignal => PlayerOn`:
the **level**, like a `FloorButton`'s, not the press that changed it. A ghost reproduces the position
the lever was IN at each frame of its recording. The drawer's fault is not that it toggles; it is that
its signal is the pull. **When you make something a toggle, record its state and not its press.**

**And then it had the OTHER fault instead - see the next section.** Recording the level is right and
is not sufficient.

## A FIXTURE WIRED TO NOTHING IS SILENT, NOT BROKEN (2026-08-20)

Room2-0's four recesses shipped with `FinalSlot.sequence` null, because `BuildBreakRoom` was written
from `BuildFinalRoom` without its `slot.sequence = sequence` line. `FinalSlot.Live` is
`sequence != null && sequence.Active`, so all four were dead: **no prompt, and E did nothing**, at a
console that otherwise looked completely finished.

There is no error for this and there cannot easily be one - a null reference that is *checked* is a
legal state everywhere else in this project (`Cycle.finalRoom` null is a supported answer). What
catches it is the shape of the symptom: **a fixture that is invisible to E rather than misbehaving is
almost always a null in its eligibility test, not a bug in its action.**

`CycleBinding.Bind` now re-establishes it for every cycle, alongside `hand`, so a second call site
cannot repeat it.

## THE ONE OBJECT THAT MOVES BY ITSELF BREAKS A POSITION TEST (2026-08-20)

`GhostReplayer.takeReach` is 2m: a ghost may only take something it is standing next to, or the object
is dragged to it from across the building. That is exactly right for the thirty-odd carryables that
are put back at an exact origin every iteration and stay there.

**Room2-6's three ducks and two beach balls are not those.** They are the only carryables in the game
on real rigidbodies - they bob, they drift, 210 plastic balls shove them, and when the pool drains the
vortex pulls every one of them to the middle of the room. So where a duck is at second 34 is different
in every iteration, and a 2m test against its CURRENT position is CLAUDE.md §1.4 in miniature:
*replaying "the player acted here" against a world that has moved makes a ghost's contribution luck.*

The symptom is the worst kind: **intermittent, silent, and looks like nothing at all.** A past self
fetched a duck last iteration and does not this one, with the player having touched nothing.

`CarryableItem.ghostTakeReach` overrides it per object - 9m for those five, which covers the whole
pool (the drain is at most 6.8m from any corner of it) and reaches no further than the room they can
only ever be taken in.

**Measuring to the object's HOME instead was considered and is wrong**: a duck taken *after* the drain
was recorded metres from home, so a home-based test fails exactly where the current-position test
succeeds. A reach that covers the room is the only measure that is right in both cases.



## A `.ps1` without a UTF-8 BOM silently mangles every non-ASCII string in it (2026-08-20)

Windows PowerShell 5.1 — which is what `powershell.exe` is, and what the project's tools scripts run
under — decodes a `.ps1` file as **ANSI** unless it starts with `EF BB BF`. PowerShell 7 defaults to
UTF-8 and does not have this problem, which is exactly why it is easy to write a script that works
for the person who wrote it and not for the repo.

**The reason it is worth a section is the failure mode, not the cause.** Generating the Korean PA
lines through `Tools/generate_narration.ps1` produced 45 files: 7 were zero-length and 38 were
normal-looking WAVs of plausible duration. The obvious reading — "seven syllables the synthesizer
cannot say" — is wrong twice over. Every one of those syllables synthesised perfectly when tested on
its own, and the 38 that *looked* fine were the voice reading mojibake aloud with total confidence.
Nothing about file size, clip count or duration distinguishes that from success.

What actually diagnoses it is asking PowerShell to read the file back:

```
Get-Content Tools\generate_narration.ps1 | Where-Object { $_ -match 'digits' }
    $digits = @{ 9 = "援?"; 8 = "??"; 7 = "移?"; ...
```

A first attempt blamed `SpeechSynthesizer` not flushing its wave file and added `SetOutputToNull()`
before `Dispose()`. That change is still in the script and is good practice, but **it fixed nothing**
— the same seven files came out empty. If a generated asset comes out wrong in a way that looks
character-dependent, check the encoding of the script before the tool.


## A panel wall overruns its own length, and where that overrun lands is your problem (2026-08-21)

`BuildPanelWall` extends its backing and its collision by `WallDepth` (0.125m) past **each end of the
wall along its own axis**. That is deliberate and its own comment explains why: two perpendicular
walls sized to the interior meet along a line and leave a `WallDepth`-square column that neither
covers, which shows as pinhole cracks at grazing angles. The overrun buries the corner.

**It is only correct when there is another wall for the overrun to be buried in.** Every wall in the
room chain has one. Room3-1's north corridor did not: its side walls run along Z and were built flush
to the two rooms' inner faces, so each overrun stood 12.5cm out into a room — a near-black slab, the
full height of the wall, one at each jamb.

**It does not look like a geometry bug. It looks like a texture.** On camera the two of them read as
two very thick black vertical lines in the panel grid, and the first three attempts at it all treated
it as one: the groove is too wide, the block overlaps the wall, the slot behind the groove is open to
an unlit corridor. All three were plausible, all three were wrong, and the arithmetic that would have
settled it — where does `zStart - WallDepth` actually fall — was never done because the symptom did
not look dimensional. What settled it was a screenshot: the left-hand bar had a visible SIDE FACE, and
a groove painted on a wall does not have one.

The fix is to inset the wall by `WallDepth` at each end and let the floor and ceiling span the full
run, so the overrun lands inside the neighbouring wall's build-up. **If a wall is ever built between
two things that are not walls, work out where its overrun goes before building it.**

Related, and the same shape of mistake one scale down: **nothing may be exactly the size of the hole
it sits in.** The corridor's block was exactly the corridor's width, so its sides were coplanar with
the wall panels and its base with the floor, and both flickered. Two coplanar faces fight for every
pixel from every angle. Clearances have to be asymmetric, though — the block's face backing is only
1mm narrower than the opening, because a wider gap there lets the lit wall behind show through the
groove as a bright edge.

## Reduce an overlap and you chase the symptom; remove it and the symptom is gone (2026-08-21)

Room3-1's corridor flickered through **five** builds. Each fix was a real coincident-face pair, each
one was measured or reasoned correctly, and each time a different pair took over:

1. the block was exactly the corridor's width, so its sides shared a plane with the wall panels
2. the block's base was exactly on the floor's top face
3. two 23m faces 2mm apart, which is inside the depth buffer's resolution at that distance
4. the corridor wall's backing plane landing on the wall panel's edge, because
   `GridLineThickness / 2` and `GrooveDepth` are both 0.025
5. the corridor wall's overrun reaching the plane of room3-1's panels

**Four of those five were the same mistake: making an overlap smaller.** The corridor's side walls
overlapped room3-1's wall the whole time, and every fix nudged a face out of one plane and into
another arrangement of the same overlap. What ended it was not a better clearance - it was inseting
the walls by two `WallDepth`s so the two structures ABUT and share no volume at all. Play said so
first: *stop the structures overlapping.*

**The rule: if two things must not fight for pixels, do not make them nearly-not-overlap. Make them
not overlap.** Abutting is safe - it is how `BuildSlab` lays the floors of adjacent rooms - and it is
checkable, which nearly-not is not.

`CycleThreeDiagnostics` is the tool that should have been written before the first attempt rather
than after the fourth. It opens the built scene and reports pairs of renderers that overlap AND share
a bounding plane, which is exactly the condition; it took the count from 12 to 0.

## A world-space placer called with `Vector3.zero` puts things at the world origin (2026-08-21)

`SceneBuilder.PlaceModel` takes WORLD coordinates - `targetXZCenter` and `floorY` are compared
against world-space renderer bounds. That is right for what it was written for, props that stand on
a floor somewhere in a room.

Called for something MOUNTED on a parent that already carries the position, `Vector3.zero, 0f` does
not mean "where the parent is". It means the world origin, and in this project the world origin is
the middle of Room1. Nine monitors and three camera housings for cycle 3 all landed in cycle 1's bed
room, in a heap, in front of the camera that captures the title screen's photograph.

**The symptom appeared nowhere near the cause.** The report was "the main menu has gone strange" -
and the main menu is built from a render of a room three cycles away from anything that had been
edited. What found it was opening `Assets/Textures/MenuBackground.png` and seeing a CCTV lens.

`PlaceModelLocal` is the mounted version: everything in the parent's frame, centred rather than
based, sized by its longest side. **Check which space a placement helper works in before using it -
the two read identically at the call site and fail nothing at build time.**

## A downloaded model's screen has a top, and it is not the one you assume (2026-08-21)

`hanging_monitor.glb` is authored face-DOWN - it is a monitor slung under a ceiling - so mounting it on
a wall is a `LookRotation` that turns its glass into the room. The one written was
`LookRotation(Vector3.up, -inward)`, and every CCTV feed in cycle 3 came out **upside down**.

The half turn between right and wrong is `LookRotation(Vector3.**down**, -inward)`, and the reason it
is safe to make blindly is worth keeping: the two differ by 180 degrees **about `inward`**, which is a
rotation IN the screen's own plane. The glass still faces the room and the model's bracket - its +Y -
is still buried in the wall either way. The only thing that changes is which end of the picture is the
top. So there is exactly one bit of freedom here and it cannot be reasoned out of the model's
dimensions; it has to be looked at.

**If a picture on a model comes out MIRRORED rather than inverted, this is not the fix** - that is the
mesh's UVs, and the answer is `mainTextureScale`, not a rotation.

## A texture stretched to the wrong shape reads as a blurry one (2026-08-21)

The same monitors were reported as "very bad quality". Two separate faults, and only one of them was
resolution:

- **512 x 288 on a 2.6m screen.** The number was chosen when the monitors were 0.72m across; they were
  made 3.6x wider the same week and nobody re-derived it.
- **16:9 on a 2.04:1 piece of glass.** The picture was squeezed 13% horizontally, and a squeezed
  picture reads as a badly made one long before anyone works out that nothing is actually out of focus.

The second is the one worth remembering, because it survives any amount of resolution. **Derive the
render texture's shape from the mesh it will be drawn on rather than writing an aspect down** - the
screen's mesh bounds are right there, and `BuildCctvMonitor` now measures them and hands the ratio to
the feed. A different monitor model cannot reintroduce the fault.

## A signal recorded as a LEVEL still dies with the ghost that recorded it (2026-08-21)

> **`ColourLever` NO LONGER EXISTS** - cycle 3's levers were replaced by carried mirrors the same
> week, for the reason the section below is about to demonstrate. The class is gone and the fix it
> forced is not: `GhostInteractable.ReleaseGhostSignal` is still there, still the difference between
> "the recording said off" and "the recording ran out", and still what the next held-state fixture
> will need.

The section above ends "when you make something a toggle, record its state and not its press", and
`ColourLever` did exactly that. Play found the other half of the problem the same day:

> *I turn it on, then the ghost disappears and the blocks switch off with it.*

Correct, and the code said so plainly: `IsActive => PlayerOn || ghostsOn.Count > 0`. A ghost
contributes its recorded level **for as long as it is replaying**, and not one frame longer. When a
past self's timeline runs out, its `true` goes with it — so a staircase raised at second 12 of a
40-second recording folded itself up at second 40, under whoever was standing on it.

**The two failure modes are opposite ends of one axis, and neither end is where a lever belongs:**

| what the ghost re-applies | what goes wrong |
| --- | --- |
| the **press** (drawer) | several ghosts reaching for one fixture leave its state as a *parity* |
| the **level** (lever, as written) | the state is only as alive as the recording that set it |
| the level, **latching a world flag** | nothing, for anything meant to stay put |

So the fix is not to change what is recorded. `PlayerSignal` is still the level, because that is what
tells a ghost *when* it threw the lever and is what keeps the parity fault away. What changed is
`GhostReplayer`: **"the recording said off" and "the recording ran out" used to be the same event**,
and they are now two — `SetGhostSignal` for a genuine transition, `ReleaseGhostSignal` for a ghost
that is simply done. The default for the second is still a falling edge, so a `FloorButton` behaves
exactly as it always has (a ghost that has left is not standing on anything); `ColourLever` overrides
it to do nothing at all.

That distinction is what let the lever go back to being switchable both ways a day later without
bringing the fault back: a past self that threw the lever *back* still throws it back, and a past self
that merely stopped existing no longer does.

**The test to apply to any new signal-driven fixture: is its effect meant to outlive the moment?** A
`FloorButton` is not — a pad you are not standing on is not held, and every gate in cycle 3 depends on
that being true. A lever is. Anything in the second class needs a latch of its own, and asking the
question is cheaper than finding out from a player standing on a step that vanished.

**It also turned out to be the better design.** A one-way lever means a colour you did not mean to
raise cannot be lowered, so the order you throw three levers in is a decision with a cost — and the
way to take it back is to let the clock run out. **The iteration is the undo.** That is the game, and
the bug fix arrived at it before the design did.

## A space between two rooms belongs to neither room's reflection probe (2026-08-21)

Play, on room3-1's north corridor once the block was fully raised: *"the ceiling flickers, and worse
the more I move."* No coincident faces anywhere near it — the scan said zero.

**The corridor had no reflection probe.** Every other space in the building has one; this one is
neither room3-1 nor room3-2N and fell between them. Twenty-three metres of white panelling at 0.85
smoothness is *almost entirely what it reflects*, so the only question was which cubemap it was
getting, and the answer was room3-1's.

**Box projection does not politely give up outside its box — it extrapolates.** Surfaces up to twenty
metres past where the cubemap was captured were being handed a reflection re-projected from it, which
smears, and which slides around as the camera moves. On a surface with no texture of its own to anchor
the eye, that reads exactly as flicker.

Two things worth keeping from it:

- **"Flickering" is not a synonym for z-fighting.** Three of the four flickers in this cycle were
  coplanar faces and the fourth was shading, and the tool built to find the first kind confidently
  reported nothing for the second. A measuring tool answers the question it was built to ask.
- **The rule for probes is per SPACE, not per room.** Anywhere the player can stand and see a wall is
  somewhere that needs a probe of its own, and a corridor is a room for this purpose even though
  nothing in the builder calls it one.

## A scan of the authored pose measures half the states the player sees (2026-08-21)

`CycleThreeDiagnostics` had been run four times and reported zero coincident faces, and play kept
finding flicker. Everything in this project is **authored in its resting pose** — the corridor full,
every gate shut, every coloured panel in — because that is what makes a scene readable without
pressing Play. It also means a scan of the built scene had never once looked at the corridor *open*,
which is the only state anybody is ever inside it in.

Moving the movers by their own offsets and scanning again found five pairs in one run, including the
one play had been describing: the block's underside came to rest at exactly `GateHeight`, and
`GateHeight` is where the wall's cutout stops, so the wall backing's own bottom face was on that plane
too — a 25mm strip of near-black against white, full width, right at the top edge of the opening you
walk under and look up at.

Two follow-ons, both of which cost a run to notice:

- **Reproduce what the runtime DOES, not just where things are.** The block's face grid is switched
  off the moment it moves; a scan that only moved transforms kept reporting it against the panels it
  lands on. A false positive is how a measuring tool stops being believed.
- **A group is only as good as its edges.** The two scan groups met exactly at the two rooms' walls,
  and a pair that straddled them fell between both.

## An imported prop has more axes than you think, and every one you assume is a coin toss (2026-08-22)

`mirror_trensum.glb` is five mirrors and the whole of cycle 3's puzzle. Getting it onto the screen
correctly took **three separate corrections, each found by play rather than by the build**, and each
one was an assumption standing in for a measurement:

| assumed | actually | how it showed |
| --- | --- | --- |
| Y-up | **Z-up** (converted from an FBX) | it lay on its face |
| the glass is on a model axis | **the head is tilted ~15° on its clamp** | every mirror in the room slightly turned |
| a pane's vertex normals say which side it is on | they give the AXIS; the sign is elsewhere | *"the front of the mirror is looking at me"* |

The second was in the dump the whole time and nobody read it: the pane's own bounds are
**(0.165, 0.0437, 0.1596)**, and 44mm is far too thick for a sheet of glass unless it is lying at an
angle inside its own box. `0.165 × sin(15°) ≈ 0.043`. **A bounding box that is thicker than the thing
it contains is a rotation you have not accounted for.** The build now measures and prints that angle,
and it is 15.0°.

### And a fourth: averaging vertex normals does not give you a face

The first attempt at "which way does the glass point" averaged the pane's vertex normals. That works
for a single-sided sheet and is **meaningless for a solid** - this pane is modelled front, back and
rim, so it has a normal for every direction and they cancel. The average came out near zero, the
magnitude guard passed anyway because it is not *exactly* zero, and the direction was noise. Every
mirror in the room ended up at an arbitrary angle; play reported it as *"the disc looks like it can
rotate"*, which is exactly what a garbage orientation looks like.

**A flat disc has one short axis and that is its normal.** The mesh's own bounds say which, and that
is a fact about the shape rather than about how somebody chose to weld it.

### And a fifth, which is the one that took longest: a prefab instance cannot be restructured

Taking the tilt out meant turning the frame and both panes on the clamp, so the first version made a
pivot object and reparented the three nodes onto it. **`SetParent` across a prefab instance is dropped
by Unity without an exception** - a model placed with `PrefabUtility.InstantiatePrefab` is an instance,
and restructuring one is not allowed. The pivot collected nothing, rotated nothing, and the build
reported *"levelled on the clamp: yes"* because what it was printing was `pivot != null`.

Two lessons, and the second is the expensive one:

- **Rotating N objects about a shared world point is exactly what a common parent would have done**,
  and needs no restructuring. `Transform.RotateAround` per node, same pivot, same axis, same angle.
- **A log that measures the INTENTION is worse than no log.** "levelled: yes" was true and useless;
  it took a second report from the user to find out the correction had never run. The line prints the
  **residual angle after the fact** now - it reads `-15° off vertical ... residual 0°` - and that is a
  sentence that can only be written by something that actually worked.

The third has a clean answer that needs no guessing: **the other pane.** They are back to back, so the
vector from one to the other IS the first one's outward direction. Anywhere a sign has to be chosen,
look for a second feature that decides it rather than picking and waiting to be told.

### And a tilt can pull two ways at once

The glass has to be vertical (the beam is horizontal) and the stand has to be vertical (it is an object
on a floor), and with a 15° tilt between them **aligning either one leans the other**. There is no
number that satisfies both.

The way out was to stop treating the model as rigid: the head is rotated back **on its own clamp**,
which is what a clamp is for. The frame and both panes go onto a pivot at the clamp's measured centre;
the arm, the stem and the foot stay. Both come out upright.

## A prompt hangs where the object's ROOT is, which is not where a held object looks (2026-08-22)

*"Taking the mirror off a ghost does not work."* It was not the taking - it was the aiming.

`CarryableItem.HintAnchor` defaults to its own transform, and `trigger` is `GetComponent<Collider>()`
on the same object. That is exactly right for anything resting on a floor. But a carried item is
parented to its holder's HAND, and for a ghost that hand is `MiddleHand.R` **on the animated
skeleton** - so the aim test, the prompt disc and the reach volume were all clustered at a wrist while
the mirror the player was looking at sat a metre away in front of the ghost's chest.

Two rules fall out, and the second is the general one:

- **If a component moves an object's visible parts away from its transform, it owns the anchor too.**
  `Mirror` puts the glass in front of the holder's body, so `hintAnchor` points at the glass and
  `Mirror.Sync` drags the reach volume to the same place every frame.
- **Anything a ghost holds is on a bone, not on a body.** That bone is animated: it is twisted, and it
  moves. Any fixture whose behaviour depends on where a held object *is* or which way it *points* must
  ask the holder, not the item - which is also the only form a timeline can reproduce, since a
  recorded frame has a position and a yaw in it and no skeleton at all.

## A clip imported without Loop Time plays once and then holds its last frame (2026-08-22)

The player body's walk looked right for about a second and then the legs froze. `Man_Walk` is imported
with Loop Time off, and nothing had ever noticed because **the ghosts have always scrubbed their own
normalized time** - `animator.speed` pinned at 0 and `animator.Play(hash, 0, phase)` every frame - so
the clip's own loop flag has never been read by anything.

The fix was not to turn the flag on. `PlayerBody` scrubs too, which removes the dependency instead of
patching it, and brings the other half of why `GhostReplayer` does it:

> **The phase is advanced by TRAVEL, not by time.** `walkPhase += travel / metresPerCycle`. A stride
> tied to distance cannot skate, at any speed, including none.

Both now read the same three numbers - `metresPerCycle`, `walkThreshold`, `idleCycleSeconds` - because
it is one rig playing one clip and two sets of them would drift.

**The general form: when a component depends on an import setting nobody set deliberately, own the
thing instead of the setting.**


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

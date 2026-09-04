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


## A mesh with no triangles hangs the GI bake, and the error you can see is not the error (2026-08-25)

`chess.glb` ships a mesh named `Material3` carrying no triangle sub-mesh. The day `ContributeGI` was
switched on for every non-moving renderer, that mesh went into APV's **Virtual Offset** stage, which
builds a ray tracing acceleration structure out of the GI set.
`HardwareRayTracingAccelStruct.AddInstance` refuses a mesh with no triangle topology — and then
registers a zero handle for it anyway, so the *second* such mesh throws `ArgumentException: An item
with the same key has already been added. Key: 0`.

That aborts `DefaultVirtualOffset.Initialize` half-built, `Step()` NREs on what it left behind, and
the wreckage surfaces six stages later as a `NullReferenceException` in
`AdaptiveProbeVolumes.GenerateScenesCellLists`.

**The last one is the only one anybody saw, and it is not the bug.** Unity's `FinalizeBake` wraps
`ApplyPostBakeOperations` in a `catch` that logs and swallows, then calls `CleanBakeData()`, which
throws `ObjectDisposedException` *out* of the bake delegate. `done` is therefore never set, Unity
calls the delegate again on the next tick, and the pair repeats at hundreds a second: 29,757
exceptions, a 129MB log, and an Editor that has to be killed from Task Manager.

Three diagnoses were made off the tail of that log and all three were wrong — including "this is a
Unity bug in 6000.5.7f1", which stood in `docs/rendering-notes.md` for three days with a workaround
plan attached to it. The stale-data theory was cheap to test and was disproved outright: a bake from
a freshly created baking set crashed identically.

**Cycle1 was the only scene that crashed because Room2West is the only room with chess pieces.** The
symptom is scene-shaped and the cause is asset-shaped, which is why "what is different about this
scene's *geometry*" was the question that solved it and "what is different about this scene's
*volume*" — the obvious one, since Cycle1's is a 17 × 15 × 73m splinter — was a dead end.

Three things to carry:

- **When a bake hangs, read the FIRST error after `'<Scene>': baking.`** A crash loop's tail tells you
  what the wreckage looks like, never what hit it. `awk 'NR>start && /Exception/ {print NR; exit}'`.
- **A released engine having an obvious bug is a claim that needs evidence.** Thousands of projects
  bake APV. "It would have been fixed already" was the right instinct and it came from the user, not
  from here.
- **`MarkReflectionProbeStatic` now asks `HasTriangles(r)` before granting ContributeGI.** A mesh with
  no triangles bounces nothing and blocks nothing, so holding it out costs the bake exactly zero — and
  the count is logged, because otherwise the next one to slip in has no symptom until the hang.

## APV's sampling noise is a TAA feature, and this project has no TAA (2026-08-25)

The hour Adaptive Probe Volumes were switched on, every wall panel in the building started pulsing —
reported from play as brightness popping, not as seams or z-fighting. Nothing was wrong with the
bake. It was the **sampler**.

APV dithers the probe sampling position to hide the seams between subdivision levels, and Unity's
`ProbeVolumesOptions` defaults are `samplingNoise = 0.1` with **`animateSamplingNoise = true`**. The
tooltip on that second one says what it is for: *"Whether to animate the noise **when TAA is
enabled**, smoothing potentially out the noise pattern introduced."*

**The player camera runs SMAA** (`BuildPlayer`), which resolves a single frame and cannot average
anything across frames. So the dither was re-rolled every frame with nothing to resolve it, on every
flat white surface in a building made of flat white surfaces.

`BuildPostProcessing` now writes a `ProbeVolumesOptions` override with the noise off and the
animation off. `leakReductionMode` is left at `Quality`, which is already the right one.

**The general form: a default tuned for a temporal antialiaser is a bug in a project that does not
run one.** Anything whose documentation contains the words "when TAA is enabled" needs checking
against `BuildPlayer` before it is left at its default — the noise is the one that was found, not
necessarily the only one.

If a visible STEP in brightness ever appears partway along a wall — a subdivision seam, which is what
the noise exists to hide — raise `samplingNoise` back toward 0.1 and leave the animation off.

## The building shipped inside out for a day, and three good theories died first (2026-08-25)

`ChamferedPanelMesh` wound every quad `(0,2,1) / (0,3,2)` from corners supplied counter-clockwise as
seen from outside. Unity treats **clockwise-from-the-front** as front-facing, so every quad faced
backwards, and `RecalculateNormals` — which derives normals from exactly that winding — gave every
wall panel in the game a normal pointing **into the wall**. The face the room can see was then shaded
as though it faced away from every light in the room.

**Result: a white building with pure black walls.** Floors, ceilings, furniture and props were
untouched, because they are plain slabs and primitives that never go through this function.

### What made it expensive

The regression landed on the same day Adaptive Probe Volumes were first made to bake, so the black
walls looked like a lighting-bake problem and were diagnosed as one three times over:

| Theory | Why it was plausible | How it died |
|---|---|---|
| APV sampling noise | Panels flickered, and `animateSamplingNoise` really is a TAA default this project should not have | Fixed the *flicker*; the black stayed |
| APV dilation off | Unity really does default it off, and invalid probes really do read black | Turned on, re-baked, no change |
| `ContributeGI` | Added the same day; lightmap-static really does change how ambient reaches a surface | Withheld for one build, no change |

Each of those was a real defect and each fix was kept. **None of them was this one.**

### What actually found it

Bisection against a reference, using a render nobody had to be asked for. `MenuBackground.png` is a
render of the room, regenerated on every build, so `git show HEAD:Assets/Textures/MenuBackground.png`
is a picture of the room BEFORE the day's changes — bright white panels — and the working copy is a
picture of it after. From there it was one variable per build: APV off (still black), ContributeGI
off (still black), ambient restored (still black), panels swapped back to plain cubes (**bright**).

Three lessons, in order of how much they would have saved:

- **A build that renders is a test you can run yourself.** Four of these builds cost about ten
  minutes total and needed nobody to look at anything. The three dead theories all came from
  reasoning about the last screenshot instead.
- **Bisect against a committed artefact.** The single most useful object in this whole hunt was the
  old `MenuBackground.png` sitting in git.
- **Do not reason about winding from a comment.** The comment above this function asserted the
  winding was correct and cited `BevelledPrismMesh` learning it the hard way — the identical mistake,
  in this project, caught the first time only because that mesh happened to be emissive. A hand
  computation agreed with the comment and was also wrong. Build it and look at it.

## A tiled detail map repeats, and no amount of noise design fixes that (2026-08-25)

Reported from play, twice: pale grey squares in a regular grid across the walls and floor, worst at
grazing angles, clean head-on. It was the **wear map** (`MakeSmoothnessMap`), and turning it off
removed it completely.

**Two wrong diagnoses first, both plausible, both mine:**

1. *Block compression.* The map carries smoothness in ALPHA and was importing at Unity's default
   `Compressed`; DXT5 quantises alpha in 4x4 blocks. Real defect, wrong culprit - **decoding the
   source PNG showed its alpha was perfectly smooth** (184..255, no blocking). The uncompressed
   import was kept because a data texture should not be block-compressed regardless, but it changed
   nothing on screen.
2. *Correlated octaves.* The noise ran two octaves at periods 4 and 12, and 12 is 3x4, so both sit on
   the same lattice. Simulating a decorrelated version (coprime periods, three octaves) side by side
   showed **no improvement at all** - which is what finally pointed at the real cause.

**The cause is the tiling.** URP/Lit drives every secondary map from `_BaseMap`'s single UV
transform, so the wear map is locked to the (5,3)-per-panel tiling the NORMAL map needs for its fine
grain. Fifteen copies of one tile on every panel, and the same fifteen on all 88 panels. Value noise
puts its extrema on a lattice, so what repeats is a regular grid of soft blobs. At grazing angles the
specular is strongest and the grid becomes obvious; head-on it never showed, which is why every
head-on render looked clean.

**A tiled texture cannot carry variation at a scale LARGER than its tile.** Wear wants patches bigger
than a panel; the tile is a fifth of one. That is not a tuning problem, it is the wrong mechanism -
and in a building made of discrete panels the right one is one value PER PANEL through a property
block, which has no tiling to repeat.

### What made it findable

`MenuBackground.png` is re-rendered by every build and its side walls are at a grazing angle - the
same view the artifact needs. Crop that region, upscale, boost contrast, and the pattern is plainly
there; disable the wear map, rebuild, crop again, and it is plainly gone. **A build that renders is a
test you can run yourself**, and the same trick had already solved the inside-out panels earlier the
same day.

Also worth having: the noise generator was reproduced in ~30 lines of Python to compare candidate
octave settings as images, without Unity in the loop at all. Three minutes, and it killed the
octave theory before a single rebuild was spent on it.

## The build writes a cross-scene reference report, and it is only useful if it is read (2026-08-25)

`LightingTuner` was built where the calibration room is built — inside cycle 1 — while the room
itself is lifted out into the core scene by `ExtractCalibrationRoom`. Unity **nulls every serialised
reference that crosses a scene boundary on save**, so the panel opened with its sliders attached to
nothing.

It did not look broken. Every group falls back to a plausible default when its array is empty, so it
displayed `albedo 1, smoothness 0.5, bump 1` and `intensity 0` — and those numbers were read off and
reported as though they were the room's values.

**`cross-scene-report.txt` had named all ninety-odd of them on the build that introduced them**, and
the comment on `ExtractCalibrationRoom` already describes this exact failure happening once before,
to `CalibrationWall`. It was made a second time by not reading the report.

Two things came out of it:

- **Read `cross-scene-report.txt` after any build that wires a new component to scene objects.** It
  is written every time, and a component that spans the boundary is broken in a way that only shows
  at runtime.
- **A fallback default is a lie when the wiring is missing.** `LightingTuner.Seed` now logs a warning
  naming the empty groups, because a panel that quietly shows defaults is worse than one that fails.

## RenderSettings is per SCENE, and three of this game's four scenes never had any (2026-08-26)

`SetupLighting` writes `RenderSettings` — ambient mode, the three Trilight bands, the reflection
mode — while the core scene is open. `SplitCyclesIntoScenes` then creates each cycle with
`EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, …)`, **and a new scene arrives with Unity's
defaults**. Nothing copied the environment across.

So from the day the cycles were split until this was found, **every tuned lighting value in this
project existed in `IterationRoom.unity` alone.** Cycle1, Cycle2 and Cycle3 ran on:

```
m_AmbientMode: 0            (Skybox, not Trilight)
m_AmbientSkyColor:     0.212, 0.227, 0.259     ← nobody chose these
m_AmbientEquatorColor: 0.114, 0.125, 0.133
m_AmbientGroundColor:  0.047, 0.043, 0.035
m_DefaultReflectionMode: Skybox
```

### It hid because it looks like lighting, not like a bug

And it invalidated every attempt to tune the room, for a day:

- **Walls that would not respond to the lights.** With the reflection mode left at Skybox, panels at
  smoothness 0.85 were mirroring the PROCEDURAL SKY — a constant no ceiling fixture can move. This is
  the anomaly that finally gave it away: the menu render's floor tracked fixture intensity exactly
  (140 / 173 / 211 for 7 / 10.5 / 17) while the wall beside it sat at 115 / 117 / 122. **A surface
  lit by those lights cannot do that.**
- **Everything measured came out blue.** Blue minus red was +34 to +36 on every sample, in a white
  room lit by white panels. That was the sky, it was on screen the whole time, and it was in every
  number recorded that day without being questioned once.
- **Setting ambient to zero blacked out the calibration room and left the cycle rooms alone**, because
  the calibration room is the only room that lives in the core scene. That inconsistency sent three
  separate diagnoses in the wrong direction.

### What fixed it, and what it was worth

`ApplyEnvironment()` is now a method of its own, and `SplitCyclesIntoScenes` calls it for each cycle
scene — after making it active, because `RenderSettings` writes to the ACTIVE scene and nothing warns
you when that is the wrong one.

| surface | before | after |
|---|---|---|
| back wall | 117, 131, 153 (blue +36) | **149, 150, 155** (blue +6) |
| left wall | 155, 169, 189 | **189, 191, 197** |
| floor | 173, 197, 219, **39% clipped** | **183, 191, 197, 0% clipped** |
| ceiling | 98, 94, 92 | **169, 169, 174** |

The ceiling nearly doubled — it is the surface with no direct light at all, so it was the one most
starved by an environment nobody had set. And the floor got BRIGHTER while its clipping went to zero,
because what had been clipping was the sky's green and blue.

**The general form: anything written to `RenderSettings`, `Lightmapping`, or any other per-scene
singleton has to be written to EVERY scene the build produces.** `SceneBuilder` makes five. Ask of
any such setting: which of the five did this land in?

## The walls are lit by a constant, and two attempts to replace it both failed (2026-08-26)

The fixtures in this building are downlights: a 130-degree cone pointed at the floor from 5.41m.
Measured with the ambient constant switched off, so the lamps were the only light, the **floor read
100 of 255 and the walls 13, the ceiling 9**. The white walls this game is made of have never been
lit by a lamp - they are lit by `RenderSettings.ambient`, a flat constant.

Two ways to fix that properly were built, measured and reverted. Both are worth knowing about before
a third attempt.

### Attempt 1 — baked global illumination (Adaptive Probe Volumes)

It works. All four scenes bake clean in about four minutes. **Switching it on adds +101 to the floor
and +166 to a dynamic prop, and +2 to the walls** - indirect light follows the direct light, and the
direct light is all on the floor. Also, `CaptureMenuBackground` renders during the build before probe
data loads and is provably blind to APV, so the menu would show a room the game does not render
(capture 81/89/141 vs in-game 114/72/181).

Tried against the +2, none of which moved it: bounces 2→8→16; wall/ceiling albedo 1.0→0.85;
Shadowmask→Baked Indirect; dilation on (Unity ships it **off**, and a building of thin panels wants
it on); Virtual Offset 0.01→0.05 with the search multiplier at 1.0, on the theory that probes were
sealed in the cavity behind the 25mm panels; per-room probe volumes; emissive ceiling panels as baked
area lights.

**Kept from it, because both measured as well as argued:** bounce count 8 (Unity's default of 2
captures about a fifth of the indirect light in a room this white) and wall/ceiling albedo 0.85 (at
1.0 the indirect series *diverges*, so room brightness tracked the bounce COUNT rather than the
lighting - which is why 2 bounces looked fine and 16 was "over-bright").

Zeroing the ambient to let the bounce stand alone gave a **black building**: walls 4-8 of 255.
Raising the fixtures cannot compensate - 10.5→17 clipped 65.7% of the floor's pixels and left the
walls black, because 2.4 times almost nothing is still almost nothing.

### Attempt 2 — a lamp that faces the wall

The textbook answer, and what real architectural lighting uses. Four spots per room aimed back at the
walls, with an emissive slot as the visible fixture. **It measured well and looked bad**: the back
wall went 81→199 with the floor and ceiling unmoved, and the render showed a bright BLOB rather than
a wash, an emissive slot reading as a fluorescent tube stuck to a dark panel, and a grey room once
the ambient bands it was meant to replace were retired.

A single point source cannot wash a wall evenly, and the fix - a row of them - is barred by
`m_AdditionalLightsPerObjectLimit` being 8, of which the four ceiling fixtures already take half.

### What both failures share, and what actually worked

**They add brightness where light already is. What this room needs is EVENNESS.** That is why the
ambient constant survives: it is not only filling a hole, it is doing art direction, because "evenly
lit" is what a clinical white cell looks like.

What settled it was `LightingTuner` (TAB in the calibration room) and an eye. The numbers that came
out - ambient 0.356 / 0.763 / 0.521, fixtures 7.02 at a 157-degree cone - are a room lit by FILL
rather than by beams, and they took two passes: the first got it properly white and was reported as
painful to look at. **Comfort, not brightness.**

### Three measurement lessons, which cost more than any single bug

- **`MenuBackground.png` cannot see APV**, and it was used to judge APV changes for hours after that
  had been demonstrated. A tool that cannot observe the variable under test is worse than no tool,
  because it returns numbers.
- **Screenshots from different camera positions were compared as one measurement.** Brightness at a
  point depends on where it is viewed from.
- **Measuring a change and looking at it are different tests.** The wall washer passed the first and
  failed the second: the wall really did get brighter, and it also got a spotlight blob on it that no
  brightness sample could report.


## A COLLISION CHECK MUST ASK THE LAYERS THE PLAYER IS ACTUALLY STOPPED BY (2026-08-26)

`AssertWalkable` and `AssertNotWalkable` both swept `Physics.OverlapSphere(..., ~0, ...)` — every
layer in the project. The player's own `CharacterController` does not: `BuildPlayer` sets
`cc.excludeLayers = 1 << balloonLayer`, and that exclusion is the whole reason room2-6's pool can hold
210 balls, 3 ducks and 2 beach balls without putting an invisible wall in the water.

So the two disagreed, and room2-6's doorway is where it showed. One floating ball drifting into the
south doorway at build time produced, on every single run:

    [SceneBuilder] room2-6 south door: 'Solid' blocks the way through (swept at (21.70, -9.97, 89.15))

`Solid` is the `SphereCollider` child `MakeFloatBody` gives every float — a thing nobody can be
blocked by. **The room was perfectly walkable the entire time.** It sat in `TODO.md` as a hard
geometry bug ("a wall collision block is standing in the doorway") and in the trailer shotlist as a
blocker to fix before filming, and it was neither.

Three lessons, in order of how much they cost:

- **A false error is worse than no check.** This assert's own comments already record being rewritten
  once for exactly this reason: the bounds-arithmetic version flagged twenty innocent things and
  "teaches everyone to ignore the output". It then spent days doing that again, and the output was
  duly ignored — which is why nobody noticed the message names a *ball*.
- **The error text was right there and nobody read it against the room.** `'Solid'` in a room whose
  defining feature is 215 floating props is a strong hint. Reading the message as "something solid"
  rather than as "the object literally named Solid" is what let it stand.
- **Reading the code and reasoning from it is not verification.** The first guess here was that the
  bug was real but stale, then that it was a swept-capsule false positive against a doorway corner.
  Both were confident, both were wrong, and the actual answer took one `grep` for `"Solid"`. Grep for
  the name in the error before theorising about the geometry.

The fix is `PlayerBlockingMask()` — one helper, used by both asserts, returning the same mask the
controller collides with. If the player ever excludes a second layer, it changes in one place.

## A dropped object would not go over a ledge, and the cause was the arc's PACING (2026-08-30)

Play reported blocks dropped off room3-2N's decks as "some fall and some do not". Neither the drop
nor the fall was wrong; the horizontal arc between them was.

`FallingItem` moves a released object sideways to its resting spot WHILE it falls, and paces that
move against the descent so the object arrives over the spot exactly as it reaches the floor. Two
things decided which floor:

- `Release` took the surface under the HAND, and
- `Update` re-asked, every frame, what was under the object's CURRENT position.

At a ledge those are two different floors and both answers were wrong. The arc was paced against the
deck the player was standing on, so the object had barely moved sideways by the time it got there —
and the per-frame probe, still seeing deck under it, settled it on the lip. Whether it cleared the
edge came down to whether the player's HAND happened to be past it, which is 1.1m in front of their
body for a block this size and is not a thing anybody can see.

The fix is two lines: pace the arc against the HIGHER of the two floors — the first one the arc
could possibly end on — and, while gliding, aim at the floor under where it is GOING rather than
under where it is. The mirror case (stepping off a lift onto a deck) wants the same answer, which is
how you can tell the maximum is the right function and not a patch.

**The general shape of it:** an object in transit has two positions and it is never obvious which one
a question is about. Both of these read as correct in isolation.

## The player's put-down and a ghost's were two different acts (2026-08-30)

Reported from play, and reported precisely: "when the player drops it, it falls; when the ghost
does the same drop, it catches on the ledge - and even the same ghost manages it on some iterations
and not others." Both halves were real and they were two separate faults.

**The throw.** `PlayerHand.Drop` picks a rest point `dropAhead` plus the object's half-width AHEAD
of the body and lets `FallingItem` carry it there while it falls — a dropped object is solid, and
one released at your own feet leaves you standing in it. `GhostReplayer.TryDrop` was
`item.DropAt(item.transform.position)`: let go of on the spot, at the hand anchor on the rig. For
four years of flat floors those are the same outcome. At a ledge they are a metre apart, and a metre
decides which storey the object ends up on.

**The stale pose.** `Tick` drained the recorded pops and carries BEFORE advancing the cursor and
applying the frame, so every event acted on the previous tick's position. The cursor walks to the
last recorded frame at or before the elapsed time, and how many frames that skips depends on this
run's frame rate against the recording's — so a ghost draining a drop was between zero and several
recorded steps behind itself, differently every run. That is the "some iterations and not others".

Both fixes are small and neither is a tolerance. The lesson is the shape they share: **the player
and their past selves must perform the same ACT, not merely reach the same state**, and an
asymmetry between the two paths can sit there unnoticed for as long as no room can tell the
difference. Room3-2N's ledges were the first thing in the building that could.

A third thing worth keeping: `DropCarried` — what a ghost does with whatever it is still holding
when its timeline runs out — deliberately does NOT get the throw. That is not a put-down, it is
letting go, and giving it one would be inventing an act the past self never performed.

## An imported model's PIVOT is not its middle, and the prompt hangs on the pivot (2026-08-30)

Reported from play as "I cannot pick the ladder up". Standing beside it, looking straight at it, E
did nothing.

`CarryableItem.HintAnchor` falls back to the item's own transform when no `hintAnchor` is set, and
every half of the press tests that anchor: `PlayerLookup.InView` asks whether it is on screen and
`IsAimedAt` ranks it against everything else by how far it sits from the middle of the view. The
TRIGGER is separate and was sized to the mesh, so `playerInRange` was true along the whole ladder.

`ladder.glb` has its origin at ONE END. Measured in the built scene: **3.88m from the middle of its
own mesh.** So the anchor sat in the air past the far rung, and the object that was plainly in front
of the player was being aimed at from four metres away.

Nothing else in this game had needed `hintAnchor` because nothing else is eight metres long with its
origin at one end - every other carryable's pivot is near its centre by luck or by construction.

**The rule: measure the gap between an imported model's pivot and its bounds centre, and give it an
anchor of its own if they are not the same place.** The same measurement fixes `floorY`, which is
"how far the ORIGIN sits above the floor" and is only half the object's height for an object whose
pivot is its own centre - it was written as `size.y / 2` here and was wrong by the same reasoning.

## A pivot at one end breaks three different things, one at a time (2026-08-30)

`ladder.glb`'s origin is at one END of the ladder, 3.88m from the middle of its own mesh. That one
fact produced three separate bug reports over two days, each of which looked like something else:

1. **"I cannot pick it up."** `CarryableItem.HintAnchor` falls back to the item's transform, and
   `InView`/`IsAimedAt` both judge that point. The trigger covered the whole ladder so it said "in
   range", while the point being aimed at was four metres away. Fixed with an explicit `hintAnchor`.
2. **"I can only grab it in the middle."** One anchor is one point. Fixed with `LongItemAnchor`,
   which slides the anchor to wherever along the length the player is looking.
3. **"Ghosts will not pick it up."** `GhostReplayer` refuses a take whose object is further than
   `takeReach` (2m) from where the ghost is standing, measured to the object's POSITION. A take the
   player made at the far end left the past self six metres from the pivot. Fixed with
   `CarryableItem.NearestPoint`, which is the pivot for everything that fits in a hand and the
   nearest point along `heldSpan` for anything that does not.
4. **"It always lands facing north."** `LieDown` restores the pose an object was BUILT in unless it
   is `restsUpright` or rolled - right for a key, and for this model the built pose is lying along
   +Z. Fixed with `CarryableItem.keepsDropYaw`, its own flag rather than borrowing `restsUpright`,
   which would give the same result and lie about why.
5. **"It goes under the bed."** `FallingItem.SurfaceUnder` cast ONE ray, at the object's origin. A
   ladder dropped across a bed had its pivot over clear floor, found the floor, and came to rest at
   floor height with the bed passing through it. Fixed by sampling along `heldSpan` and taking the
   highest surface under any part of it - which is what resting ON something means.

6. **"It stands three metres off the deck."** Latent, and never reported because the ladder had not
   been installed by hand yet: `LadderMount`'s seat is at half the ladder's length, which puts the
   PIVOT there - so an end-pivot stood it from 3.15m to 9.5m above deck B instead of 0 to 6.31.

**The rule: for an imported model, measure the gap between the pivot and the bounds centre before
anything else.** If it is not small, every system that asks "where is this object" is going to get
a different answer from the one the player sees, and they will surface one at a time in whatever
order the game happens to exercise them.

**AND THE FIX FOR ALL SIX WAS ONE LINE OF GEOMETRY, not six patches.** `SceneBuilder` now moves the
ladder's root to its own bounds centre and puts the children back, so the mesh does not move and only
the meaning of the origin changes - the same move `MakeChessPiece` makes for the mirrored half of the
chess set. `heldSpan` is centred on the origin as a result, which is the definition every reader of it
now uses. The five patches stay, because each is right on its own; what they no longer have to
compensate for is a pivot in the wrong place.

## "Held, or it does nothing" did not extend to a ghost taking one (2026-08-30)

`GhostReplayer.Eligible` lets a ghost take an object out of ANOTHER ghost's hands only when the
object has a SOCKET. The stated reason is sound: "a pin has no destination a hand-over could matter
for", and three interchangeable pins reshuffling between past selves reads as random.

Having a socket was a PROXY for "a hand-over of this is worth reproducing", and it silently
excluded the one family of objects for which the hand-over is the whole point. **A mirror has no
socket** — it is never put anywhere, it is held — and which hand holds it is a position in space
bending light. So a player who took a pane off a past self and stood in its place had that take
refused in every later iteration, and the thing they did never accumulated. Reported from play as
"I took over what the ghost was doing and it isn't reflected".

`CarryableItem.ghostHandover` says it directly instead. **The lesson is the proxy**: the rule was
written about pins, tested against pins, and its stated justification did not survive the first
object that broke the correlation between "has somewhere to go" and "matters in a hand".

## A cast that begins inside a collider reports distance 0, and that is not a measurement (2026-08-30)

Reported from play: **walking BACKWARDS into a wall with the ladder made it vanish from view and
left the player barely able to move.** Two faults compounding, and the second is the general one.

**The ladder only ever looked forwards.** `LevelCarry.GiveWay` cast from the trailing end along the
object's axis, which sees everything the LEADING end can run into and nothing the trailing one can.
Backing into a wall put the trailing end inside it — which is where the cast begins.

**And a `SphereCast` that begins overlapping a collider returns `distance == 0` and a zero normal.**
Unity has no surface to report, because the cast never travelled. Read as a reading it says "the
whole length is buried", which is the worst possible answer: the give-way slid the ladder five
metres backwards in a single frame, out of the view entirely, and `HeldItemClearance` then set the
movement stop from a normal that pointed nowhere — so the player was refused in a direction that
meant nothing.

Both fixed where they belong. `Sweep` and `GiveWay` skip degenerate hits, because what is already
inside something cannot be measured out of it. And the give-way asks BOTH ends how far they can
travel and slides away from whichever is more buried, so backing into a wall pushes the object
forward into view instead of backward out of it. Both ends against something is a space shorter
than the object — the difference is zero, nothing slides, and the player is refused until they
turn, which is the honest outcome and needs no case of its own.

**The rule: never take `distance == 0` from a cast as a distance.** It means the query could not
run, not that the answer is zero — and every consumer downstream will treat it as the most extreme
reading possible.

## Making two cases symmetric is not the same as giving them one formula (2026-08-30)

The ladder gave way correctly when its LEADING end met a wall and not when its trailing one did.
The fix looked obvious — ask both ends, slide by the difference — and it introduced two new faults,
both from the same mistake: **a formula that is right on one side of a case is not automatically
right in the middle of it.**

**The difference is wrong when both ends are buried.** With `Bf` and `Bb` metres past each surface,
sliding by `Bb - Bf` leaves `Bb` buried at the front and `Bf` at the back — it TRADES the burial
rather than reducing it, every frame, and the player is refused in whichever direction it landed on.
Reported from play as backing into a wall and sticking. A space shorter than the object cannot be
resolved at all; the honest answer is to share the burial (`(Bb - Bf) / 2`) and report the contact.

**And casting exactly the object's own length can only say "is this end buried".** It cannot say
"is there room to slide into", because both questions come back as the same number. With the cast
capped at `length`, every free end reported zero headroom, the one-sided branch computed a slide of
zero, and the case that had always worked stopped working. The cast has to run `length + maxSlide`.

**One authority, too.** `HeldItemClearance.LongContact` was casting the span a second time to decide
whether to stop the player, after `LevelCarry` had already swept both ends to decide where to draw
the object. Two casts of one line is two answers that can disagree — the poser slides the object
clear and the stop still reports the contact it just resolved. The thing that MOVES the object is
the thing that knows what it could not escape, so it publishes that and the stop reads it.

## The drawer parity is gone, and it was a workaround's cost (2026-08-31)

Cycle 2's chest had two bays and `Drawer.canClose = true`, which made E a **toggle**. A toggle
replayed by several past selves is order-dependent where an idempotent `Open()` is not: two ghosts
that both recorded a pull open the drawer and then shut it again, and every ball inside gates on
`IsFullyOpen`. A ghost's ball errand therefore failed on **even** numbers of past-self pulls — the
"drawer parity" this file has recorded as costing cycle 2 an unknown number of iterations.

`canClose` was never wanted for itself. It was set to solve a *different* problem: an open drawer
stays in the aim contest and its front has slid 0.23m nearer the eye, so the upper bay won the press
from in front of the **lower** bay and play reported the lower one as unopenable. Being able to shut
it was the only way out of that from inside the game, and the parity was the price.

The chest is one drawer now (by request, matching cycle 1's nightstand). There is no second bay for
an open drawer to hide, so the reason is gone — and with it the price. **When a flag is set to work
around something else, it has to be re-examined when that something else changes; nothing about the
flag itself will tell you it is now pure cost.**

The rule that survives: **a drawer that holds anything is open-only.** Cycle 1's nightstand always
was, which is why the pins never had this bug.

## An object's origin can be the thing that deletes it (2026-08-31)

Play: put the yellow triangle down anywhere, let the structure it came out of close, and the triangle
**disappears**.

`RewardPlinth` was rewritten once already, on 2026-08-29, because it hid and teleported an object it
no longer held. That fix made ownership a **parentage** test — `key.transform.IsChildOf(plinth)` —
which is right, and identity-based as CLAUDE.md §1.4 asks.

What it missed is the one path that puts the object back under that parent with nobody carrying it:

```csharp
// CarryableItem.DropAt
transform.SetParent(dropParent != null ? dropParent : originParent, true);
```

The reward is **authored as a child of its plinth**, so its `originParent` IS the plinth, and none of
the three rewards had a `dropParent`. Drop it, and it is re-parented home; `OwnsKey` reads true
again; the next pad release calls `Hide()` on it.

**A fallback that points at the wrong place is worse than no fallback**, because it works everywhere
the two agree. `dropParent` is set in `BuildKeyPlinth` now — the one method all three rewards come
through — so a fourth reward cannot arrive without one.

## The floor probe was the only cast in the project that did not exclude the player (2026-08-31)

`HeldItemClearance.Sweep`, `LevelCarry.Clear` and `PlayerHand.RoomAhead` all skip the player's capsule
by name. `FallingItem.ProbeUnder` cast `~0` and skipped only the falling object and its support.

That probe runs **at the hand** the moment something is released, to decide what the fall lands on. A
downward ray from there can hit the player's own capsule, and where that lands — and therefore whether
a block settles on a ledge or clears it — depends on where the player was standing and whether they
were in the air.

Play reported it as blocks dropped from room3-2N's third storey catching the deck on some iterations
and not others, and as jumping while throwing "not registering".

**When a rule holds for three casts out of four, the fourth is not an exception — it is the bug.**

## A rotation is an axis AND an amount, and only one of them got checked (2026-08-31)

The ladder was built leaning 12 degrees. The scene had the rotation, the wiring was right, nothing
overwrote it at runtime — and play reported *"the ladder is standing vertical."*

It was leaning **sideways**. The seat's rotation was

```csharp
Quaternion.AngleAxis(LadderLeanDegrees, Vector3.forward) * Quaternion.Euler(-90f, 0f, 0f)
```

`Euler(-90,0,0)` stands the model up — its long axis is local +Z, and that goes to world +Y. But the
model is **1.18m wide on local X**, so after that turn the two stiles are separated along world X.
Leaning about world +Z then tilts the length *and the width together*: the ladder tips over like one
falling, instead of leaning back against the wall.

**From the only place anybody sees it, that looks like no lean at all.** The player is on deck B at
the foot, looking straight up the ladder's face. A sideways lean happens inside the plane of that
face, foreshortened to nothing.

Two lessons, and the second is the expensive one:

- **A rotation is two facts.** The angle was verified — quaternion decoded, 12.0 degrees off vertical,
  correct. The *axis* was never checked against the model's own width. Verifying half a rotation
  proves nothing about it.
- **Raising the angle made it worse, not better.** Told the lean read as vertical, the obvious move
  is more lean — which tipped it over further while still looking vertical from the foot. A fix that
  targets the symptom will happily confirm the wrong diagnosis.

The check that would have caught it, and which is now the one to run after any authored rotation:
**decode all three axes and say what each one is FOR** — length, width, thickness — not just the one
you were thinking about.

```
LENGTH    (model +Z) -> (-0.342, +0.940,  0.000)   20.0 deg off vertical
WIDTH     (model +X) -> ( 0.000,  0.000, -1.000)   across the lean  OK
THICKNESS (model +Y) -> (-0.940, -0.342,  0.000)
```

**And the fix moved the footprint, which moved a bug.** With the width finally across the lean the
foot is four times broader in z, and it overhung deck B's void by 14cm — a ladder standing with one
foot over a hole. Correcting an orientation changes what a thing occupies; re-check its clearances,
not just its pose.

Which way it FACES was decided by measuring the model, not by guessing: the rungs sit at 24.5% of
the thickness toward local −Y while the stiles run the full depth, so −Y is the climbing face and +Y
is the back that rests on the wall. The rotation puts −Y east, into the room.

## A liveness test that names a thing the room does not have (2026-08-31)

Room3-0's console refused the Bedlam cube for ever, with no prompt and no refusal sound. The chain:

`FinalSlot.Live` needs `FinalRoomSequence.Active`, which needs `EscapeTrigger.PlayerArrived`, and
`EscapeTrigger.TryArm` opened with

```csharp
if (door == null || !door.IsOpen) return;
```

**Room3-0 has no doorway at all.** Its own header comment says so — the only way in is the hole in
its floor, which you climb into. So `door` is null, `TryArm` returned on its first line every time,
and nothing downstream could ever become true.

The test was never wrong for the rooms it was written for: the trigger sits *in* a doorway, so "the
door is open" is a cheap way of saying "this is a threshold somebody could be standing in". It became
a lie the moment a final room was built without one. `requiresOpenDoor` now says which it is, and
room3-0 sets it false.

**Two more of the same shape were sitting behind it**, each of which would have kept the console
dead on its own:

- `halfDepth` kept its 0.5m default while the step-off point is 1.37m away in z. The trigger was
  built with `halfWidth` set and its partner forgotten.
- The volume ignores Y **by design** — "a doorway is a doorway at any height", which is true of a
  building laid out in one plane and false the moment cycle 3 stacked room3-0 on top of room3-2N.
  Untested, this volume also covered the deck two storeys down, so standing at the ladder's foot
  armed the console upstairs. `halfHeight` is opt-in so cycles 1 and 2 are unchanged.

**A default that encodes an assumption about the building is a bug waiting for the building to
change.** All three of these read as correct code; what went stale was the world around them.

## A block on the mark makes a fixture uninstallable, silently (2026-08-31)

`LadderPlacer` ends in `PlayerLookup.InView`, like every press path in the game — and `InView` casts
a ray from the eye to the fixture's anchor and refuses if anything is in the way. `LadderMount`'s
anchor was its own origin, which sits **on deck B's surface**, so anything RESTING on the mark stood
squarely in that ray.

Deck B's Bedlam-block scatter rectangle (`BedlamShelves`) ran x[−8.00, −2.50] z[4.37, 9.75] with no
exclusion for the ladder, and the mount is at (−7.50, 9.20) — inside it. Verified by disabling the
new exclusion and rebuilding: a block lands at (−7.62, 9.33), **17cm from the mount's centre**.

What makes it expensive is that it does not look like a bug. A block on the mark is one of twelve
blocks lying around a room full of blocks, and the failure it causes is *the left click does nothing
and no prompt appears*. Nothing connects the two.

Fixed at both ends, because either alone leaves it possible:

- **The build keeps the square clear** (`Grow(LadderShaftHole(...), inset)`), and an assert at the
  end of the scatter loop says so by name if that ever stops being true.
- **The prompt anchor came off the deck** — `LadderMount.hintAnchor`, 1.2m up in the shaft. The build
  is not the only thing that can leave an object on the mark: the player can put one down there, and
  so can a past self.

**The general rule: a hint anchor at floor level is a hint anchor anything can stand on.** Put it
where a person would look to judge the spot, not where the spot is.

## A long object goes THROUGH a wall, so an end of it is not a place to measure from (2026-08-30)

A wall in this building is **0.125m thick** (`WallDepth = WallThickness + GrooveDepth`). The ladder
is **6.31m long**. Those two numbers together are the whole of a bug that survived three rounds of
fixing.

`LevelCarry.GiveWay` asked "how far can each end of this travel before it meets something" by
standing **at that end** and casting. That reads correctly right up to the moment an end is buried by
more than 12.5cm — at which point it is not inside the wall at all, it is **out the far side**, in the
next room. The cast then starts in clear air on the wrong side of the surface, immediately meets the
wall's *back* face 0.1m away, and reports that as the free run. The arithmetic dutifully concludes
that the OTHER end is buried six metres, slides the object further backwards, and refuses the player
**forward** — the one direction that would have freed them.

Played as: hold the ladder, press S into a wall, and W stops working. Only turning the mouse or
dropping it gets you out.

**Measure from the GRIP, outward, in both directions.** The middle of a carried object is where the
holder's hands are, so it is in open air by construction and on the near side of every surface the
object is pressed against. Two casts out of it answer both questions and cannot start inside
anything.

**And the failure modes are not symmetric, which is the reason to prefer this even where both work.**
If a grip-centred cast somehow starts inside geometry, both runs come back clear, nothing slides and
nothing is blocked: the object passes through a wall, which is a *graphical* fault. The end-based
version's failure was to refuse the player a direction, which is a *trap*. When picking between two
formulations of a spatial test, ask which way each one fails, not only whether each one is right.

**A refusal that cannot help is a refusal that can only hurt.** Where the free run is genuinely
shorter than the object — a six-metre ladder across a 1.75m corridor — no slide fixes it and walking
either way along the axis leaves it exactly as buried. The old code still picked a direction to
refuse; it now blocks in **at most one** direction ever, so the opposite is open by construction and
nothing in this system can trap anybody.

---

## A WALL SIGN'S CANVAS POINTS ITS +Z INTO THE WALL (2026-09-01)

**The readable side of a world-space canvas in this project is its LOCAL -Z**, so a sign that looks
in code as though it is turned away from the room is the one that can be read, and a sign rotated to
"face the player" is drawn on the inside of the panelling.

The convention is stated in three places and nowhere as a rule, which is how it got broken twice in
one day:

| Wall | Rotation used | Canvas forward |
| --- | --- | --- |
| North (+Z) | `identity` | +Z, into the wall |
| South (−Z) | `Euler(0,180,0)` | −Z, into the wall |
| West (−X) | `Euler(0,−90,0)` | −X, into the wall |
| East (+X) | `Euler(0,90,0)` | +X, into the wall |

`MakeFourWallFaces` uses all four. `BuildLadderSign` uses the west entry and its own comment says
the −90 makes it *"face east, into the room"* — which is the convention stated in the opposite
words, and is exactly what makes it easy to get wrong.

**What it cost.** Two signs on north walls were authored at `Euler(0,180,0)`, on the reasoning that a
sign on the far wall should be turned to face the player:

- **room3-0's way-down arrow.** The break raised it, correctly, to alpha 1 — onto the inside of the
  wall. The room read as having no sign in it at all, at the one moment the player needs to be told
  where to go.
- **The evaluation board.** It printed its entire report, a line at a time, into the panelling.

Neither produced an error, a warning, or a missing reference. Both looked like "the thing never
fired", which sent the search to the trigger and the wiring rather than to the rotation.

**Check a new sign by which way its wall faces, not by which way the player does.** And if a sign is
blank, rule the rotation out before looking at what raises it — a canvas facing a wall and a canvas
that was never told to appear are indistinguishable from inside the room.


---

## THE OUTSIDE OF THIS BUILDING IS LIGHTMAPPED BLACK, AND NO AMOUNT OF AMBIENT FIXES IT (2026-09-01)

The ending flies the player round the outside of the facility. The first ride was past a building
that rendered **completely black**, and it read as broken geometry rather than as unlit geometry -
which sent the search to the cutaway code and the wall names instead of to the lighting.

**The cause, in three facts:**

1. Every light in this game is a spot set into a ceiling pointing DOWN inside a sealed room. Nothing
   has ever lit an exterior surface, because until this sequence there was no vantage point outside
   a room.
2. The building is **lightmap-static** - the project bakes GI. Its exterior faces were baked with
   nothing outside to light them, so they carry a lightmap that is black. **A black lightmap is not
   a surface waiting for light; it is a surface that already has its answer.**
3. So raising `RenderSettings.ambientLight` does nothing to it. A lightmapped surface takes no
   ambient.

**The tell that identifies it instantly**: in the same frame, the imported backdrop model was lit
correctly and the facility was black. The backdrop is not static. If some things in a shot are lit
and the *static* ones are not, it is the lightmap, not the ambient.

**What does work**: a realtime light, which adds on top of a lightmap. But note the second half -
twenty-one realtime POINT lights were added first and were not enough, because **URP hands each
renderer only a handful of additional lights, chosen per object**, and the building's walls are
enormous single meshes. A **directional** is not an additional light: it is the main light, every
renderer gets it, and it costs one. That is `FacilityExterior.sun`.

It is authored disabled and switched on at the reveal. It has to be: shadows are off, so it passes
straight through walls, and enabled during play it would flood every sealed room in the building.

---

## THE DEFAULT SKYBOX IS INVISIBLE UNTIL IT IS NOT (2026-09-01)

Nothing in this game had ever looked at the sky - every room is sealed - so `RenderSettings.skybox`
was left at Unity's default, the blue procedural one with a sun in it. That was free for a year and
stopped being free the moment the ending opened a shaft with a hole in the top of it. Play's report
was that the map "still shows what you get when no background is set", which is exactly what it was.

It is set in `ApplyEnvironment` now, at build time, in **every scene** - and also at runtime by the
ending. The build-time half is the belt to that brace: a runtime assignment that does not run leaves
the default showing, and there is no version of this game where the default is the right answer.
Ambient is `Trilight`, so the skybox feeds nothing and this cannot move a tuned lighting value.

**A field nothing looks at is not a field that is set correctly.**

---

## A MODEL CANNOT ALWAYS TELL YOU WHICH WAY ITS DOOR FACES (2026-09-01)

`cable_car.glb`'s facing was derived: take the two door leaves, average their offset from the cabin
centre, call that the doorway's outward normal, yaw the cabin so it points at the room. Sound
reasoning, and it produced a car that play reported as backwards.

Measured off the file, both leaves sit at the same place - the build logs
`L=(0.08, -2.05, 0.00) R=(0.08, -2.05, 0.00)`. They are two leaves of one sliding door, modelled
nearly shut and near the cabin's own middle, so the horizontal signal is **0.08 in a hull 1.74
wide**. That is noise wearing the shape of an answer, and averaging it produced a confident direction
with nothing behind it.

CLAUDE.md's rule is to *measure a rotation off the object rather than write it* (learned from the
chess set and the ladder). **The rule assumes the measurement exists.** Where it does not, a constant
that is honest about being a constant beats a derivation that is wrong - so the facing is now one
authored number, `CabinDoorYaw`, with the offsets logged beside it so it can be re-checked.

**And the better fix was to stop needing the answer.** The cabin's collision cage now opens on BOTH
sides to be boarded and seals both the instant somebody is aboard, so which way the visible doorway
points is a cosmetic question rather than one that decides whether the vehicle can be entered.

---

## THREE FIXES AIMED AT THE CAR, AND THE LIP WAS IN THE ROOM'S OWN FLOOR (2026-09-03)

Play reported four times, over three days, that the cable car could only be **jumped** into. Each
report was answered by changing something on the car: the cabin's collision slab was reached back
`CabinThresholdReach` toward the room, then dropped `CabinThresholdDrop` below the room floor, then a
threshold check was added — which turned out to be comparing `carRoot.position.y` with `path[0].y`,
i.e. a number with itself. Made real, it measured the step at **−0.03m** against a `stepOffset` of
0.72, which proved only that the snag was somewhere else. It was never re-aimed, because each round
had a plausible story and none of them was measured.

So the build was made to print the route instead (`SceneBuilder.ReportBoardingRoute`): every collider
between the room and the seat, in room3-2N's own local metres, with the breach plane and the floor
plane stated beside them. The first run answered it in one line.

```
room/BreachApron   x  8.75..13.25   y  -0.40.. 0.00
```

**The apron's top face was at y = 0.00 — the room floor's own plane — overlapping the room's floor
slab by `WallDepth`.** Two box colliders sharing a plane. A `CharacterController` resolves by
sweeping, and where two colliders meet on one plane both report contact at once; the depenetration
lifts the capsule a hair and the step stalls. It reads exactly as reported: a lip you cannot walk
over and can jump.

**The project already knew this.** `SceneBuilder.Departure.cs` says it twice in its own comments —
*"two colliders that merely touch leave a seam exactly where a sweep will find it"* — and it is the
stated reason the cabin's slab was given `CabinThresholdDrop` in the first place. The apron was added
later, to fix an earlier round of the same complaint, and never got the same treatment. **A fix that
introduces the very thing the fix was about is the easiest one to not look at again.**

The three surfaces now step down and no two share a plane: room floor `0.000`, apron `−0.015`
(`ApronDrop`), cabin floor `−0.030` (`CabinThresholdDrop`). The apron also starts `ApronUnderlap`
INSIDE the room rather than at the wall plane, so it is unambiguously under the player before the
room's floor ends — an overlap cannot have a seam in it.

**The lesson is about the loop, not the geometry.** Three guesses cost three builds and three
playtests; the report cost one build and read the answer off a line. When a symptom survives two
fixes, stop fixing and make the build print the thing being reasoned about — and leave the print in,
so the next reader gets the number rather than the story.

**And a diagnostic must know what is switched off at runtime.** The first run of the report flagged
five obstacles; three of them — both doorway walls and the breach gate — are disabled before the
player is asked to walk the route. Flagging those would have sent the next reader after the wrong
ones, which is the exact failure the report exists to end, so it now marks them `(opened to board)`
and excludes them from the verdict.

---

## A LID THAT TRAVELS ITS PARK DEPTH ARRIVES HALF ITS OWN THICKNESS TOO HIGH (2026-09-03)

Room3-0's shaft lid was parked `ShaftLidPark` under that room's floor and raised by
`ShaftLidPark`. Symmetrical, obvious, and wrong: a park depth is measured to the lid's **centre**,
and the surface it has to end up flush with is a **face**.

`BuildSlab` centres a floor at `-WallThickness / 2`, so room3-0's floor runs from `-0.1` to `0`. A
lid `WallThickness` thick arriving with its centre at `0` therefore stood 5cm proud of that floor
with its underside 5cm **above** the slab's — plugging nothing but air.

What play saw from room3-2N, sixteen metres below, was the consequence rather than the cause: the
2cm side clearance became a slot only 5cm deep, open on a **21.8° cone** straight into a lit room, so
the sealed hatch wore a bright outline. It was reported as "the ceiling door's gap is still visible",
which is a description of a gap and says nothing about a rise being half a thickness long.

Stated as a **recess off the surface** now, with the travel derived from it — the stop is tied to the
face it must be flush with, and `ShaftLidRise` is a `const` expression rather than a number typed
twice. The lid is also `2 × WallThickness` thick and clears the hole by 5mm a side instead of 20,
which takes the residual sightline to **1.3°** — 0.018° subtended from the floor below, a third of a
pixel at 1080p.

**It is not zero, and it cannot be**, because the lid has to pass up through the shaft to get there
and so can never be wider than the hole it fills. A plug in a hole always leaves a slot; all that can
be chosen is how narrow and how deep. Anything wanting a true seal has to arrive from a side the
shaft does not constrain.

**Check both halves of a "move it into place" number: what the distance is measured to, and what the
destination is measured to.** They were a centre and a face here, and the two are not the same point.

---

## A LOG OF THE INPUTS IS NOT A MEASUREMENT (2026-09-03)

Three bugs in one session had the same shape, and each one survived a diagnostic that was working
correctly.

- **The bed collider** printed `bedding to y=0.691 (0.69m of mattress)` while the box it had just
  built covered a horizontal axis. `BoxCollider.size` is read in the collider's OWN space and
  `messy_bed.glb` is imported at 0.00941 with `Euler(-90, 180 + yaw, 0)`, so writing a height into
  `size.y` shortened the box along the bed's LENGTH. Play found it as "throwing something at the
  pillow end drops it under the bed" — three rounds after the first fix.
- **The fall's keep-out check** computed the nearest standing rack face from the constants that were
  supposed to govern the racks. It passed every build while play kept reporting the cabin flying
  through structures. A sum agreeing with itself proves the arithmetic was copied correctly and
  nothing else.
- **The cable car's strike box** was tested at the car's pivot, which `BuildCableCar` had already
  moved to the cabin's FLOOR — so the box sat 2.1m below the cabin. Three fixes were aimed at *which
  structures the car was told about*, all of them measured, all of them agreeing, none of them
  measuring this.

**The inputs were right in all three.** What was wrong was the object built from them, because the
space it was built in was not the space assumed — a rotated import, a scaled parent, a recentred
pivot. A log of the values going in cannot see any of that, and worse, it reads as confirmation:
three separate investigations were sent elsewhere by a number that was correct.

**So print the finished thing.** World bounds of the collider that now exists, the world centre of
the box a test actually uses, the position of the object as placed. `SplitBedCollider` and
`ReportBoardingRoute` both do this now, and both settled their bug on the first run after they were
added.

**And prefer an assertion measured off what was really built** over one computed from the constants
that were meant to govern it — `nearestStandingFace` is scanned off the cells that exist, not
derived from `FallShaftRadius`.

---

## THE MENU CAPTURE CANNOT BE TRUSTED TO SHOW A REFLECTION (2026-09-03)

> **OUTCOME, 2026-09-04: the reflective floor this entry chases was finished, played, and then
> REJECTED by request** — "I thought reflection would be good, it is really not". `FloorSmoothness`
> is back to 0.65, `FloorMetallic` to 0, `FloorBump` to 0.6. Everything below still stands and is
> still in the build: the bugs were real, the fixes are correctness fixes, and two of them
> (`ProbeBoxMargin`, `WithFlatReflection`) are what the WALLS reflect through. What was thrown away
> is one art decision, not the plumbing. Read this before rebuilding any of it.

Raising the floor's smoothness and giving it a little metallic (`FloorSmoothness`, `FloorMetallic`
in `SceneBuilder.cs`) was meant to match a reference title screen where the floor mirrors the room.
Both values land in `FloorWhite.mat` exactly as written — confirmed by reading the asset back off
disk — and `LightingProbeDump` shows the floor on the same `BlendProbes`/`ReflectionProbeStatic`
footing as a wall panel that does visibly mirror the room. And yet `MenuBackground.png` came back
**byte-identical on the floor** across four rebuilds: smoothness 0.65, 0.9 and 0.97, metallic 0 and
0.22, every combination. A pixel-value comparison (not a look) is what found this — see
[[feedback_verify_built_output_not_inputs]].

A one-frame warm-up render right after `room.SetActive(true)` — on the theory that a `ReflectionProbe`
just reactivated by `SleepCycle`/wake is not yet in whatever table URP consults for
`unity_SpecCube0` — made no difference either, and is not in the build.

**RESOLVED, SAME DAY — and it was neither of the two possibilities above.** `LightingProbeDump`'s
claim that "the floor is on the same footing as a wall panel that visibly mirrors the room" was true
of the wrong scene: its hardcoded path opens `Cycle1.unity`, and nobody had pointed it at the floor
and wall the menu shot actually photographs before concluding the two matched. Confirmed instead by
substituting a **loud, unmistakable magenta cubemap** for the scene's default reflection during
`CaptureMenuBackground` and rebuilding (see `WithFlatReflection` in `SceneBuilder.Capture.cs`): the
floor and the two SIDE walls picked the magenta up — fully and partially — and the FAR wall (the one
carrying the two soft highlights this entry wondered about) did not. So the far wall's highlights
were a real probe reflection all along, and the floor and side walls were never inside Room1's
reflection probe at all — a one-frame timing race was never the cause.

**The actual bug**: `BuildReflectionProbe` boxed every room's probe at exactly
`RoomWidth × RoomHeight × RoomDepth`, centred so its six faces land exactly on the floor slab's top
surface, the ceiling, and each wall's inner face — the same "nothing may be exactly the size of the
hole it sits in" trap already known from coplanar meshes (§3), just in a bounding box instead of a
mesh. A wall panel's renderer bounds sit BEHIND that face by `WallDepth`; a floor slab's sit BELOW it
by its own thickness. Both renderers straddled the box boundary instead of sitting inside it, so
Unity's automatic reflection-probe assignment read them as outside this probe and fell back to
`RenderSettings`' default reflection — the blue procedural skybox, which is exactly what the floor
had been showing regardless of `FloorSmoothness`/`FloorMetallic`: changing either tunes how a
material uses whatever reflection reaches it, and none was reaching it at all.

**The fix, in two parts, because they are two different bugs that happened to look like one:**

1. `ProbeBoxMargin` (0.5m, in `SceneBuilder.cs`) pads every room's probe box past its walls and
   floor, so a renderer sitting exactly on the room's own boundary is comfortably inside rather than
   straddling it. This is the fix for the RUNNING GAME — verified geometrically (not by eye) by
   dumping Room1's actual probe bounds and every floor/wall renderer's actual bounds out of the built
   scene and checking containment on all three axes, not by rendering anything.
2. `CaptureMenuBackground`'s one-shot build-time render still races the scene's own probe data the
   way `CaptureCyclePreview` already documented — that part of the original write-up was right, just
   attached to the wrong symptom. It now shares `CaptureCyclePreview`'s fix
   (`WithFlatReflection`): a flat, near-white cubemap stands in for the real probe for the duration of
   both menu-background frames. It buys correctness (no more blue cast) at the cost of detail — a
   flat colour has no room geometry to put into the reflection, so the static title-screen PNG will
   never show the door or the furniture mirrored in the floor the way a live, fully-loaded frame of
   the running game can.

**The play question above got answered, by the user's own screenshots (2026-09-04), not by this
build.** The padded probe box worked — the in-game floor now shows real reflected structure (soft
streaks, a hint of the grid) it never showed before — but it came back visibly BLUE next to the
title screen's near-neutral flat substitute. Measured, not eyeballed: sampling both PNGs put the
menu floor at B−R ≈ +4 (of 255) and the in-game floor at B−R ≈ +23.

That sent the same "confirm off a loud colour, not a guess" method at the bake itself. A new
diagnostic (`CubemapDump`, temporary, deleted after use) averaged every face of the baked
`Room1_Reflection.exr` directly — no rendering, just the asset:

| face | before | after ambient fix | after `PanelLitColor` fix |
|---|---|---|---|
| +X | (.609, .613, **.632**) | (.609, .613, .628) | (.609, .613, **.621**) |
| +Y (ceiling) | (.866, .866, **.876**) | (.866, .866, .871) | (.866, .866, **.866**) |
| −Y (floor) | (.710, .727, **.764**) | (.710, .727, .761) | (.710, .727, **.754**) |

Two real, separate sources, found in order and each worth keeping regardless of the other:

1. **`ApplyEnvironment`'s trilight ambient colours carried B a hair above R/G in all three bands**
   (0.356/0.356/**0.361** and so on) — invisible on a directly-lit wall, where diffuse albedo and
   direct light dominate the sum, but this constant is "doing almost all of the wall and ceiling
   lighting" per the entry above it, and a probe bake sums the room fully lit, ambient included.
   Flattened to equal R/G/B; the level (comfort tuning) is untouched.
2. **The bigger one: `PanelLitColor`, used for every wall panel, the ceiling, AND the floor (which
   duplicated its exact triple as a separate literal rather than referencing it) was
   `(0.85, 0.85, 0.86)`** — the same ~1% blue-over-white on literally every surface in the building.
   Invisible on a wall lit by one or two bounces; not invisible on a bake that sums eight GI bounces
   (`docs/rendering-notes.md`) of a colour multiplied by itself bounce over bounce. Flattened to
   `(0.85, 0.85, 0.85)`, and the floor now references the constant instead of duplicating it, which
   is how the two drifted a hair apart in blue in the first place without anyone deciding that on
   purpose.

**What's left after both (+Y is now exactly neutral; the side faces and −Y are not, at roughly half
their original bias) is very likely genuine, not a bug**: the room contains one clearly coloured
object, the bed's blue duvet, sitting low in the room — exactly where a downward-facing cubemap face
would pick up its bounce. A floor that mirrors the room honestly should show a whisper of the one
coloured thing in it. Chasing that last residual to zero would mean de-saturating the bedding's own
GI contribution, which is correcting the reflection for being accurate rather than for being wrong.

## A ROUND HIGHLIGHT ON A SQUARE FIXTURE IS THE PANEL AND THE LIGHT DISAGREEING (2026-09-04)

Once the floor was actually reflecting (`ProbeBoxMargin`, above), play reported the wrong shape: the
soft white blobs on the walls read as circles sitting on a grid of square ceiling panels, and — by
request — this needed to look calm and even, closer to `mainmenu_example.png`, not lit by visible
spots.

**The mismatch is structural, not a tuning slip.** Every ceiling fixture is two separate objects
occupying the same spot: a square 1.4m emissive panel (the thing that LOOKS like a light) and a
`Light` component standing in for what it emits (`BuildCeilingLights` — URP has no realtime area
light, the same gap this file's shadow entry above already names). A wall's glossy highlight is the
`Light`'s own specular lobe, and a point-like source's specular lobe is round no matter what shape
the object casting it is. The panel's true square shape only ever reaches the wall through the baked
reflection probe, at a small fraction of the light's direct strength — so what dominates on screen is
the round one.

**Narrowing the cone was the first idea, and the geometry rules it out.** `CeilingSpotAngle` was
156.8° — wide on purpose, to behave "like a panel rather than a torch." Clearing the nearest wall
entirely needs `atan(2.6 / 5.41) ≈ 26°` of half-angle, i.e. an ~52° cone — and four fixtures that
tight leave a floor radius of about 2.6m each, with dark gaps between them across an 8.75 × 10.5m
room. Worse, the fix does almost nothing over most of that range: `tan` blows up fast near 90°, so
120° reaches almost as far up the wall as 156.8° does. There is no cone angle between "still hits the
wall" and "leaves the floor dark" for four fixtures at this height and this spacing.

**Softening the SURFACE instead of the light works, and was still reverted the next day.**
`WallSmoothness` 0.85 → 0.65 costs nothing in coverage — no light, ambient or intensity constant
moves, only how tightly a wall returns a point-source highlight — and it did what it promised: the
two circles spread out and the wall read as an even gradient, visibly closer to the reference in
the menu capture. **Reverted to 0.85 by request the same day** ("the previous feel was better"),
along with the reflective floor, because the same knob blurs the wall's own mirroring of the room
along with the highlight — the identical knock-on `FloorSmoothness`' history records at this value,
"a reflection probe samples a blurred mip... a white room averaged to a flat sheet". A wall that
reflects and a wall with no round highlight on it are the same setting pointed two ways.

**So the standing answer is: the round highlight is the price of a wall that reflects at all, and
that price has now been looked at from both sides and judged worth paying.** The one route nobody
has built, and the only one that would make the highlight actually square without touching the cone
or the surface, is a light COOKIE shaped like the panel on each fixture.

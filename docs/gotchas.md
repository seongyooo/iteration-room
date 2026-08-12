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

# Rendering, lighting and art

Fixture placement and ambient tuning, materials, probes, and the art rules the room is built on. Numbers here were found by eye over several passes.

---

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
  - **`_BumpScale` can go well past the sub-1 that looks sane.** Central differences across a smooth field give tiny gradients, so the map is genuinely shallow: at 0.7 the surface renders *perfectly flat* even with the camera against it; at 10 it is stucco. Ceiling **1.8**, floor **0.6**, walls **0.2** — and **the number that is right depends on the surface's gloss**, because relief on a matte surface only reaches the diffuse while relief on a glossy one modulates the specular. See *Wall, floor and ceiling* below for how the floor's 1.8 became visibly wrong the moment its smoothness went up.
  - **Walls are a smooth glazed panel, not plaster**: `_Smoothness` **0.85** with relief kept to a whisper purely so the specular isn't a uniform sheet, which is what makes a flat surface look CG. Floor **0.65**, ceiling **0.30** — neither is matte any more; see below for why each moved.
  - **Tiling must be set on `_BaseMap`, not `_BumpMap`** — URP/Lit drives the normal map's UVs from `_BaseMap`'s transform, so a scale on `_BumpMap` does nothing. Per face: (5,3) for a 1.7 × 0.9m wall panel vs (26,30) for a 9 × 10.9m slab, for the same ~0.35m grain.
  - It imports as `TextureImporterType.NormalMap`; without that the shader reads raw RGB as a normal and tilts every surface. It also means you **cannot measure it by sampling `.r`/`.g`** — Unity re-encodes to DXT5nm. Judge it from a render.
- **`BalloonPink` is premultiplied alpha, and the two halves of that have to agree.** The material
  carries `_ALPHAPREMULTIPLY_ON`, so the blend must be `One` / `OneMinusSrcAlpha`. Captured all three
  ways to settle it: premultiplied + `One` gives pink translucent balloons; premultiplied +
  `SrcAlpha` multiplies alpha twice and washes them grey; keyword off makes the bodies **disappear**,
  because with premultiply off the diffuse is no longer carrying them and only the opaque knots are
  left. So the keyword stays and the blend follows it.
  - **`MakeTranslucentMaterial` now writes the whole pairing**, so a scene build and a build-target
    switch finally agree and the `.mat` stops churning. It used to force `SrcAlpha` and never touch
    the keyword, which left the Editor duller than the player.
  - **Do not write `_Blend = 1` (Premultiply) to say so.** It looks like the tidy way to declare the
    intent and it backfires: URP re-derives the keywords when the material is saved and **disables**
    `_ALPHAPREMULTIPLY_ON`, leaving `One` blending over non-premultiplied colour - the balloons come
    out washed toward white. `_Blend` stays **0 (Alpha)** with the keyword forced on, which is the
    pairing the asset has carried all along. Caught by capturing the room again after the change.
  - The other three transparent-looking materials are not transparent at all - `BedSheet`,
    `BedPillow` and `KeyGold` are `_Surface 0`, queue 2000, no transparent keyword. Their `_SrcBlend`
    does nothing whichever value it holds.
### Cost, and what was cut for WebGL

The first itch build came back "laggy everywhere, regardless of room", and *everywhere* is the
diagnostic: a draw-call bottleneck would make Room2 with its 71 balloons far worse than an empty
room. Uniform cost points at the full-screen passes and fill rate instead.

- **Additional-light shadow atlas 4096 → 2048.** The 4096 was set because *six* fixtures shared the
  atlas and 2048 made URP log `Reduced additional punctual light shadows resolution by 2 ...` and
  drop each map to 512. **There are four now** — only Room1 casts — so 2048 gives each a 1024 map
  with no reduction. If the shadowed count ever climbs past four, watch for that warning and put it
  back.
- **Main light shadows OFF.** URP's main light is the brightest *directional* light, and this project
  deletes the scene's because the rooms are sealed boxes with a ceiling slab. Its shadows had been
  enabled the whole time, reserving a 2048 map and a pass for a light that does not exist.
- **Shadow distance 30 → 15.** Shadows are cast only in Room1, which is 10.5m deep. Halving the
  distance also doubles the texel density of what is left, so it is a quality gain as much as a cost
  one.
- **SSAO at half resolution** (`Downsample`). The radius is 0.045, so what it draws is a thin contact
  line under objects, and a thin line survives being resolved at half res. Turn it back off if the
  contact shading starts to crawl along the door edges.
- **Soft shadow quality was already Medium**, not High — nothing to win there, despite the guess.
- **Still on the table, both visible**: MSAA is `4x`, and this room is black grid lines on white
  panelling, which is the worst case there is for aliasing — `2x` is the compromise. `renderScale` is
  1.0 and 0.85 would take about 30% of the fill rate. Neither has been touched.

**Do not trust a screenshot taken straight after a scene rebuild.** The glb furniture instantiates
behind the build, and a capture that races it shows Room1 with the bed missing and its shadow still
lying on the floor — which reads exactly like a rendering regression and is not one. Capture a second
time before believing it.

- **Gloss only works because of the reflection probes.** Raising smoothness without them paints the blue procedural sky over every white panel — the exact failure the old "0.03 smoothness everywhere" rule existed to dodge. Note that **a mirror of a white room is still white**: the gloss reads as sheen and falloff rather than visible reflected objects. If a harder "switched-off display" look is wanted, the panels need a *darker* albedo.
- **One baked reflection probe per room** (`BuildReflectionProbe`), box-projected and sized to the room so a reflected wall stays on the wall instead of sliding with the camera. `reflectionIntensity` is **1.0**.
  - **Baked during `Build()`** via `Lightmapping.BakeReflectionProbe`, writing `Assets/Textures/*_Reflection.exr`. A Realtime probe was tried first, reasoning that rebuilds would invalidate baked data — but **a realtime probe renders nothing until play mode**, so `probe.texture` came back EMPTY and the glossy walls had nothing to reflect. Baking in the build gets a real asset regenerated in lockstep with the geometry it captures.
  - **AND IT WAS STILL REFLECTING NOTHING, THROUGH FOUR MORE FAULTS, UNTIL 2026-08-12.** Everything above about gloss needing something to mirror was true and was not happening: the probes were capturing the skybox and, after save, not even carrying that. The four faults and their fixes are enumerated in `gotchas.md` — bake ordering, `Baked` vs `Custom` mode, two URP asset toggles, and `ReflectionProbeStatic` flags that no script-built object had. In short:
    - `BakeReflectionProbes()` runs **last**, immediately before `EditorSceneManager.SaveScene`, and walks the scene rather than taking a list so a room added later cannot be missed. Baking last is better anyway: the rooms are furnished by then, so a glossy wall mirrors the room the player actually sees rather than a bare shell.
    - Probes are **`Custom` mode with `customBakedTexture` assigned** — a `Baked` probe keeps its cubemap in a lighting data asset this build never generates, so the reference did not survive a save.
    - `ConfigureLightingPipeline` sets **`m_ReflectionProbeBoxProjection` and `m_ReflectionProbeBlending`**; the per-probe `boxProjection` flag is inert without them, and blending is what stops a pop at the doorways where two rooms' probes overlap.
    - `MarkReflectionProbeStatic()` flags **966 renderers** before the bake, excluding movers by component so nothing is captured in a pose it does not hold.
    - It logs `wired/total` plus the static count, because the failure has no other symptom — an empty probe does not error or render magenta, it just makes the room slightly flat.
    - Skipped entirely under `-nographics` (a bake is a render), leaving the previous cubemaps in place — the same bargain `CaptureMenuBackground` makes.
    - **This changes every glossy surface in the building, not just the metal.** The walls now mirror the room they are in rather than nothing at all.
  - `resolution` **512**: at 256 the ceiling fixtures reflect as vague smears.
- **The three escape objects are the only metal in the building** (`MakePolishedMetalMaterial`): metallic **0.9**, smoothness **0.97**, base colour lifted 30% toward white, emission **0.40** of the accent. All four numbers were rendered and looked at; the reasoning, including what the three rejected settings looked like, is in the function's own comment. Two rules general enough to carry: **a near-mirror suits a small object where the walls' 0.85 does not**, and **base colour on a metal is reflectance rather than paint**.
- **The wall panels are displays and they boot at the start of every iteration** (`WallPanelDisplay`, driven from `WakeUpSequence`): dark (`WallPanelColor` 0.13) when you wake, sweeping to white as you sit up. This is the visual half of "New cycle initialized", and it retroactively makes the wall grid *diegetic* — the panels aren't tiles, they're screens, which is why the room is built out of them.
  - Panels come up **from the floor rows first with per-panel scatter** (`onsetJitter`). A uniform fade reads as a dimmer; a staggered one reads as separate screens waking. Onsets are computed once in `Awake` from world Y with a **fixed seed**, so the boot is identical every iteration — a loop should be.
  - **The invariant that makes the sweep finish: every onset must be `<= 1 - panelFade`.** `PowerUpRoutine` stops at `powered = 1`, and a panel only completes when `(powered - onset) / panelFade` reaches 1. This shipped broken and did not look like a bug: the formula was `height * (1 - panelFade) + jitter`, so the top row could land at 0.897 against a 0.780 ceiling, and **29 of 279 panels froze part-lit at albedo 0.526 — mid grey, permanently, in a room whose whole point is that it is white.** The jitter is now subtracted from the ramp rather than clamped off the top, so the scatter survives at the ceiling instead of half the top row sharing one onset. If `panelFade` or `onsetJitter` is ever retuned, re-check that `max(onset) <= 1 - panelFade` still holds.
  - Driven through a **`MaterialPropertyBlock` on `_BaseColor`** (`_Color` would silently do nothing — URP/Lit's albedo is `_BaseColor`), which is what allows panels to differ mid-sweep. This **breaks SRP batching** across the array; fine at this scale, worth knowing if the room grows.
  - `PowerDown()` is called with the eyelids already shut, so the player only ever witnesses the room coming *back*.
  - **The last `collapseLeadTime` (10s) of a cycle is a collapse**: `SetFlare` blows the panels past white with heavy emission while `CameraShaker` ramps up, both held at full through the blink before being cleared behind the black. The ramp is **squared** — a linear one reads as a slider being dragged. `PanelWhite` has `_EMISSION` enabled with a *black* colour purely so the keyword is compiled in; a property block cannot turn a shader keyword on. Escaping is the one case where it does not peak.
  - The material itself stays **white** — the resting state for all but the first seconds of an iteration, and what the probes bake against.
- `Assets/ArtAssets/Furniture/` holds `messy_bed.glb`, `chess.glb`, `gold_key.glb` and `rubiks_cube.glb`. **All four are CC-BY, and their authors and source URLs live in `asset-licences.md`** — which is also where the attribution obligation that comes with BY is tracked. `nightstand.glb` was deleted 2026-08-14, unused. `messy_bed` won a five-way in-engine comparison because its bedding is rumpled and slept-in; the loop opens every iteration by waking up here, so a neatly-made bed undercuts the premise. Cost: **306,907 triangles**, 20-60× the alternatives.
  - **`.glb` needs `com.unity.cloud.gltfast`** (6.19.0). Without it Unity imports a `.glb` as an opaque `DefaultAsset`.
  - Keep the glTF materials these ship with; a hand-rolled URP/Lit stand-in drops the metallic/roughness maps.
  - **These models bake contact shadows into their occlusion maps**, which only looks right in the authored pose. Moving the pillow left those shadows painted on the bedding as dirt. `DisableBakedOcclusion` clones the material into a project asset with `occlusionTexture_strength = 0`; SSAO then supplies contact shadows that follow the actual geometry. The clone must be its own asset because glb materials are sub-assets regenerated on every reimport. **If furniture shows a shadow that doesn't move with the light, this is why.**
  - They are authored **Z-up** (`Quaternion.Euler(-90, …)`) and their **units differ wildly** with bounds not centred on the origin. Don't assume centimetres. Measure bounds at scale 1 and derive `uniformScale = 2.05f / max(horizontal extent)` — that is how `messy_bed`'s 0.00941 was found.
- `PlaceModel(...)` instantiates via `PrefabUtility.InstantiatePrefab` and repositions so *rendered bounds* (not the mesh pivot, which is at an arbitrary corner) centre on a target X/Z with the base on a given floor Y. Its optional `rotation` is applied **before** bounds are measured, and the generated `BoxCollider` un-rotates world-axis-aligned bounds back into local axes so a rotated model doesn't get height and depth swapped. **Reuse this for any future model placement.**
- **The HUD's glyphs are drawn from code** — `Assets/Textures/Icons/` written by `IconCanvas`, a ~60-line software rasteriser. Shapes are predicates over a 0..1 square composed with `&&` and cut back with a `sign` of -1, at **4×4 supersampling**, which is the only reason a 128px disc has a clean edge. Origin is **bottom-left**, matching Unity's texture coordinates. They are **white with the shape entirely in alpha** (the HUD tints via `Image.color`) and import **uncompressed** — block compression frays something that is nothing but alpha. The pin's needle is drawn deliberately fatter than the real pin: at 58px displayed, a true needle lands on one screen pixel and vanishes.
- `GhostFaint.mat` is **live again**, now on `IterationRoom/GhostFaint` rather than URP/Lit (see **What a ghost looks like**). Its GUID was kept across the shader swap rather than making a new asset, so nothing referencing it had to be found and repointed. Note that **swapping a material's shader does not drop the old keywords** — they move to `m_InvalidKeywords` and sit in the diff forever, so `GhostFaintMaterial()` clears `shaderKeywords` explicitly. The worked example of the URP *transparent* set-up that the balloons still need — all of `_Surface`/`_SrcBlend`/`_DstBlend`/`_ZWrite` **and** the `_SURFACE_TYPE_TRANSPARENT` keyword — is now `MakeTranslucentMaterial` alone.
- **A marble floor was built and dropped** — the veining pulled the room from "cell" toward "hotel lobby". If it comes up again: turbulence marble needs `turbulenceScale` above **one full cycle** (0.55 wobbles the bands half a cycle and reads as wood grain; 2.2 makes neighbours cross and merge into something stone-like). A textured floor also **cannot carry the grain normal as well**, since URP/Lit drives every secondary map from `_BaseMap`'s single UV transform.
- **Furniture is the remaining art gap.** Recommended CC0 sources: Sketchfab's CC0 filter, Poly Haven, ambientCG. Claude can't download these — drop them in `Assets/ArtAssets/Furniture/` and `PlaceModel` handles placement.

## Wall, floor and ceiling: the six numbers, and why each is that number (2026-08-20)

| surface | albedo | smoothness | `_BumpScale` |
|---|---|---|---|
| wall (`PanelWhite`) | 1.0 | 0.85 | 0.2 |
| floor (`FloorWhite`) | 0.85 | 0.65 | 0.6 |
| ceiling (`CeilingWhite`) | 1.0 | 0.30 | 1.8 |

All three share one generated normal map at one physical grain size (~0.35m per repeat on every
surface); the tiling numbers differ only because a wall panel face is 1.7×0.9m and a slab is 9×10.9m.

### The floor's albedo, and why it is the lever

**Albedo 1.0 is a surface that returns every photon that hits it.** Nothing does — fresh white paint
is about 0.85 — and this building was painting every wall, floor and ceiling at exactly 1.0 with four
spots at intensity 10.5 pointing straight down. Play reported the obvious consequence: *stand in the
middle of a room, look at the floor, and it is too white*. It is measurable, not a matter of taste —
the title screen's own capture had the bed room's floor at (235, **253, 255**), two channels already
clipped.

The three ways to fix it are not equivalent, and this is why the fix went where it did:

| lever | what it touches |
|---|---|
| light intensity | darkens the **walls** with the floor — the room is supposed to be bright |
| exposure (volume) | **every surface in the game**, including the objects |
| **albedo** | the one surface that is wrong |

The floor is 15% off clipping now and still reads white, because the eye has nothing brighter on
screen to compare it against.

### Smoothness: the floor up, the ceiling off zero

A surface at smoothness 0 is a perfectly uniform field, and **the eye reads a field with no variation
in it as blown out rather than as bright** — which was the other half of "too white". The floor at
0.65 catches the ceiling fixtures as broad pools with the grain riding in them, so there is structure
to read distance off.

The ceiling went 0 → 0.3 for the same reason arrived at from the opposite end. The argument for 0 was
that there is nothing above a ceiling to reflect. What that missed: the spots point down, so a
ceiling has **no direct light at all** and ambient ground as its only term, which makes it a constant
field across the whole slab — and the grain cannot rescue it, because trilight's ground and equator
are only 0.075 apart and perturbing the normal moves the result by under a percent. Only the probe
can put a gradient there, and at smoothness 0 the probe contributes nothing.

### Bump: the mismatch that gloss exposed

Raising the floor from 0.3 to 0.65 exposed something that had been sitting there all along. Wall and
floor differ **only** in `_BumpScale` — 0.2 against 1.8, nine times apart. While the floor was matte
that relief only reached the diffuse and read as the tooth of sealed concrete. Glossy, the same grain
modulates the **specular**, and the floor stopped being the matte counterpart to a glazed wall and
became a third finish that was neither. Play called it as *the wall and the floor don't go together*.

**The floor went to 0.6 and the wall stayed at 0.2, deliberately unequal.** A wall is a grid of
1.7×0.9m panels with near-black grooves between them and already has geometry breaking up its
reflection; the floor is one bare slab where the grain is the only thing doing that job. Matching
them exactly was never the goal — what had to go was the *asymmetry of visibility*, one surface's
grain showing in the specular while the other's showed nowhere.

**Taking the wall to 0 was considered and rejected.** On its own it widens the very gap being closed
(0 against 1.8), and a perfectly flat wall at smoothness 0.85 sharpens the reflection until the 512
probe — already chosen over 256 because the fixtures smeared — starts to show.

### Why the fixtures are square but their pools are round

Asked during the same pass, and the answer is that **the current rendering is close to correct.** A
1.4m emissive panel 5.41m above the floor is 3.9× its own width away, subtending about 15°: it is
very nearly a point source, and the illumination it lays on a parallel plane is dominated by
inverse-square and cosine falloff, both radially symmetric. A square pool would only be visible
within about twice the emitter's width. What *is* square on the floor is the panel's own reflection,
baked into the probes (the fixtures are not in `MovesDuringPlay`) and sharpened by the floor's new
gloss — and that is square because the panel really is.

**A spot cookie was considered and rejected**, though it is the textbook fix for a square emitter.
Three reasons, in order of weight: (a) the 130° cone is not a spotlight, it is what lights the room
evenly — its radius at floor level is 11.6m against a 8.75×10.5m room, so masking it into a square
necessarily darkens the corners, and "the room lost the clinical evenness it is supposed to have" is
a failure this project has already had once, from the ambient pass; (b) a cookie masks flux, so
`intensity` 10.5 would have to be re-derived, and every albedo/smoothness judgement above sits on top
of that number; (c) see the paragraph above — there is no error to correct. If the reflected square
is ever too loud, the lever is the fixtures' emission (3.5 in `MakeEmissiveMaterial`), which does not
touch room brightness at all.

### How the numbers were found, and the tool that is BACK (2026-08-25)

A rebuild-look-guess cycle is four minutes a step, so these were found at a **live dial** built into
the calibration room for the day: three columns (wall, floor, ceiling), a slider each for albedo,
smoothness and bump scale. It was **removed once the values were settled** — a development panel on
the first screen every player sees is not something to ship — and `SceneBuilder` holds the numbers, as
CLAUDE.md §2 requires. Recorded here because the technique is worth having again, along with the four
things that made it work:

- **Every write went through a `MaterialPropertyBlock`**, which is per-renderer. The calibration
  room's walls are the same `PanelWhite` asset as every wall in the building, so driving them there
  touched neither the asset nor any other room.
- **Nothing was saved.** Numbers were read off the panel and typed into the builder. A tuner that
  persisted would be a second source of truth for a value the builder owns.
- **`_BumpScale` is drivable from a block only because `_NORMALMAP` is already compiled in** by
  `ApplySurfaceDetail`. A property block cannot turn a shader keyword on — the same rule the wall
  panels' emission is set up under.
- **The cursor was the whole awkwardness.** That room keeps the pointer captured, because that is what
  the sensitivity step measures, while a slider needs it loose. It took a TAB toggle and a hook in
  `FirstPersonController` to stop the first click at a handle taking the lock back.

**It has been rebuilt** (`Assets/Scripts/Dev/LightingTuner.cs`), because the bake landing invalidated
the balance every one of those numbers sat on: ambient was halved the same day, and the six surface
values were chosen against walls that turned out to be rendering inside out. It carries all four
rules above, plus two the first one did not need:

- **`DynamicGI.UpdateEnvironment()` after every ambient write**, or the values never reach a shader.
- **The shadow biases live on the URP ASSET**, which is the one thing here that is not per-instance -
  so the panel seeds them on open and puts them back on close. A rebuild would restore them anyway
  (`ConfigureUrpAsset` authors both every time), but that would leave the Editor sitting on a dirty
  asset until somebody happened to build.

**It is disarmed in a release player** (`Application.isEditor || Debug.isDebugBuild`, `CaptureRig`'s
bargain) rather than deleted this time. Groups on it: ambient x3, fixture intensity and cone, both
shadow biases, and the six surface numbers.

**AND IT LIES ABOUT EVERYTHING THAT IS BAKED.** The fixtures are Mixed - direct realtime, bounce
baked - so ambient, intensity and albedo show their direct half live and their indirect half frozen
at the last bake. The panel gets you to the right neighbourhood; the loop is still **type the numbers
in, rebuild, RE-BAKE, look again**. Only the shadow biases are honest live, having no baked
component at all.

## Shadows: what the rooms cast, and why the player casts nothing (2026-08-24)

Before this day, **exactly one light in the building cast a shadow**: `BuildCeilingLights` was called
with `castShadows: true` for Room1 alone, and inside Room1 `index == 1` handed it to a fixed CORNER
fixture of the 2x2 ceiling grid. Main-light shadows are off in the URP asset and every directional
light is destroyed at build, so thirteen of fourteen rooms cast nothing at all.

That surfaced as a complaint about the PLAYER's shadow reading as detached. It was: standing at a
room's centre puts that corner fixture 3.13m to one side and 5.41m up, so a 1.8m body throws a shadow
whose head lands about a metre away, in a room that looks evenly lit by four panels overhead.

### The player's shadow was removed, after four attempts

Recorded because the objection outlived every shape tried, which is the useful information:

| Attempt | Result |
| --- | --- |
| The body, one CORNER fixture casting | Detached — a long shadow off to one side |
| The body, all four fixtures casting | "Like a skeleton" — four silhouettes, eight limbs |
| A plain capsule proxy | Fixed the limbs by deleting them; not a person's shadow |
| The body, nearest fixture only | Short, overhead, correctly shaped — still not good enough |

**So the caster count and the silhouette are both exhausted.** The two suspects nobody has tested are
worth writing down, because a fifth shape will not help:

1. **These are POINT lights standing in for 1.4m emissive panels.** A real area source of that size
   gives a wide penumbra; a point gives a hard edge that no resolution fixes. This is the likeliest
   cause of "the quality is bad", and it is a lighting-model problem rather than a shadow problem.
2. **`m_ShadowDepthBias` and `m_ShadowNormalBias` are both at URP's default 1**, which peter-pans the
   contact point away from the caster's base — the detachment complaint, from a second cause.
   Lowering it blind trades that for shadow acne on the floor, so it needs an eye, not a guess.

Resolution was ruled out on the way: the atlas was given to a single caster at 2048, which halves the
~2cm texel a 1024 map spreads over a 130-degree cone from 5.4m.

### What the rooms kept, and the three numbers that are one decision (2026-08-25)

Play reported two separate faults on the same day and they pull in opposite directions: shadows read
as **blurred blobs** (a 20cm key throwing a 10cm smudge), and shadows **only appeared once you got
close**, on objects already visible on screen.

The second one was not the shadow system at all — it was `m_ShadowDistance`, **15**, which is a radius
around the CAMERA. Raised to 30. The argument for 15 had been that halving it doubles the effective
texel density, and **that argument is false in this project**: cascade resolution is a directional
light property, every directional light is deleted at build, `m_MainLightShadowsSupported` is off and
the cascade count is 1. Every shadow here comes from a spot, whose map is sized by its cone and its
resolution tier and does not know that number exists. 15 was buying nothing and paying for it in
pop-in. What it still does is stop a corridor of lit rooms all rendering maps at once, so it is the
number to watch if the frame rate moves.

The first one is an **atlas budget**, and the atlas, `ShadowBudget.maxCasters` and
`m_AdditionalLightsShadowResolutionTierHigh` are **one decision**. A 130° cone from 5.41m spreads its
map over 23.2m of floor, so the tier is what sets the texel:

| atlas | casters x tier | texel | verdict |
|---|---|---|---|
| 2048 | 4 x 1024 | 22.7mm | blurred — a key is thinner than one texel |
| 2048 | 1 x 2048 | 11.3mm | sharp, but the shadow JUMPS as you cross the room |
| **4096** | **4 x 2048** | **11.3mm** | sharp and stable — chosen |

The middle row failed for a reason worth keeping: the single caster is whichever fixture is **nearest
the player**, so walking across a room swaps it and every shadow swings to a new angle. `range` (10m)
is sized precisely so the casting SET does not change while you are inside a room — and that
guarantee only holds if the whole room's ceiling is in the set.

**4096 is four times the shadow pixels this game drew before, and this number was 4096 once already
and came down after play reported lag.** It is the first thing to put back if the frame rate suffers.
The suspected cause then was full-screen passes rather than this, but that was never measured.

### What the rooms kept

Every room's four fixtures are eligible, and `ShadowBudget` (one per `Cycle`, on the cycle's own
GameObject so it survives the per-cycle scene split) keeps the **nearest one to the player** casting
and the rest dark. Same cost as the single caster the game already paid for, and shadows now fall
from roughly overhead in every room rather than from one corner of one room.

Two details that cost a build each to find:

- **Ownership is tested against `Cycle.worldRoot`, not by walking up to a `Cycle`.** The enrolment
  sweep runs before the probe bake, and at that point a cycle's rooms are still under `worldRoot` with
  that root not yet parented to the `Cycle`. `GetComponentInParent<Cycle>` finds nothing there.
- **The sweep switches every fixture off before `BakeReflectionProbes`.** A probe cubemap renders 360
  degrees and sees most of the corridor, so with all 64 eligible it asked for 24–42 shadow maps a face
  and URP logged its atlas-reduction warning 76 times in one build.

## Three passes at making it look less like a whitebox (2026-08-25)

Asked for after play: the game works, but the graphics wanted lifting. What was actually wrong was
not resolution or textures — it was that **every surface in the building was mathematically perfect**.
Three passes, in the order they are worth doing.

### 1. Chamfered panel rims

**The argument was already in this project and had only ever been applied to three objects.**
`BevelledPrismMesh`, written for the escape objects, states it: a Unity cube's faces meet at
perfectly sharp edges, so each face is one flat shade under any lighting, "and no material fixes it,
because there is no geometry near the edge for a highlight to run along."

The building is made of some three thousand cubes. `ChamferedPanelMesh` gives every wall panel a 6mm
chamfer on its front rim — four edges, not twelve; the back is buried against the backing slab and
the sides are the groove.

It is worth more on the architecture than on the props, because of what the panels already are: each
stands `GrooveDepth` (25mm) proud of its backing, so the building is a grid of raised rectangles each
with a shadow line around it. The chamfer puts a **lit** line inside every one of those shadow lines.

- **True size, not a scaled unit cube.** A panel is 1.72 × 1.32 × 0.025m. A chamfer written as a
  fraction and scaled with the box comes out 100mm across the face and 1.5mm through the depth —
  a wedge, not a chamfer. The metres have to survive into the vertices, which is why this is a mesh
  per size rather than one mesh scaled.
- **20 distinct sizes for the whole game**, cached as assets, so batching is unaffected. The grid's
  identical cells share one; the partial panels around doorways mint the rest. The build logs the
  count — hundreds would mean the sizes had stopped repeating.
- UVs are an XY projection across the whole panel rather than per-face 0..1, which reproduces what
  the cube gave the material to tile against and runs the grain continuously over the rim.

### 2. Roughness variation — BUILT, THEN TURNED OFF THE SAME DAY

`ApplySurfaceDetail` gave the walls a normal map but **one smoothness number**, so every square metre
of the building reflected exactly as sharply as every other. Perfectly uniform roughness is one of
the strongest tells that something is rendered.

`MakeSmoothnessMap` generates a wear map from the same tileable noise the normal map uses, and
`ApplyWear` puts it on every material that gets a normal map — the two are halves of one idea, so
they are applied in one place and a new surface cannot get one without the other.

- **Low frequency on purpose.** The normal map wants near-pixel grain because it stands in for
  plaster. This wants patches the size of a hand, because it stands in for wear. Fine noise in
  roughness reads as sparkle, which is the opposite of the intent.
- **It only ever dulls.** URP multiplies (`specGloss.a *= _Smoothness` in `LitInput.hlsl`), so the
  alpha is a fraction of the authored smoothness. The walls' 0.85 becomes a range of about 0.61–0.83.
  Nothing can come out glossier than the number somebody chose.
- **`_METALLICSPECGLOSSMAP` is the whole of it.** URP's `SampleMetallicSpecGloss` is wrapped in
  `#ifdef` — assign the texture without enabling the keyword and it costs memory and is never read.
  Third time this project has been bitten by a URP keyword (transparency needs
  `_SURFACE_TYPE_TRANSPARENT`; `_BumpScale` only works because `_NORMALMAP` is compiled in).
- The map carries **metallic in red**, read off the material, so applying it cannot silently un-metal
  something. One texture per distinct metallic value.

**And it is OFF (`WearFloor` 1.0), because it replaced one rendering tell with a worse one.** Play
reported pale grey squares repeating across the walls and floor. The cause is not the noise and not
the compression - both were investigated and neither was it - but the **tiling**: URP/Lit gives every
secondary map `_BaseMap`'s single UV transform, so the wear map is locked to the (5,3)-per-panel
repeat the normal map's grain needs. Fifteen copies of one tile per panel, identical on all 88, and
value noise's lattice makes that repeat legible as a grid. Worst at grazing angles. Full account, and
the two wrong diagnoses that came first, in `docs/gotchas.md`.

**If it is wanted back, it cannot be a tiled texture.** One smoothness value PER PANEL through the
property block `WallPanelDisplay` already owns is the shape that fits: no tiling to repeat, and
variation at the scale the building is actually built at.

### 3. Bounce light — WORKING, 2026-08-25

**All four scenes bake, in about four minutes total** (IterationRoom 5.3s, Cycle1 12.6s, Cycle2
214.4s, Cycle3 18.0s), with no exceptions in the log. Before this the bake had never once completed.

No object set `ContributeGI`, so a white room — where most of what the eye sees is light off the
walls — was lit by four downlights and a flat Trilight ambient constant standing in for all of it.
That is most of why it read as a whitebox.

**Adaptive Probe Volumes, not lightmaps, and there is no choice about it.** A lightmap needs a second
UV set per mesh; every surface here is generated from script, so lightmapping would mean unwrapping
several thousand objects first. APV stores irradiance in a grid in space and needs no UVs. It also
lights what a lightmap cannot: ghosts, carryables and the player's body sample the same volumes.

`BakeLighting` is a **separate menu item, not part of `Build`** — see that file for the argument.
Scenes are disposable output regenerated in seconds (CLAUDE.md §1.1) and a GI bake does not fit in
that loop.

Four things had to be true before a bake produced anything, and each failed silently first:

| Symptom | Cause |
|---|---|
| "It is not possible to generate lighting" | APV bakes **one scene at a time**; all four were loaded |
| `0 lights` in the bake snapshot | Fixtures were **Realtime**, which contributes nothing to a bake. Now **Mixed** — direct light and its shadow stay live for `ShadowBudget` to switch, only the bounce is baked |
| `0 instances` for Cycle1 and Cycle2 | `SleepCycle` leaves a cycle's world root **inactive**, and an inactive renderer is invisible to the baker. Woken for the bake and put back before the save — world roots only, never props that are deliberately off |
| Editor **crash**, 32k exceptions, 129MB log | No **baking set**. `AdaptiveProbeVolumes` dereferences it unconditionally while writing results. The Lighting window creates one as part of drawing its UI; nothing draws that UI in batchmode |

#### The fifth fault, and the three days spent blaming Unity for it

**After all four, it still crashed — and the diagnosis was wrong.** `IterationRoom` (107 instances)
and `Cycle3` (1,137) baked cleanly in seconds; `Cycle1` threw a `NullReferenceException` out of
`AdaptiveProbeVolumes.GenerateScenesCellLists` tens of thousands of times and took the Editor down.
A baking set existed, the scene was awake, one scene was loaded, the lights were Mixed, and the
snapshot extracted correctly (868 instances, 24 lights). It was recorded here as a Unity bug in
6000.5.7f1, with a suggested workaround of splitting Cycle1's volume per room.

**It was not a Unity bug. It was `chess.glb`.** That file ships a mesh named `Material3` with no
triangle sub-mesh at all. The `ContributeGI` pass added the same day handed it, like every other
non-moving renderer, to APV's **Virtual Offset** stage — which builds a ray tracing acceleration
structure. `HardwareRayTracingAccelStruct.AddInstance` refuses a mesh with no triangle topology and
then registers a zero handle for it anyway, so the second such mesh throws `ArgumentException: An
item with the same key has already been added. Key: 0`. That aborts `DefaultVirtualOffset.Initialize`
half-built, `Step()` NREs on the wreckage, and the `GenerateScenesCellLists` NRE everyone was looking
at is what the damage looks like six stages downstream.

**Chess pieces are Room2West, which is Cycle1 — and no other scene has them.** A scene-shaped symptom
with an asset-shaped cause, which is exactly why "what is different about Cycle1's geometry" was the
question that solved it and "what is different about Cycle1's volume" was not.

Two lessons worth more than the fix:

- **When a bake hangs, read the FIRST error after `'<Scene>': baking.`, never the tail.** Unity's
  `FinalizeBake` wraps `ApplyPostBakeOperations` in a `catch` that logs and swallows, then calls
  `CleanBakeData()`, which throws `ObjectDisposedException` *out* of the bake delegate — so `done` is
  never set and Unity calls the delegate again every tick, forever. The log fills with the same two
  exceptions at hundreds a second (29,757 of them, 129MB) and the one line that names the cause is
  30,000 lines above the noise. Three separate diagnoses were made off the tail and all three were
  wrong.
- **"A released engine would have this fixed already" was the right instinct and it was the user's.**
  The stale-baking-set theory was tested first and disproved — a bake from a freshly created set
  crashed identically — and only then was the log read from the top.

Things tried that did **not** help, and are not worth trying again: waking the world root before
rather than after volume placement; creating the baking set explicitly rather than letting Unity find
one; single-scene mode; deleting the baking set and its cell data.

#### Two things that had to change the moment APV came on

- **The ambient was halved** — 0.155 / 0.644 / 0.719 → 0.078 / 0.322 / 0.360. Every one of those
  numbers was found while ambient stood in for a bounce that did not exist; with a real bounce
  underneath they were the same light counted twice, which makes the building brighter and *flatter*.
  This is the prescribed starting point, not a settled value: compare a corner against a wall centre,
  and if the corner is not visibly darker, ambient is still winning and they go down again.
- **The panel meshes were wound inside out**, which is what actually blacked out every wall in the
  building the day APV came on — a `ChamferedPanelMesh` bug, not a lighting one, though it wore a
  lighting one's clothes for most of a day and killed three good theories on the way. Full account,
  and the bisection method that found it, in `docs/gotchas.md`.
- **APV's sampling noise was turned off**, because it is a TAA feature and this project runs SMAA.
  Left at Unity's defaults it re-rolls a dither on the probe sample position every frame with nothing
  to resolve it, and every wall panel in the building visibly pulses. Full account in
  `docs/gotchas.md`. `BuildPostProcessing` owns the override.

#### `m_LightProbeSystem` follows the data now

It used to be pinned to `LegacyLightProbes` on every build as a safety catch — APV with no baked data
is *worse* than no APV, because every renderer is marked `ReceiveGI.LightProbes` and would sample a
grid that does not exist — with a note that the two halves would have to move together if a bake were
ever made to stick.

**They now do.** `ConfigureUrpAsset` asks `AnyBakedProbeVolumes()` and sets the probe system to match:
baked data anywhere in the project leaves APV on, none pins it back to legacy. The catch is intact,
only asked rather than assumed. Leaving it pinned would have thrown away every cell of a good bake on
the next rebuild — a failure quieter and worse than the crash, because nothing would announce it.

`HasBeenBaked()` is `internal`, so this goes through checked reflection, the same wall and the same
answer as `BakeLighting.EnsureBakingSet`: a Unity version that renames it makes the check return
false, which lands on legacy probes — the wrong answer in the safe direction.

**And the ambient will need rebalancing when it does.** `SetupLighting` carries almost all of the
room's light on flat ambient precisely *because* there was no bounce. Once the walls bounce, that
constant is a second helping — left alone, a successful bake makes the building brighter and
**flatter** rather than richer.

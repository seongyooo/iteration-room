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
  - **`_BumpScale` is much larger than the sub-1 that looks sane.** Central differences across a smooth field give tiny gradients, so the map is genuinely shallow: at 0.7 the surface renders *perfectly flat* even with the camera against it; at 10 it is stucco. Floor **1.8**, walls **0.2**.
  - **Walls are a smooth glazed panel, not plaster**: `_Smoothness` **0.85** with relief kept to a whisper purely so the specular isn't a uniform sheet, which is what makes a flat surface look CG. The floor stays matte at **0.18**.
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

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

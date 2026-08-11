# Gotchas

Things that cost a session to discover once. Do not rediscover them.

---

## Gotchas (don't rediscover these)

- **A Unity cube's opposite faces carry opposite U directions**, so the same texture on the east wall
  is the mirror of the one on the west - and north against south likewise. Wall panels are cubes, and
  this went unnoticed for the whole project because nothing had ever put a texture on one: flat
  colour has no handedness. It surfaced the moment the ending gave every panel a test card, and it
  surfaced **in a screenshot** - no amount of reading the code was going to show it. Fix is a
  per-panel `_BaseMap_ST` U scale of -1 chosen from which wall the panel sits on.
- **A world-space Canvas is legible when its forward (+Z) matches the direction the viewer is LOOKING** — not when it points at the viewer. Unity's default scene is the proof: camera at z=-10 looking toward +Z, canvas unrotated, text the right way round. So a wall message must face **away from the room, into its wall**. Getting it backwards renders the text mirrored, which is what happened to all four of Room3's messages. "Face the normal inwards" is the intuition to distrust — it is what you would do for a physical sign.
- **A `RectTransform`'s serialized `m_LocalPosition` is stale, and reading it will convince you a correct build is broken.** Its x and y come from `m_AnchoredPosition`; Room3's wall messages all serialize as `{0,0,0}` while sitting exactly where they should. **Inspect `m_AnchoredPosition`.** Related: `AddComponent<Canvas>()` (or any UI component) *replaces* a plain `Transform` with a `RectTransform`, so a position written before that call is discarded.
- **There is no scripting API that creates a layer.** `EnsureLayer` edits `ProjectSettings/TagManager.asset` through a `SerializedObject`, so the build has a side effect **outside the scene** — expect that file in a diff after a fresh clone's first build. It is idempotent by name. Indices **0-7 are Unity's own**; three look blank and are not, and writing into one is silently dropped, so the search starts at 8.
- **`-nographics` cannot render anything**, which is why the menu capture checks `SystemInfo.graphicsDeviceType`. Anything else needing a real render must make the same check or the canonical headless build stops working.
- A fresh Unity project via `-createProject` does **not** include uGUI — `"com.unity.ugui": "2.0.0"` had to go into `Packages/manifest.json` before any `Text`/`Canvas` script would compile.

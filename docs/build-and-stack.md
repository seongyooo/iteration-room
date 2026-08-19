# Build and stack

Engine version, pipeline wiring, and the two ways to rebuild the scenes. The short version of the
build loop is in CLAUDE.md section 5; this is the rest of it.

---

## Stack and build

- **Unity 6000.5.7f1, URP** (`com.unity.render-pipelines.universal` 17.5.0). Pipeline assets in `Assets/Settings/` (`IterationURP` + `IterationRenderer` + `IterationVolume`), wired into both `GraphicsSettings.defaultRenderPipeline` and `QualitySettings.renderPipeline`. It started on Built-in and moved because Built-in with no post-processing reads flat no matter what the materials do.
  - Materials must use `Universal Render Pipeline/Lit`, **not** `Standard` — URP has no Standard shader and a material left on it renders magenta. `SceneBuilder.OpaqueShader()` resolves whichever exists; `SetSmoothness()` handles `_Glossiness` vs `_Smoothness`.
- **Unity MCP** ([CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp)), needs `uv`/`uvx`. **MCP servers only load at session start** — if its tools are missing, start a new session rather than re-registering.
- **There is no manual scene editing.** Both scenes are assembled by `Assets/Editor/SceneBuilder.cs`. `MainMenu` is build index 0 (where a standalone player opens); `IterationRoom` is index 1. Pressing Play in the Editor runs whichever is open.
- Rebuild headlessly:
  ```
  "C:\Program Files\Unity\Hub\Editor\6000.5.7f1\Editor\Unity.exe" -batchmode -projectPath "C:\Users\seonl\Desktop\c\2026\summer\Iteration" -executeMethod IterationRoom.EditorTools.SceneBuilder.Build -quit -logFile <log>
  ```
  Check the log for `error CS` and exceptions; `[SceneBuilder] IterationRoom scene built at ...` plus exit code 0 means it worked. Ignore `[Licensing::Client] Error: HandshakeResponse...` retries.
  - **`-nographics` SKIPS THE BAKES**, and this is the flag to think about rather than paste. `BakeReflectionProbes` and the menu-background capture both check `SystemInfo.graphicsDeviceType` and bail with no device — correctly, since neither can render — so the build succeeds while leaving every cubemap as it was. A room that moved then keeps a probe of where it used to be, and since a metal is *entirely* reflection (and so is water) that is a real visual regression reported as a clean build. The log says which happened: `Reflection probes baked and wired: n/n` or `probes NOT baked (-nographics)`.
  - Measured 2026-08-19: plain `-batchmode` on this machine gets a real D3D device with the Editor closed, bakes all 14 probes and re-captures the menu background. Use `-nographics` only when the bakes genuinely do not matter and the seconds do.
- To play it, launch the Editor binary directly (`Unity.exe -projectPath ...`) — Unity Hub's project picker is flaky on this machine ("프로젝트를 찾을 수 없습니다" on a valid project).
- **A batchmode build fails outright if the Editor has the project open.** Drive the build through MCP instead: `execute_menu_item("Iteration Room/Build Whitebox Scene")`, then `read_console`. **Clear the console first** — stale entries look exactly like fresh failures. If the Editor is in Play mode, `manage_editor(action:"stop")` first; `NewScene` throws during play.
- **Player Settings → Run In Background must stay ON.** Without it play mode stops ticking when the Editor loses focus, so any MCP-driven test captures a stale frame and looks like the change did nothing. `Build()` sets it, since setting it during play mode does not persist.
- Prefer MCP for incremental visual tweaks; keep `SceneBuilder.cs` authoritative for anything structural.

## Shipping a WebGL build

1. Build the scenes first — `PlayerBuilder` reads `EditorBuildSettings.scenes`, so a stale scene ships as-is.
2. **`Iteration Room > Build WebGL Player`** (`Assets/Editor/PlayerBuilder.cs`). ~13 min from a cold target switch, ~49 MB out. The MCP call **will time out** long before it finishes and that is not a failure — watch `Logs/Editor.log` for `[PlayerBuilder] WebGL build SUCCEEDED`.
   - Switching the active build target reimports every asset and runs URP's material validator, which rewrites four `.mat` files every time. See `rendering-notes.md`.
3. **`powershell -NoProfile -File Tools\make_webgl_zip.ps1`** → `Build/iteration-webgl.zip`. Do **not** substitute `Compress-Archive`; see `gotchas.md` for the backslash that broke the first upload.
4. Upload by hand. **There is no butler on this machine** and no stored credentials, so this step cannot be automated from here.
   - itch.io project settings that matter: Classification **Games**, Kind of project **HTML**, tick **"This file will be played in the browser"**, viewport **1280x720** (the UI scales against a 1920x1080 reference), **fullscreen button on**, **mobile off**, and leave *automatically start on page load* **off** so the pointer lock happens inside a user gesture.


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
  "C:\Program Files\Unity\Hub\Editor\6000.5.7f1\Editor\Unity.exe" -batchmode -nographics -projectPath "C:\Users\seonl\Desktop\c\2026\summer\Iteration" -executeMethod IterationRoom.EditorTools.SceneBuilder.Build -quit -logFile <log>
  ```
  Check the log for `error CS` and exceptions; `[SceneBuilder] IterationRoom scene built at ...` plus exit code 0 means it worked. Ignore `[Licensing::Client] Error: HandshakeResponse...` retries.
- To play it, launch the Editor binary directly (`Unity.exe -projectPath ...`) — Unity Hub's project picker is flaky on this machine ("프로젝트를 찾을 수 없습니다" on a valid project).
- **A batchmode build fails outright if the Editor has the project open.** Drive the build through MCP instead: `execute_menu_item("Iteration Room/Build Whitebox Scene")`, then `read_console`. **Clear the console first** — stale entries look exactly like fresh failures. If the Editor is in Play mode, `manage_editor(action:"stop")` first; `NewScene` throws during play.
- **Player Settings → Run In Background must stay ON.** Without it play mode stops ticking when the Editor loses focus, so any MCP-driven test captures a stale frame and looks like the change did nothing. `Build()` sets it, since setting it during play mode does not persist.
- Prefer MCP for incremental visual tweaks; keep `SceneBuilder.cs` authoritative for anything structural.

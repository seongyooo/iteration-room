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

## Shipping a desktop build (Windows / macOS / Linux)

Three menu items in the same file, one per platform: **`Iteration Room > Build Windows Player`**,
**`Build macOS Player`**, **`Build Linux Player`** → `Build/Windows`, `Build/Mac`, `Build/Linux`.
Deliberately three items and not one that does all three: **switching the active build target
reimports the whole project** (89 MB of glb), so three platforms cost three reimports however they
are triggered, and separate items at least let a failed one be redone alone.

1. **Build the scenes first.** Same rule as WebGL — `PlayerBuilder` reads `EditorBuildSettings.scenes`,
   so a stale scene ships as-is.
2. Build each platform. The switch is also the "is the module installed" test: `SwitchActiveBuildTarget`
   returns false for a target this install cannot build, and the builder stops there rather than
   letting `BuildPlayer` quietly produce a **Windows** player in `Build/Mac`.
3. Upload. **Not by hand for macOS or Linux** — see the packaging trap below.

### Cross-compiling, and the one thing that needs a Mac

A Windows editor builds all three. Only the Hub module is missing: **Installs > 6000.5.7f1 (gear) >
Add modules > Mac Build Support (Mono) / Linux Build Support (Mono)**. Installed here today:
WebGL and Windows only.

**macOS with IL2CPP cannot be cross-built** — it runs Apple's toolchain. Mono is the standalone
default and is what makes the cross-build work at all; the cost is that the `.app` is **x86_64 only**
and Apple Silicon machines run it under Rosetta 2, which is fine for a playtest and is not what you
would ship. `BuildStandalone` refuses rather than failing halfway if the backend has been changed
to IL2CPP.

### THE PACKAGING TRAP: a zip made on Windows drops the executable bit

This is the thing that goes wrong, and it goes wrong on the tester's machine rather than in the log.
Windows zips carry no POSIX permissions, so:

- **Linux**: the binary arrives non-executable. `chmod +x Iteration.x86_64` or it does nothing.
- **macOS**: the binary *inside* the `.app` arrives non-executable, and the app then does not open
  at all — it reads as a corrupt download rather than as a permissions problem.

**Use butler** (itch.io's CLI), which preserves permissions and tags the platform from the channel
name — `butler push Build/Windows user/game:win`, `:osx`, `:linux`. There is no butler on this
machine today and no stored credentials; the WebGL section above says the same about that upload.

**And an unsigned Mac app is blocked by Gatekeeper** whatever the permissions are: quarantine makes
it report as damaged. A tester either right-clicks > Open, or runs
`xattr -dr com.apple.quarantine Iteration.app`. Signing and notarising it properly needs an Apple
Developer account and a Mac, which is not worth it to hand a build to a few people.

### Icon and splash

**The app icon is a build product, like the scenes and the menu background.**
`SceneBuilder.CaptureAppIcon` renders it from the player camera at the end of every scene build —
the same eye as the title screen, a 26-degree lens instead of 41.3, square at 1024 — and writes
`Assets/Textures/AppIcon.png`. `PlayerBuilder.ApplyBrand` loads that file and hands it to
`PlayerSettings.SetIcons` for every size Unity asks for. A fresh clone has no icon until the scenes
are built, and the builder warns rather than shipping Unity's default silently.

The framing is **derived, not composed**: it is the menu camera with one number changed, chosen so
the doorway — the only dark shape in a white room — is big enough to survive being drawn 32 pixels
wide in a taskbar, and so no ceiling enters the frame (the top of a 26-degree frame lands at 4.27m
against a 5.41m wall; it re-enters above about 38 degrees). **Look at the PNG at icon size after a
build.** One number, `iconFov`, is the whole of the framing.

**The splash screen is turned off in `ApplyBrand`** — `PlayerSettings.SplashScreen.show` and
`.showUnityLogo`, both false. Unity 6 allows this on a Personal licence where earlier versions did
not. **A licence that is not entitled to hide the logo forces both back on during the build and says
nothing about it**, so the builder reads them back and logs them — but that log only proves the
setting was written, not that it survived.

**VERIFIED BY RUNNING THE BUILT PLAYER, 2026-09-02**: the Windows build opens straight on the
project's own main menu, no Unity logo. So this licence is entitled to hide it and the setting
sticks. Watched by a human, which is the only check that can answer this one. Nothing else about the
splash is set, because there is nothing left to set once it is off.

### Before a build leaves this machine

- **Development Build OFF.** `CaptureRig` arms on `Application.isEditor || Debug.isDebugBuild`, so a
  development build hands a tester K, L and J — and K wipes the HUD. `BuildStandalone` forces the
  build non-development and clears the Build Profiles checkboxes so the two cannot disagree, but the
  log is the proof: a player log containing `[CaptureRig] armed.` is a build that should not go out.
- **The cycle picker is still ungated** (`TODO.md`, Next steps §6) — CYCLE SELECT lists every cycle
  whether or not it has been reached. Useful when asking someone to look at one cycle; a spoiler when
  asking for a blind run. Decide which before building.
- **The name is an address, and it is now fixed.** `ApplyBrand` writes `companyName` = **`seonline`**
  and the standalone bundle identifier **`com.seonline.iteration`** (2026-09-02, by request); it was
  `DefaultCompany` and an empty identifier, which would have put `com.DefaultCompany.Iteration` on a
  Mac app. These two decide where a built player keeps its data and `PlayerPrefs` — the registry key
  `HKCU\Software\<company>\<product>` on Windows — so **changing either again abandons every setting
  and key binding a player has saved.** It was set before anything shipped to anybody for exactly
  that reason. `productName` is still `Iteration`, which is what a player's title bar says; renaming
  it to *Iteration Room* is open and costs the same reset, and the title itself is unresolved with
  the film's authors (`docs/lupini-permission-email.md`).

## Shipping a WebGL build

1. Build the scenes first — `PlayerBuilder` reads `EditorBuildSettings.scenes`, so a stale scene ships as-is.
2. **`Iteration Room > Build WebGL Player`** (`Assets/Editor/PlayerBuilder.cs`). ~13 min from a cold target switch, ~49 MB out. The MCP call **will time out** long before it finishes and that is not a failure — watch `Logs/Editor.log` for `[PlayerBuilder] WebGL build SUCCEEDED`.
   - Switching the active build target reimports every asset and runs URP's material validator, which rewrites four `.mat` files every time. See `rendering-notes.md`.
3. **`powershell -NoProfile -File Tools\make_webgl_zip.ps1`** → `Build/iteration-webgl.zip`. Do **not** substitute `Compress-Archive`; see `gotchas.md` for the backslash that broke the first upload.
4. Upload by hand. **There is no butler on this machine** and no stored credentials, so this step cannot be automated from here.
   - itch.io project settings that matter: Classification **Games**, Kind of project **HTML**, tick **"This file will be played in the browser"**, viewport **1280x720** (the UI scales against a 1920x1080 reference), **fullscreen button on**, **mobile off**, and leave *automatically start on page load* **off** so the pointer lock happens inside a user gesture.


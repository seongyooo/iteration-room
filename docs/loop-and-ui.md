# The loop, and everything on screen

The iteration coroutine, the wake-up, ending a cycle early, the ending, mouse sensitivity and its calibration room, pausing, the title screen and the HUD.

---

## The loop (`Loop/LoopManager.cs`)

The whole loop is **one coroutine (`RunLoop`), not `Update`** — an iteration opens and closes with the wake-up sequence, and the clock and the ghosts stay frozen while that plays. **Recording starts only once the player has control**, so every timeline covers the same window; start it before the wake-up and you get a pile of frames all stamped t=0.

- **The `while (true)` has exactly one exit, and where it sits is load-bearing.** The escape check is taken immediately after `IterationRunning = false` and *before* the collapse held at full, the pull-in, the blink, the panels going out and the recording becoming another ghost. All of that is the cycle closing and re-opening.
- The calibration step is yielded on **outside** the `while`, before `IterationNumber` is first incremented.
- **`AcceptsInput` (`IterationRunning && !IsPaused && !RunOver`) is the gate every `Update` that reads a key must use.** `BalloonTool`, `Drawer`, `CarryableItem`, `KeyLock`, `EndCycleControl` and `ControlHintDisplay` all use it. **A new interactable that reads input must too.**

### `Loop/WakeUpSequence.cs`

Eyelids (two black UI panels driven through their *anchors*, so they cover any resolution) fall shut as time runs out, then open on the ceiling before the player sits up. It borrows the camera by clearing `ControlEnabled` and posing the eye via `SetEyePose(height, pitch, roll)`; negative pitch looks up.

- **The lids blink rather than sweep** (`blinkShutKeys` / `blinkOpenKeys`, arrays of `x = lid position, y = seconds`). Eyes fall, snap part of the way back, fall further, and only then give out. That flutter is what makes the loop taking you read as involuntary rather than as a fade-to-black.
- **The rise decouples body from head.** The body eases out quick off the pillow and settles; the head lags a quarter of the way in, levels only near the top, and carries a slight `riseRoll` that leaves and returns to level. Driving height and pitch from one shared `SmoothStep` is exactly what made it read as a camera on rails.

### `EndCycleControl` — 60 seconds is a ceiling, not a quota

Started as a test aid and was promoted to a real mechanic: once you have done what you came to do the remainder is dead time, and spending it was the one thing the loop never let the player decide. Available from iteration 1. (A deliberate departure from spec §4.3–4.4.)

- **Hold to commit** (`holdDuration` 0.6s with a fill gauge), not press — it is irreversible and it truncates the recording the next ghost is made from. Dims to `unavailableAlpha` when `IterationRunning` is false so it reads as unavailable rather than broken.
- Answers to **`N`** as well as a click, and the key is what gets used: the cursor is locked, so clicking means freeing it and watching the view spin. The scene also needs an **`EventSystem`** — uGUI is inert without one.
- `EndCycleEarly` sets a flag rather than shoving `ElapsedTime` to `loopDuration`, because `ElapsedTime` is the timestamp written into every recorded frame and forcing it would corrupt the tail of the timeline. The flag is armed just before the timed window so an input during the wake-up can't end the next cycle instantly, and re-arms only on release so holding the key through the blackout doesn't chain.
- **Its label was silently wrapping onto two lines**, which is probably why testers asked for a skip button it has had since the first build: `HOLD [N] — END CYCLE` is 20 monospace cells (~192px at `fontSize` 16) inside a rect that was **168** wide. Now 224 with `Overflow` set. **Check any fixed-width HUD label against `0.6 × fontSize × length`** — this is the second time a label has quietly rewrapped.

### The ending (`Loop/EscapeTrigger.cs`, `Loop/EndingSequence.cs`)

Walking out of Room3's north door ends the run — the **only** exit condition in the game.

- **`EscapeTrigger` sits ON the threshold, not past it, and that is forced.** The `FarCap` is directly behind the doorway and the pocket in front of it is 0.1m against a controller of radius 0.3 — **there is physically no "through" to stand in**. The volume is centred on Room3's north wall face (Z 26.95, `halfDepth` 0.5), which a player reaches to Z≈26.875 before the cap stops them. The ending takes control on the frame it fires, so they never reach the wall.
  - Two gates, both needed: **`door.IsOpen`**, so walking up to a shut door ends nothing (it reads as *you got out*, not *you arrived*); and **`halfWidth` = `DoorWidth/2 + 0.05`**, because without an X bound anyone anywhere along the north wall is at the same Z as the opening and the run would end from across the room the moment the fourth pad went down.
  - Y is deliberately **not** tested — the room has a floor and a ceiling, and a jump must not be a way to miss the end of the game.
- **The ending is defined by what it does NOT do**, because the reset is the only vocabulary the game has built and the player has seen it dozens of times:
  - **The collapse lets go instead of peaking** — `RunEnding` ramps flare and shake down from `WallPanelDisplay.Flare`, so escaping inside `collapseLeadTime` means watching the thing that takes you every sixty seconds try, and stop.
  - **The panels do not power down.** That is the loop's signature; using it would say the cycle continued.
  - **The lids do not blink.** The blink is involuntary and belongs to the thing that just failed. `EndingSequence` fades its own opaque scrim instead — reusing the eyelids would have been free and wrong.
  - **The ghosts are left standing**, visible under the scrim, holding their pads: the last image of the run is the people it took to get out.
  - **The room tone fades and stops** (`RoomAmbience.FadeOutTone`), the only thing that ever stops it. It fades rather than cuts — a cut reads as a sound failing.
- Timing: scrim 2.4s → 0.9s black → card 1.4s → hold 6.5s → `MainMenu`, **automatically, not on a keypress**. The card is `C Y C L E   B R O K E N` over `ESCAPED ON ITERATION N` — the iteration number is the one score the game keeps, and it is diegetic.
- **Everything in `EndingSequence` runs on `Time.unscaledDeltaTime`.** Pausing is locked out (`RunOver`, read by `PauseMenu`), but a coroutine that can be frozen with no loop left to unfreeze it is a soft lock at the one moment the game must not have one.
- `RunEnding` **discards the final recording** — there is no next iteration for it to haunt.

## Mouse sensitivity and calibration

The first itch.io play-test came back with "far too sensitive", from a build that felt fine in the Editor. **The same constant genuinely means different things on the web and on the desktop, and on two testers' machines.**

- **Unity's WebGL build hands the engine the browser's pointer-lock delta completely unscaled.** Verified by decompressing the shipped `WebGL.framework.js.unityweb`: `fillMouseEventData` does literally `HEAP32[idx+8] = e["movementX"]` — no `devicePixelRatio`, no canvas correction. That value has already been through the OS pointer-speed slider and its acceleration curve, and Chrome, Firefox and Safari do not agree on it.
- **It is not frame rate**, the usual first guess. `Input.GetAxis("Mouse X")` is already the delta accumulated *since the last frame*, so total rotation over a sweep is the same at any rate. That is why `HandleLook` correctly uses **no `deltaTime`** — don't "fix" it.
- So the fix is not a better number. `GameSettings.MouseSensitivity`, default **1.1** (was a serialized 2.0), backed by `PlayerPrefs`.
- **`GameSettings` applies live but writes on the way out.** A `Slider` raises `onValueChanged` every frame of a drag and each `PlayerPrefs.Save` is a storage flush on WebGL. Nothing relies on `OnApplicationQuit` — a browser tab is *closed*, not quit.
- Still available if complaints continue: request pointer lock with `{ unadjustedMovement: true }` via a `.jslib`, bypassing the OS acceleration curve. Chrome/Edge support it; Firefox/Safari fall back silently. Not done — the slider makes it optional.

### The calibration step (`Menu/SensitivityCalibration.cs`)

Before iteration 1 the run stops and asks the player to look around and set the value, because the pause slider can only be found by someone who has *already* lost an iteration to a default that did not suit them.

- **It runs in a room of its own, and that is the third attempt.** Both failures looked fine until played:
  1. On the title screen, panning the menu's background still within ~16° of headroom. Not a test — sensitivity *is* the relationship between hand travel and how far the world goes round, and that cannot be judged inside a window narrower than one flick.
  2. Still on the title screen, turning a camera inside **Room1's baked reflection cubemap** hung as a skybox. It turns a full circle and shows *nothing*: sampled, the cubemap's floor face runs **0.940–0.944** and its ceiling **0.690–0.734**. The room is a white box, so its reflection probe is a blank field. **Do not reach for a reflection probe as a preview of anything.**
- **`CalibrationRoom` is sealed** — `Rect.zero` for both cutouts, no doorways at all, sitting behind Room1 with a room's worth of nothing between. **Bare on purpose**: what is judged is how fast the room goes round, and furniture is something to look *at* rather than something that shows motion. The panel grid does it better — a regular grid whose grooves sweeping past give an unambiguous read on speed. The only two things in it are the display on the south wall and the start panel on the north.
- **`CalibrationSpawn` holds the SAME offset and facing as `BedSpawnPoint`** — (0, centre − 0.7) at yaw 180, so the wall ahead is 4.55m and the sides 4.38m in both rooms. Turning in place has the same angular rate whatever is in front of you, so the *number* is identical either way — but what the eye counts is detail crossing the view, and a far wall puts many more panel edges in a degree. An earlier version stood the player 3m from the south wall looking down the length: **8.25m of wall ahead against 4.55m**, same sensitivity, noticeably quicker. **If either spawn moves, move the other.** The residual difference is the furniture, and it is accepted.
- Its ~88 panels are **excluded from `WallPanelDisplay`** (`InCalibrationRoom`) — seen once and never again, so booting and flaring them would write a per-frame property block to renderers nobody can look at. It gets its own reflection probe, or the 0.85-smoothness walls mirror the sky and come out blue.
- **The menu background capture poses the player at the bed first**, since they now start in an empty white box. Safe because the room scene is already saved by then.
- **The rest of the HUD is switched off** (`hideWhileActive`) — a countdown reading 1:00 describes a loop that has not started. `SceneBuilder` fills the list as **"every canvas child except this page and `PauseMenu`"** rather than by name, so a HUD element added later is covered without anyone remembering. PauseMenu stays live so a settings screen cannot trap someone.
- **Control is left fully on, walking included.** Iteration 1 teleports everyone back to the bed regardless, and every other control is already inert via `AcceptsInput`.
- **The value is changed with the wheel** (`SCROLL TO ADJUST` on the wall). Arrow keys and A/D were tried and had to go — both are bound to the `Horizontal` axis, so every press that nudged the number also strafed the player. The wheel is the one input on a mouse not already spoken for while looking around.
- **The pointer stays captured, which is what makes the number honest** — a browser reports different deltas for a free pointer than a locked one, and the game runs locked. That is also why the gauge is a readout rather than a slider: with the pointer captured there is no cursor to drag one with. The draggable slider stays in the pause menu. `FirstPersonController` owns the lock and the click-to-retry.
- **Almost none of it is on the screen.** The control list, the gauge and the value are a **world-space display on the room's south wall** — the wall the player spawns facing — 6.40 × 2.64m centred at y 3.05, built by `SceneBuilder.BuildCalibrationWall`. The room is built out of displays, so the facility explaining itself on one is the same move Room3 makes. The screen carries **one line, and only when it has something to say**: `CLICK TO ENABLE MOUSE LOOK`, for when the browser has refused pointer capture — the one thing the wall cannot usefully tell a player whose view will not turn. It has **no plate behind it**, deliberately: the line is empty for all of a normal run, and a plate under an empty string is a black bar across the bottom of the screen for no reason.
- **The step ends by pressing E at a `B E G I N` panel on the OPPOSITE wall** (`Room/CalibrationStartButton.cs`). Not a key prompt: a fixture the player walks up to and operates is the room saying *begin*, where an overlay would be the game saying it — and **the player's first E press of the run lands on a real fixture**.
  - **The 10.4m between the display and the button is the design, not a layout accident.** Reading the panel and reaching the way out costs a full 180° turn and a walk, which is exactly what the step is asking the player to judge. Adjust at the display, turn, feel it, press — and if it was wrong, turn back.
  - **It is one cell of the wall grid**, not a box stuck on the wall: panel-sized (1.70 × 1.3019, i.e. `GridCell` minus the groove), sat 30mm proud, at column 2 of 5 and row 1 of 4 → **(0, 2.028)** on the north wall. Dark where its neighbours are white, with the word on it in spaced caps. The room's fiction is that the panels are displays, so one cell lit and asking to be pressed is the room speaking its own language — where the green slab it replaced was an object from a different game.
  - **It is an `IInteractHintTarget`, so the game's own grey E disc prompts over it** — the player meets the prompt before they meet the game. That needs two things wired: `ControlHintDisplay` gates on `AcceptsInput || calibration.Active` (the step runs before the loop, so `AcceptsInput` is false throughout), and `ControlHints` is excluded from `hideWhileActive` alongside `PauseMenu`.
  - It is appended to `interactTargets` **after the fact**, because the button lives in a room built later than the hint display — `BuildControlHints` returns the display for exactly that.
  - **It gates on `SensitivityCalibration.Active`, not `LoopManager.AcceptsInput`** — the deliberate exception to the rule every other interactable follows.
  - `Confirm()` is still gated on the pointer being captured, so nobody can commit a number they were never able to test. If the browser refused the lock the button does nothing and the screen line says why.
- **Keys are drawn in their real keyboard positions**, which is the whole value of caps over the text `[WASD]`: the shape is recognised before the letters are read. A cap is uGUI's own 9-sliced `UISprite` in red with a second one inset 3px in near-black — a border without needing a border sprite, and the slicing gives a square `W` and a wide `SPACE` the same corner radius. E is with the keyboard, not with the mouse: a player scanning for "which button" looks at the keys.
- **Rows are spaced from the edges of what sits on them, not by a uniform pitch**, because the rows are different heights: the W/A/S/D block is two caps deep and straddles its row where SPACE is a single bar. **A uniform pitch put the SPACE bar inside the A/S/D row twice** — first by 13px, then by 3.
- `icon_mouse` is a second generated glyph beside `icon_mouse_left`, with **no button filled**: highlighting one would say "click", the one thing the LOOK row does not mean. Its two seams are what stop it reading as a plain capsule.
- **When moving anything here, check glyph widths, not rect widths.** Every `Text` is `Overflow`, so a rect can be far wider than its text — and rect-based bounds checking reports overflows that are not real while missing the ones that are.
- `[N]` is deliberately absent: Room3 owns teaching that one, at the point it becomes worth knowing.

## Pausing (`Loop/PauseMenu.cs`)

**Escape** freezes the room and puts `RESUME` / `MAIN MENU` / `QUIT` over it, plus the sensitivity slider.

- **The freeze is one line — `Time.timeScale = 0` — and it works because every moving part already runs on scaled time**: the loop's clock, the wake-up's `WaitForSeconds`, the collapse ramp, the balloons, `CameraShaker`'s Perlin sampling. Verified by reading for `Time.unscaled*` before building it.
- **What a zero time scale does not stop is `Update`** — every raw key read still lands, and `HandleLook` uses no `deltaTime` at all, so the view would swing with the very mouse movement made to reach Resume. Hence the `AcceptsInput` gate above.
- `ControlEnabled` is forced false **every frame** while paused, not once on open — the loop hands control back at the end of every wake-up regardless. On resume it is restored to *what it was*, so pausing during a wake-up cannot hand back a camera the loop had taken away.
- `AudioListener.pause = true` as well: the announcer and room tone are not on scaled time and would talk over a frozen room.
- **`Time.timeScale` and `AudioListener.pause` are global**, so both are restored in `OnDestroy` as well as through the buttons — that covers the scene change, the path easiest to miss. `ToMainMenu` unfreezes *before* the load, since a scene arriving at `timeScale 0` cannot start itself.
- Built **last on the canvas** so it covers the HUD, the prompts and the eyelids. Its scrim keeps `raycastTarget` on deliberately — that is what stops a click reaching the end-cycle control underneath. The `CanvasGroup` clears **`blocksRaycasts` as well as alpha** when closed; an alpha-0 graphic still receives clicks.
- **The slider re-reads `GameSettings` on every open (`SyncSensitivity`), not once in `Awake`** — a bug this shipped with. `Awake` runs at scene load; the calibration step runs from `LoopManager.Start()` *afterwards*; so a slider seeded in `Awake` held the load-time value and snapped away from the real one when dragged. **Anything that can change a value behind a panel's back makes a single seed stale.**
- It is seeded with `SetValueWithoutNotify` **before** the listener is attached — a `Slider` raises the event on assignment, so seeding afterwards writes its own starting value back over the saved one.
- `MakeSlider` assembles the `Slider` by hand: Unity drives the fill's and handle's anchors itself every frame and finds their containers **through the hierarchy, not through fields**, so each must be a child of its own container rect with offsets left at zero.

## The title screen (`MainMenu.unity`)

A separate, near-empty scene: camera, canvas, a still of the room, `PLAY` and `QUIT`.

- **Separate scene rather than a state of the room, and that is what makes the loading bar real.** `IterationRoom` is a 306,000-triangle bed, 367 wall panels, seventy balloons, four baked probes and a pile of shaders. Held in one scene there would be nothing to load and the bar would be theatre.
- **The background is a still of the game's own first frame**, `Assets/Textures/MenuBackground.png`, rendered by `CaptureMenuBackground` **on every build** so the menu can never advertise a room that no longer exists.
  - Captured through `RenderPipeline.SubmitRenderRequest` with a `SingleCameraRequest`, **not** `Camera.Render()` — URP does not support a bare `Render()` from arbitrary code, and the request is also what runs the volume stack, so the still carries the same tonemapping, bloom and vignette.
  - `targetTexture` is restored in a `finally`; left assigned, the game renders into a `RenderTexture` instead of the screen and the saved scene carries it.
  - **Skipped under `-nographics`** — the previous PNG stands and the build logs a warning. A stale background beats a failed build.
  - The view is the south wall's panelling and nothing else, because the spawn faces away from the bed. That was chosen over a dedicated framing showing the furniture: it is the frame the game genuinely opens on, and a grid of blank panels says what this place is more plainly. **If revisited, add a menu-only camera — do not move the spawn to improve the menu.**
- **Buttons are white with the dark look coming entirely from the `ColorBlock`** — a `Button` tints by multiplying, so an already-near-black background has nothing left to brighten with on hover.
- `onClick` is wired in `Awake` from serialized references, not as persistent listeners (which would need `UnityEventTools` to serialize at all).
- The menu **puts the cursor back** (`Cursor.lockState = None`) in `Start` — Unity does not reset that across a scene load.
- The progress bar shows the **lesser** of real progress and elapsed fraction of `minimumLoadingTime`, so a fast machine does not flash the load past in two frames while the bar never claims more than has happened. Unity reports `0..0.9` while `allowSceneActivation` is withheld and never reaches 1, so it is rescaled — a bar stopping dead at 90% reads as a failed load.

## HUD

- **The HUD is monospace** — `Assets/Fonts/JetBrains_Mono/static/JetBrainsMono-Regular.ttf` via `SceneBuilder.UIFont()`, used by every text object in both scenes.
  - Diegetic reason: everything on screen belongs to the *facility*, and a terminal face says so where a proportional one reads as the game's own UI. Practical reason: the countdown changes every second, and proportional digits reflow and twitch every tick.
  - **Licensing: JetBrains Mono is SIL OFL and `OFL.txt` ships beside it.** Keep that file where it is. This replaced Consolas, which was Microsoft's and fine only for a prototype that never left this machine — embedding a font in a distributed build is redistribution.
  - Point `UIFont()` at the **static Regular**, never the variable font beside it — uGUI's legacy `Font` has no axis control, so a variable face is a coin toss on which weight renders.
  - `UIFont()` falls back to the built-in font rather than throwing. **Check it did not fall back** after a font change: `grep -o "m_Font: {fileID: [0-9]*, guid: [a-f0-9]*" Assets/Scenes/*.unity | sort | uniq -c` should show one guid.
- **The "Iteration N" label sits dead centre**, upper case and spaced out (`IterationLabel.spacedCaps`). It used to sit 150px high, which read as a subtitle floating over the room; the label is the iteration announcing itself, so it gets the middle of the screen with nothing else on it. Spacing is done **in the string** because uGUI's `Text` has no tracking control; in a monospace face a space is exactly one cell, and the single space between words becomes three. `horizontalOverflow` is `Overflow` so a narrow rect can't break it as "ITERATIO / N 12".
- `Loop/CarriedItemsDisplay.cs` — top-left readout of what you are carrying, **as icons**, with the one in your hand at full strength and the stowed ones at alpha 0.33. It exists because carrying is a rewound state the player otherwise cannot fully see — only one item is in the hand at a time, and Tab means the readout has to answer "what am I about to use" as well as "what do I have".
  - Icons rather than words for the same reason the HUD is monospace — the facility's readout, not the game's subtitle. They also survive being glanced at, which is all this gets.
  - A **fixed pool of four `Image` slots**, enabled and disabled rather than created and destroyed, rebuilt only when `PlayerHand.Version` changes. `itemId` is the wire value other scripts match on (`KeyLock` asks for `"Key"`).
- `Loop/ControlHintDisplay.cs` — a grey disc with `E` over whatever interactable the player has walked up to, and a mouse glyph on the pin once it is in hand.
  - **These used to retire on first use and play-testing overruled it.** The reasoning was real — the loop's texture is repetition, so an instruction replaying every sixty seconds becomes the most repeated thing in the prototype — but testers never found the door button, and those who did could not find it again an iteration later. **A prompt shown once has not been taught, and a control the player cannot find is worse than one they are reminded of.**
  - What survives is the restraint: the disc appears only when E would actually *do* something (`WantsInteractHint`, not proximity), only over the nearest such thing, and at `maxAlpha` **0.75** — a label stencilled on the fixture rather than the game talking. That is the dial if it starts to grate.
  - `IInteractHintTarget` means "E right now would do something here". A shut drawer with the pin inside is one place with two answers, and prompting over the object that would *ignore* the press teaches the wrong thing. Implemented by `Drawer`, `CarryableItem` and `KeyLock`.
  - `interactTargets` is typed `MonoBehaviour[]`, not the interface, because **Unity does not serialize interface fields** — the cast happens once in `Awake`.
  - Positions come from `WorldToScreenPoint` into a full-screen rect via `ScreenPointToLocalPointInRectangle` with a **null camera** (the canvas is ScreenSpaceOverlay; passing one skews it). The `z <= 0` test is load-bearing — `WorldToScreenPoint` returns a mirrored on-screen position for anything behind the eye, so without it a prompt for something at your back appears in front of you.
  - Its fade uses `Time.unscaledDeltaTime`, or a prompt caught mid-fade sits frozen under the pause overlay.

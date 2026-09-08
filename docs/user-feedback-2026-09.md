# September play feedback

## Accepted presentation decisions

- Compose the game and overlay UI at 16:9. Other window/display ratios use centered black bars.
- Keep the in-game ITERATION label in English. Keep the current PA wording for now.
- Resolution and fullscreen controls stage a selection; APPLY commits both together.
  Closing and reopening settings discards unapplied selections.
- Retire the inventory drop hint after the first release in a run, across subsequent items
  and iterations. Starting a new run may teach the control again.

The display policy is implemented by `VideoSettings`, `SettingsPanel` and
`FixedAspectPresentation`. Native fullscreen is still the first-launch Player Settings default;
an existing player's saved windowed preference is respected. Unity's borderless mode also
[preserves aspect ratio when scaling output](https://docs.unity.cn/6000.7/Documentation/ScriptReference/FullScreenMode.FullScreenWindow.html).

## Scene source and incremental refresh

`SceneBuilder.ApplyUserFeedback` applies the same finishing pass used by the full builder:

- Adds aspect framing to main cameras and root overlay canvases, plus the graphics APPLY button.
- Sets the first-release drop-hint threshold.
- Adds standing-tree collision using the existing stump's measured trunk width, separate from
  the felled bridge colliders. Whole-mesh bounds include branches and must not size this collider.
- Adds a backing slab 2 cm under the Cycle 3 north corridor, overlapping its floor joints
  without placing two visible top surfaces on the same plane.
- Aligns the final ladder vertically against the wall and offsets the climb volume toward
  the room so the player capsule is in front of the rails.
- Extends the cable from the car's initial position into the station wall.

Run **Iteration Room > Apply User Feedback** with Play Mode stopped and saved scenes.
The operation saves enabled build scenes and restores the editor's prior scene setup.
This incremental operation does not rebake lighting or reflection probes. Follow the full
SceneBuilder bake workflow for final visual verification after geometry changes.

Korean localized UI uses bold while English restores the authored font style.
The existing switch-on clip remains; switch-off now plays only on an actual on-to-off
transition, so initial setup/repeated resets do not emit extra clicks.

## Verification

- Unity EditMode 14/14 and PlayMode 10/10 passed.
- Windows player build succeeded (634.3 MB) at `Build/Windows/Iteration.exe`.
- The new player started without command-line fullscreen overrides on a 2880x1800 (16:10)
  monitor. Native window bounds matched the monitor and the client area was also 2880x1800;
  the startup log contained no exceptions. `Tools/Check-WindowsPlayerStartup.ps1` reproduces
  this check and closes only the process it started. This verifies window geometry, not
  screenshot appearance or full gameplay. Viewport composition is covered by automated tests.

## Still requires visual/play evaluation or a design decision

- Visual black-bar/UI composition on other displays, especially ultrawide monitors.
- Ladder entry, climbing and dismount comfort; floor seam appearance from the reported viewpoint.
- Korean weight/readability at lower resolutions and the volume of simultaneous switch resets.
- Main-menu music direction and sourcing; confirming whether "elvanlab" means ElevenLabs.
- Cosmic-horror staging during the cable-car ride and which rooms need a stronger sense of enclosure.

The above art/audio decisions have not been silently substituted with new assets or a different ending.

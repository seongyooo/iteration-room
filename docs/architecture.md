# Architecture and script index

What each script owns. Responsibility boundaries are load-bearing — the summary table and the rule
against merging roles are in CLAUDE.md §2.

---

## Script index (`Assets/Scripts/`, namespace `IterationRoom`)

Only what is not covered above.

- `Player/FirstPersonController.cs` — CharacterController movement, mouse look, jump. Also owns the pointer lock and the click-to-retry (browsers only grant capture inside a user gesture, so the lock requested in `Start` is routinely refused on WebGL). **Looking is skipped entirely while the cursor is loose**, or a pointer travelling across the page swings the view across the room.
- `Player/CameraShaker.cs` — sits on a **`CameraRig` between the player and the camera**, and that placement is the point: `FirstPersonController` rewrites the camera's own `localPosition` and euler angles every frame, so a shake applied to the camera itself is wiped instantly. Uses Perlin rather than `Random` — continuous noise reads as a building judder, per-frame random values as a broken frame rate.
- `Interactables/FloorButton.cs` — hold-type, active if the player OR any ghost is on it. The player side is **polled in `FixedUpdate`, deliberately not `OnTriggerEnter/Exit`**: `Teleport` disables and re-enables the CharacterController inside one frame, so a player standing on it when the iteration ended never generated the exit callback — the pad stayed lit forever and every ghost recorded afterwards held it forever too. **Don't "simplify" this back to trigger events.** `Drawer`, `CarryableItem`, `PlayerHand`, `KeyLock` and `EscapeTrigger` all poll for the same reason.
  - The pad has **no collider at all** — a logical volume testing the player's centre against `activationRadius` (derived from the visual radius so the two can't drift), plus a foot-height check so jumping off releases. Its old trigger was `radius 0.5, height 0.5`, which Unity silently clamped to a *sphere*, and an AABB test added the player's own 0.3 radius: a ±0.8m square around a disc of radius 0.35.
- `Interactables/PressPlate.cs` — the shared base for the two plates the player presses with E **outside the loop**: `Room/CalibrationStartButton.cs` before the first iteration and `Room/FinalRoomButton.cs` after the last. Range polling, the grey disc, and the red press flash live here; a subclass supplies only `IsLive` and `OnPressed`. They share a base precisely because neither can gate on `LoopManager.AcceptsInput` — it is false for exactly the moments each has to work.
- `Room/FinalRoomSequence.cs` — Room4, past the last door. Raises the plinth, waits on the plate, then seals the door back to Room3, announces the break, turns every wall panel into a test card showing `ERROR` and shakes the building, before handing to `EndingSequence`. **The player keeps control until the press.** See `loop-and-ui.md`.
- `Interactables/DoorIndicator.cs` — the lamp: one block split red/green, only ever one lit (albedo *and* emission — albedo alone looks like coloured plastic). **It tracks the condition, not the door.**
- `Interactables/BalloonTool.cs` — uses an **overlap sphere, not a raycast**: in a room packed with balloons, having to line one up in the crosshair turns a physical act into a shooting gallery.
- `Loop/IterationLabel.cs` — the eyelids are built first in `BuildUI` so they sit at the back of the canvas and the label draws on top of the black rather than under it.

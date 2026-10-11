# Progress

AR Enclosure, Coventry University BSc dissertation artefact. Newest task first.

## 2.4 Move and rotate

**Status:** tested on the Pixel 9a on 2026-10-11, all 7 checks passed, merged and tagged `task-2.4`.

**What was built**

- `PlacementManipulator`: after placement, a one-finger drag that starts on the panel (or within 0.25 m of its box) moves it across horizontal planes. It raycasts every frame and keeps the base seated on the plane under the finger. A two-finger twist turns the panel about world up only, pivoting on the centre of the base so it turns on the spot. Pinch distance is never read and scale is never written.
- Anchoring: the `ARAnchor` is removed when a gesture starts, and a new plain world anchor is added on release. This is the same anchoring as at placement, and it is still not attached to a plane.
- The 1 m reference rod follows the panel during gestures.
- `AppModeController` (app layer): holds `AppMode { Move, Measure, Obstacle }` and defaults to Move. The UI calls `RequestMode(...)` and never sets the mode itself. The manipulator only responds in Move mode. It also has a `ManipulationEnabled` flag that other tools can use. Switching mode mid-gesture ends the gesture and re-anchors.
- HUD: a new MOVE section shows the mode, the current gesture, yaw, a scale-lock check, the last move (distance and angle), and whether the anchor is attached. The placed size is re-measured after every move, so the SIZE ratio keeps proving true scale.
- The placement tap cannot also start a drag, because touches from the placement frame are ignored. The 4 s warm-up still applies to the first placement only.

**Files touched**

- `Assets/_Project/Scripts/Placement/PlacementManipulator.cs` (new)
- `Assets/_Project/Scripts/App/AppModeController.cs` (new)
- `Assets/_Project/Scripts/Core/ModelBounds.cs`: new `TryGetLocalBounds`. `TryGetLocalSize` now uses it and gives the same output (checked: 1.6485 x 0.4500 x 1.9247).
- `Assets/_Project/Scripts/Placement/ARPlacementController.cs`: new read-only `ReferenceInstance` property, nothing else changed.
- `Assets/_Project/Scripts/Diagnostics/ScaleVerificationHUD.cs`: new MOVE section and re-measure after a move.
- `Assets/Scenes/ARPlacement.unity`: `AppModeController` and `PlacementManipulator` added to `/Controllers` and wired.
- `Packages/manifest.json`, `packages-lock.json`: added `com.unity.pipeline`, a tooling package that lets Claude Code drive the Editor.

**How to test**

1. Place the panel as before, after the 4 s warm-up.
2. Drag it with one finger that starts on the panel. It should slide along the floor and stay seated.
3. Start a finger on the floor away from the panel. Nothing should move.
4. Twist with two fingers. The panel should turn on the spot, with no drift and no size change.
5. After each gesture the HUD should show "Anchor attached, re-anchored", "Scale 1.000 locked" and a height ratio of 1.0000.
6. Walk around for about 30 s after a move. The panel should stay put.
7. Press Reset and place again. This should work exactly as in 2.2.

**Known issues**

- If the panel is dropped on a plane that ARCore later merges, it can still shift slightly, as at placement. This is the documented world-anchor limitation.
- On the device the HUD fits, and twist direction is correct.

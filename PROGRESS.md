# Progress

AR Enclosure, Coventry University BSc dissertation artefact. Newest task first.

## 2.5 Measurement tool

**Status:** tested on the Pixel 9a on 2026-10-12. The first test passed steps 1 to 7 (run_id 1 to 26, carried across a restart) and found a tap-through bug. The fix passed its re-test (runs 27 to 35). Merged and tagged `task-2.5`.

**Fix: UI tap-through.** A button press also counted as an AR tap behind it. For example, pressing Close loop added a fifth corner, as in runs 18 and 21, which logged 5 points for 4 corners. Those two CSV rows are invalid.
- New `Core/UiTouchFilter`: once per frame it checks each new touch with `EventSystem.RaycastAll` at the touch position. A finger that begins over UI stays blocked until it lifts.
- `ARPlacementController`, `MeasurementTool` and `PlacementManipulator` all ignore blocked fingers. The manipulator also leaves them out of finger counts, so a finger resting on a button cannot become half of a twist.
- This replaces `IsPointerOverGameObject`, which was unreliable with the Input System UI module.
- All five buttons now have a 60% black rounded background with a white child label, so they are readable over the camera feed.

**What was built**

- Mode button (bottom left): switches between Move and Measure. The mode lives in `AppModeController`, and the HUD only calls `RequestMode`. In Measure mode, drag, rotate and placement taps are all off. In Move mode they work as before.
- `MeasurementTool`:
  - Distance: tap A, then tap B. The app shows the straight-line distance to 3 decimals, with markers and a line between the points.
  - Perimeter: tap 3 or more points, then press Close loop. The app shows the closed perimeter.
  - A tap after a result starts a new measurement. The Distance/Perimeter button and the Clear button only show in Measure mode.
  - Points are frozen world positions, not parented to any plane, so plane merging cannot move them.
- `MeasurementLog`: each result is written to `measurements.csv` in `Application.persistentDataPath`.
  - Columns: `run_id, timestamp, mode, point_count, points, distance_m, camera_distance_m, tracking_state`.
  - `run_id` continues from the highest id already in the file, so restarting the app never reuses a number. The screen shows "Run #N" next to each result.
  - `points` is written as `x;y;z|x;y;z`, so commas only separate columns.
  - `camera_distance_m` is measured from the camera to the segment's midpoint, or to the centre of the points for a perimeter.
  - `tracking_state` is the session state plus `PlanesTracking` or `PlaneLimited`. That second part shows whether every hit plane was fully tracked at the time of the tap.
- `MeasureMarker.mat` is a URP Unlit material asset that the scene references, so IL2CPP keeps its shader in the build.
- `ARPlacementController` now places the panel only in Move mode, so a measurement tap cannot drop the panel.
- The `ScaleReadout` text no longer accepts taps. It used to block placement taps over the top-left 700 x 900 area.

**Files touched**

- `Assets/_Project/Scripts/Measurement/MeasurementTool.cs` (new)
- `Assets/_Project/Scripts/Measurement/MeasurementLog.cs` (new)
- `Assets/_Project/Scripts/UI/MeasurementHUD.cs` (new)
- `Assets/_Project/Materials/MeasureMarker.mat` (new)
- `Assets/_Project/Scripts/Placement/ARPlacementController.cs`: optional `m_Modes`, and taps only place the panel in Move mode.
- `Assets/Scenes/ARPlacement.unity`:
  - New UI on `/Canvas`: ModeButton, MeasureKindButton, MeasureClearButton, CloseLoopButton and MeasureResult.
  - `MeasurementTool` added to `/Controllers`, and `MeasurementHUD` added to `/Canvas`.
  - `ScaleReadout` raycast target turned off.

**How to test**

1. The app starts in Move mode. Place the panel, then drag and twist it as in 2.4.
2. Tap Mode. It should read "Mode: Measure", and the Distance and Clear buttons should appear. Dragging the panel should now do nothing.
3. Distance: tap two points on the floor that you have marked with tape. You should see a yellow line, then "Run #N" and "x.xxx m". Measure the same gap with the tape and note it next to Run #N.
4. Tap again to start the next run, and check that N goes up by 1.
5. Tap Distance so it changes to Perimeter. Tap 4 corners of a rectangle on the floor, then press Close loop. The loop should close and the perimeter should appear.
6. Close the app, reopen it and do one measurement. Run # should carry on from the last number, not restart at 1.
7. Pull the CSV with `adb pull /sdcard/Android/data/com.mihil.arenclosure/files/measurements.csv`, and check that the `run_id` values match what you wrote down.

**Re-test after the tap-through fix**

1. Perimeter: tap 4 corners, then press Close loop. The result should say "Perimeter, 4 pts", and the CSV `point_count` should be 4.
2. Distance: after tapping A, press Clear. No marker should appear under the Clear button.
3. Press Mode while in Measure mode. No point should be added, and in Move mode the panel should not jump or start dragging.
4. Rest one finger on a button and drag another finger on the panel. It should drag, not twist.
5. Check that the buttons are readable over a bright floor.

**Known issues**

- Runs 1 to 26 in `measurements.csv` were logged before the tap-through fix. Runs 18 and 21 are confirmed to have an extra point. Run 23 (perimeter, 5 points) was also logged before the fix and may be affected. Use runs 27 and later for the accuracy analysis, or check each earlier perimeter against your notes.
- Only horizontal planes are detected for now (vertical detection is a 3.3 decision), so measurement points can only go on floors and tables, not walls.
- Taps land on the inside of a detected plane outline only. If a corner is outside the detected area, the tap is refused with "No tracked surface under that tap".

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

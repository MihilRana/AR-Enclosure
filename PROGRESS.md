# Progress

AR Enclosure, Coventry University BSc dissertation artefact. Newest task first.

## 2.5b Floor lock (false upper planes)

**Problem seen on the device:** ARCore sometimes makes a horizontal plane above the real floor, or several overlapping planes for one floor. Drag and measurement taps then hit the upper plane, and the panel jumps up onto it. Plane outlines also show wrong corners and overlap each other.

**Step 1, diagnostics only. Status:** tested on the device on 2026-10-12. The findings are below. It changed no behaviour.

- New `Core/RaycastProbe.cs`: placement, drag and measurement each report their full hit list (nearest first) and which hit they used.
- New `Diagnostics/PlaneDiagnostics.cs`, with an on-screen `PlaneReadout` between the scale readout and the buttons. It shows:
  - every plane sorted by height: short id, height above the lowest plane, size, area, tracking state, and which plane it was merged into, if any;
  - the last raycast: every hit, which one was used, and how far each hit sits above the lowest plane.
- `plane_diagnostics.csv` in `persistentDataPath` logs placement and measurement taps every time. Drag is logged only when the winning plane or the hit count changes. Each row holds the full trackable ids, every hit and a snapshot of all planes.

**Working hypothesis, to confirm with the data:**
1. Raycast hits come back nearest first. A false plane above the floor is nearer, so drag and measure take it, and drag seats the panel on it.
2. One floor is often detected as several planes a few cm apart until ARCore merges them. A merged plane may stay in the list for a while and can still be hit.
3. If the "false" plane is 30 cm or more higher, it is probably a real surface (rug, step, furniture) rather than tracking error.

**How to test Step 1**

1. Place the panel. Note the PLANES list and which plane the LAST HIT used. That is the floor.
2. Walk around until an extra plane appears or the panel jumps during a drag. Take a screenshot of the readout at that moment.
3. Note the "above" value of the upper plane and whether its merged column shows "into xxxx".
4. Measure across the floor where the overlap is, and check which plane the LAST HIT used.
5. Pull the log: `adb pull /sdcard/Android/data/com.mihil.arenclosure/files/plane_diagnostics.csv`

**Step 1 findings (device, 2026-10-12, `plane_diagnostics.csv` and 3 screenshots)**

- Two separate floor estimates existed at the same time, both fully tracked: y = -1.2915 m (3.1 m²) and y = -1.1682 m (19.8 m²), 12.3 cm apart. An earlier session showed a 15.7 cm gap. The floor is flat tile, so this is depth error, not a real surface.
- Raycasts do not hit merged planes. At placement, 4 planes overlapped the tap but there was 1 hit. A merged plane copies its parent's height and outline. Merged planes still draw, which caused the overlapping visuals.
- ARCore then settled on the upper estimate: all 5 planes ended at -1.167 m, with 4 merged into one. Choosing the lowest plane would have been wrong.
- Placement happened 5.16 m away, where depth error is largest.

**Step 2, floor lock. Status:** the lock works on the device. The four Step 2b fixes below are built and compiled clean in the Editor, waiting for the device test.

- New `Placement/FloorLock.cs`:
  - A usable plane is unmerged, fully tracked, horizontal and at least 0.25 m².
  - On placement, it locks the hit plane's id and height. It follows the lock through ARCore merges and tracks drift.
  - After placement, drag and measurement accept only the locked floor (or a plane merged into it), or a usable plane within ±3 cm of the locked height. Nearer false planes are skipped.
  - It hides merged and small planes, but keeps the locked floor visible.
  - Reset clears the lock.
- Placement:
  - It is refused while two usable planes within 1.0 m of the tap are more than 3 cm apart in height, with the message "Floor still settling (N cm apart), keep scanning".
  - The maximum distance is now 3.5 m (it was 6 m).
  - It uses the first usable plane hit.
  - Refused-tap messages now stay on screen for 2.5 s. Before, they were overwritten the next frame.
- After placement, if the locked floor drifts by more than 3 cm, the status reads "Placed. Floor estimate moved ±X cm". The panel's world anchor is unchanged.
- Measurement: before placement it uses usable planes only. After placement it uses the locked floor only. Refused taps say why, and the reason stays on screen for 2.5 s.
- Diagnostics:
  - Each plane row now has a `use` column: `LOCKED`, `ok`, `off-floor`, `merged`, `small` or `limited`.
  - A new LOCK block shows the locked id, height, current floor height and drift.
  - The CSV is now `plane_diagnostics_v2.csv`, with added `lock_id, lock_y, lock_current_y, lock_drift_m` columns.
- New `Core/PlaneMetrics.cs`: shared polygon area and horizontal distance helpers.

**Step 2 device test (2026-10-12, 21:45 to 21:53):** the lock worked. Four issues were found and fixed in Step 2b:

1. Band bug: the ±3 cm band was measured from the height at lock time. After the floor drifted by -7.2 cm, planes about 5 cm above the current floor were marked `ok` and could take taps. The band now follows the locked floor's current height, in both `TryPickHit` and the readout's `use` column.
2. Visual pile-up: after the lock, only the locked floor (the end of its merge chain) is drawn. Every other plane is hidden, whatever its height or size. Merged members copy their parent's outline exactly, so drawing them would only stack the same dots again. Before the lock, usable planes are still shown.
3. Drift: when drift is more than 3 cm, a "Re-seat on floor" button (amber, bottom right) appears.
   - Pressing it removes the anchor, waits one frame, moves the panel and the 1 m rod vertically so the base sits on the floor's current height, then adds a fresh anchor.
   - It only moves height: position on the floor, yaw and scale are unchanged.
   - It then rebases the lock, so drift reads 0. The status shows "Re-seated on floor, moved ±X cm".
   - The drift warning in the status line is kept until you re-seat.
4. CSV filename: `plane_diagnostics_v2.csv` was never created, because the scene had saved the old filename in a serialized field, which overrode the code. The filename and header are now constants. If the file on disk has a different header, it is renamed to `plane_diagnostics_v2_old_<stamp>.csv` and a fresh file is started. Step 2 rows from 21:45 to 21:53 are in the old `plane_diagnostics.csv` under the v1 header, so read them with that in mind.

New `UI/PlacementHUD.cs` shows the Re-seat button only when `ARPlacementController.CanReseat` is true.

**Files touched (Step 2):** `Placement/FloorLock.cs` (new), `Core/PlaneMetrics.cs` (new), `Placement/ARPlacementController.cs`, `Placement/PlacementManipulator.cs`, `Measurement/MeasurementTool.cs`, `Diagnostics/PlaneDiagnostics.cs`, `Scenes/ARPlacement.unity` (FloorLock on `/Controllers`, wired into all four components, placement limit 3.5 m).

**How to test Step 2**

1. Start scanning. While two floor estimates disagree, a placement tap should say "Floor still settling (N cm apart)". Keep scanning until a tap places the panel.
2. After placement, the plane list should show one `LOCKED` row, and the LOCK block should show the locked height with a drift of about 0.0 cm.
3. Merged and small planes should no longer be drawn, so there should be no stacked dot patterns.
4. Drag the panel across the whole floor, including where a second plane used to appear. It should never jump up. If a hit is rejected, LAST HIT should show it was skipped and which plane was used.
5. Measure on the floor and on a table, if one is in view. Floor taps should work, and table taps should say "That surface is not the locked floor".
6. Tap a surface more than 3.5 m away. It should say "Step within 3.5 m".
7. Press Reset. The LOCK block should show "none", and placing again should create a new lock.
8. Pull the log: `adb pull /sdcard/Android/data/com.mihil.arenclosure/files/plane_diagnostics_v2.csv`

**How to test Step 2b**

1. Run `adb shell ls /sdcard/Android/data/com.mihil.arenclosure/files/`. After one placement, `plane_diagnostics_v2.csv` should exist. Its first line should end in `lock_id,lock_y,lock_current_y,lock_drift_m,hits,planes`, and the rows should have lock values filled in.
2. After placement, only one plane (the LOCKED one) should be drawn. Other planes should still be listed in the readout but not drawn.
3. Walk around until drift is more than 3 cm. Any plane that is more than 3 cm from "floor now" should show `off-floor`, not `ok`, even if it is close to the original lock height. Taps on it should be refused.
4. While drift is more than 3 cm: the status shows the warning and the amber Re-seat on floor button appears. Press it. The panel should move by about the drift, then the status should show "Re-seated ... moved ±X cm". Drift should read about 0.0 and the button should disappear.
5. After re-seating, the HUD should show Scale 1.000 locked, Ratio 1.0000, and an attached anchor. Walk around and check the panel stays put.
6. Press Reset: the lock should clear, the button should stay hidden, and usable planes should be drawn again.

**Known issues**

- After the lock, measurement is floor-only, so a table or worktop can't be measured. This is intended for now. Wall and obstacle points come in 3.3.
- If ARCore moves the whole floor estimate after placement, the anchored panel does not move with it. The drift warning reports this; it is the documented world-anchor limitation.
- With 8 or more planes, the plane readout may spill past its box.

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

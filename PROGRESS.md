# Progress

AR Enclosure, Coventry University BSc dissertation artefact. Newest task first.

## 3.2b Confirm the floor, then stop plane finding

**Status:** first device test failed on the reflective tile floor. The settling gate was changed from blocking to advising (below). Built and compiled clean, waiting for the device re-test. The demo fallback is `demo_3.4a.apk`.

**Device test 1 (2026-10-13), confirm flow failed**

- At 6.8 s, plane 2d30 (2.6 m²) was offered too early. It later turned out to sit 21 cm below the main floor, most likely a reflection.
- At 13.5 s, plane 5c33 appeared 21 cm away. The gate withdrew the offer and the buttons vanished.
- At 78.5 s there were 9 planes spread over 55 cm. Plane 1377 (16 m²) was the real floor. Plane a011 sat 55 cm below it, a reflection that never merges, so the gate could never settle and the buttons never came back.
- Conclusion: on a glossy floor, a "no conflicting planes" rule can never be met. Conflicts must be advice, not a block.

**Change: advising gate**

- Candidate: the largest usable plane, not skipped by Rescan. It is offered only after 8 s of tracking, if its area is at least 1.0 m², and once its own height has stayed within ±3 cm for 2 s.
- Once offered, the buttons stay. The offer is replaced only if the candidate merges or disappears, its height moves more than 3 cm, or a usable plane at least 1.5× its area appears. Any replacement goes through the 2 s hold again. Other planes appearing do not hide the buttons.
- Conflicting planes become a warning under the offer: "Another surface N cm below (likely reflection)." or "Another surface N cm above (table, step or false floor?)." The user still decides.
- Rescan rule unchanged: the plane is skipped until it merges, disappears, or moves more than 3 cm.
- The settings have new field names (`m_MinTrackingSeconds` 8, `m_MinCandidateArea` 1.0, `m_HoldSeconds` 2, `m_LargerFactor` 1.5). The old serialized values could not override them, and the scene was checked to hold the new values.
- `FloorLock.IsPlaneSettled` was replaced by a conflict finder (see below).

**Device test 2 (2026-10-13), advising gate:** the offer came after about 8 s on the big floor. The buttons stayed while sweeping. Rescan worked. After confirming, the plane count stopped growing, taps worked outside the outline, taps on the sky were refused, and Reset worked.

- Problem: the outline appeared on the wrong surface many times.
  - In shot 19, the offered plane `3e57` was at floor height, but its outline and dots ran over the wall, curtain and table, because ARCore's outline reaches past walls and nothing hides it behind them. Once confirmed, the panel sat correctly.
  - In session 2, two large planes 37 cm apart overlapped (`0797` 24 m² and `c8d7` 11 m²). The rule picked `0797`, and the panel looked seated on the tiles, so `c8d7` was the reflection.
  - The warning showed only the biggest gap, so `c8d7` (34 cm below) was hidden behind `7370` (37 cm above).
- Conclusion: the outline is the wrong tool for judging the floor height. Which plane is chosen is unchanged, because the largest plane was correct in both sessions.

**Change: floor ring (the last floor change before Phase B)**

- While a floor is offered, no plane outline or dots are drawn (`FloorLock.HideAllPlanes`).
- Instead, a green ring (0.4 m across, with a small centre cross) is drawn at the candidate's height, where the centre of the screen meets it, within 4 m. The prompt reads "Point at open floor. The ring must stay on one tile as you move the phone." At the right height the ring stays on one tile as the phone moves; at a wrong height it slides across the tiles.
- The warning now lists the nearest conflicting surface above and the nearest below, each with its area, for example "Surface 37 cm above (2.8 m2, table, step or false floor?)." and "Surface 34 cm below (10.9 m2, likely reflection).".
- `FloorLock.FindConflicts` returns the nearest-above and nearest-below usable planes near the candidate, beyond the ±3 cm band.
- The outline settings were renamed to ring settings with `FormerlySerializedAs`, so the scene kept its material and colour. The scene was checked.

**Device test 3 (2026-10-13), floor ring:** the offer, the hidden dots, the warnings (for example "21 cm below (likely reflection)", and "11 cm above" plus "33 cm below" in another session), confirm, place, drag, measure, obstacles and Reset all passed.

- The ring check **failed by design**: the ring was redrawn under the screen centre every frame, so it followed the phone at any height and could never show parallax.

**Fix: fixed ring (the last floor change)**

- When a floor is offered, the ring is dropped once where the screen centre meets the candidate's height, and then stays fixed in the world. Only its height follows the candidate, which stays within ±3 cm while the offer stands.
- If the screen centre is not on the floor within 4 m, the ring waits until it is. The prompt reads "Point at open floor to drop the ring".
- Once dropped, the prompt reads "Move the phone sideways: the ring must stay on the same tile. If it slides, press Rescan."
- A new **Move ring here** button (bottom left, in the 180 slot, Move mode, only during an offer) re-drops the ring at the current screen centre.
- The ring is dropped again whenever the offer is replaced or withdrawn.

**Why:** ARCore keeps creating, merging and moving planes for the whole session. Each new plane is another chance for a false floor, and the floor in session 2 moved 10 cm. Once the user has accepted a floor, there is nothing more to find.

**What was built**

- New `Placement/FloorConfirm.cs`:
  - The candidate is the largest usable plane (unmerged, fully tracked, horizontal, at least 0.25 m²).
  - It is offered when no other usable plane near its outline (within 1.0 m) differs in height by more than 3 cm, and that has held for 1.5 s. Its height must stay within 1 cm during that time.
  - While offered, only that plane is drawn, with a green outline. In Move mode two buttons appear: "Use this floor" and "Rescan".
  - **Use this floor:** locks the floor on that plane and sets `ARPlaneManager.requestedDetectionMode = None`. Existing planes keep tracking, so drift is still measured. Only the locked floor is drawn.
  - **Rescan:** skips the offered plane until ARCore changes it, that is until it merges into another plane, disappears, or its height moves more than 3 cm. It can then be offered again. Other planes can be offered in the meantime.
  - **Reset:** restores the detection mode set in the inspector (Horizontal), clears the skip list and the candidate, and starts searching again.
- `FloorLock.TryRayFloor`:
  - After the lock, a tap meets an endless flat plane at the floor's current height, so it works anywhere on the floor.
  - Rays less than about 5° below the horizon are refused, and so are hits beyond the distance limit. The distance is measured from the camera, not from the near clip plane.
  - `FloorLock.IsPlaneSettled` applies the ±3 cm and 1 m rules to a whole plane.
- Using the flat floor plane after the lock:
  - placement (3.5 m limit);
  - drag (6 m, unchanged);
  - measurement (3.5 m; these runs are tagged `LockedFloor` in the `tracking_state` column of measurements.csv);
  - obstacle marking (3.5 m).

  Before the lock, all of them work exactly as before.
- Placement: until a floor is confirmed, a tap shows "Confirm the floor first". The warm-up gate still applies.
- The clearance panel shows the placement status before placement, so the floor prompts can be read with Debug off.
- Diagnostics: a "PLANE FINDING" line in the readout. Taps on the flat floor plane are logged as `place_floor`, `drag_floor`, `measure_floor` and `obstacle_floor`, with the locked plane's id.
- Unchanged: drift readout, Re-seat, the ±3 cm band, the rule engine, obstacle storage, and all CSV layouts.

**Editor check:** with a floor at y = -1.2 m and the camera at the origin:

| Camera pitch | Result |
|---|---|
| 45° down | hit at (0, -1.2, 1.2), 1.697 m away |
| 10° down | 6.91 m, refused as too far |
| 20° up | refused as above the horizon |

**Files touched:** `Placement/FloorConfirm.cs` (new), `Placement/FloorLock.cs`, `Placement/ARPlacementController.cs`, `Placement/PlacementManipulator.cs`, `Measurement/MeasurementTool.cs`, `Obstacles/ObstacleTool.cs`, `UI/PlacementHUD.cs`, `UI/ClearanceHUD.cs`, `Diagnostics/PlaneDiagnostics.cs`, `Core/RaycastProbe.cs`, `Scenes/ARPlacement.unity` (FloorConfirm on `/Controllers`, plus `UseFloorButton` and `RescanButton`).

**How to test**

1. Start the app and scan the floor. For the first 8 s it should read "Scanning for the floor (N s)", then "Checking the floor", then "Floor found (N m2)" with the ring prompt and up to two warning lines (above and below). No planes should be drawn while the floor is offered. A green ring with a cross should follow the centre of the screen on the floor, and the buttons should stay while other planes appear.
1b. Ring check: when the ring drops, note the tile and grout corner under its cross. Step or move the phone about 30 to 50 cm sideways. On the real floor, the ring stays on that tile. On a reflection plane it slides and looks sunk below the tiles. On a false upper plane it slides and looks raised. Press Move ring here to re-drop it, or Rescan if it slides.
2. Tap the floor before confirming. It should say "Confirm the floor first" and place nothing.
3. Press Rescan. The outline should go. The same plane should only come back after it changes, while another floor can be offered meanwhile.
4. Press Use this floor. The outline should go, the debug readout should show PLANE FINDING off, and the PLANES count should stop going up while you walk around.
5. Place the panel well outside the old plane outline. It should work up to 3.5 m. Drag it across the whole floor; it should not stop at outline edges. Measure and mark obstacles away from the drawn plane.
6. Tap a wall above the floor line, or the sky through a window. It should say "Tap the floor, below the horizon".
7. Re-seat and drift should work as before.
8. Press Reset. Plane finding should come back on, planes should be drawn, and the search should start again.
9. Tape-check one front_gangway reading against a measurement taken away from the original plane outline.

**Known issues**

- A tap on a wall in the camera image meets the floor plane behind the wall. For obstacle marking, tap where the wall meets the floor.
- With plane finding off, ARCore no longer refines the floor outline. Only the floor's height is used after confirming, so this does not matter.

## 3.2 to 3.4 Obstacles and rule engine, Phase A (demo)

**Status:** device test passed on the Pixel 9a on 2026-10-13.

**Device test, 2026-10-13, 16:21 to 16:38, two sessions** (`rule_checks.csv`: 248 rows, 31 checks; `obstacles.csv`; 6 screenshots)

- Placement showed INCOMPLETE with "3 manufacturer gaps not entered" and "Door leaf 0.587 m". Pass.
- The wall line and fence appeared. Undo removed the newest lines in order, Kind switched to Enclosure, and Clear obstacles worked (obstacles.csv rows 4 to 8). Pass.
- Thresholds: front_gangway failed below 0.700, front_handle_clearance below 0.600, and door_swing_90 below 0.587 (screenshot 2, all three FAIL at 0.546 m). Moving back gave COMPLIANT (IEC 60364-7-729 checks) at 0.733 to 0.920 m. Pass.
- Debug off hides the readouts. Pass.
- Rotate on the device reads "moved 0.000 m, turned 91.8 deg", which confirms the 2.5b base-centre fix.
- Logging only on change: 31 checks over about 17 minutes. Verdict counts: 11 Compliant, 4 NotCompliant, 16 Incomplete.
- Check 13 was INCOMPLETE with 2 obstacles: after a drag, no wall was in the strip in front of the door side. This is correct.
- The Re-seat button appeared on the device (drift -3.8 cm in session 1, +10.0 to +11.3 cm in session 2), so its show and hide logic is verified. Pressing it is still to be confirmed.
- Session 2 floor move: the locked plane 4011 (y -1.020) was merged by ARCore into 3270, 10 cm higher. This is the third time ARCore has settled on the upper estimate. The gaps are horizontal, so the verdicts are unaffected by the height change, but the panel looked sunk until re-seated.
- Tape comparison (2026-10-13, 17:49): front_gangway read **0.727 m** (rule_checks.csv, 17:49:25) against **0.73 m** taped from the panel front to the wall, a difference of 3 mm. The 0.752 m in that screenshot was measurement Run #38 (Measure tool, two tapped points, +22 mm), not the rule engine.
- Re-seat on floor pressed on the device: drift went to 0.0 and the button hid. This closes the open 2.5b item.
- Cosmetic: with Debug on, long scale-readout lines run under the verdict panel. Use Debug off for demos; to be fixed in Phase B.

**Decisions (2026-10-13)**

- Vertical plane detection stays off. Obstacles are lines marked on the locked floor along the base of a wall or of a facing enclosure. Google publishes no CPU figure for vertical mode, and plain walls detect poorly.
- Verdict: NOT COMPLIANT if any applicable required rule fails. Otherwise INCOMPLETE if any required standard or model rule is unknown or not set. Otherwise "COMPLIANT (IEC 60364-7-729 checks)".
  - `user_input` rules left null are skipped and reported as "N manufacturer gaps not entered", so the verdict never overclaims.
  - Advisory rules only warn.
- Front = the door side = local +Z of the enclosure model. This is read from `model_profile.json`.
- Handle and enclosure gaps are both measured from the model's front envelope. This is a limitation recorded in the IEC notes.
- Door leaf width comes only from the explicit `Door` group: 0.587 m (`Door_1`). The `DOL_*` parts are not counted until they are confirmed.

**What was built**

- `Assets/StreamingAssets/rules.json`: the final rules file, copied unchanged. No clearance value exists in code.
- `Assets/StreamingAssets/model_profile.json`: model id, front axis, door group name, and the minimum width for a part to count as a door leaf (0.2 m, which excludes hinges and locks).
- `Rules/RuleData.cs`: data classes matching the two JSON files. `min_m` can be null.
- `Rules/ClearanceGeometry.cs`:
  - computes the gap from a face of the panel's oriented box to obstacle lines;
  - each face looks only along the strip of floor straight out from it, as wide as the face, so a wall off to one side does not count;
  - the nearest point of the clipped line gives the gap;
  - also works out the widest door leaf from the door group.
- `Rules/RuleEngine.cs`:
  - the value comes from `value_source` (standard, model door geometry, user input);
  - `min_m` null gives "not set";
  - `applies_if` conditions are all off by default, giving "n/a", and an unknown condition key is never treated as met;
  - `level` decides fail or warn;
  - `door_edge_open_90` subtracts the leaf width from the gap;
  - results are compared in whole millimetres.
- `Rules/ClearanceChecker.cs`:
  - loads both JSON files with `UnityWebRequest` (needed on Android) and Newtonsoft JSON;
  - evaluates every frame while the panel is placed and draws a gap line from each face to its nearest obstacle (green pass, red fail, amber warn, white otherwise);
  - logs to `rule_checks.csv` whenever the outcome changes and no gesture is in progress;
  - if a file fails to load, the HUD shows RULES NOT LOADED and the reason.
- `Obstacles/ObstacleSet.cs`: stores obstacle lines as frozen world points. Each is drawn as a floor line plus a 1 m translucent fence (orange for a wall, blue for an enclosure). Changes are logged to `obstacles.csv`.
- `Obstacles/ObstacleTool.cs`: Obstacle mode. Tap two floor points to make a line, minimum 0.10 m. Uses the floor lock, with a 3.5 m maximum. Undo removes a pending point first, then the last line.
- `UI/ClearanceHUD.cs`:
  - the verdict panel at top right, with the verdict, gaps not entered, warnings, obstacle count, door leaf, and each rule's measured value against its required value;
  - Obstacle-mode buttons: Kind (wall or enclosure), Undo, Clear obstacles;
  - a Debug toggle that hides the scale and plane readouts for a cleaner demo.
- The Mode button now cycles Move, Measure, Obstacle. The measurement result shows only in Measure mode, and the clearance panel shows in the other two modes.
- `Core/CsvLog.cs`: a shared CSV writer with a fixed header. If a file has an old header, it is renamed aside.
- `link.xml` keeps the rule data classes and Newtonsoft for IL2CPP. Newtonsoft 3.2.2 is now a direct dependency in `manifest.json`; it was already installed through another package.
- New material `ObstacleFence.mat`: a transparent URP Unlit asset, referenced by the scene.

**Editor test of the engine** (real rules.json and model, synthetic obstacles):

| Case | Result |
|---|---|
| No obstacles | INCOMPLETE (3 required unknown, 3 manufacturer gaps not entered) |
| Wall 0.750 m in front | COMPLIANT |
| Wall 0.650 m | NOT COMPLIANT: gangway fails, handle (0.600) passes, door swing (0.587) passes |
| Wall 0.550 m | all three front rules fail |
| Wall 0.30 m ahead but off to one side | ignored (unknown) |
| Slanted wall | its nearest end (0.720) decides |
| Panel turned 90° | 0.800 measured correctly |
| Conditions switched on | door_fixed_open_passing = 0.800 - 0.587 = 0.213, so it fails |

In Play mode, the rules loaded through `UnityWebRequest`. With a panel and a wall 0.650 m away injected, the HUD read NOT COMPLIANT and `rule_checks.csv` had one row per rule.

**How to test on the phone**

1. Place the panel in Move mode. The panel at top right should show INCOMPLETE, "3 manufacturer gaps not entered", "Door leaf 0.587 m", and the three front rules as unknown.
2. Tap Mode twice to reach "Mode: Obstacle". The prompt should read "Tap the floor at the base of a wall".
3. Tap the floor where a wall meets it, then tap again about 1 m along the wall. An orange line and fence should appear on the wall.
4. If the wall is in front of the panel's door side, a gap line should appear. The front rules should show measured values, and the verdict should update.
5. Tape-check: measure from the panel's front (door side) to the wall on the floor with a tape, and compare it with front_gangway's measured value.
6. Go back to Move mode and drag the panel closer to the wall than 0.700 m. front_gangway should go red (FAIL) and the verdict should read NOT COMPLIANT. Drag it below 0.600 m and front_handle_clearance should fail too.
7. Drag it back beyond 0.700 m. The verdict should read COMPLIANT (IEC 60364-7-729 checks), with "3 manufacturer gaps not entered".
8. Tap Kind to switch to Enclosure and mark a facing enclosure. It should be blue and count for the front rules.
9. Test Undo (a half-marked point first, then the last line) and Clear obstacles.
10. Debug: off should hide the scale and plane readouts.
11. Pull the logs: `adb pull /sdcard/Android/data/com.mihil.arenclosure/files/rule_checks.csv` and `obstacles.csv`.

**Known issues and limits**

- No vertical planes: obstacles are marked on the floor only. A ceiling (`vent_gap_top`) cannot be measured. That rule is skipped while it has no value.
- Reset clears the panel but keeps the obstacles, because they belong to the room. Use Clear obstacles to remove them.
- Phase B is not built yet: the toggle UI for `applies_if` conditions, entry fields for the vent gaps, confirming the `DOL_*` parts, and the 3.5 clearance zones.

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

**Step 2, floor lock. Status:** tested on the device. The lock, the band, the CSV, the settling gate and the 3.5 m limit are verified. The Re-seat button is not yet triggered on the device. Merged and tagged `task-2.5b`.

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

**Step 2b device test (2026-10-12, 23:23 to 23:32, `plane_diagnostics_v2.csv` plus 4 screenshots)**

- CSV: the v2 file was created with the new header, and the lock columns are filled in after placement. Verified.
- Band follows the current floor height: planes 68 cm below the floor and 1.19 m above it were marked `off-floor`. Plane `7133` went from `small` (0.24 m²) to `off-floor` (0.26 m²) as it grew, not to `ok`. Every drag hit was on the locked plane. Verified.
- Settling gate: at 23:29:40 to 43, eight placement taps were refused while three estimates sat 23 to 37 cm apart. Placement went ahead at 23:30:34 once they agreed near the tap. Verified.
- 3.5 m limit: taps at 3.59 to 4.96 m were refused. All 5 placements were at 3.45 m or less, with ratio 1.0000. Verified.
- Rotation pivot: in screenshot 3, a 32.4° turn moved the FBX pivot 0.457 m. That equals 2 × 0.82 m × sin(16.2°), the pivot's offset from the base centre, so the panel turned exactly about its base centre. Screenshot 2 also matches (13.6° gives 0.194 m).
- Re-seat on floor: **not exercised**. Drift peaked at -1.4 cm, below the 3 cm threshold, so the button never appeared.
- Only the locked floor is drawn: probably working, but not proven, since no other plane was in camera view.

**Finding: a plane below the floor.** Screenshot 4 shows plane `efb7` 68 cm below the locked floor, which is physically impossible on a solid floor. The likely cause is the glossy tiles: ARCore tracks reflected features that appear to lie below the surface. In every session, ARCore merged the lower plane into the upper one (20:16, and 23:31:27 where `…5950` merged into `…54F0`). So the false planes are most likely the lower, reflection-made ones, and the original "jump up" was probably a placement on a reflected plane followed by a drag onto the real floor. The floor lock covers this case. Record it as an environmental limitation: reflective floors.

**Fix: the "moved" readout after a rotate.** `ManipulationReport.MovedMetres` and `FinalPosition` now use the base centre (the point the twist pivots on) instead of the FBX pivot. A pure turn now reports 0 m moved. Checked in the Editor: a 32.4° turn about the base centre gives 0.457 m with the old method (matching screenshot 3) and 0.0000 m with the new one. Changed file: `Placement/PlacementManipulator.cs`.

**Status at merge:** merged and tagged `task-2.5b` with the Re-seat button not yet tested on the device. It will be checked during the tape test, by placing and then walking a slow lap of the room until drift passes 3 cm.

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

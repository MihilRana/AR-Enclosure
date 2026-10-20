# AR Enclosure

A mobile augmented reality app for Android (ARCore) that places a representative low voltage enclosure model in a real room at true scale, measures real distances, and checks the installation clearances around the enclosure against IEC 60364-7-729 with a rule engine. It was built as the artefact for a Coventry University BSc (Hons) Computing dissertation, delivered at NIBM.

The 3D model is a representative low voltage enclosure model, used with permission for academic purposes only (1.649 × 0.450 × 1.925 m, 155 parts).

## Features

- **Floor confirm.** The app offers one floor once it is stable. A green ring is dropped on it and stays fixed, so you can check the height by moving the phone sideways: on the real floor the ring stays on the same tile. Use this floor accepts it, Rescan skips it, and Move ring here re-drops the ring. Planes at other heights are shown as warnings (for example a likely reflection). After confirming, plane finding is switched off.
- **Placement at true scale.** The model is placed on the confirmed floor with its scale locked at 1.000. There is no pinch-to-scale.
- **Move and rotate.** One finger drags the enclosure across the floor, and a two-finger twist turns it about its base centre.
- **Measure mode.** Two-point distance and closed-loop perimeter, shown to 3 decimals.
- **Obstacle mode.** Walls and facing enclosures are marked as lines on the floor (tap both ends).
- **Rule engine.** Driven by `rules.json` and `model_profile.json`. No clearance value is written in code.
- **Inputs panel.** Switches for rule conditions, manufacturer ventilation gaps, and the ceiling height.
- **Verdict panel.** Shows COMPLIANT (IEC 60364-7-729 checks), NOT COMPLIANT or INCOMPLETE, with each rule's measured gap against its required value. Gap lines and a coloured strip follow the result.
- **Re-seat on floor.** If the floor estimate drifts by more than 3 cm, one button moves the enclosure back onto the floor.
- **Two-tap Reset.** Restarts the AR session, like reopening the app. A single stray tap does nothing.
- **CSV logging.** Every placement, measurement, obstacle and rule check is logged (see Data files).

## Requirements

- Unity 6000.3.18f1 LTS with Android Build Support (OpenJDK, Android SDK and NDK).
- Packages, resolved from `Packages/manifest.json`: AR Foundation 6.3.5, ARCore XR Plugin 6.3.5, Universal Render Pipeline 17.3.0, Input System 1.19.0, Newtonsoft JSON 3.2.2.
- Player settings (already set in the project): IL2CPP, ARM64, minimum API level 30, OpenGL ES 3 only, Input System package (new) only. ARCore is required.
- An ARCore-supported Android phone with Google Play Services for AR installed.
- Tested only on a Google Pixel 9a.

## How to open and build

1. Install Unity 6000.3.18f1 through Unity Hub, with Android Build Support.
2. Clone this repository.
3. In Unity Hub, choose Add project from disk and select the cloned folder.
4. Open the project and wait for the packages to resolve and import.
5. Open `Assets/Scenes/ARPlacement.unity`. It is the only scene in the build.
6. Open File > Build Profiles, select Android, and switch platform if asked.
7. Leave the player settings as they are (see Requirements).
8. Connect the phone by USB with USB debugging enabled.
9. Press Build And Run. The app installs as `com.mihil.arenclosure`.

Build note: if the build fails with "Unable to delete directory … libc++_shared.so", Gradle has hard-linked that file from the NDK in the Unity install, and the link carries read-only permissions. Delete only the project's copy, under `Library/Bee/Android/…/obj/arm64-v8a/`, from an administrator shell, then build again.

## How to use

1. Open the app and slowly sweep the phone over open floor.
2. After about 8 s, "Floor found" appears and a green ring is dropped on the floor.
3. Move the phone sideways. If the ring stays on the same tile, press Use this floor. If it slides, press Rescan.
4. Tap the floor, within 3.5 m, to place the enclosure. Its door side faces you.
5. Drag with one finger to move it, and twist with two fingers to turn it.
6. Tap the mode button for MEASURE. Tap two points for a distance, or switch to Perimeter, tap the corners and press Close loop.
7. Tap the mode button for OBSTACLE. Tap where a wall meets the floor, then tap again further along the wall (set Kind to Enclosure for a facing enclosure).
8. In OBSTACLE mode, open Inputs to switch conditions on, and to enter ventilation gaps and the ceiling height.
9. Read the verdict panel. Each rule shows its measured gap and its required value.
10. If the status says the floor estimate moved, press Re-seat on floor.
11. To start again, tap Reset twice within 2 s.
12. Pull the data files with `adb` (see below).

## Data files

All files are written to the app's storage folder on the phone. Pull them with:

```
adb pull /sdcard/Android/data/com.mihil.arenclosure/files/ ./results
```

| File | What it holds |
|---|---|
| `scale_verification.csv` | One row per placement: authored and placed size, height ratio, rig scale, camera distance, tracking warm-up, plane count. |
| `measurements.csv` | One row per measurement: run id (continues across app restarts), mode, points, distance in metres, camera distance, tracking state. |
| `obstacles.csv` | Every obstacle line added, undone, cleared or reset, with both end points and its length. |
| `rule_checks.csv` | One row per rule each time the result changes: verdict, rule id, status, measured and required metres, panel position and yaw, floor drift, conditions switched on, ceiling height. |
| `plane_diagnostics_v2.csv` | Raycast and plane log: which plane each tap used, all planes with height and area, the floor lock and its drift. |
| `clearance_inputs.json` | The saved Inputs panel values: conditions switched on, ventilation gaps and ceiling height. |

If a CSV's columns change between versions, the old file is renamed to `<name>_old_<timestamp>.csv` and a new file is started, so one file never mixes two layouts.

## Rules as data

The clearance rules live in `Assets/StreamingAssets/rules.json`, and the model's geometry facts in `Assets/StreamingAssets/model_profile.json`. Changing a value or adding a rule is a data change. No code changes are needed.

Each rule in `rules.json` has:

- an `id` and a readable `label`;
- the `face` it applies to (front, back, side or top);
- `min_m`, the required gap in metres (null means not set);
- `value_source`:
  - `standard` for a value from the standard;
  - `model_door_geometry` for a value read from the model;
  - `user_input` for a value the user enters;
- `level`: `required` can fail, `advisory` only warns;
- `applies_if` conditions that must be switched on before the rule runs;
- the `source` clause.

`model_profile.json` gives the model's front (door side) axis, the name of its door group, and the minimum width for a part to count as a door leaf.

The values used:

- **Front gangway (0.700 m)** and **handle clearance (0.600 m):** from IEC 60364-7-729, clause 729.513.2.1, the case where live parts are inside enclosures. They are the minimum free space in front of the enclosure, and in front of its handles, up to a wall or a facing enclosure.
- **Door swing:** the required gap is the widest door leaf, measured from the model's door geometry at run time (0.587 m for this model).
- **Door fixed-open passing (0.500 m, Annex A):** runs only when the user switches on a closed restricted area with doors that lock open.
- **End wall access (0.700 m, advisory, 729.513.2.3):** runs only when the user confirms the gangway is longer than 10 m.
- **Ventilation gaps (back, side, top):** the standard gives no number, so these are user inputs taken from the manufacturer's installation instructions. A gap left empty is skipped and reported as "not entered". The top gap is the ceiling height entered by the user minus the enclosure's height.

Gaps are measured horizontally, from the face of the enclosure's oriented box to the nearest obstacle line within that face's width. The verdict is:

- NOT COMPLIANT if any applicable required rule fails;
- otherwise INCOMPLETE if any required rule has no result;
- otherwise COMPLIANT (IEC 60364-7-729 checks).

## Design decisions and scope

- **Scale lock.** True scale is the first objective, so the model's scale stays at 1.000 and the user cannot resize it. A resized model would silently invalidate every measurement and clearance check.
- **Rules as data.** All clearance values come from `rules.json`, so the standard's values can be checked and changed without touching the code.
- **World anchor.** The enclosure uses a plain AR anchor that is not attached to a plane, and it is re-anchored after each move. Plane merging cannot drag the model, but floor drift can leave it slightly above or below the floor (see Re-seat).
- **Floor lock.** After the floor is confirmed, every tap (place, drag, measure, obstacle) uses a flat plane at the locked floor's current height. False and reflected planes cannot take a tap.
- **Vertical plane detection off.** Obstacles are marked as lines on the floor, where the wall or facing enclosure meets it. This is reliable on plain walls and avoids extra planes.
- **Not used:** ARCore Depth API, occlusion, light estimation and image classification. These were scoping decisions.
- **Responsibilities.** ARCore provides motion tracking and plane detection. This project is the application layer: floor confirmation and locking, true-scale placement, measurement, obstacle marking, the rule engine and logging.

## Known limitations

- **Reflective floors.** Glossy tiles can create planes below the real floor, from mirrored features. The floor ring check and the floor lock reduce this, but the user still has to confirm the right floor.
- **Floor drift.** ARCore can re-estimate the floor by several centimetres after the floor is locked. The app warns, and Re-seat on floor corrects the model's height. The horizontal gaps are not affected.
- **Horizontal-only measuring.** After the floor is locked, measurements and obstacle points are taken on the floor only. Walls and the ceiling are not measured; the ceiling height is entered by hand.
- **Front envelope.** Handle and enclosure gaps are both measured from the model's front envelope, which includes the handles.
- **No occlusion.** Virtual content is drawn over real objects.
- **Single test device.** Tested only on a Google Pixel 9a.
- **Phone heating.** Long AR sessions warm the phone, which can reduce tracking quality. Keep sessions short.
- **National deviations not applied.** Only the base IEC values are used.

## Version history

| Tag | Summary |
|---|---|
| `task-2.2` | True-scale placement verified: height ratio 1.0000 over repeated placements. |
| `task-2.4` | One-finger drag and two-finger twist with the scale locked; re-anchors on release. |
| `task-2.5` | Measurement tool: two-point distance and perimeter, logged with a persistent run id. |
| `task-2.5b` | Floor lock against false and reflected planes; settling gate, 3.5 m limit, Re-seat on floor. |
| `task-3.2b` | Floor confirm with a fixed ring check; plane finding off after confirm; taps on the locked floor. |
| `task-3.4a` | Obstacle lines and a rule engine from `rules.json`, with front-face verdicts. |
| `task-3.4b` | Conditions as data, ventilation gaps and ceiling height with saved inputs, readable labels. |
| `task-3.5` | Reset restarts the AR session; readable rule names in the verdict panel. |
| `task-ui-polish` | Visual polish: one palette, rounded panels, button roles, verdict strip, two-tap Reset. |
| `user-study-freeze` | The frozen build used for the user study. |
| `v1.0-submission` | Submitted artefact: the frozen user-study build plus this README. |

## Note on AI assistance

This project was developed with AI assistance (Claude Code), which the university permitted. The author tested every build on the device and made the final decisions.

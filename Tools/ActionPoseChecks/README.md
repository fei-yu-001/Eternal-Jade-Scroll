# Action pose size regression checks

2026-11-23 update: Qingshi's six new standing views have individually remeasured alpha registrations. The eight-frame walk cycles deliberately include knee bend and body rise/fall. `approved-walk-bounds.json` pins every reviewed frame's exact alpha top/sole; replacing a pose requires explicit review and registration. An additional 3% standing-height envelope limits body rise/fall, while the existing 0.7-unit sole check remains. Attack height/sole calculations keep their 0.0002-unit limit. Final offline result: **508 assertions / 108 layouts / 6 stands / 48 walk frames / 6 strike frames PASS**. Current Unity action regression: **48 cases / 16,491 assertions / 48 rendered boards / 0 failures**. Evidence and player-facing changes: `Documents/账号菜单与行走改进-2026-11-23.md`. The November 20 results below remain historical.

2026-11-20: skill size correction, using existing artwork only.

## Root causes and correction

- `ActionCombatPose` overwrote the owner root scale with `(1,1,1)` during attacks/dodges and reduced motion. It now uses `PuppetActor.BaseScale`, including tier changes during an action.
- The town's height 170 is a texture rectangle height. Alpha >= 32 in the six approved Nov7 standing views spans only 158.38–161.04 units. The old attack rectangle `170 * 576 / 450` displayed 168.87–171.51 units: a 4.86%–8.05% increase even at tier 1.0.
- `ActionPoseGeometry` registers each standing view and each attack phase against the actual source alpha bounds. Attack size and pivot preserve the current standing view's visible height and sole position. This includes the male front view's four-pixel transparent bottom margin. The image pivot remains at the actor's supplied ground point under both UV directions.
- Dodge keeps its walking-sheet rectangle and bottom pivot, with the existing crouch deformation. Unregistered fallback art retains the legacy full-rectangle convention; it is not newly calibrated.
- Runtime textures need not be readable. Registration drift is checked against PNGs offline and by the editor fixture; replacing these assets requires rerunning the checks.

## Offline checks

Run from the repository root:

```powershell
python Tools/ActionPoseChecks/measure_assets.py Tools/ActionPoseChecks/obj/asset-measurements.json
dotnet run --project Tools/ActionPoseChecks/ActionPoseChecks.csproj -- Tools/ActionPoseChecks/obj/asset-measurements.json
dotnet build Tools/UnityCompileChecks/UnityCompileChecks.csproj --no-restore -v minimal
```

The measured input comes from `town-character-art.json`: both genders, all three source views, 48 walk frames and both three-phase attack sheets. The .NET check executes the production geometry helper for rectangle heights 170 (town) and 336 (combat) and tiers 0.8/1/1.25. This is independent source-alpha evidence, not a copy of the runtime registration table.

Observed locally: 340 assertions / 108 layouts passed; corrected body-height and sole error < 0.0002 units. The Unity compilation gate passed with 0 warnings / 0 errors. The architect subsequently executed the Unity fixture successfully; the actual editor/rendering result is recorded below.

## Unity editor fixture (architect runs in an independent Unity 6 copy)

Execute method: `TwelveJade.Editor.ActionPoseSizeChecks.Run`

Menu: `Tools > Twelve Jade > Check action pose sizes`.

Use a graphics-enabled editor/batch process, without `-nographics`. This fixture creates an editor preview scene, explicitly moves its hidden camera/canvas roots into it, and binds the camera to that scene. It never changes the active user scene or saves, closes, discards or reopens a user scene. This also works with an untitled unsaved startup scene, which rejects `NewScene(Additive)`. Initialization is inside the error-reporting scope; partial setup is cleaned up in independent steps, including restoring the active render target and closing only this fixture's preview scene. Cleanup asserts that user scene handles/load states, dirty flags, root objects and the active scene are preserved, and that no preview scene is leaked. Rendering uses an ordinary game camera restricted to the preview scene, with an explicit 1200×320 world-space UI canvas (one unit per output pixel). The fixture rebuilds the real UGUI geometry/materials and mesh modifiers before `Camera.Render`, checks projected canvas/graphic bounds and submitted meshes, and retains the >300 foreground-pixel assertion in every character column. It invokes the real actor/pose update and lifecycle methods explicitly in edit mode, so no Play-mode timer or game save is required.

Coverage: 48 gender × direction (front/right/back/left) × size-tier × reduced-motion cases; light/heavy startup/active/recovery; dodge phases/progress and mesh foot anchoring; idle/hitstun/death restoration; live size changes; view switches while renderers are hidden; disable/destroy restoration; phase UVs and mirroring. Visible bounds are measured from readable temporary copies of the real PNGs and transformed through the actual RectTransforms. Source texture importers are untouched.

Outputs: `<Unity project>/Temp/ActionPoseSizeChecks/report.txt`, 48 PNG boards (standing, light startup, light active, light recovery, active dodge), and matching `.render.txt` diagnostics (projection, mesh/material submission, and foreground counts). A failed framebuffer is saved before the foreground assertion, so an empty capture remains inspectable without passing the check. `EJS_ACTION_POSE_OUTPUT` can select another local output directory. Each rendered column must contain visible foreground; numeric size, sole and scale checks are separate. Batch mode exits 0 on success or 1 on failure. Screenshots and generated measurements stay local.

On 2026-11-20, the architect's third graphics-enabled Unity 6000.3.25f1 batch run in the independent `.batch/DesktopMotion` project passed:

```text
User scene preservation PASS; no scene saved, discarded, reopened or replaced.
PASS cases=48 assertions=16491 renderedBoards=48 failures=0
```

The assistant checked the actual report at `Tools/logs/desktop-review-20261120/action-pose-unity/report.txt` and confirmed all 48 PNG boards and 48 corresponding `.render.txt` diagnostics exist. The Unity log is `Tools/logs/desktop-review-20261120/action-pose-unity.log`. These are real component/resource/rendering results, including the retained foreground-pixel checks, all size/direction/reduced-motion cases, action/restoration paths, and user-scene preservation. Examples of the measured standing baseline are male/front 128.0313 / 160.0391 / 200.0488 units and female/side 128.8281 / 161.0352 / 201.2939 units for tiers 0.8 / 1 / 1.25; each matching attack passed the height/sole comparisons.

The first two runs exposed fixture setup/rendering failures (additive creation with an untitled unsaved scene, then insufficient foreground from the preview-camera screen-space UI path). The third run validates the preview-scene isolation and explicit camera/world-space-canvas rendering fixes while retaining the pixel assertions.

The architect has visually reviewed male/front tier 0.8, male/right tier 1.0 and female/left tier 1.25 boards (including reduced motion), confirming aligned visible heights and soles; motion during normal gameplay and phone verification are not established by this edit-mode fixture. Screenshots and logs stay local. The assistant did not launch Unity or commit/push changes. Updating this result required no rerun of compilation or tests.

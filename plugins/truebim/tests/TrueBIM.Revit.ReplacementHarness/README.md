# Family replacement regression, Revit 2022

Run `plugins/truebim/scripts/test-family-replacement-2022.ps1` from the repository root with Revit closed.

`-AllowExistingRevit` explicitly permits launching a separate Viewer alongside a running Revit. The script still terminates only its own process. Use `-ShowViewer -TimeoutSeconds 300` to show the Viewer and allow time for manually approving the test add-in; the default launch is hidden.

The harness compiles the production replacement sources into an isolated test add-in and opens the installed metric project template in Revit Viewer. It creates two simple test families (Fire Alarm / Nurse Call), reference walls and test instances in memory. It never saves the template, families or model. The script removes its temporary add-in manifest and terminates only the viewer process it started.

Four scenarios distinguish canvas state from operation scope:

1. `SingleSelected`: original service called with one selected instance; reproduces additional cascade-deletion IDs and rollback.
2. `MultipleSelected`: same service, two selected instances; both replace.
3. `SingleUnselected`: same service, one ID with canvas selection cleared; replacement succeeds.
4. `SingleSelectionFixed`: the UI runner clears and refreshes canvas selection before calling the service; one selected instance replaces successfully.

Additional scenarios use asymmetric geometry with different source/target origins:

5. `RotatedInsertion`: three different angles, insertion points and full frames preserved.
6. `MirroredCenter`: three mirrored and rotated instances, geometry centers and full frames preserved.
7. `OverlapStrict`: replacement would duplicate an existing target; warning rolls back replacement.
8. `OverlapIgnored`: the same duplication is accepted with the option enabled; warning appears in the result.
9. `CommitMovementRollback`: an isolated test updater moves/rotates the pinned target during commit; the original pinned source must be restored.
10. `MixedWarningsStrict` and `MixedWarningsIgnored`: an overlap warning plus an unrelated warning must roll back with either option.

For each replacement the harness also compares the surviving source/new element with its pose captured before the call. A skipped replacement must leave the original pose intact.

The script checks all results and the original error. JSON reports include original/new IDs and dependency diagnostics. The selection coordinator also has unit tests for restoring replacement IDs, skipped sources and unrelated selected elements, failure cleanup, empty selection and restoration errors.

The first invocation may require approval of the test add-in by Revit. The script does not dismiss security prompts. Missing runtime initialization or a failed scenario makes the test fail; build success alone is not a runtime pass.

# Family replacement: selection regression, Revit 2022

Run `plugins/truebim/scripts/test-family-replacement-2022.ps1` from the repository root with Revit closed.

The harness compiles the production replacement sources into an isolated test add-in and opens the installed metric project template in Revit Viewer. It creates two simple test families (Fire Alarm / Nurse Call), reference walls and test instances in memory. It never saves the template, families or model. The script removes its temporary add-in manifest and terminates only the viewer process it started.

Four scenarios distinguish canvas state from operation scope:

1. `SingleSelected`: original service called with one selected instance; reproduces additional cascade-deletion IDs and rollback.
2. `MultipleSelected`: same service, two selected instances; both replace.
3. `SingleUnselected`: same service, one ID with canvas selection cleared; replacement succeeds.
4. `SingleSelectionFixed`: the UI runner clears and refreshes canvas selection before calling the unchanged service; one selected instance replaces successfully.

The script checks all four results and the original error. JSON reports include original/new IDs and dependency diagnostics. The selection coordinator also has unit tests for restoring replacement IDs, skipped sources and unrelated selected elements, failure cleanup, empty selection and restoration errors.

The first invocation may require approval of the test add-in by Revit. The script does not dismiss security prompts. Missing runtime initialization or a failed scenario makes the test fail; build success alone is not a runtime pass.

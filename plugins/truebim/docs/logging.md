# TrueBIM Logging

TrueBIM writes a local current-user log file for troubleshooting.

## Location

The log file is stored at:

```text
%APPDATA%\TrueBIM\Logs\truebim.log
```

Example:

```text
C:\Users\<User>\AppData\Roaming\TrueBIM\Logs\truebim.log
```

## Opening Logs

Open TrueBIM from the Revit ribbon, then click `Logs` in the launcher window.

TrueBIM will create the log folder and `truebim.log` file if needed, then open the file in the default Windows text editor.

## What To Share

When reporting an issue, send the `truebim.log` file from the path above.

The log should contain TrueBIM launcher/module diagnostics and exception details. It should not contain model contents, Revit file paths, or cloud telemetry.

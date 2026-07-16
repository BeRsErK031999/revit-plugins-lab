# IsoField CLI worker contract

This document describes how TrueBIM calls an external IsoField recognition worker.
The worker may use Python, OpenCV, Tesseract, ONNX, or any other heavy dependency
outside the Revit process. TrueBIM itself only starts the configured process,
waits for completion, and validates the output JSON.

## Configuration

The CLI worker is disabled by default. When it is disabled, TrueBIM uses
`StubIsoFieldRecognitionRunner`.

Set these environment variables before starting Revit:

| Variable | Required | Description |
| --- | --- | --- |
| `TRUEBIM_ISOFIELD_WORKER` | yes | Full path to the executable that starts the worker. For Python scripts, point this to `python.exe`. |
| `TRUEBIM_ISOFIELD_WORKER_ARGS` | no | Argument template. Defaults to `--request "{request}" --output "{output}"`. |
| `TRUEBIM_ISOFIELD_WORKER_TIMEOUT_SECONDS` | no | Positive timeout in seconds. Defaults to `30`. |

The argument template supports these tokens:

| Token | Description |
| --- | --- |
| `{request}` | Temporary request JSON path. |
| `{source}` | Selected source file path. |
| `{output}` | Temporary recognition result JSON path that the worker must create. |

Example for a Python script:

```powershell
$env:TRUEBIM_ISOFIELD_WORKER = "C:\Python312\python.exe"
$env:TRUEBIM_ISOFIELD_WORKER_ARGS = "`"C:\TrueBIMWorkers\isofield_worker.py`" --request `"{request}`" --output `"{output}`""
$env:TRUEBIM_ISOFIELD_WORKER_TIMEOUT_SECONDS = "45"
```

## Invocation

For each source image TrueBIM creates a temporary working directory under
`%TEMP%\TrueBIM\IsoFieldRebar\...`.

For a validated four-file source set the worker is called sequentially in the
order `As1X`, `As2X`, `As3Y`, `As4Y`. The worker still receives one image per
invocation and does not need to implement source-set grouping. TrueBIM assigns
the layer role while merging the four results.

TrueBIM writes `request.json`:

```json
{
  "schemaVersion": "1.0",
  "sourcePath": "C:\\path\\to\\source.png",
  "outputPath": "C:\\Users\\...\\Temp\\TrueBIM\\IsoFieldRebar\\...\\recognition-result.json"
}
```

Then TrueBIM starts the configured executable with the resolved argument
template. The process runs without shell execution and without a visible console
window.

## Output

The worker must exit with code `0` and create the output file passed through the
`{output}` token or `request.outputPath`.

The output must follow `docs/IsoFieldRebar/recognition-result-contract.md`.
Current required fields:

```json
{
  "schemaVersion": "1.0",
  "polylines": [
    {
      "id": "zone-a",
      "zoneName": "Zone A",
      "confidence": 0.9,
      "points": [
        { "x": 10.0, "y": 20.0 },
        { "x": 30.0, "y": 40.0 }
      ]
    }
  ],
  "diagnostics": []
}
```

TrueBIM validates the output through `IsoFieldJsonReader`. Invalid JSON,
unsupported `schemaVersion`, missing `polylines`, malformed points, non-zero exit
codes, missing output files, and timeout failures are treated as recognition
errors.

## Safety Boundaries

- The worker run does not create or modify Revit elements.
- Heavy recognition dependencies stay outside the Revit process.
- Temporary request/output files are deleted after the run.
- Model preview and test rebar creation remain separate explicit user actions.

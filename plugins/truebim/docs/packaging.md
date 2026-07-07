# TrueBIM Packaging

TrueBIM release packaging builds one installer with separate Revit-version payloads.

## Version Matrix

- Revit 2019-2024: `net48`, compiled against the matching `RevitAPI.dll` and `RevitAPIUI.dll`.
- Revit 2025: `net8.0-windows`, compiled against Revit 2025 API assemblies.

The project validates these pairings in `TrueBIM.App.csproj`, so a Revit 2025 `net48` build or a Revit 2019-2024 `net8.0-windows` build fails before references are resolved.

## Build Installer

From the repository root:

```powershell
.\plugins\truebim\scripts\build-installer.ps1
```

The script:

- clears `dist`;
- builds Revit 2019-2024 payloads with `net48`;
- builds the Revit 2025 payload with `net8.0-windows`;
- writes versioned outputs to `dist/revit/<year>`;
- generates one `TrueBIM.addin` per year;
- copies documentation Markdown and `plugins/truebim/docs/assets` into each versioned `Docs` payload;
- validates manifest XML and per-year `Assembly` paths;
- fails if `RevitAPI.dll` or `RevitAPIUI.dll` are copied into the installer payload;
- compiles `plugins/truebim/installer/TrueBIM.iss` with Inno Setup.

Installer output:

```text
dist/installer/TrueBIM-Setup.exe
```

Example payload layout:

```text
dist/
  installer/
    TrueBIM-Setup.exe
  revit/
    2019/
      TrueBIM.App.dll
      TrueBIM.addin
      Modules/
      Assets/
      Docs/
        assets/
    2025/
      TrueBIM.App.dll
      TrueBIM.App.deps.json
      TrueBIM.addin
      Modules/
      Assets/
      Docs/
        assets/
```

## Installer Layout

The release installer is current-user and does not require admin privileges.

Installed payload:

```text
%APPDATA%\TrueBIM\<year>\TrueBIM.App.dll
%APPDATA%\TrueBIM\<year>\Modules\...
```

Installed Revit manifests:

```text
%APPDATA%\Autodesk\Revit\Addins\<year>\TrueBIM.addin
```

The installer defaults to detected Revit versions only. Selecting a version that is not detected requires explicit confirmation. Upgrade cleanup removes install-owned payload folders and `.addin` files for the selected years before copying the new package.

## Installed File Validation

After installing the setup manually or silently, run:

```powershell
.\plugins\truebim\scripts\test-installation.ps1
```

The script reports, per Revit year:

- whether Revit is detected on the PC;
- whether the installed payload DLL exists;
- whether the `.addin` manifest exists;
- whether the manifest XML is valid;
- whether the manifest `Assembly` path exists;
- whether the Revit 2025 `.deps.json` file exists;
- whether a runtime smoke-test was explicitly marked.

Runtime smoke-tests are not automated by this script. Pass `-SmokeTestedYears 2022,2025` only after those Revit versions were actually launched and the add-in loaded successfully.

## Local Development Deploy

The existing `deploy-local-2022.ps1` and `deploy-local-2025.ps1` scripts remain current-user development helpers. They install under `%APPDATA%` and are separate from the release installer.

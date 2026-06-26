# Revit Plugins Lab

Workspace for Revit add-in experiments and future product work.

## Current structure

- `Product01_TZ_pending/` - first product folder, waiting for the technical specification.

## Local environment snapshot

- Revit installed: 2017, 2018, 2019, 2020, 2021, 2022, 2023, 2024, 2025.
- Git installed.
- Visual Studio Build Tools 2022 installed.
- VS Code installed.
- .NET runtimes installed, including .NET 8 and .NET 9.
- .NET SDK is not currently visible through `dotnet --list-sdks`.

## Initial recommendation

For Revit 2025 development, install the .NET 8 SDK and build add-ins as `net8.0-windows`.
For Revit 2024 and older, prepare a separate target/build path for .NET Framework 4.8.

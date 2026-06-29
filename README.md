# Revit Plugins Lab

Workspace for Revit add-in experiments, Dynamo automation, and future product work.

## Repository structure

- `docs/` - environment notes, Revit API notes, and development decisions.
- `plugins/` - standalone Revit add-in products.
- `templates/` - reusable project templates and scaffolding notes.
- `dynamo/` - Dynamo graphs, Python scripts, and Dynamo-specific notes.

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

## First product

The first real product workspace is located at `plugins/truebim/`.

TrueBIM is a modular Revit add-in platform. The first module is sheet numbering.

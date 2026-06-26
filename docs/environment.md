# Environment

## Installed locally

- Revit 2017
- Revit 2018
- Revit 2019
- Revit 2020
- Revit 2021
- Revit 2022
- Revit 2023
- Revit 2024
- Revit 2025
- Git
- VS Code
- Visual Studio Build Tools 2022
- MSBuild
- .NET runtimes 8 and 9

## Missing or recommended

- .NET 8 SDK for Revit 2025 add-in development.
- Revit SDK samples for reference implementations and API examples.
- RevitLookup for inspecting live Revit elements and parameters.

## Version strategy

- Revit 2025: target `net8.0-windows`.
- Revit 2024 and older: target .NET Framework 4.8 in a separate build path.
- Keep each plugin isolated so it can later move into its own repository if it becomes a standalone product.

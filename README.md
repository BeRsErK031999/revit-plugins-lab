# Revit & AutoCAD Plugins Lab

Workspace for Revit add-ins, AutoCAD plugins, Dynamo automation, and future product work.

**Для разработчиков и ИИ-агентов** — [как работать вдвоём, проверять плагины и отправлять изменения](CONTRIBUTING.md).

## Repository structure

- `docs/` - environment notes, Revit API notes, and development decisions.
- `plugins/` - standalone Revit add-in products.
- [`autocad/`](autocad/README.md) - AutoCAD plugins and automation, with separate build outputs, tests, and working instructions. Includes Model Sheet Publisher for AutoCAD 2021–2026.
- `templates/` - reusable project templates and scaffolding notes.
- `dynamo/` - Dynamo graphs, Python scripts, and Dynamo-specific notes.

## Revit products

- [TrueBIM](plugins/truebim/README.md) — modular Revit add-in, supported releases, and build instructions.
- [Revit add-in products](plugins/README.md) — standalone add-ins and recommended project layout.

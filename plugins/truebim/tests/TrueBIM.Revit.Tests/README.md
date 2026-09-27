# TrueBIM Revit family contract tests

These checks complement the fast unit tests with contracts that must be read
through the real Revit API. The default local route uses a small purpose-built
add-in harness so it also works on corporate networks where Autodesk sign-in is
unavailable. An optional NUnit/RevitTest route is retained for signed-in test
machines.

## IsoField family contract

Run from the repository root:

```powershell
& .\plugins\truebim\scripts\test-revit-2025.ps1
```

Use `-FamilyDirectory` when the supplied `.rfa` files are stored elsewhere.
The test opens every family read-only and writes
`plugins/truebim/test-results/revit-2025/isofield-family-contract.json` with
the family names, types, parameters, storage types and formulas. It also checks
that `(Массив • У) Арматура • 000` exposes the parameters required by the
placement code:

- instance length `A`;
- instance array width `Зона • Ширина`;
- type diameter `• Деталь • Арматура. Диаметр`;
- type spacing `• Деталь • Шаг элементов`.

The test starts Revit in read-only Viewer Mode because it only inspects family
contracts and never saves model changes. Autodesk sign-in is not required. On
the first run Revit can show an unsigned-add-in warning for `TrueBIM Family
Contract Harness`; verify that the displayed assembly path is inside this
repository and choose `Load Once`. Revit can also show its Viewer Mode
information dialog. After it is closed, the inspection is unattended. The
script closes only the Revit process it started and removes its unique temporary
manifest in a `finally` block.

For the optional signed-in NUnit route, run:

```powershell
& .\plugins\truebim\scripts\test-revit-adapter-2025.ps1
```

That route uses `ricaun.RevitTest.TestAdapter` and requires an Autodesk user
already signed in to Revit. Its reports are kept separately under
`plugins/truebim/test-results/revit-2025-adapter/`.

Build products are ignored by the repository. Test reports are written below
the existing ignored `plugins/truebim/test-results/` directory.

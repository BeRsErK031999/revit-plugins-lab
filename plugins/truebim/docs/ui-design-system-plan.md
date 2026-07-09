# TrueBIM UI design system plan

## Context

Most TrueBIM WPF windows are built programmatically in `.cs` files. The current UI works, but each module repeats its own colors, margins, button helpers, status text, table settings, and card borders. This makes small visual fixes expensive and makes the plugin look like a set of separate tools instead of one product.

The design system must stay local to TrueBIM windows. It must not register global `Application.Current` resources, replace Revit window chrome, or change modeless Revit API behavior.

## UI audit

- Repeated button helpers exist in `PrintWindow`, `SheetNumberingWindow`, `FamilyManagerWindow`, `ScheduleImportWindow`, `AutoTagWindow`, `TitleBlockFillWindow`, `ColorByParameterWindow`, `ViewVisibilityWindow`, and placeholder BIM tool windows. Heights vary between 26, 28, 30, and 32.
- Local `new Thickness(...)`, `Height`, `MinWidth`, `FontSize`, `Margin`, and `Padding` values are spread through nearly every window. `PrintWindow`, `ParaManagerWindow`, `FamilyManagerWindow`, `JoinCutWindow.xaml`, `IsoFieldRebarWindow`, and `ViewVisibilityWindow` have the densest layout-specific values.
- Local colors are hardcoded through `Brushes.*`, `Color.FromRgb(...)`, and XAML hex values. The highest-impact files are `IsoFieldRebarGuideWindow`, `ViewVisibilityWindow`, `VoltageDropWindow`, `ParaManagerWindow`, `FamilyManagerCompactPaneControl`, `OpeningViewsGuideWindow`, and `JoinCutWindow.xaml`.
- DataGrid configuration is repeated in print, sheet numbering, schedule import, family manager, auto tags, title block fill, clash report, parameter manager, and join/cut flows. Row height, header style, selection visuals, and checkbox columns are not centralized.
- Header/content/footer structure is inconsistent. Some windows use `DockPanel`, some use fixed-row `Grid`, some have status text above the footer, and some scroll the whole content area.
- Cards and bordered panels are usually hand-built with `BorderBrush = Brushes.LightGray`, `Background = Brushes.White`, or module-specific pale backgrounds. Corner radius and padding are inconsistent.
- Status and empty states are mostly plain `TextBlock` text. Some modules still use `TaskDialog` for non-critical feedback, while others use inline status lines.
- `JoinCutWindow.xaml` is the only substantial XAML window and has its own local button, border, and field sizing, so it should be converted after the code-created windows have stable shared resources.
- Ribbon grouping already exists, but panel names and command density should be reviewed separately before moving commands into new pulldowns.

## Design direction

Create a lightweight TrueBIM design system on top of native WPF:

- semantic colors and brushes for brand, surfaces, borders, text, disabled, and status severities;
- spacing, radius, control height, footer height, and icon size tokens;
- programmatic styles for Button, TextBox, ComboBox, CheckBox, DataGrid, GroupBox, TabControl, ListBox, and ListBoxItem;
- small UI factories for headers, command bars, sticky footers, section cards, status badges, info banners, search boxes, field labels, settings rows, and action buttons;
- a base `TrueBimWindowChrome` that applies only local window settings and local resource keys.

## Staged implementation

1. Done in `c03b876`: add `UI/DesignSystem`, wire local theme resources through `TrueBimWindow`, migrate `ModuleLauncherWindow` as the first low-risk consumer, and expand `IconFactory` for shared UI states.
2. Done in `4a5f2b9`: apply shared shell, button, table, footer, and status helpers to `PrintWindow`.
3. Done: repeat the same pattern for `SheetNumberingWindow` and `ViewVisibilityWindow`.
4. Done: apply the same patterns to `ColorByParameterWindow`, `FamilyManagerWindow`, and `FamilyManagerCompactPaneControl`.
5. In progress: move `VoltageDropWindow`, `IsoFieldRebarWindow`, and `IsoFieldRebarGuideWindow` from local palettes to theme tokens; `VoltageDropWindow` is migrated, IsoField windows remain next.
6. Pending: convert `JoinCutWindow.xaml` to use local `TrueBimWindow` resources and shared style keys.
7. Pending: review ribbon grouping after the common window layer is stable.

## Current readiness

- Design system foundation: done.
- Local `TrueBimWindow` resource/chrome integration: done.
- First migrated consumer, `ModuleLauncherWindow`: done.
- `PrintWindow` migration: done in `4a5f2b9`.
- `SheetNumberingWindow` migration: done.
- `ViewVisibilityWindow` migration: done.
- `ColorByParameterWindow` migration: done.
- `FamilyManagerWindow` migration: done.
- `FamilyManagerCompactPaneControl` migration: done.
- `VoltageDropWindow` migration: done.
- Next production windows: `IsoFieldRebarWindow` and `IsoFieldRebarGuideWindow`.
- Third-party library adoption: intentionally not started.
- Manual Revit UI smoke: pending because it requires local deploy/restart conditions.

## Third-party libraries

Do not add a third-party UI dependency in the first pass. ModernWpf, WPF UI, MaterialDesignInXamlToolkit, and MahApps.Metro need a separate compatibility review for `net48`, `net8.0-windows`, Revit host resource isolation, dependency size, and license impact before any package reference is added.

## Quality gates

- Keep all design resources under `src/TrueBIM.App/UI/DesignSystem`.
- Do not add global WPF resources to `Application.Current`.
- Preserve existing command handlers and Revit API entry points.
- Keep each migration slice buildable and reviewable.
- Use existing WPF primitives and `IconFactory` images instead of PNG assets.

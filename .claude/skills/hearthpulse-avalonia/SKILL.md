---
name: hearthpulse-avalonia
description: Use when Avalonia 12 framework mechanics decide the outcome in Hearthpulse.UI or Hearthpulse.Desktop - a compiled binding or x:DataType build error, a binding that broke in one UI after a shared view-model change, StyledProperty vs DirectProperty on a control, a control template, UI-thread dispatch, TopLevel services (file picker, clipboard, launcher), or an 11-to-12 API difference. Not for how a page or panel should look or be laid out (hearthpulse-main-page, hearthpulse-overlay-panel, hearthpulse-design). Triggers - "Avalonia", "axaml", "x:DataType", "compiled binding", "привязка не работает", "биндинг сломался", "StyledProperty", "Dispatcher", "не в том потоке".
---

> Source: [codewithmukesh/dotnet-claude-kit](https://github.com/codewithmukesh/dotnet-claude-kit) `skills/avalonia/SKILL.md` at
> `6de62a5`, MIT, © 2025 Mukesh Murugan, see [THIRD-PARTY-NOTICES](../THIRD-PARTY-NOTICES.md). Hearthpulse changes:
> kept the Avalonia 12 binding, property, template, threading and `TopLevel` rules; replaced the sample app with this
> repository's composition root, shared view models and two-UI checks; dropped Generic Host, DI, `ViewLocator`,
> CommunityToolkit.Mvvm, Native AOT, packaging and Velopack.

# Avalonia 12 mechanics in Hearthpulse

Framework rules for `src/Hearthpulse.UI` (views, controls) and `src/Hearthpulse.Desktop` (host, windows, per-OS
overlay). What a surface contains and how it looks: [hearthpulse-main-page](../hearthpulse-main-page/SKILL.md),
[hearthpulse-overlay-panel](../hearthpulse-overlay-panel/SKILL.md), [hearthpulse-design](../hearthpulse-design/SKILL.md).

## What this project fixes

- **Avalonia 12.1.4**, packages `Avalonia`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter` in the UI project;
  `Avalonia.Desktop` and `Avalonia.Headless` in the host. A new package is a dependency decision for the task card,
  never a side effect of following a sample.
- **Composition root**: `Hearthpulse.UI/AppHost.cs` builds the services by hand and `Hearthpulse.Desktop/DesktopApp.cs`
  owns the windows. Keep it that way: construct the object in `AppHost` and pass it down. Views are created in XAML
  or code-behind, and typed `DataTemplate`s live in the view that uses them.
- **View models are shared source.** `MainViewModel*`, `OverlayViewModel*`, `Observable` compile from
  `src/Hearthpulse.Presentation` into this assembly under `HEARTHPULSE_AVALONIA` and into the WPF app under
  `HEARTHPULSE_WPF`. A property renamed there changes both UIs at once. `RelayCommand` is local to each UI.
  Boundary: [the presentation map](../../../src/Hearthpulse.Presentation/README.md).
- **Compiled bindings are on** in both Avalonia projects (`AvaloniaUseCompiledBindingsByDefault`). Neither
  project sets Native AOT or trimming, so AOT-only advice from other sources does not apply here.

## Bindings

- Every view root and every `DataTemplate` declares `x:DataType`: a template changes the data context, so it names
  its own type. A path that does not exist on that type is a **build error** in Avalonia.
- The same path in the WPF `.xaml` is resolved at run time and fails **silently** (an empty field, a line in the
  debug output). So a binding change is finished only after both sides are checked, see "A binding broke" below.
- `{ReflectionBinding}` and `x:CompileBindings="False"` are absent from the views; keep them absent. A path that is
  only known at run time is bound in code-behind, as `Overlay/CardPoolPanel.axaml.cs` does once with `new Binding(path)`
  (in Avalonia 12 the C# `Binding` class always means a reflection binding). For a path known at compile time use
  `CompiledBinding.Create((Vm vm) => vm.Property)`.
- Texts come through the `{i:Loc Key}` markup extension, not through bound string properties; the binding check for
  it is the Headless suite (`LocBindingTests*.cs`).
- 12 renames that bite when copying older samples: `TextBox.Watermark` is `PlaceholderText`; `OpenFileDialog` and
  `SaveFileDialog` are gone; the visual root is no longer a `TopLevel` to cast to.

### A binding broke (one UI or both)

1. Name the member and where it lives: shared (`src/Hearthpulse.Presentation`) or a local platform partial.
2. Avalonia: build `src/Hearthpulse.Desktop`; the compiler names the view and the path. Search the code-behind too:
   a path passed to `new Binding(...)` is a string the compiler does not check.
3. WPF: search the `.xaml` files of `src/Hearthpulse.App` for the old path (`Binding <Name>`, `Path=<Name>`), since
   nothing fails at build time. Open the pair of every `.axaml` you touched; the file names match.
4. Fix the shared member once, or the two views; add local platform code only for a real platform difference.
5. Evidence, in this order of strength: `tests/Hearthpulse.Presentation.Tests` on Windows (loads both UIs' XAML and
   checks shared contracts), the Headless binding tests in `tests/Hearthpulse.Tests`, then a render of both UIs
   through [hearthpulse-visual-check](../hearthpulse-visual-check/SKILL.md). A green Avalonia build alone says
   nothing about WPF. Commands: [hearthpulse-testing](../hearthpulse-testing/SKILL.md).

## Control properties

| Need | Use |
|---|---|
| Set from XAML, a style or a theme; inherited or animated | `StyledProperty<T>` via `AvaloniaProperty.Register` (the pattern of `UI/Controls/*.cs`) |
| Read-only or computed state exposed for binding | `DirectProperty` with a backing field and `SetAndRaise`; styles cannot set it (none in the repo yet) |
| React to a change | override `OnPropertyChanged` and compare `change.Property`; for a drawn control, register the property with `AffectsRender` |
| A state that styles should target | a pseudo-class (`PseudoClasses.Set(":name", on)`) instead of one more property (none in the repo yet; existing views switch style classes) |

Styled properties pass through the value-priority system (local value, then style, then theme, then inherited, then
default): a value set locally in code silently wins over the theme's setter. Colours and sizes of a control stay
theme tokens, see `hearthpulse-design`.

## Templates

A `UserControl` composes a piece of a screen; almost every panel here is one. A lookless `TemplatedControl` with a
`ControlTheme` is for a control restyled from outside. In a template use `{TemplateBinding}` for one-way links to
the control, name parts `PART_*`, look them up in `OnApplyTemplate` through `e.NameScope.Find<T>()` and tolerate
`null`: the theme files may re-template.

## Threads

- Log parsing, the simulator, memory reads and network run off the UI thread; the host posts each `LiveSnapshot` to
  the UI thread and the view models are touched only there. Follow the existing call sites: `Dispatcher.UIThread.Post`
  for fire-and-forget updates, `InvokeAsync` when the caller needs the result.
- Touching a control or a bound collection from a task throws "call from invalid thread" only on some paths; treat
  any view-model write from a background continuation as the bug even when it happens to work.
- Avalonia 12 allows a dispatcher per thread. App code may use `Dispatcher.UIThread`; a reusable control uses its own
  `Dispatcher` property.
- Blocking the UI thread on a task (`.Result`, `.Wait()`) freezes the overlay over the game. Measuring a stall:
  [hearthpulse-performance](../hearthpulse-performance/SKILL.md).

## Platform services

File pickers, clipboard and opening a folder or link come from `TopLevel.GetTopLevel(control)` (`StorageProvider`,
`Clipboard`, `Launcher`), as the existing views do. Code that must differ per OS (overlay window, click-through,
game window lookup) belongs in `Hearthpulse.Desktop/Platform/*OverlayPlatform.cs`, not in a view; Win32 calls in
shared UI code break macOS and Linux.

## Before you call it done

- Both hosts build with 0 warnings (`hearthpulse-dev`, "Build and test"); the Avalonia build is the binding check.
- The feature exists in the WPF view as well, or the gap is one of the accepted ones in `hearthpulse-design`.
- No new package, service container, locator or reflection binding entered the diff.

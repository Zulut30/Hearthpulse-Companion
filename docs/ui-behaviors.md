# UI behaviors library

Keyboard, focus, selection and popup behavior that both UIs share, ported from the design of
[UI SDK](https://github.com/vercel-labs/ui) (`vercel-labs/ui`, `packages/core`, revision `af0321f`, Apache-2.0,
Copyright 2026 Vercel, Inc.; attribution in `THIRD-PARTY-NOTICES.txt`). UI SDK keeps each behavior in a
framework-neutral core and maps it onto React, Vue, Svelte, Solid and Web Components with thin adapters. Hearthpulse
does the same for WPF and Avalonia: the rules are plain C# with no framework type, and each UI has a small adapter that
reads its own keys, finds its own controls and moves its own focus. It is a port of the design and of the rule code,
not a dependency: no npm package, no WebView.

## Where it lives

| Part | Path | What it holds |
|---|---|---|
| Core (shared source) | `src/Hearthpulse.Presentation/Behaviors/` | The rules, compiled into both UI assemblies as `Hearthpulse.App.Behaviors` / `Hearthpulse.UI.Behaviors`; no WPF or Avalonia type |
| WPF adapters | `src/Hearthpulse.App/Behaviors/` | `RovingFocus` (keys, focus, scroll, PositionInSet), `PopupFocus` (a `Popup` made in code) |
| Avalonia adapters | `src/Hearthpulse.UI/Behaviors/` | `RovingFocus`, `PopupFocus` (attached property `b:PopupFocus.ReturnFocus` on a `Flyout`) |
| Tests | `tests/Hearthpulse.Tests/UiBehaviorsTests.cs`, `UiBehaviorsHeadlessTests.cs`; Windows: `CardPageContractTests`, `CollectionPopupTests`, `MainNavigationContractTests` | Every core through its interface; the Avalonia adapters headless; the WPF adapters in the Windows suite |

The core sits in Presentation, not in Core: it is interface behavior (keys, focus, popups), not game or data logic, and
Presentation already holds the other framework-neutral control logic (`Controls/ChartGeometry.cs`,
`Controls/CardPreviewLayout.cs`). It compiles into each UI, so the adapters call it without a new assembly, and
`tests/Hearthpulse.Tests` reaches it through `Hearthpulse.UI`.

## Behaviors

| Core | UI SDK source | Interface | What it decides |
|---|---|---|---|
| `ListNavigation` | `focus/collection.ts`, `focus/roving-focus.ts` | `Target(current, key, count, options, usable)` | The item an arrow, Home/End or Page key moves the focus to in a one-Tab-stop group: vertical, horizontal or both axes (radios), loop or stop at the ends, right to left, skipping items that cannot take the focus. -1 = not this list's key |
| `Typeahead` | `text/typeahead.ts` | `Search(text, labels, current)`, `Active`, `Reset()` | Type-to-find (WAI-ARIA): a query that lives 500 ms after the last character, the next label that starts with it, a repeated letter steps through the items with that letter; case and accents fold by the culture (`е` finds `Ё`) |
| `SelectionModel<TKey>` | `selection/selection-model.ts`, `selection/listbox-range.ts` | `Choose`, `Toggle`, `Extend`, `ToggleAll`, `SetAnchor`, `Reset`; `Changing` (cancellable), `Changed` | Single and multiple selection: Ctrl toggles, Shift ranges from the anchor (a new range replaces the last, like a file manager), Ctrl+A twice selects all then none, unselectable keys |
| `Dismissal` | `popups/dismissal.ts` | `Dismissible`, `Allows(reason)`, `Attempted` | Whether Escape, an outside press, focus or the window leaving may close a layer; its close control and code always can; a refused try on purpose raises `Attempted` ("Discard changes?") |
| `PopupState<TElement>` | `popups/popup.ts`, `popups/create-popup.ts` | `Open(trigger, opener)` -> initial focus, `Close(reason, focusInside, pressReachedFocusable)` -> closed + focus target | Where the focus goes as a popup opens (into the content; not on hover or from the start; the popup itself on touch) and whether it returns to the trigger as it closes (after Escape, a close key, a press on nothing focusable, or code closing it with the focus inside: yes; after a press on another control or the focus leaving: no). A closed popup keeps no element |
| `FocusRecovery<TElement>` | `focus/focus-recovery.ts`, the return chain of `popups/popup.ts` | `Remember(element, collection)`, `Take()`, `Resolve(origin, fallback)`; the adapter answers `IFocusTree<TElement>` | Keeps a keyboard user's place when the element that held the focus leaves: the same element while usable, else the item with its key in the same collection (a rebuilt row of the same card), else the first item or the one now at its place, else the caller's fallback |

Left out as DOM- or web-only (WPF and Avalonia do these themselves or have no such input): `definePrimitive` and
slots/parts (each UI has its controls and XAML), SSR tab-stop claims and hydration, `tabbable` scanning, `inert` and
scroll lock, shadow-DOM focus, top-layer and `::backdrop`, Android back, sheet swipe and snap, hover-card delays,
virtualizer and `MutationObserver` re-ordering, `aria-*` attributes (replaced by UI Automation, below). Drag and drop was
not ported: the overlay's `MovablePanel` drag already shares its geometry (`PanelLayout`, `PanelSnap`), and moving its
press/capture state machine into the library would touch overlay arming for little gain.

## Accessibility in place of ARIA

UI SDK writes `role`, `aria-*` and `tabindex`. Here the role comes from the control (a `Button` is a button, a
`RadioButton` a radio), the one Tab stop from `KeyboardNavigation.TabNavigation="Once"`, and the adapters set UI
Automation properties: `RovingFocus.Announce` sets `AutomationProperties.PositionInSet` / `SizeOfSet` on the focused
item ("3 of 30", ARIA's `aria-posinset` / `aria-setsize`). Names stay `AutomationProperties.Name` in the views
(`better-accessibility`).

## Used in both UIs

| Place | Behaviors | WPF | Avalonia |
|---|---|---|---|
| Card rows (deck page, meta deck lists, mulligan guide) | `ListNavigation` (Up/Down, focus stays at the ends, Home/End left to the page), `Typeahead` (type a card's first letters), PositionInSet/SizeOfSet | `Controls/CardLink.cs` | `Controls/CardLink.cs` |
| Card page focus (back to the row it was opened from, else the same card's row, else the list's first row, else the sidebar button) | `FocusRecovery` | `Main/MainWindow.CardFocus.cs` | `Main/MainView.CardFocus.cs` |
| Missing cards of a deck's build label | `PopupState` + `Dismissal` | `Main/Meta/ConstructedMetaView.Collection.cs` (`PopupFocus` for its code-made `Popup`; Escape, Enter, Space close it) | `b:PopupFocus.ReturnFocus="True"` on the `Flyout` in `ConstructedMetaView.axaml` |
| History (replay library) list: Up/Down between games, Right/Left along a game's buttons | `ListNavigation` | `Main/Match/ReplayLibraryView.xaml.cs` | `Main/Match/ReplayLibraryView.axaml.cs` |

`SelectionModel` has no user yet: no list in either UI selects more than one item, and the one single choice
(`MainViewModel.SelectedArchetype`) is a two-line setter. It is ready, with tests, for the first multi-select list.

## Using it in a new control

1. Keep the rule in the core. If the control needs a behavior that is not here, add it to
   `src/Hearthpulse.Presentation/Behaviors/` as plain C# (no `System.Windows`, no `Avalonia`), with the conditional
   namespace the other files use, and unit tests in `tests/Hearthpulse.Tests/UiBehaviorsTests.cs`. A port from UI SDK
   keeps the header: source file, revision, "Copyright 2026 Vercel, Inc. Licensed under the Apache License, Version
   2.0" and a NOTICE line saying what Hearthpulse changed.
2. Map it in both adapters. A list that moves the focus by arrows: call `RovingFocus.Move(current, items, key,
   options)` from the item's tunnelling key handler (`PreviewKeyDown` in WPF, `AddHandler(KeyDownEvent, ...,
   RoutingStrategies.Tunnel)` in Avalonia) and set `e.Handled` from its answer; set the container's
   `KeyboardNavigation.TabNavigation` to `Once`; call `RovingFocus.Announce(item, items)` when an item gets the focus.
   Type-to-find: keep one `RovingFocus.NewTypeahead()` per list and call `RovingFocus.Search` from `TextInput`.
3. A popup: in Avalonia set `b:PopupFocus.ReturnFocus="True"` on its `Flyout`; in WPF keep a `PopupFocus` per popup,
   call `Opened(trigger, content)` after `IsOpen = true`, `RequestClose(reason, focusInside)` before closing it from a
   key or code (`PopupFocus.ReasonFor(key, activateCloses)` maps the keys) and `Closed()` from its `Closed` event.
4. Focus that must survive a rebuilt list: a `FocusRecovery<T>` with an `IFocusTree<T>` that says when an element is
   usable, what a collection's items are and what key an item has (`CardLink.FocusTree` in each UI is the example).
5. Check both UIs: a headless test of the Avalonia adapter, a test in `tests/Hearthpulse.Presentation.Tests` for WPF,
   both hosts at 0 warnings.

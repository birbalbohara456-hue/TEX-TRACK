# UI / Keyboard Focus Findings — 03-04 September 2026

**Scope:** Diagnosis of a reported UI issue — keyboard shortcuts mostly work, but focus behavior becomes ambiguous on a few specific report pages: it's hard to tell which row the mouse is hovering versus which row keyboard actions will actually act on.
**Owner going forward:** UI/keyboard workflow is now Claude Code's responsibility, separate from the Codex-owned production-engine work. This document is the starting reference for that work.
**Method:** Verified directly against current source — not a general theory about keyboard UIs being hard. Three specific files were identified with a precise, checkable gap versus the pattern used correctly elsewhere in the same codebase.

---

## The Finding

TexTrack's report pages implement row selection and keyboard navigation through two mechanisms that must work together:

1. **A CSS class** (`selectable-grid` on the `<table>`, `selected-row` on the current `<tr>`) that gives the selected row a strong, distinct visual treatment: `background: var(--tt-selection); color: white` (see `wwwroot/css/app.css:1191-1192`). Without `selectable-grid` on the table, a row only ever gets the generic `:hover` style (`background: #e4edf6` or similar) — there is no visual difference between "the mouse happens to be over this row right now" and "this is the row keyboard actions apply to."
2. **A JS interop call**, `texTrackFocusById` (plus `texTrackScrollRowIntoView`), invoked whenever `selectedIndex` changes, that moves the browser's *actual* DOM focus to match. Without this call, the C# `selectedIndex` field and the CSS class it drives are purely cosmetic — the real, functional keyboard focus can be sitting somewhere else entirely (or nowhere, if a re-render replaced the previously-focused element, which Blazor Server re-renders can do).

`ProductionReports.razor`, `InventoryClosingStock.razor`, and `StockRegister.razor` correctly pair both mechanisms together and are the working reference implementation. Three other pages don't:

| File | `texTrackFocusById` called? | `selectable-grid` used? | Resulting symptom |
|---|---|---|---|
| `Components/Pages/Reports/JobWorkerAgingDetailPanel.razor` | **No** | **No** (plain `data-grid` only, 4 occurrences) | Neither mechanism present. Keyboard focus can genuinely be somewhere other than what's shown, and there's no distinct visual cue even if it weren't. This is the page most likely to show outright "lost focus" behavior. |
| `Components/Pages/Reports/JobWorkerControl.razor` | Yes | **No** (plain `data-grid` only, 2 occurrences) | Focus itself is probably landing correctly (the sync call is present), but there's no strong visual indication of where — only the generic hover style applies, so once the mouse moves away, nothing shows which row is actually selected. |
| `Components/Pages/Reports/PendingMaterialIssue.razor` | Yes | **No** (plain `data-grid` only, 1 occurrence) | Same as above — functional focus likely fine, visual feedback missing. |

This matches the reported symptom precisely: *most* pages work, because most pages correctly pair the two mechanisms; the few that break are the ones with an incomplete copy of the pattern.

## Root Cause

Each report page implements row selection and keyboard handling independently — there is no single shared component or base class for "a keyboard-navigable data grid" in this codebase. A search across `Components/Pages/Reports/*.razor` found **8 separate, hand-written implementations** of `selectedIndex` state and `HandleKeyDown`/`HandlePageKeyDown` logic, one per page, rather than one shared implementation reused everywhere.

This is the same class of risk that caused the Material Out/Material In cancellation regression found during the backend audit: when a correct pattern exists in multiple independent copies, some copies drift from the reference as the codebase evolves, and nothing structurally prevents or even flags the drift. Here, it produced three pages that are missing one or both halves of the focus-sync pattern that the majority of pages implement correctly.

## Recommended Fix — Immediate

Small, low-risk, and additive — this is not a redesign, it's bringing three pages in line with a pattern already proven correct on the others:

1. Add the `selectable-grid` class to the `<table>` elements in `JobWorkerControl.razor` and `PendingMaterialIssue.razor`.
2. In `JobWorkerAgingDetailPanel.razor`: add `selectable-grid` to its tables, and add the `texTrackFocusById` / `texTrackScrollRowIntoView` JS interop calls wherever `selectedIndex` changes — copy the exact call sites and pattern from `ProductionReports.razor` (see its handling around `selectedIndex` changes for the reference implementation).

**Regression check:** after the fix, manually verify on all three pages that (a) arrow-key/Tab navigation moves a visibly distinct highlighted row, (b) that highlight persists correctly when the mouse is moved away and not hovering anything, and (c) a keyboard action (e.g., Enter to open, a shortcut to act on the row) affects the row that's visually highlighted, not a different one.

## Suggestion — Structural, for consideration before this recurs on a future report page

Since UI work is now being handled separately from the backend engine, this is a natural point to decide whether to keep accepting this risk or close it structurally:

- **Extract the shared "keyboard-navigable grid" behavior into one reusable component** (a base Razor component, or a shared partial/mixin pattern) that every report page uses for selection state, focus sync, and the CSS class pairing — instead of 8 independent copies. New report pages would then get correct behavior automatically, rather than needing someone to remember to wire up both halves of the pattern by hand every time.
- **If a shared component is more work than is justified right now**, a lighter-weight interim step: a simple, repeatable manual (or automated) check — "does this page's `selectable-grid` usage count match a `texTrackFocusById` call being present" — run whenever a new report page is added, so a future omission like these three is caught immediately rather than discovered later as a reported UX complaint.

The existing `KeyboardContextComponentTests.cs` test file provides some coverage of the keyboard contract already; it may be worth extending it (or adding a parallel check) to assert this specific pairing across all report pages automatically, rather than relying on it being remembered by whoever adds the next page.

## Files Referenced

- Reference (correct) implementation: `Components/Pages/Reports/ProductionReports.razor`, `Components/Pages/Reports/InventoryClosingStock.razor`, `Components/Pages/Reports/StockRegister.razor`
- Needs fixing: `Components/Pages/Reports/JobWorkerAgingDetailPanel.razor`, `Components/Pages/Reports/JobWorkerControl.razor`, `Components/Pages/Reports/PendingMaterialIssue.razor`
- Shared CSS: `wwwroot/css/app.css` (`.selectable-grid`, `.selected-row`, `.data-grid` definitions around lines 1185-1192, and the `desk-shell` variants around 659-661)
- Shared keyboard/focus JS: `wwwroot/js/textrack-alt-delete.js` (contains `texTrackFocusById` and related interop functions)

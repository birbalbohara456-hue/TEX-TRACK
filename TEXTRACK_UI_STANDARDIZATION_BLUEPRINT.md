# TexTrack UI Standardization Blueprint

> Given verbatim by the product owner on 2026-09-09 as the guiding direction for all voucher UI
> work going forward. Treat this as the standing reference alongside
> `JWO_KEYBOARD_WORKFLOW_MASTER_SPEC.md` (the keyboard-behavior contract this blueprint builds on)
> and `CLAUDE_UI_OVERHAUL_CHANGELOG_2026-09-08.md` (the record of what's already been done on JWO).

## Overall objective

Preserve TexTrack's current speed and density, make it more predictable and informative, and
standardize every voucher around the JWO experience without disturbing backend logic.

## The decisions

* **JWO is the master voucher reference.** All current and future voucher screens should copy
  its compact structure, speed, dropdown behavior, keyboard interaction, grid density, and
  overall visual language rather than inventing separate styles.
* **Keep keyboard speed exactly as it is.** Do not slow Enter navigation or add artificial
  debounce. Instead, make the flow feel more predictable with stronger active-field visibility,
  consistent focus order, reliable modal/dropdown focus return, and uniform Enter/Esc behavior.
* **When a BOM is selected in JWO, show the actual calculated RM/component requirements on
  screen.** The requirement should be visible and read-only, because BOM remains authoritative.
  The user should not be made to re-enter material that the BOM already determines.
* **Add "Stock in Hand" beside editable quantity fields.** This should show stock available in
  the currently selected source godown for that exact item/variant. If no source godown is
  selected, show "—".
* **Add a compact stock-position info action near Stock in Hand.** It should show selected
  godown stock, other internal godowns, aggregated third-party stock, and total company stock.
  Detailed jobber/location-wise stock should only appear on drill-down, not in the initial popup.
* **Godown classification logic will support the UI.** The intended structure is `Internal` vs
  `Third Party`, but that is backend/business-logic work, not UI-only work.
* **Rate-field assistance can appear automatically on focus.** A small contextual box may show
  useful reference rates such as FIFO, weighted average, and latest purchase. It disappears
  immediately when focus leaves the field. The UI must not calculate valuation itself.
* **Editable and system-derived rates must look different.** Normal reference rates can remain
  editable where business rules allow; manufactured/intermediate-component rates derived from
  material + process cost should appear protected/non-editable.
* **Keep voucher fields compact.** Voucher No., Date, Batch, Godown, Job Worker, Ledger, etc.
  should remain Tally-like in size and density, not become oversized dashboard controls.
* **Use consistent controls across vouchers.** Same dropdown style, focus state, selected-row
  state, quantity/rate/amount alignment, add/remove row pattern, shortcuts, modal behavior,
  Enter behavior, and Esc behavior.
* **Do not literally open reports in the background to fetch stock information.** The UI should
  eventually consume focused backend stock-position services.
* **Do not move business logic into the UI.** FIFO, stock valuation, COGS, accounting, security,
  transaction posting, BOM calculations, migrations, and PostgreSQL integrity remain backend
  responsibilities.
* **WebView2 remains the future desktop shell direction.** Avoid heavy browser hacks now for
  behavior that can later be handled cleanly by the thin WebView2 wrapper later.

## How to apply

This is a roadmap spanning several independent workstreams, not one task - at minimum:
1. Rolling JWO's dropdown/keyboard/focus conventions out to the other voucher pages (MaterialIn,
   MaterialOut, MasterJobOrder, and the still-stub financial vouchers once they're built).
2. Displaying BOM-calculated RM/component requirements read-only in JWO.
3. "Stock in Hand" next to quantity fields, backed by a real stock-position query (not a report
   opened in the background).
4. The compact stock-position drill-down popup.
5. Godown Internal/Third-Party classification (backend-led, UI consumes it).
6. Rate-field reference-rate assistance box (FIFO/weighted-average/latest-purchase, read-only).
7. Visually distinguishing editable vs. system-derived (protected) rate fields.

Each of these needs its own scoping/sequencing conversation before implementation - don't attempt
the whole blueprint in one pass. When picking up any voucher-page work from here on, check it
against this blueprint's decisions (especially "copy JWO, don't invent a new style") before
building something bespoke.

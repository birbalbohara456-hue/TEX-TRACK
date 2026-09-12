# TexTrack UI/UX Design Notes

**Purpose:** A living record of UI/UX design decisions, findings, and open questions for TexTrack's frontend — the same "write it down instead of relying on memory" discipline already used for business rules and audit findings, applied to design. This is the ongoing reference for UI/UX work going forward, not a one-time report.

**Design thesis (stated by the product owner):** *Tally Prime + ERPNext = TexTrack.* Keep Tally's keyboard-first density and workflow discipline as the skeleton; bring a modern, attractive visual layer closer to ERPNext's sensibility; end up as neither a copy of Tally nor of ERPNext, but a distinct product with its own identity. Every UI decision should be checked against this thesis: does it feel keyboard-native, does it look current, does it look like TexTrack rather than either source.

**Thesis sharpened after the three-way screen comparison (Tally / TexTrack / ERPNext), in the product owner's own words:**
- Tally: "compact, simple, professional, less clutter and mess on screen" — density from information packing, not decoration.
- ERPNext: "exposes way too much and the user gets confused" — a lot of the surface area on any given screen (sidebar metadata, tabs, toolbar dropdowns) is generic framework chrome, not content the task actually needs.
- TexTrack (current state, correctly): "doesn't expose too much like Tally" — i.e. TexTrack already avoids ERPNext's over-exposure problem; the remaining gap is density/politeness of the fields themselves, not screen clutter.
- **Governing rule going forward:** take **Tally's restraint on what is shown** (only task-relevant fields, no persistent metadata panels, no framework surface for its own sake) and **ERPNext's polish in how it's rendered** (typography, spacing rhythm, color, toolbar-style controls) — never ERPNext's quantity of simultaneously-visible surface area, and never at the cost of Tally's density. Concretely: **borrow ERPNext for quality of presentation, borrow Tally for quantity of what's presented.**
- This directly answers Open Design Question #2 below in principle: default new/reworked fields toward a lighter, less-boxed treatment (Tally- and ERPNext-table-row style, not a bordered rectangle per field) unless a specific field needs to visually stand out.

---

## Confirmed, Resolved

### The date-field-width pattern
- **Original issue:** `VoucherDateInput.razor.css` sets `width: 100%` on the input, meaning date fields inherit whatever column width the surrounding page layout gives them rather than sizing to their own content. On generic/equal-width grid layouts, this made date fields look stretched next to genuinely wide fields (Party Name, Reference).
- **Correct fix pattern (confirmed in place, e.g. `JobWorkOutOrder.razor.css`):** give the specific instance of the date field an explicit fixed width via a page-scoped override (`.jwo-context-date ::deep .jwo-header-date-input { width: 126px !important; }`), rather than changing the shared component's own `width:100%` default (which other contexts may legitimately need).
- **Standing principle for future fields:** short, fixed-format fields (dates, quantities, rates, UQC codes) should get an explicit, content-sized max-width at their point of use, not rely on `width:100%` plus whatever column the page happens to allocate. Apply this check to any new short field before it ships, not just dates.
- **Open item (small, cosmetic):** the JWO page's date badge width (126px) is still somewhat larger than an 11-character bold date string needs at 12px. Recommended: reduce to ~92px. One-line CSS value change, not structural.

### Keyboard focus / row-selection consistency across report pages
- Original finding: see `docs/archive/audits/UI_KEYBOARD_FOCUS_FINDINGS_2026-09-04.md`. It flagged three report pages (`JobWorkerAgingDetailPanel.razor`, `JobWorkerControl.razor`, `PendingMaterialIssue.razor`) as missing one or both halves of the `selectable-grid` CSS class + `texTrackFocusById` JS-sync pairing.
- **Re-verified 2026-09-11, resolved by architecture change rather than the literally-described fix:** all three pages have since been restructured. `PendingMaterialIssue.razor` now uses a card layout with a `.pmi-order.selected` outline (same distinct-highlight goal, different markup). `JobWorkerControl.razor` was rebuilt into a paginated, non-interactive batch report with no row-selection concept left to highlight. `JobWorkerAgingDetailPanel.razor` is a read-only expansion panel with no selection logic of its own — the actual selectable list is its parent `JobWorkerAging.razor`, which already has `selectable-grid`, a `.selected` row class, and syncs the visible row via `texTrackRevealRow` (an established convention also used by `BillOfMaterials.razor` and `JobWorkerControlCenter.razor`). No code change was needed.

---

## Confirmed, Intentional — Not a Defect

### No top-level "Party A/c name" field on the Job Work Out Order form
- **Tally's model:** one order, one job worker — "Party A/c name" sits at the top because there's nowhere else for it to go.
- **TexTrack's model:** a single JWO can span multiple production stages (true multi-level BOM), each independently assignable to a different job worker (`JobWorkOrderBomStage.AssignedJobWorkerId`, versioned through `JobWorkOrderStageAssignment`). A single top-level "the job worker" field would be actively wrong the moment an order has more than one stage, since there'd be no single correct value to show. Moving job-worker assignment to where it actually lives (per stage/component) is the correct response to a real capability Tally's own form was never built to express, not a UI gap relative to Tally.
- **This behavior is confirmed by direct audit evidence**, not just the explanation given: stage/assignment versioning was independently verified live during the production-engine audit (reassignment preserves stage identity, appends history, doesn't disturb already-pending material under the prior assignment).

---

## Open Design Questions — Recommended, Not Yet Decided

### 1. At-a-glance job worker visibility for the common single-stage case
- **The question:** for the (likely common) case where a JWO has only one stage and therefore only one job worker across the whole order, does the user currently have to open the component/stage popup just to see who it's assigned to — where Tally would show that name on the main form at a glance?
- **Suggested approach:** add a **read-only** summary display — e.g. "Job Worker: Acme Corp" — at the top of the form, shown only when every stage on the order currently resolves to the same single job worker. The moment an order has more than one distinct job worker across its stages, the summary should either disappear or explicitly indicate multiple workers (e.g. "Job Worker: Multiple") rather than guess.
- **Important constraint:** this must stay purely a read-only display. The actual editable assignment must continue to live exactly where it correctly lives now (per stage/component). Do not let this summary become a second place to *edit* the assignment — that would reintroduce the single-worker assumption this design correctly avoided.
- **Status:** suggested, not yet implemented or scheduled. Product owner acknowledged this is worth deciding on purpose.

### 2. Bordered-box-per-field convention vs. Tally's borderless inline text
- **The observation:** TexTrack renders essentially every field as a distinct bordered rectangle with internal padding (Voucher Type, Voucher No., Batch, Reference, etc.). Tally renders most of its equivalent fields as plain inline `label : value` text with no border or background at all. This is a more precise version of the date-field-width issue — it's not that any one field is oversized, it's a systemic convention: every field pays a fixed border+padding+margin cost regardless of how short its content is, and that overhead compounds across a whole form. This is very likely a meaningful part of why full TexTrack pages read as less dense than Tally even when individual font sizes are comparable.
- **This is a real design decision, not automatically a defect.** Full borderless-everywhere would be a stronger Tally echo but might cost some modern affordance/accessibility clarity that boxed fields provide. The question worth deciding deliberately: does *every* field need the same boxed treatment, or would some fields (short, low-emphasis ones especially) read better lighter, closer to Tally's convention, reserving strong box treatment for fields that need to visually stand out?
- **Status:** flagged for discussion, no decision made yet. Worth revisiting once more voucher screens have been compared.

---

## Three-Way Comparison: Tally vs. TexTrack vs. ERPNext (Job Work Out Order equivalent)

ERPNext's nearest equivalent to a Job Work Out Order is its **Subcontracting Order** (created from a Purchase Order flagged "Is Subcontracted" — see ERPNext's own docs, `docs.frappe.io/erpnext/user/manual/en/subcontracting`). Screenshots pulled from ERPNext's official documentation (a live "Fast Transporter" / `SC-ORD-2026-00002` example) and forwarded to the product owner directly.

### What ERPNext's form actually looks like
- A breadcrumb trail (`Subcontracting / Subcontracting Order / Fast Transporter`) plus a `Status: Open` chip, instead of a fixed page title.
- A slim top action bar: `Status ▾ / Create ▾ / Transfer ▾ / ··· / Cancel` — actions are buttons in a toolbar, not menu-driven like Tally or button-row-at-bottom like TexTrack.
- Content is split into tabs (`Details / Address and Contact / Other Info / Connections`) rather than one continuous scroll or a modal-per-row.
- Tables (Items; Supplied Items) have borders only on the table itself and between rows — individual cells are NOT boxed the way TexTrack boxes nearly every field. This is the same "systemic bordered-box" contrast already flagged in Open Design Question #2 below, now with a concrete third data point: ERPNext's own answer is closer to Tally's borderless convention than to TexTrack's current one, even though ERPNext is otherwise the more "modern SaaS" visual reference point.
- A dedicated collapsible section per logical group (`Service Items`, `Raw Materials Supplied`) — each with its own toggle, rather than everything always visible or hidden behind a popup.
- A persistent right-hand metadata sidebar (Assign / Attachments / Tags / Share, plus "Last Edited by" / "Created By" with relative timestamps) that is fully separate from the transactional form fields — an affordance neither Tally nor TexTrack currently has, and not necessarily one to copy, but worth naming as a real difference.
- Generous row height and whitespace throughout — this is the form that reads as least dense of the three, i.e. the opposite end of the spectrum from Tally, with TexTrack currently sitting closer to ERPNext's spaciousness than the product owner wants.

### How this sharpens the design thesis
This gives a concrete anchor for "Tally Prime + ERPNext = TexTrack": ERPNext contributes the clean typography, tab/section organization, and toolbar-style actions; Tally contributes the density and borderless-field convention; TexTrack's current implementation has adopted ERPNext's spaciousness more than intended and Tally's density less than intended. The bordered-box-per-field question (Open Design Question #2, below) is now the highest-leverage single change available — ERPNext's own real-world form uses table-row borders, not per-field boxes, which removes "boxes make it feel more legible/modern" as a reason to keep TexTrack's current convention, since ERPNext achieves a modern feel without them.

---

## Tally's Exact Keyboard-First Mechanics (Research Reference)

The product owner's point: Tally's keyboard workflow isn't "has shortcuts," it's a set of mechanics that reinforce each other so both hands stay on the keyboard *without ever losing your place in the current voucher*. Researched directly (see sources) rather than assumed:

1. **Forward/back field flow** — `Enter` commits the field and advances to the next one; `Backspace` steps back to the previous field. (TexTrack's JWO footer already states this exact convention — `Enter: Next field · Backspace: Previous field` — see `JobWorkOutOrder.razor:402`. Whether it's fully wired for every field, and whether reaching the last field of the last row and pressing `Enter` auto-creates the next row, was not confirmed in source and is worth checking before claiming parity.)
2. **Create-on-the-fly (`Alt+C`)** — sitting in a ledger/stock-item field and the record doesn't exist? `Alt+C` opens a "secondary" creation screen right there, without abandoning the voucher. Save it and you land back in the original field with the new record already selected. No context switch to a separate masters screen.
3. **Live incremental search in every selection list** — typing filters the list as you type, no separate search box or dialog; arrow keys move, `Enter` picks.
4. **`Alt+G` "Go To"** — universal fuzzy jump to any report/voucher/master from anywhere, bypassing the menu tree.
5. **`PgUp`/`PgDn` inside voucher entry** — cycles to the previous/next voucher of the same type without exiting the entry screen.
6. **`Ctrl+Enter`** — alters the master currently pointed at (e.g. edit the ledger) directly from inside the voucher field, then returns.
7. **Shortcuts are printed on the controls themselves** (underlined/bracketed letter convention) — discoverable by looking at the screen, not by memorizing a cheat sheet. TexTrack's footer command bar (`Alt+A Accept`, etc.) already does a version of this.

**Sources:** [AI Accountant — TallyPrime shortcut keys](https://www.aiaccountant.com/blog/all-tally-prime-shortcut-keys-list), [TallyHelp — Create, Alter and Delete Ledgers in TallyPrime](https://help.tallysolutions.com/ledgers-in-tallyprime/)

**Why this matters for TexTrack:** the unifying idea across all seven is that a detour (creating a master, searching, checking an adjacent record) never costs you your place in the transaction — it's an inline pop-in/pop-out, not a navigation. Any TexTrack keyboard work should be checked against that standard specifically, not just "does a shortcut exist for this."

### Decided: `F3` is a single context-sensitive key for both create and edit
Tally splits this across two keys — `Alt+C` to create a master on the fly, a separate alter-shortcut to edit the one already selected in a field. Product owner's decision for TexTrack: collapse both onto the one key TexTrack already uses, `F3`, distinguished by field state:
- Field is empty / has no matching record selected → `F3` creates a new master (this half already exists — see `F3` handling in `wwwroot/js/textrack-alt-delete.js:783` and `JobWorkOutOrder.razor:521-523`).
- Field already has a record selected → `F3` opens *that* record for editing, instead of create.
- **Not yet implemented/confirmed as of this writing** — the edit-existing-record half of this behavior, and the auto-return-with-selection behavior noted above, should be treated as one shared piece of work when TexTrack's keyboard/lookup layer is actually touched, not two separate asks.

### Decided: global "Go To" search on `Alt+G`
TexTrack has no global search yet. Product owner initially proposed `Ctrl+G` "same as Tally," but verified against source (`tallysolutions.com`, `aiaccountant.com`): Tally's actual universal jump-to-anything search is `Alt+G`; `Ctrl+G` is a narrower "Switch To" feature for hopping between recently used contexts, not a fuzzy search across the whole system. Decision: TexTrack's new global search binds to `Alt+G`, matching what Tally itself means by "Go To." A separate lighter "switch to" shortcut on a different key was considered but not requested — revisit if the product owner wants fuller parity later.
- **Confirmed absent, not assumed** — product owner recalled asking Codex to build this previously. Searched the full source tree (`.razor`, `.razor.cs`, `.js`, and every build changelog/status `.txt`/`.md`) for any global-search implementation, `Alt+G`/`Ctrl+G` handler, or `MainLayout.razor` search bar: nothing found. Either it was never actually built, was built and later reverted, or exists in a build/branch not currently checked out. Worth asking Codex which build it shipped in, if any, so that specific claim can be checked the same way rather than assumed.

---

## Reported Defects — Not Yet Source-Verified

### Lookup dropdowns didn't open on empty focus, and showed nothing on zero matches — same root cause, both fixed on JWO
- **Reported by product owner (2026-09-04), live-testing the JWO page**, in two separate sessions that turned out to be the same underlying bug: (1) typing a lookup field with no matching results left the dropdown never opening, with no "nothing found" feedback; (2) focusing an *empty* lookup field (e.g. Colour, before typing anything) also never opened the dropdown, even though a full scrollable list of options should reasonably appear immediately.
- **Root cause, confirmed in source:** every lookup panel on `JobWorkOutOrder.razor` gated its render on `activeLookupKey == "..." && !string.IsNullOrWhiteSpace(fieldText)` — the panel simply never rendered while the field was empty, regardless of whether `FilterXxx()` would have returned every option (it does, when text is empty). And once text was typed with zero matches, the panel's render condition was still true but its `@foreach` had nothing to iterate, so it rendered as an empty, near-invisible box — reading as "did nothing" rather than "found nothing."
- **Status: fixed and verified live (2026-09-04)** on all 7 lookup fields on this page (Voucher Type, Finished Good item, Colour, Finished Goods Godown, Process, Component item, Component godown): dropped the text-emptiness requirement from every render condition, and each panel now shows an explicit "Nothing found" message (`.erp-lookup-empty`, matching the style already used on `BillOfMaterials.razor`) when its filtered list is empty. Verified live: focusing an empty Colour field now opens the full 3-item scrollable list immediately; typing "zzz" (no match) shows "Nothing found" instead of a blank box.
- **Scope note carried over from the original report:** this fix covers every lookup field on the JWO page specifically. The product owner's original scope ask ("every typeable field with an applicable dropdown across the whole application") is broader than one voucher — other vouchers/masters using the same `erp-lookup-panel`/`data-erp-lookup` pattern still need the same fix applied, per the "one voucher/form at a time" sequencing rule.

### Production stage Job Worker / Process / Output Godown were plain `<select>` elements, not typeable lookups
- **Observed live (2026-09-04):** the nested BOM stage table's three assignment fields (Job Worker, Process, Output Godown) were native HTML `<select>` dropdowns — functional (they do open and list every option), but not searchable/typeable, unlike every other lookup on this page. With a company that has many job workers or godowns, scrolling a plain list is a real step down from the rest of the voucher's UX.
- **Status: fixed and verified live (2026-09-04).** Converted all three to the same typeable `erp-lookup-panel` pattern used elsewhere on the page (open on focus, filter as you type, "Nothing found" on zero matches). No backend/model changes were needed — `JobWorkBomStageEditModel` already carried `AssignedJobWorkerText`/`ProcessText`/`OutputGodownText` string fields (populated correctly on load and on BOM-default generation) that the old `<select>` markup simply never used. Verified live: typing "Rah" in Job Worker filters to "Motiar Raham"/"Rahim Printing Works", and a selected value persists correctly. All 11 JobWork-related integration tests still pass.

### Existing saved JWOs expose everything permanently — worse than expected, and not just an entry-time issue
- **Observed live (2026-09-04)** on a real saved JWO ("TEST GARMENT 08", multi-stage, permanent jobber allocation): opening/altering an *already-saved* JWO renders the colour/size breakdown, the full "BOM Production Route" block, and the entire "Production Stages" table (including nested sub-stages) permanently inline below the finished-good row — none of it collapsed or behind an expand affordance.
- **This broadens the scope of the expand-in-place structural item already noted above** (previously scoped around the entry-time raw-materials modal, which the product owner confirmed already correctly hides things during creation). The same over-exposure happens — arguably worse — when *viewing or altering* an existing JWO, which is the more common daily case. The expand-in-place redesign needs to cover both: hiding component/BOM/stage detail during entry (already working) and collapsing it by default when reopening a saved voucher (not working today).
- **Status: fixed and verified live (2026-09-04).** Implemented "Option B" (chevron reveals sizes + BOM route; a nested "N stages ›" reveals the stage table specifically), scoped to `editModel.Id != 0` only — active entry (`editModel.Id == 0`) is untouched, since that block contains live fields wired into the Enter-key keyboard chain, not just display data. Verified live: existing JWO collapses by default and both toggle levels work in both directions; a fresh JWO entry still auto-shows everything exactly as before, no regression. Committed on `claude/ui-overhaul`. Still open: the `Alt+F1` "expand everything" power-user shortcut — a smaller follow-up now that this base mechanism is proven correct.

### Decided: expand-in-place layout — "Option B" (summary first, drill into stages), plus a power-user override key
- Presented two layout concepts as mockups: Option A (sizes/route/stages all shown together in one expand) vs. Option B (a compact one-line summary, with the stage table only appearing behind a second "N stages" click). **Product owner's decision: Option B — "if we want to keep it clean then we have to go the Tally way."**
- **Added on top of Option B:** a keyboard shortcut (product owner's example: `Alt+F1`, not necessarily the final key) that expands every nested table across the whole voucher at once — bypassing the need to click into each row's "N stages" toggle individually. Default view stays minimal for normal use; power users get a one-key way to see everything expanded when they actually want the full picture (e.g. reviewing, printing, scanning a whole order).
- **Option B itself: implemented and verified live (2026-09-04)** — see "Existing saved JWOs expose everything permanently" above for the full detail. The `Alt+F1` expand-all shortcut on top of it is **still not implemented** — before building it: check the actual key chosen doesn't collide with TexTrack's existing shortcut scheme (known bindings so far: `Alt+M/T/R/A/D/X`, `Alt+F2`, `F2`, `F3` — `Alt+F1` doesn't appear to conflict with anything seen so far, but confirm at implementation time, not now) and verify it live once built rather than assuming it works from the plan alone, per standing verification discipline.

### Lookup dropdown: mouse-hover and keyboard-selected rows are visually identical
- **Observed live (2026-09-04)**, typing "te" into the Finished Good lookup: two rows in the dropdown ("TEST GARMENT 01" and "TEST GARMENT 05") both show the same dark highlight simultaneously, with no visual distinction between whichever one the mouse happens to be over and whichever one is actually keyboard-selected (the one `Enter` would pick).
- **Same underlying problem as `docs/archive/audits/UI_KEYBOARD_FOCUS_FINDINGS_2026-09-04.md`**, already documented for report-page data grids (since resolved there via page restructuring — see the "Confirmed, Resolved" entry above) — but this is a different component (the `erp-lookup-panel` lookup/combobox dropdown), not a report grid, and remains unresolved here. That earlier finding's fix (a distinct `selected-row` style separate from generic `:hover`) doesn't automatically cover this, since it's not the same markup/CSS.
- **How Tally actually handles this, confirmed live (2026-09-04) against a real Tally ledger-account dropdown:** Tally doesn't try to visually *distinguish* hover from keyboard focus — it removes hover feedback for list items entirely. Moving the mouse over an item does nothing visually; the single highlight shown is always wherever keyboard focus actually is. A click moves that same indicator (and commits the selection) rather than introducing a second, independent hover highlight. There is structurally no ambiguity, because mouse position alone is never rendered at all — only one state exists, not two competing ones.
- **This is the relevant reference for both instances of this problem** — the lookup-dropdown case here and the (since-resolved) report-grid case in `docs/archive/audits/UI_KEYBOARD_FOCUS_FINDINGS_2026-09-04.md`. Worth considering the same "one indicator, no separate hover state" approach for TexTrack's own fix, rather than trying to make hover and focus visually distinct from each other (two states, styled differently) — Tally's approach is simpler and removes the ambiguity at the root instead of just making it easier to tell apart.
- **Status:** noted, not yet implemented. This is a real design decision (remove hover-only feedback vs. keep it but make it visually distinct) worth deciding deliberately before either lookup dropdowns or report grids are touched.
- **Related, separate fix landed (2026-09-05):** lookup dropdowns previously showed no keyboard-highlighted item at all until the user pressed an arrow key — reported live as "the dropdown is there but the keyboard focus is not there." Fixed in the shared `App.razor` keyboard script (site-wide, every voucher's lookups): the first filtered match now auto-highlights the moment a *typed, non-blank* query narrows the list, without needing an ArrowDown press first, and Enter picks it up immediately via the existing `highlightedLookupIndex` check. Deliberately does **not** apply when the field is blank (e.g. a colour dropdown opened via the "open on empty focus" fix showing every option unfiltered) — preserves the existing "blank lookups never auto-select an item" rule, verified live: an empty Colour field's dropdown shows zero highlighted rows, only highlighting once text is typed. This narrows but doesn't close the hover-vs-keyboard-focus ambiguity above — it guarantees a keyboard highlight always exists once you're browsing filtered results, but hover can still show a second, visually-identical highlight simultaneously.

### Colour dropdown allows re-selecting a colour already used in the same finished-good group — confirmed intentional, not a defect
- **Observed live (2026-09-04)** on saved JWO "TEST GARMENT 07" (voucher #7): the group already had a "Black" line (owner, 75 pcs across sizes) and a "Red" line (25 pcs). Adding another colour line via "+ Colour" and opening its colour dropdown offered "Black" again as a selectable option — picking it creates a *second*, independent "Black" line under the same finished good, with its own size quantities, godown, and BOM/process/stage allocation.
- **Initially flagged as a defect; product owner corrected this (2026-09-04) — it's intentional.** Splitting the same colour across multiple lines lets each lot be routed independently: different job worker, different process/BOM route, different output godown, or a separate rework lot — all for the same colour, same finished good. `HasUnusedColours` (`JobWorkOutOrder.razor:1037`) governs only whether the "+ Colour" button still offers genuinely *new* colours; `FilterColours` (`:1919`) and `ResolveFinishedGoodColourText` (`:1435`) deliberately don't narrow the dropdown to unused colours only, since re-picking a used one is a valid way to open another lot for it.
- **Still worth keeping in mind, not as a defect but as a downstream constraint:** any report or stock calculation must aggregate by (item, colour) across all matching lines rather than assuming one row per colour, since this feature means that assumption is false by design.
- **Status:** closed — confirmed intentional, no fix needed.

### Production Stages table has no column headers
- **Observed live (2026-09-04)**, same saved JWO screen: the "Production Stages · One JWO Reference" table has no header row above its data at all. Specifically flagged: the Process dropdown ("Packing ▾") and the Output Godown dropdown ("Main Godown ▾") have no "Select Process" / "Select Output Godown" label — a user has to infer what each dropdown means from position/context alone. Confirmed by zooming into the live screenshot: none of the columns in this table (stage/finished good, qty, job worker, process, output godown, expected date, Final/Intermediate flag) carry a header label.
- **Status: fixed and verified live (2026-09-04).** Added a header row to `JobWorkOutOrder.razor` (`.jwo-bom-stage-header`, same `grid-template-columns` as the data rows so columns align): Item, Qty, Job worker, Process, Output godown, Expected date, Stage, Expected rate. Marked `aria-hidden="true"` since every field already had its own correct `aria-label`/`title` for screen readers — purely a visual affordance for sighted scanning. Confirmed live against the real "TEST GARMENT 08" JWO: all 8 headers render aligned above their columns, including "Expected rate" over the rate inputs after scrolling right. Committed on `claude/ui-overhaul`. No existing field, binding, or behavior touched.

---

## Reference Screens Compared So Far

- Tally Prime — Gateway of Tally (main menu)
- TexTrack — Login screen (`/login`)
- TexTrack — Operations Dashboard (post-login home)
- Tally Prime — Job Work Out Order (Order Voucher Creation)
- TexTrack — Job Work Out Order (`/vouchers/job-work-out-order`)
- ERPNext — Subcontracting Order (docs example, `SC-ORD-2026-00002`) — the Tally/TexTrack JWO equivalent

More voucher-screen comparisons to come; this document will keep growing as they're reviewed.

# Master Specification: ERP Keyboard Workflow & Traversal Engine

> Given verbatim by the product owner on 2026-09-08 as the authoritative reference for
> keyboard-workflow behavior across TexTrack. Applies to every voucher/form page, not just the
> Job Work Out Order page it uses as its worked example in §5. See
> `CLAUDE_UI_OVERHAUL_CHANGELOG_2026-09-08.md` §20 for the JWO audit performed against this spec,
> what was already correct, and what was fixed.

## 1. Core Architectural Principles

* **Hands-on-Keyboard Rule:** The system must support complete document creation, data grid entry, sub-modal execution, and record posting strictly via the keyboard without requiring mouse interaction.
* **Strict Directional Flow:** Navigation strictly follows natural visual reading order: Left-to-Right, Row-by-Row, Top-to-Bottom.
* **Exclusion of Destructive Controls:** Destructive or abortive actions (`Quit`, `Delete`, `Cancel`) are permanently removed from sequential focus (`tabindex="-1"`). They can never be reached by pressing `Enter` or `Tab`.
* **Sequential Commitment:** The primary commit control (`Save` / `Post`) represents the final node of the natural traversal sequence.
* **Skip & Advance Paradigm:** Leaving an optional or empty field blank and pressing `Enter` commits the blank/default state and immediately advances to the next sequential field, section, or action button.

---

## 2. Global Key Behavior Matrix

| Key | Context | Behavior | Resulting Focus / State |
| --- | --- | --- | --- |
| **`Enter`** | Field populated | Commits entered data. | Advances to immediately next editable field. |
| **`Enter`** | Field left empty | Accepts empty/default value; bypasses field. | Advances to immediately next editable field/action. |
| **`Enter`** | Last field in line/row | Commits current line. | Wraps to the first field of the subsequent line/row. |
| **`Enter`** | First cell of new blank grid row | Signals termination of grid entry. | Escapes the grid; advances to footer section. |
| **`Enter`** | Focused on `Save` / `Post` | Executes validation and commits transaction to DB. | Saves transaction; initializes clean screen. |
| **`Backspace`** | Field contains text | Standard text deletion (removes character before caret). | Focus trapped in current field. |
| **`Backspace`** | Text just cleared to empty | Field becomes completely blank. | Focus remains trapped in current field for 1 cycle. |
| **`Backspace`** | Field is completely empty | Reverses navigation order (Right-to-Left, Bottom-to-Top). | Jumps to previous editable field; caret at string end. |
| **`Backspace`** | At voucher origin (Row 1, Field 1) | Boundary lock reached. | No-op. Cursor stays locked to first field. |
| **`Esc`** | Active field has dirty/typed text | Clears all uncommitted text in the current field only. | Focus stays locked on the now-blank field. |
| **`Esc`** | Field is blank & form is pristine | Aborts active view immediately. | Closes page/modal; returns to previous screen. |
| **`Esc`** | Field is blank & form has dirty data | Halts exit; prevents data loss. | Triggers confirmation: *"Quit without saving? (Y/N)"*. |
| **`Esc`** | Inside active dropdown/lookup list | Collapses overlay menu. | Retains focus on the parent input field. |

---

## 3. Element Inclusion & Exclusion Rules

| Element / Control Type | Natural Traversal Chain (`Enter`) | Reverse Chain (`Backspace`) | Direct Access Method |
| --- | --- | --- | --- |
| **Header Inputs** (Dates, Doc No, Ledgers) | Included | Included | Sequential Focus |
| **Grid Row Inputs** (Items, Quantities, Rates) | Included | Included | Sequential Focus |
| **Footer Inputs** (Charges, Narration) | Included | Included | Sequential Focus |
| **Calculated Displays** (Taxes, Net Totals) | Excluded (Auto-skipped) | Excluded (Auto-skipped) | None (Read-only) |
| **Save / Post Action** | Included (Final Node Only) | Included (Backs out) | `Ctrl + A` or `Enter` at end |
| **Quit Action** | Excluded | Excluded | `Esc` or Mouse Click |
| **Cancel / Reset Action** | Excluded | Excluded | `Alt + C` or Mouse Click |
| **Delete Row / Voucher Action** | Excluded | Excluded | `Alt + D` or Mouse Click |

---

## 4. Main Voucher Traversal Lifecycle

### Step 1: Header Section

1. Focus automatically initializes on **Row 1, Field 1** on page mount.
2. Traversal advances left-to-right via `Enter`.
3. Upon confirming the final field of Row 1, focus wraps automatically to **Row 2, Field 1**.
4. Read-only fields (e.g., party balances, auto-numbering) are automatically bypassed.

### Step 2: Line Items Grid

1. Focus enters **Row 1, Column 1** of the grid.
2. Traversal steps through all active editable columns (`Item Name` → `Qty` → `Rate` → `Discount`).
3. Pressing `Enter` on the final column commits the row and immediately spawns **Row 2**, landing on **Row 2, Column 1**.
4. **Table Exit Rule:** When the cursor rests on Column 1 of a newly spawned blank row, pressing `Enter` without entering text breaks out of the grid and transitions focus to the Footer.

### Step 3: Footer & Final Commitment

1. Focus traverses additional ledger expenses, tax allocations, and Narration.
2. Pressing `Enter` past Narration lands directly on the **Save / Post** button.
3. Pressing `Enter` on Save writes the transaction and resets the form.

---

## 5. Modal Flow: Job Work Out Order — Material Allocation

### Phase 1: Process & Stage Loop

1. Modal opens → Focus mounts automatically on **Job Worker Details**.
2. Sequence: `Job Worker Details` → `Enter` → `Process Details` → `Enter` → `Output Godown` → `Enter` → `Expected Date` → `Enter` → `Expected Rate` → `Enter` → **`+ Stage` Button**.
3. Pressing `Enter` on `+ Stage` commits the stage block and opens a new process iteration, cycling focus back to **Job Worker Details**.
4. **Stage Termination:** On the new iteration, if the user leaves **Job Worker Details blank** and presses `Enter`, the system skips all remaining stage fields and jumps focus directly to the **`Enter Component`** button.

### Phase 2: Component Entry Grid

1. Pressing `Enter` on **`Enter Component`** initializes the component grid, placing focus on **Row 1, Column 1** (`Component Item Name`).
2. Data entry proceeds across row columns via `Enter`.
3. Pressing `Enter` on the final column commits Row 1 and spawns **Row 2, Column 1**.
4. Row 2 details are completed; pressing `Enter` on the final column commits Row 2 and spawns **Row 3, Column 1**.
5. **Modal Exit Trigger:** On **Row 3**, leaving Column 1 completely blank and pressing `Enter` signals the end of component entry:
   * The blank 3rd row is discarded.
   * The Material Allocation modal closes immediately.
   * Keyboard focus returns to the main Job Work Out Order screen at the next sequential field.

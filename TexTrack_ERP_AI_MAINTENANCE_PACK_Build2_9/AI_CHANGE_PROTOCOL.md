# AI Change Protocol — Credit-Efficient TexTrack Development

## Rule 1: Classify the task before opening source

### Level 1 — Visual-only

Examples:

- spacing
- hierarchy indentation
- clipping
- paging controls markup
- table alignment

Inspect only:

- target `.razor`
- matching `.razor.css`
- focused browser test

### Level 2 — Local keyboard/navigation

Examples:

- Esc returns incorrectly
- focus is lost
- row restoration fails
- report-to-menu return fails

Inspect only:

- target `.razor`
- `MainLayout.razor` if menu-related
- focused browser test
- shared keyboard framework only if the problem reproduces across unrelated pages

### Level 3 — Report calculation/query

Examples:

- wrong pending amount
- wrong weighted rate
- missing row from server query

Inspect only:

- exact report service method
- exact report DTO
- target Razor page
- focused integration test

### Level 4 — Transaction/business engine

Examples:

- posting is wrong
- save fails
- FIFO allocation is wrong
- rollback fails

Inspect:

- relevant repository
- entities/mapping
- migration chain if schema-related
- transaction/integration tests

Level 4 requires explicit evidence. Do not escalate a visual bug to Level 4.

---

## Rule 2: Follow the narrow-review sequence

1. Read `CURRENT_BASELINE.md`.
2. Read the relevant entry in `TEXTRACK_CODE_MAP.md`.
3. Read the issue in `KNOWN_ISSUES.md`.
4. Open only the primary files.
5. Reproduce or identify the exact code path.
6. Expand review only when a concrete call/reference requires it.
7. Make the smallest possible change.
8. Run focused tests.
9. Run the frozen-module smoke tests relevant to that route.
10. Update documentation only if architecture or scope changed.

---

## Rule 3: Do not perform broad “safety” rewrites

Forbidden by default:

- reformatting entire files
- renaming unrelated methods
- reorganizing folders during a bug fix
- touching repositories for a CSS issue
- touching migrations for an Esc issue
- changing shared keyboard code for one page before proving a shared defect
- replacing stable IDs with names

---

## Rule 4: Require a change boundary statement

Before coding, the AI must state:

- task level
- primary files to inspect
- files allowed to change
- protected files not to touch
- targeted tests to run

If evidence later requires expanding scope, the AI must explain why before changing additional files.

---

## Rule 5: Testing strategy

### Focused test first

Run the smallest test that proves the fix.

### Then regression smoke tests

For report UI/navigation patches:

- report browser test
- one keyboard-context test
- build
- application startup

Do not rerun every database integration fixture when no service/model/schema file changed, unless the project’s release policy requires a final full run.

### Full suite before release package

A final distributable build should still run the full accepted suite, but source review remains narrow.

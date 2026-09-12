# Full Voucher Audit History — Requirements

Recorded: 2026-09-04

## Status

User-requested requirement, documented only. The recent authentication audit and process-charge audit fixes do not implement this complete capability. This is a separate audit/accountability requirement, not a broader ERP industry feature and not authority to expand the current bug-fix pass without scheduling it explicitly.

## User-facing behaviour

- Each voucher shows its creator and creation timestamp, last editor and last edit timestamp.
- A Show Full History action opens an ordered revision timeline.
- The original saved voucher is readable as a full snapshot.
- Each subsequent successful edit records actor, time, full resulting snapshot, and a comparison identifying old/new values, added lines, removed lines and changed lines.
- Cancellation and deletion record actor, time, reason and the relevant final/pre-deletion snapshot.
- Deleted vouchers and all their revisions remain accessible through a permission-controlled Audit Trail menu, clearly marked deleted and read-only.

## Snapshot content and identity

Preserve the header, line identities and ordering, items/variants, quantities and units, rates/amounts, godowns, jobbers, process assignments, BOM/revision references, voucher references and status applicable to the voucher type. Preserve display names as they appeared at that revision alongside stable IDs; later master renames must not rewrite history.

Use an immutable voucher identity, company and financial-year identity, voucher type/number, revision number, authenticated actor identity and recorded timestamp. Voucher-number reuse must not merge unrelated histories. Snapshot schema must be versioned so older history remains renderable after application upgrades.

## Integrity requirements

- Commit voucher mutation and its successful history entry atomically. A failed mutation must not appear as a successful revision; failed attempts, if recorded, must be identified separately.
- Retain history independently of the live voucher: no cascading deletion when the voucher is removed.
- History must be append-only through application workflows; prohibit ordinary users and administrators from editing, deleting or disabling the trail. Database-level privileges/tamper safeguards require explicit design review, not a claim of absolute immutability.
- Cover all supported mutation paths, including imports, APIs, maintenance, automated status changes and developer operations; distinguish system actions from user edits.
- Concurrent edits must not overwrite history or duplicate revision identities.
- Apply company isolation and audit-view permissions on the server, including deleted-record searches, snapshots and exports.
- Do not include passwords, credentials, session tokens or unrelated secrets in snapshots.
- Preserve history through normal maintenance. Define retention, backup/restore and exceptional data-purge policy with compliance review; no silent audit wipe.

## Existing data

Reuse valid existing revision evidence where available. If earlier contents were never recorded, do not fabricate them. Preserve a clearly labelled baseline snapshot of the current state and distinguish its capture time from the original creation time. Do not claim that future capture repairs historical audit gaps.

## MCA regulatory context — qualified

The proviso to Rule 3(1) of the Companies (Accounts) Rules, 2014 addresses companies using accounting software for books of account for financial years commencing on or after 1 April 2023. It requires transaction audit trails, dated edit logs of changes to the books, and a trail that cannot be disabled. Reference: [ICSI compilation of the rule](https://e-book.icsi.edu/Actpagedisplay.aspx?PAGENAME=18052) (search-index text checked on 2026-09-04; direct page retrieval timed out).

Do not describe this as applying automatically to every manufacturer or every business entity. Applicability to a customer's legal entity and TexTrack's role in maintaining or feeding the books should be confirmed with its chartered accountant/statutory auditor.

Full snapshots, side-by-side comparisons and the proposed menu are our implementation requirements; the cited rule does not prescribe this exact UI. A history button alone does not establish MCA compliance. Before making compliance claims, review capture coverage, non-disablement, tampering controls, retention, backups, audit reporting obligations and the complete deployed accounting environment with the customer's auditor.

## Acceptance cases for the eventual build

1. Create, edit twice, cancel or delete a voucher; every successful state and actor remains readable in order.
2. Change line quantities, rates, variants and godowns; compare revisions accurately without relying solely on mutable row positions.
3. Rename a master or remove the live voucher; historical snapshots remain intelligible and accessible to authorized users.
4. Reject a voucher save; no successful revision is committed. Inject a history-write failure; voucher mutation also rolls back.
5. Race two edits; no lost revision or overwritten successful history.
6. Attempt cross-company access, unauthorized export, trail disablement and maintenance deletion; verify controls.
7. Restore a backup and render older-version snapshots correctly.
8. Display legacy baseline records honestly without invented historical changes.

Implementation scope and priority must be agreed before coding. Reconcile existing JWO revision storage and general audit logs with this requirement rather than creating overlapping, inconsistent histories.

# A04 — Full Voucher Audit History

Date completed in source: 9 September 2026

## Outcome

TexTrack now has a general, read-only voucher revision trail for the implemented
production voucher engine:

- Job Work Out Order (JWO)
- Master Job Order (MJO)
- Material Out (MO)
- Material In (MI)
- the supported Tally XML import/update/cancellation paths for those vouchers

Every successful create, alteration, cancellation and eligible deletion is
recorded in the same database transaction as the business change. Automated JWO
status changes caused by MO/MI are recorded as distinct system revisions.

This implementation does not replace the existing `audit_logs` table or the
business-level JWO/BOM revision entities:

- `audit_logs` continues to record attempts and concise operational events.
- JWO/BOM revisions continue to pin production structure and BOM identity.
- `voucher_audit_revisions` stores the complete evidentiary state of a voucher at
  each successful lifecycle point.

## Database changes — Migration 028

File: `Data/Migrations/028_full_voucher_audit_history.sql`

1. Adds `vouchers.audit_identity uuid`.
2. Backfills every existing voucher with `gen_random_uuid()`.
3. Makes the identity required, default-generated and unique.
4. Creates `voucher_audit_revisions` with:
   - immutable voucher identity and the former numeric live-voucher ID;
   - company, financial year, voucher type and voucher number as values captured
     at revision time;
   - sequential revision number and snapshot schema version;
   - action, reason, authenticated actor and UTC time;
   - exact full-snapshot and comparison JSON text;
   - content hash, previous-chain hash and chain hash.
5. Adds uniqueness and lookup indexes.

There is deliberately no foreign key from retained history to the live voucher,
company, period or master rows. This is what lets evidence remain readable after
the live voucher is deleted or a display name later changes.

The migration is additive and does not rewrite business voucher contents. On a
large production database, the UUID backfill and unique-index creation still
require a maintenance window and a verified backup. Source completion does not
mean this migration has been applied to the currently running database.

## Snapshot coverage

`Services/VoucherAuditHistoryService.cs` captures the voucher header and the
applicable rows from:

- JWO finished goods, size/colour allocations, components, processes, frozen BOM
  stages and stage/jobber assignments;
- MJO finished goods and variant allocations;
- MO header detail and material lines;
- MI header detail, stage outputs, variant allocations, consumption and source-MO
  allocations;
- stock movements;
- upstream/downstream voucher links;
- Tally sync metadata.

Relevant item, UQC, colour, size, godown, jobber, process, BOM, voucher-type,
company and period names are copied beside their stable IDs. Passwords, sessions,
credentials and raw Tally XML payloads are not included.

Line additions, removals and field changes are calculated against the previous
full snapshot. Stable row IDs or stable keys are used before positional fallback.

## Integrity and concurrency

- Callers must already hold the voucher's database transaction; audit recording
  fails otherwise.
- The business mutation and audit revision commit or roll back together.
- A PostgreSQL transaction advisory lock keyed by immutable voucher identity
  serializes revision-number allocation.
- Exact snapshot text is SHA-256 hashed.
- Each revision hash includes the previous chain hash, immutable identity,
  revision number, action, content hash, normalized UTC time and actor.
- The read side recomputes the hashes and chain sequence. The UI displays either
  `Integrity verified` or `INTEGRITY WARNING`.

This is tamper-evident at application level, not proof against a database owner.
Database-role separation, trigger/privilege hardening and external evidence
anchoring require a separate deployment/security decision.

## Existing vouchers

After a successful database bootstrap, startup captures one current-state
baseline for each voucher with no full-history row. It is explicitly labelled as
a legacy baseline and states that earlier revisions are unknown. TexTrack does
not present that baseline as the original creation event.

## Read-side security and UI

- `SecurityPolicies.ViewAuditTrail` permits Developer, Administrator, Owner and
  Manager roles only.
- Both list and detail queries enforce the current company on the server.
- Operators and Viewers cannot load full history merely by knowing an identity.
- Current voucher stamps use `ViewReports`, but first resolve the live voucher's
  immutable identity inside the active company and financial year.
- The Reports menu contains `Voucher Audit Trail`, including deleted-voucher
  search and a read-only ordered revision timeline.
- Persisted JWO, MJO, MO and MI views show first evidence, last action/actor and a
  permission-controlled `Show Full History` action.

## Maintenance behavior

Normal developer business-data reset deliberately excludes
`voucher_audit_revisions`. Audit history therefore survives the existing reset
workflow. Retention duration, exceptional legal purge, backup and restore policy
remain deployment/governance work and must not be inferred from this exclusion.

## Verification performed

- Isolated build: succeeded with zero warnings and zero errors.
- Focused audit, migration and bootstrap tests: 16/16 passed.
- Full .NET integration suite: 210/210 passed.
- Shared JavaScript behavior suite: 73/73 passed.

The automated cases cover create/update/delete retention, accurate field diffs,
legacy-baseline honesty, mandatory transaction use, hash-chain verification and
tamper detection, role enforcement, company isolation, numeric-ID reuse, complete
migration-chain creation, migration integrity, rollback and concurrent bootstrap.

All database tests use disposable PostgreSQL schemas. The running TexTrack app
was not stopped or restarted, and its live database was not migrated during this
source implementation.

## Honest remaining boundaries

- Generic accounting voucher pages that are still placeholders have no business
  mutation flow to audit yet.
- Direct out-of-band SQL changes are outside repository interception.
- Audit export is not exposed; if added, it must enforce the same policy and
  company boundary.
- Ordinary deletion currently records the system-validated reason for why the
  delete was eligible; cancellation retains the user's entered reason.
- Backup/restore drills, retention policy, database-level append-only privileges
  and legal/compliance review are separate production-readiness gates.
- No claim of MCA or other statutory compliance should be made solely from this
  feature. Applicability and the deployed control environment require review by
  the customer's auditor.

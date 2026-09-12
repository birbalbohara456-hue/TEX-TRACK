# TexTrack ERP Audit Remediation — 03 September 2026

This document identifies the exact repository changes made in response to the independent audit. It is intended as an entry point for a follow-up audit. Business workflows were preserved; the changes strengthen infrastructure, database invariants and concurrency behavior.

## Completed in this remediation

### Migration runner integrity

- `DatabaseBootstrapper` now computes every packaged migration checksum before deciding to skip it.
- An applied version must match both the recorded file name and SHA-256 checksum. A mismatch marks the database unavailable and refuses normal connected startup; it never reruns or rewrites the migration.
- A PostgreSQL session advisory lock serializes migration initialization across application instances.
- Migration SQL and its `schema_versions` row remain in the same transaction.
- `DatabaseBootstrapperTests` exercises first application, idempotent second startup, checksum tampering, failed-migration rollback and simultaneous bootstrappers.

### Migration 025 — relational integrity

`025_audit_integrity_hardening.sql` is additive and does not rewrite historical vouchers.

Before adding constraints it checks for:

- a child BOM revision belonging to a different child BOM;
- a stage/assignment pair where only one identifier is populated;
- an assignment that belongs to a different BOM stage.

If any mismatch exists, the migration aborts with a specific exception and its transaction rolls back. If data is clean, it adds composite foreign keys for BOM child revision pairs and the stage/assignment pairs on Material Out, Material In finished outputs and Material In consumption.

### EF/migration parity

`MigrationChainTests` builds migrations 001–025 from an empty PostgreSQL schema and checks critical EF mappings against the resulting PostgreSQL column names and nullability. It also verifies the four new composite foreign keys.

### BOM concurrency

- Structural BOM save/delete operations now use `Serializable` isolation.
- A transaction-scoped advisory lock serializes BOM graph mutations.
- Serialization conflicts return a safe reload-and-retry result rather than exposing a database exception.
- The concurrency test simultaneously attempts A→B and B→A and verifies exactly one operation commits.

### Voucher numbering

- `VoucherSequenceAllocator` is the shared atomic PostgreSQL allocator.
- New JWO and Master Job Order saves now reserve through `voucher_sequences`, matching Material Out and Material In.
- Display-only number previews remain non-reserving.
- A test runs 20 simultaneous reservations and verifies unique, gap-free results.

### Stock semantics and validation

- `StockMovementSemantics` is the canonical movement vocabulary and WIP classification source used by operational reports.
- `StockPostingService` now rejects every zero-quantity movement, aligning application behavior with PostgreSQL. Value-only changes require a future dedicated valuation transaction rather than masquerading as a physical movement.

### Audit and HTTP security

- Initial JWO stage assignments record the authenticated immutable actor; `Reason` retains the operation description.
- Authentication and first-run setup endpoints have IP-partitioned fixed-window rate limiting.
- Production authentication cookies are always Secure.
- Production startup refuses blank or wildcard `AllowedHosts` configuration.
- The development PostgreSQL setup generates a random 256-bit application password and stores it in .NET User Secrets instead of embedding a universal password.

### Production-engine freeze follow-up

- Material Out cancellation is refused when any active Material In allocation consumes the dispatch; the message identifies the linked Material In voucher.
- Material Out and Material In dates cannot precede their JWO date.
- Material In enforces the ordered ceiling per colour/size variant, not only at aggregate finished-good level.
- BOM-driven Material In enforces proportional component consumption. A receipt cannot capitalize component value while understating consumption.
- Manual/non-BOM JWOs use nullable persisted stage identities; the UI sentinel is never stored as a fake foreign key.
- JWO status is derived after MO/MI save, alteration, cancellation and deletion. Only final-stage output receipts complete a JWO; intermediate receipts are production progress.
- Pending-finished-goods, Job Worker Control, Aging and T-format final-output totals exclude intermediate-stage receipts.
- Material Out and Material In use the shared voucher sequence allocator. Voucher types with `ResetPeriod = Never` continue across financial years.
- Database maintenance preserves the audit trail and records the authorized clear operation.
- The migration runner requires a contiguous packaged sequence. Two exact, independently probed legacy migration identities are accepted through a closed compatibility manifest without rewriting `schema_versions` history.

### Migration 026 — production-freeze constraints

`026_production_freeze_constraints.sql` does not update or delete historical voucher data. It first aborts if an unknown stock movement kind exists or if a BOM points at another BOM's revision. On clean data it restores the complete eight-value movement-kind check and replaces the single-column current-revision relationship with a composite `(current_revision_id, id) -> (id, bom_id)` foreign key. It must be rehearsed against a restored production backup before deployment; this remediation did not apply it to the live database.

### Verification snapshot

- Isolated application build: succeeded with 0 warnings and 0 errors.
- PostgreSQL integration suite: 203 passed, 0 failed, 0 skipped.
- JavaScript shared-UI suites: 5 files passed in the preceding UI verification run.
- Tests use disposable database schemas. No production migration was executed during verification.

## Deliberately not represented as completed

The following require explicit product/deployment architecture or business policy and should not be hidden inside this correctness patch:

- PostgreSQL RLS and company-scoped roles;
- universal audit interception/tamper-resistant external audit storage;
- automatic retries for every serializable voucher workflow (requires an idempotency design);
- complete perpetual weighted-average valuation for Purchase, Sales, Stock Journal and backdating;
- company-configurable exception thresholds;
- production backup retention, off-site replication and verified restore operations;
- self-contained Windows Service/client installers;
- persistent rolling/centralized production logging and deployment health monitoring;
- trusted reverse-proxy networks and forwarded-header configuration, which depend on the actual host topology.
- a company-approved Material Out rate policy (strict JWO/BOM rate, authorized variance, or warning threshold);
- a controlled yield/scrap variance workflow around strict proportional BOM consumption;
- MJO allocation ceilings are not applicable: clarified on 2026-09-04 that MJO groups existing JWOs and is not an independent production-demand authorization;
- a final decision on whether component variant identity must be persisted below the current item/UQC/component linkage.

These are deployment or future-module work, not silently fixed by weakening current controls. A follow-up auditor should report them as open scope unless a deployment package supplies them independently.

## Verification commands

```powershell
dotnet build TexTrack.sln --no-restore
dotnet test TexTrack.sln --no-restore --verbosity minimal
```

The follow-up audit should use an immutable commit/archive and record its hash. The current development working tree contains accumulated unreleased module work and must not be mistaken for a tagged production release. The production engine is a freeze candidate after the open business-policy decisions and a backup/restore migration rehearsal; the full application is not yet certified as production-ready.

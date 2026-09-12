# TexTrack ERP — WebVella Pre-FIFO Architecture Audit

**Date:** 11 September 2026  
**TexTrack baseline:** `bc7b028a03bd29f2821d81ea75a869b7027a135a` (`codex/negative-stock-policy-2026-09-11`)  
**WebVella baseline:** [`c5fd3f800d29865726dcf5df582e6454424574c7`](https://github.com/WebVella/WebVella-ERP/tree/c5fd3f800d29865726dcf5df582e6454424574c7) (4 September 2026)  
**Method:** Static source comparison of the pinned revisions. No TexTrack application code, schema, migration, UI, or runtime behavior was changed.

## Purpose and limits

This audit uses WebVella as an external architectural reference before TexTrack begins perpetual FIFO work. It compares:

- authentication, authorization and multi-user behavior;
- database migration integrity;
- transactions and concurrency;
- audit/logging facilities;
- extensibility and background processing; and
- operational/recovery readiness.

WebVella's UI and business workflows are outside scope. Its code is evidence of one implementation, not an industry certification or a specification TexTrack should copy. No claim below means that either application has completed penetration testing, production load testing, or a formal compliance audit.

## Executive verdict

TexTrack should proceed to FIFO design after a small set of explicit safeguards listed in this document. A broader WebVella audit does **not** need to delay FIFO.

The comparison does not reveal a superior WebVella inventory-valuation engine that TexTrack should adopt. TexTrack is already materially stronger in the areas that protect inventory history: password/session controls, migration immutability, company-scoped authorization, serializable stock transactions, exact-position negative-stock locking, and tamper-evident voucher revisions.

WebVella's principal architectural advantage is extensibility: plugins, hooks, metadata-driven entities/pages, scheduled jobs and background execution are first-class concepts. TexTrack can learn from the separation of these facilities, but should not place dynamic code or bypass-capable plugins inside its inventory posting core.

The correct conclusion is therefore:

> Keep TexTrack's existing security, migration and posting foundations. Adopt only narrow operational patterns from WebVella—especially a durable run envelope and typed extension boundaries—then build FIFO on TexTrack's own non-bypassable stock engine.

## Comparison matrix

| Area | WebVella evidence | TexTrack evidence | Decision |
|---|---|---|---|
| Password security | Raw, unsalted MD5 in [`PasswordUtil.cs`](https://github.com/WebVella/WebVella-ERP/blob/c5fd3f800d29865726dcf5df582e6454424574c7/WebVella.Erp/Utilities/PasswordUtil.cs) and comparison in [`SecurityManager.cs`](https://github.com/WebVella/WebVella-ERP/blob/c5fd3f800d29865726dcf5df582e6454424574c7/WebVella.Erp/Api/SecurityManager.cs). | PBKDF2-SHA256 with random salt, legacy upgrade, failed-attempt counting and lockout in `Services/UserIdentityService.cs`. | **Retain TexTrack. Reject WebVella pattern.** |
| Session security | Cookie/JWT hybrid; login cookie is assigned an extremely long expiry in [`AuthService.cs`](https://github.com/WebVella/WebVella-ERP/blob/c5fd3f800d29865726dcf5df582e6454424574c7/WebVella.Erp.Web/Services/AuthService.cs). Default CORS permits any origin/method/header in [`Startup.cs`](https://github.com/WebVella/WebVella-ERP/blob/c5fd3f800d29865726dcf5df582e6454424574c7/WebVella.Erp.Site/Startup.cs). | Strict SameSite, HttpOnly, production Secure cookie, fixed two-hour lifetime, fallback authenticated policy, HSTS/HTTPS and login rate limiting in `Program.cs`; persisted server-side revalidation in `Services/PersistedSessionValidator.cs`. | **TexTrack stronger.** Still test production TLS and Data Protection persistence. |
| Authorization | Entity CRUD permission checks through `SecurityContext`; flexible role model. | Named server policies, server-side persisted-session revalidation, active company membership and company/FY claims in `Services/SecurityPolicies.cs`, `Services/DatabaseStatus.cs` and `Services/PersistedSessionValidator.cs`. | **Retain TexTrack's fail-closed business scope.** WebVella's entity-permission vocabulary is useful only for future typed extensions. |
| Multi-user requests | `AsyncLocal` request context plus a concurrent context dictionary in [`DbContext.cs`](https://github.com/WebVella/WebVella-ERP/blob/c5fd3f800d29865726dcf5df582e6454424574c7/WebVella.Erp/Database/DbContext.cs). | Scoped services/DbContexts, persisted-session validation, serializable voucher work and transaction-level stock-position locks. | Both support concurrent web requests; **TexTrack has stronger inventory collision protection**. Load tests are still required. |
| Tenant/company isolation | No core company/tenant isolation boundary was found; deployment is effectively one application database scope. | Company and FY are explicit business scopes with membership revalidation and query tests. PostgreSQL RLS is not yet present. | **Do not use WebVella as a cloud-tenancy reference.** Before cloud subscriptions, choose database-per-tenant or add database-enforced RLS. |
| Core migrations | Integer version checks and transactional upgrades in [`ERPService.cs`](https://github.com/WebVella/WebVella-ERP/blob/c5fd3f800d29865726dcf5df582e6454424574c7/WebVella.Erp/ERPService.cs). Plugin patches are versioned and transactional. | Contiguous numbered chain, SHA-256 checksums, immutable applied files, compatibility manifest, future-version rejection, global advisory lock and per-migration transactions in `Data/DatabaseBootstrapper.cs`. | **TexTrack materially stronger.** Preserve this design for FIFO migrations. |
| Transaction composition | Async-local transaction context, nested connections/savepoint support and an advisory-lock helper in [`DbConnection.cs`](https://github.com/WebVella/WebVella-ERP/blob/c5fd3f800d29865726dcf5df582e6454424574c7/WebVella.Erp/Database/DbConnection.cs). | Serializable posting repositories and shared `StockPostingService`; exact stock positions receive deterministic transaction advisory locks before negative-stock validation. | Learn from composable transaction boundaries, but **do not lower TexTrack to default Read Committed** for valuation-critical work. |
| Concurrency control | Advisory-lock helper exists, but no callers were found in the inspected revision; no general optimistic-concurrency or database-safe multi-node job-claim mechanism was found. | Unique constraints/concurrency tokens, BOM advisory locking, voucher allocation safeguards and exact-position posting locks. | **TexTrack stronger for stock.** Add explicit FIFO allocation/replay concurrency tests. |
| Audit history | System log records type/source/message/details; many records carry creator metadata and hooks/notifications. | General `audit_logs` plus full voucher snapshots, change JSON, content hash, previous hash and chain hash in `Services/VoucherAuditHistoryService.cs`. | **TexTrack stronger for business evidence.** Add operation/run correlation IDs for FIFO replay. |
| Background work | Persisted jobs, schedules, priorities and terminal states in [`Jobs/`](https://github.com/WebVella/WebVella-ERP/tree/c5fd3f800d29865726dcf5df582e6454424574c7/WebVella.Erp/Jobs). | No generic durable background-job platform is required by current synchronous posting. | **Adopt the durable envelope concept**, but add leases, idempotency, heartbeat/attempt data and database-safe claiming before distributed execution. |
| Extensibility | Strong plugin model in [`ErpPlugin.cs`](https://github.com/WebVella/WebVella-ERP/blob/c5fd3f800d29865726dcf5df582e6454424574c7/WebVella.Erp/ErpPlugin.cs), hooks, metadata, data sources, pages, jobs and schedules. | Domain-specific services and reusable UI controls; broader customization remains planned. | **WebVella stronger. Defer a general plugin platform.** Introduce narrow typed interfaces only where a real TexTrack extension use case exists. |
| Testing/CI | No conventional automated test project or GitHub Actions workflow was found in the pinned repository. | Extensive .NET integration and JavaScript regression suites; latest verified pre-audit result was 269/269 .NET and 74/74 JavaScript. No GitHub Actions workflow yet. | **TexTrack stronger in tests; CI remains open.** Add a protected automated workflow before broad FIFO rollout/release. |
| Backup/recovery | No project-native PostgreSQL backup/restore verification workflow was found. | Backup and isolated restore drill, table fingerprints and sequence/compatibility checks in `Tools/`. | **TexTrack stronger.** Production roles/ACL, key custody, off-machine schedule/PITR and deployment drill remain open. |

## Security and multiple-user handling

### What WebVella demonstrates

WebVella isolates a database context per asynchronous request flow using `AsyncLocal`, and its user/role model supports multiple authenticated users. Entity managers consult `SecurityContext` for CRUD permissions. This is a useful proof that its metadata-driven platform was designed for concurrent authenticated requests rather than as a single-user desktop process.

That does not make it a security baseline for TexTrack. The pinned source contains several patterns TexTrack must not adopt:

1. Passwords are reduced to unsalted MD5 values.
2. The login cookie is assigned a 100-year expiry.
3. Default CORS accepts every origin, method and header.
4. Sample/runtime `Config.json` files contain reusable database/JWT/encryption defaults that would be unsafe if deployed unchanged.
5. No rate-limit or account-lockout implementation was found in the authentication path.
6. Middleware user reload paths need careful disabled-user/session-revocation review.
7. A general code-compilation endpoint exists in the authorized API surface; it must be protected as a highly privileged extension operation in any real deployment.

Razor handlers show explicit antiforgery/ModelState checks in several pages, so it would be inaccurate to say WebVella has no CSRF handling. However, the inspected startup/API surface did not establish one uniform, globally provable antiforgery contract for every cookie-authenticated mutation. TexTrack should retain its explicit antiforgery middleware and endpoint validation rather than infer protection from individual pages.

### What TexTrack demonstrates

TexTrack currently has the stronger multi-user security boundary:

- a user must be active and have an active company membership and role;
- login failures lock the account temporarily;
- persisted sessions are revalidated against user state, membership, roles, company and financial year;
- password/role/membership changes can revoke the effective session;
- policy checks are server-side, not only hidden buttons;
- authentication is rate-limited; and
- production cookies and transport have stricter defaults.

For simultaneous inventory work, TexTrack also has a stronger data-integrity boundary. A transaction changing an exact `company + item + variant/base + UQC + godown` position obtains a deterministic PostgreSQL transaction lock and rechecks the final transaction state. This prevents two users from both consuming the same last physical stock when negative stock is blocked.

### Remaining proof required

Source structure is not a load test. Before release, TexTrack still needs behavioral tests with multiple authenticated sessions for:

- simultaneous outward posting against the same and different stock positions;
- simultaneous sequence allocation;
- one user's company/FY switch not leaking into another circuit/session;
- role or membership revocation during an active session; and
- FIFO layer consumption/replay under concurrency.

Cloud tenant isolation is a separate architecture decision. Application-level `company_id` checks are strong but cannot prove that a future missed predicate will never leak data. Database-per-tenant gives the hardest boundary; shared database plus PostgreSQL RLS gives database-enforced company isolation with more operational complexity. This decision is required before hosting unrelated customers in one cloud environment, not before local FIFO development.

## Migration integrity

WebVella's useful migration pattern is that core upgrades and plugin patch sequences run transactionally and save their plugin version only with the associated changes. That atomicity is worth retaining in every TexTrack schema evolution.

TexTrack already goes further:

- a single global PostgreSQL advisory lock serializes migration startup;
- migration numbers must be contiguous and unique;
- every applied file is recorded with SHA-256;
- changing an already-applied migration is rejected;
- narrowly reviewed legacy checksum equivalents are explicit rather than silent;
- a database newer than the application is rejected; and
- each migration commits atomically with its version record.

No WebVella pattern justifies weakening these rules. FIFO tables must be introduced only by new migrations. An applied migration must never be edited to accommodate a later FIFO correction.

Every FIFO migration should be classified before approval as one of:

- additive schema only;
- deterministic backfill with reconciliation totals;
- constraint activation after preflight cleanup; or
- destructive/semantic conversion requiring a separately rehearsed upgrade plan.

## Transaction, concurrency and FIFO implications

WebVella supplies a reusable transaction context, nested savepoints and a lock helper. Those are general platform conveniences, not proof of correct inventory valuation. Its normal database operations use PostgreSQL's default isolation unless a caller establishes something stricter, and the audit found no implemented perpetual-FIFO layer/allocation model to copy.

TexTrack must keep one non-bypassable posting boundary. Razor forms, imports, APIs and future extensions may prepare a command, but only the domain posting service may create stock movements and FIFO allocations.

The FIFO transaction must atomically:

1. lock every affected exact stock position in deterministic order;
2. validate period, chronology, company, UQC, variant and godown identity;
3. select eligible inward layers by immutable valuation order;
4. allocate the outward quantity without over-consumption;
5. create stock movement and layer-allocation records;
6. carry actual material value into WIP/intermediate/finished output;
7. write the voucher audit revision and operation/run identity; and
8. commit all effects or none.

Do not use an editable MO reference rate as the book valuation when FIFO layers exist. It may remain a visible/reference field, but the stock value must be calculated from allocated inward layers. Manufactured/intermediate output continues to use actual allocated material cost plus actual conversion/job-work cost.

## Extensibility: adopt the boundary, not unrestricted dynamism

WebVella's cleanest lesson is separation: plugins have identity/version data; hooks and job types are registered; metadata and page components are distinct concepts. TexTrack should preserve this idea in its broader ERP expansion plan.

For the current production engine, the safe extension model is narrower:

- typed, server-registered strategy interfaces;
- explicit permission declarations;
- company-aware execution context;
- migration/version declaration;
- no direct write access to stock movements, cost layers, allocations or voucher audit chains;
- commands routed through the same domain validation as native entry; and
- an audit event naming the extension and version that requested a change.

A future analytical valuation dropdown may use registered read-only valuation strategies. The company's book method remains a controlled setting, and changing a report view must never rewrite transactions or layers.

Dynamic code compilation, database-written runtime code, arbitrary plugins and a marketplace are deliberately deferred. They expand the attack and integrity surface and are unnecessary for FIFO.

## Durable FIFO rebuild/replay envelope

WebVella's persisted job records are a useful starting concept, but not sufficient for a valuation replay that may run in multiple processes. Before TexTrack permits asynchronous rebuild/replay, it needs a durable run record containing at least:

- immutable run ID and idempotency key;
- company and requested valuation scope/cutoff;
- algorithm/schema version;
- requested-by, requested-at, started-at and finished-at;
- state: Pending, Running, Succeeded, Failed or Cancelled;
- attempt count and failure detail safe for logs;
- worker lease owner and lease expiry/heartbeat;
- earliest affected valuation order and affected-position count;
- reconciliation totals before/after; and
- correlation to audit events and changed allocations.

Database-safe claiming must prevent two workers from running the same replay. A unique idempotency key prevents a retried request from creating a second run. The valuation positions should be marked pending/locked so reports do not present stale values as final.

This does **not** require building a generic background-job framework before FIFO Phase 1. A synchronous first implementation can persist the same run envelope and execute inside the initiating process. Leases/heartbeats become mandatory before the work is detached or distributed.

## Adoption register

### Adopt before or during FIFO foundation

| ID | Pattern | Required result |
|---|---|---|
| WV-A1 | Typed posting/valuation boundary | No UI, import or plugin can write layers or stock values directly. |
| WV-A2 | Durable replay-run envelope | FIFO build/replay has identity, version, status, actor, cutoff, result and failure evidence. |
| WV-A3 | Correlation/operation identity | Voucher audit, stock movements, allocations and rebuild logs can be traced as one operation. |
| WV-A4 | Atomic versioned evolution | Preserve TexTrack's checksum migration system; introduce only new files with preflight/reconciliation. |
| WV-A5 | Automated merge gate | Run .NET, JavaScript and database migration/integration tests in GitHub Actions before FIFO is merged for release. |

### Defer until its triggering product phase

| Pattern | Trigger |
|---|---|
| General plugin marketplace/dynamic entities/pages | After the garment production engine is frozen and extension security is separately designed. |
| Distributed background workers | When rebuild/import volume requires execution outside the request process. |
| External observability platform | Before hosted production or when operational support requires centralized telemetry. |
| Database RLS | Before choosing shared-database cloud tenancy; unnecessary if database-per-tenant is selected. |
| General metadata-driven ERP platform | Broader ERP expansion, not FIFO. |

### Explicitly reject

- unsalted MD5 or any fast password hash;
- effectively permanent authentication cookies;
- permissive production CORS;
- committed reusable secrets/default production keys;
- migration mutation without immutable checksums and future-version rejection;
- valuation-critical operations relying only on default Read Committed behavior;
- in-process-only job claiming for multi-node work;
- dynamic code execution exposed as ordinary authenticated functionality; and
- extensions writing stock movements or audit history directly.

## TexTrack gaps that remain after this comparison

| Gap | When it must close |
|---|---|
| No perpetual inward cost-layer and layer-allocation model yet | Current FIFO workstream. |
| Immutable global valuation ordering and controlled replay are not implemented | FIFO foundation, before safe backdating/revaluation. |
| Generic retry/idempotency is incomplete | Before imports/retries can create stock or replay can be retried. |
| GitHub CI workflow is absent | Before broad FIFO rollout/release merging. |
| Data Protection key persistence/custody is not production-proven | Before production deployment. |
| Production TLS, Windows service identity and real recovery exercise remain open | Before production deployment. |
| Backup schedule/off-machine retention/PITR and roles/ACL restoration remain open | Before hosted or business-critical production. |
| Cloud tenant architecture (database-per-tenant versus shared+RLS) is undecided | Before unrelated cloud customers share infrastructure. |
| Formal multi-user/load/long-transaction tests are incomplete | Before beta sign-off; FIFO concurrency cases belong in the FIFO suite. |
| Tally/import domain parity is incomplete | Before imports are permitted to establish authoritative FIFO history. |

## FIFO go/no-go decision

**Go, with four narrow gates.** WebVella provides no reason to replace or postpone TexTrack's valuation design. Before the first FIFO migration is accepted, freeze these decisions:

1. **Layer identity and order:** exact valuation position and immutable company-wide posting tie-breaker.
2. **Replay identity:** the durable run/correlation schema, even if Phase 1 executes synchronously.
3. **Chronology contract:** mapping of purchase/opening, MO, MI, cancellation/reversal, cross-FY continuity and backdating to layers.
4. **Proof contract:** reconciliation invariants, migration tests, concurrent-allocation tests and CI execution.

The negative-stock policy delivered at the TexTrack baseline is a suitable prerequisite: blocking mode serializes and validates exact positions, while allow mode is explicitly a compatibility escape hatch rather than fake FIFO valuation. FIFO must define how any permitted historical negative position is handled; it must not silently invent a final cost.

## Recommended next sequence

1. Approve/freeze this audit as a comparative evidence record.
2. Produce the FIFO Phase 1 design contract and migration plan—schema and invariants first, no posting cutover yet.
3. Add immutable valuation order, cost-layer, allocation and rebuild-run schema with migration preflight/reconciliation.
4. Post Purchase and Stock Item Opening Inventory into inward layers through the shared domain boundary.
5. Replace ordinary MO book value with FIFO allocations; retain the entered/reference rate only as non-authoritative metadata.
6. Carry allocated cost through jobber WIP, child-stage output and final MI, with cancellation/reversal symmetry.
7. Add as-on FIFO/analytical report views and third-party-godown reconciliation without rewriting transactions.
8. Enable controlled replay/backdating only after deterministic and concurrent tests pass.
9. Route Tally and all future imports through the same domain contract.
10. Run CI, concurrency/load checks, restored-backup rehearsal and production-mode verification before beta release.

## Evidence confidence

- **High:** direct source findings about password hashing, cookie expiry, CORS, TexTrack migration checks, TexTrack voucher audit chaining and negative-stock locking.
- **Medium:** absence findings such as no WebVella RLS/tenant scope, tests/CI, backup workflow, lock callers or full voucher revision chain. These were based on repository-wide searches of the pinned revision, but a separate external module or deployment layer could exist outside that repository.
- **Not assessed:** penetration resistance, dependency CVEs, infrastructure/network policy, real production performance, jurisdiction-specific accounting compliance and the correctness of WebVella business workflows.

This audit is a design gate, not a certification. Its useful result is that TexTrack can move forward without copying a weaker security or migration foundation, while borrowing WebVella's strongest idea—well-defined extensibility and durable work identities—in a constrained form appropriate for inventory valuation.

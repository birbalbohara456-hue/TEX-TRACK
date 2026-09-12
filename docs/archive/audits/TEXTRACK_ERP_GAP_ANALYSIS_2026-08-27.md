# TexTrack ERP — Production Readiness and Open-Source ERP Gap Analysis

**Audit date:** 27 August 2026  
**Compared with:** ERPNext/Frappe and Odoo architecture patterns, plus current ASP.NET Core platform guidance  
**Scope:** architecture, security, authorization, API readiness, data integrity, operations, performance, testing, and production-module differentiation

## Executive verdict

TexTrack's **production transaction engine is strong and internally coherent**. The JWO → Material Out → Material In flow, garment colour/size variants, multi-level BOM snapshots, stock posting, cancellation rules, PostgreSQL constraints, transactional repositories, and automated regression suite are credible foundations. These are the parts where TexTrack is already differentiated from general-purpose ERPs.

TexTrack is **not yet on par with ERPNext or Odoo as an internet-facing, multi-user ERP platform**. The main gap is not voucher logic. It is the platform perimeter: real users and roles, policy enforcement on every page/API, per-user company context, secret management, CSRF-safe authentication endpoints, rate limiting, versioned APIs, durable background work, and operational observability.

The recommended direction is therefore **not a rewrite and not an ERPNext/Odoo clone**. Preserve TexTrack's workflow and voucher UI. Add the proven security, API, tenancy, and operations layers around the existing domain engine.

## Current scorecard

| Area | TexTrack today | Relative position | Required action |
|---|---:|---|---|
| Production workflow/domain model | 8.5/10 | Ahead for garment job work | Preserve and extend |
| Transaction and stock integrity | 8/10 | Strong foundation | Add more concurrency/failure-path tests |
| Database constraints and migrations | 8/10 | Strong foundation | Add deployment rollback/backup runbook |
| Keyboard-first ERP usability | 9/10 | Clear differentiator | Keep as a product invariant |
| Automated regression coverage | 8/10 | Strong for project stage | Add browser E2E and security tests |
| Authentication and identity | 3/10 | Behind mature ERPs | P0 redesign |
| Authorization and permissions | 2/10 | Behind mature ERPs | P0 role/policy/record enforcement |
| Multi-company/tenant isolation | 4/10 | Schema-aware, runtime incomplete | P0 per-session context and enforcement |
| External/mobile API | 2/10 | Not yet a platform API | P1 versioned API layer |
| Background jobs and long operations | 4/10 | UI lock exists; work is not durable | P1 job infrastructure |
| Observability and operations | 3/10 | Development-level | P1 health, logs, metrics, backups |
| File/media security | 4/10 | Basic validation only | P1 content verification/storage hardening |

## Where TexTrack is already strong or ahead

1. **Garment-native variants.** Colour and size are first-class operational details rather than narration-only metadata.
2. **Single production chain.** JWO, partial Material Out, partial Material In, pending quantities, process charges, cancellation, stock impact, and reports share one domain rather than loosely connected documents.
3. **Optional multi-level BOM.** Parent and child BOMs support intermediate processes while JWO snapshots protect historical transactions from later BOM edits.
4. **Keyboard-first workflow.** Voucher entry, list navigation, lookup behavior, contained scrolling, and hard-wall rules are treated as tested product contracts.
5. **Database integrity.** PostgreSQL foreign keys, company-aware indexes, voucher-number uniqueness, concurrency tokens, audit records, and repository transactions protect important invariants.
6. **Meaningful automated coverage.** The current verification run contains 115 .NET tests and 77 JavaScript contract tests, including voucher lifecycle, stock posting, migrations, BOM, reporting, keyboard behavior, and security-adjacent developer access tests.
7. **Scale work has begun correctly.** Server-side paging and contained report grids are the right direction for high-volume registers and inventory reports.

## Evidence from the current codebase

- `Program.cs` registers Blazor Server, cookie authentication, authorization, data protection, EF Core pooling, PostgreSQL, antiforgery middleware, HSTS outside development, and the core repositories/services.
- `Data/TexTrackDbContext.cs` defines company-aware keys and indexes, voucher uniqueness, stock movement indexes, audit logs, and optimistic concurrency tokens.
- `Services/StockPostingService.cs` centralizes stock movement creation and reversal inputs.
- `Services/VoucherLifecycleService.cs` centralizes dependency checks, lifecycle state changes, audit creation, and concurrency-token renewal.
- `Data/DatabaseBootstrapper.cs` applies each migration transactionally. The discovered chain is complete and ordered from `001_initial_foundation.sql` through `020_stock_item_custom_fields_photos.sql`.
- `Services/GlobalOperationState.cs` provides a scoped busy lease so overlapping UI operations can be blocked safely within the current circuit.

## P0 — required before multi-user or network production deployment

### 1. Implement real user identity and authorization

Current state: authentication is primarily a special Developer cookie. Normal business operations do not yet have a complete persisted User → Role → Permission model. `AddAuthorization()` is registered, but broad page/endpoint policy enforcement is not visible.

Required:

- Persist users, password/identity provider references, roles, and company memberships.
- Define permissions by module and operation: View, Create, Alter, Delete, Cancel, Export, Import, Maintain Masters, and Developer Maintenance.
- Apply a fallback policy requiring authentication by default.
- Apply explicit policies to Razor components and every API endpoint.
- Add resource checks for company, voucher status, ownership/assignment where applicable, and protected developer operations.
- Keep Developer override separate, highly audited, and unavailable by default in production.

ERPNext/Frappe exposes users, roles, DocType permissions and permission levels. Odoo combines model access controls, record rules, and field-level access. ASP.NET Core supports the same enforcement style through role/claim policies and resource handlers.

### 2. Make company and financial-year context session-bound

Current state: `CurrentCompanyContext` is a singleton hard-coded to Company 1, FY 1, Demo Company. The schema and most queries are company-aware, which is good, but the runtime selection is not tied to an authenticated user/session.

Required:

- Resolve allowed companies from the signed-in user.
- Store the active company/FY in a scoped, validated context.
- Reject any request whose route/body resource does not belong to that context.
- Add integration tests attempting cross-company reads and mutations.
- Consider PostgreSQL row-level security later as defence in depth; application enforcement remains mandatory.

### 3. Secure every HTTP endpoint

Current state: `/api/tally-xml/export/{package}` and `/api/stock-item-photos/{id}` do not show explicit authorization requirements. The photo endpoint does filter by current company, but that is not a substitute for authentication/permission.

Required:

- Require authentication and operation-specific policies.
- Authorize each requested record against active company membership.
- Add cache-control/content-disposition policies for sensitive files.
- Add negative tests for anonymous, wrong-role, and wrong-company access.

### 4. Remove production secrets and unsafe diagnostics from committed configuration

Current state: `appsettings.json` contains a plaintext PostgreSQL username/password, `Include Error Detail=true`, and `AllowedHosts="*"`.

Required:

- Move secrets to Windows credential-protected configuration, environment variables, or a secrets manager.
- Commit only safe placeholders.
- Disable detailed database errors outside local development.
- Restrict allowed hosts and configure reverse-proxy forwarded headers deliberately.
- Rotate the currently committed database password.

### 5. Correct authentication endpoint protections

Current state: developer login/logout POST endpoints explicitly disable antiforgery. Cookie-authenticated browser writes should be CSRF protected. Return URL validation accepts any string beginning with `/`; it should reject protocol-relative values such as `//host`.

Required:

- Enable antiforgery on browser form POSTs.
- Validate return URLs with a local-URL helper or equivalent strict check.
- Add login throttling/lockout and security-event logging.
- Mark developer maintenance cookies `Secure` in production and define key persistence/rotation.

### 6. Make audit identity truthful and durable

Current state: audit columns/logs exist, but several operations still use generic `Developer` or `User` actor strings rather than an immutable user ID plus display snapshot.

Required:

- Propagate the authenticated actor into every command/repository.
- Store actor user ID, company ID, action, entity, before/after summary, UTC time, correlation ID, and source.
- Prevent ordinary application paths from editing/deleting audit history.
- Audit developer data clear, import/export, cancellation, deletion, and permission changes.

## P1 — required before mobile app or public/integration API

### 1. Introduce a versioned application API

Create `/api/v1` endpoints over application use-cases, not directly over EF entities. Add:

- Request/response DTOs and centralized validation.
- OpenAPI documentation and stable error envelopes.
- Cursor/keyset pagination for large datasets.
- OAuth/OIDC or short-lived bearer tokens for users; scoped API keys/service accounts for integrations.
- Idempotency keys for voucher creation/import retries.
- Optimistic-concurrency tokens on edits with a user-readable conflict response.
- Explicit API version and deprecation policy.

Frappe exposes authenticated REST resources and method endpoints; Odoo's current external API applies the normal access rights, record rules, and field access to API operations. TexTrack should adopt those security principles while keeping its own domain-specific contracts.

### 2. Add rate and concurrency limiting

Apply per-user/client limits to login, search/autocomplete, photos, exports, imports, expensive reports, and future AI endpoints. Apply concurrency limits to high-cost operations and return a clear busy/429 response.

### 3. Move long work to durable jobs

`GlobalOperationState` is useful for preventing interaction within one Blazor circuit, but it is not a durable queue. Imports, exports, bulk reports, media processing, and future migration jobs need:

- A persisted job record and state machine.
- One execution lease/lock per company and operation type.
- Progress, cancellation, retry, timeout, and failure details.
- Safe resume/idempotency after app restart.
- UI busy overlays driven by job state.

### 4. Harden media handling

Current photo validation includes extension/content type and a 5 MB limit. Add magic-byte verification, decoded-image validation, safe re-encoding/thumbnails, randomized object keys, malware scanning where deployment risk requires it, authorization on every download, and object storage abstraction for future S3/R2 use.

### 5. Add health and observability

- `/health/live` for process liveness and `/health/ready` for PostgreSQL/migration readiness.
- Structured logs with correlation, company, user, operation, duration, and outcome.
- Metrics for request latency, database latency, report duration, queue depth, failures, and active Blazor circuits.
- Distributed tracing when API/background services are introduced.
- Alert thresholds and an operator runbook.

### 6. Establish backup and disaster recovery

Document and automate encrypted PostgreSQL backups, retention, off-machine copies, restore drills, media backup, migration rollback strategy, RPO/RTO targets, and upgrade rehearsal against a restored production-sized database.

## P2 — platform maturity improvements

1. Split the solution into clear layers as it grows:
   - `TexTrack.Domain`: entities, value objects, invariant logic.
   - `TexTrack.Application`: commands/queries, authorization requirements, DTOs, validation.
   - `TexTrack.Infrastructure`: EF/PostgreSQL, files, jobs, integrations.
   - `TexTrack.Web`: Blazor UI and API transport only.
2. Add a permission-aware workflow/approval engine only where business processes need it; do not burden fast voucher entry.
3. Add notification/inbox and assignment features after identity and authorization exist.
4. Add configurable document numbering, print formats, and exports behind stable application services.
5. Add browser E2E tests for critical keyboard journeys, authorization, paging, return-focus, and long-operation blocking.
6. Use architecture rules/tests to prevent UI components from bypassing application/domain services.

## Recommended implementation sequence

### Gate A — secure local multi-user foundation

1. User/role/company membership schema.
2. Authentication, fallback authorization policy, and permission matrix.
3. Scoped current company/FY.
4. Protect every page/API and developer action.
5. Secrets, CSRF, login throttling, and real audit actor.

### Gate B — operational production foundation

1. Health/readiness checks and structured logging.
2. Backup/restore tooling and deployment configuration.
3. Durable long-operation jobs and concurrency controls.
4. Browser E2E/security test suite.

### Gate C — inventory expansion and mobile-ready API

1. Continue inventory features through application services.
2. Add `/api/v1`, DTO validation, versioning, OpenAPI, bearer/service authentication, rate limiting, and idempotency.
3. Add media storage abstraction and mobile sync/conflict rules.

Inventory domain work can continue while Gate A is implemented, but **TexTrack should not be exposed to untrusted networks or shipped as a multi-user production server until Gate A is complete**. Mobile/API work should begin only after Gate A's identity, permissions, and company context are stable.

## Production-module completion assessment

Following the closing-stock navigation hard-wall correction, the production module is functionally at a release-candidate stage, subject to final browser acceptance testing. The correction ensures that only real data rows participate in fast keyboard navigation and that pressing Up on the first row or Down on the last row performs no additional reveal/scroll action. Headers, totals, pagination controls, and empty space therefore cannot become pseudo-selections.

Completion still requires an acceptance pass covering JWO, Material Out, Material In, Master Job Order, BOM, registers, inventory drill-down return behavior, cancellation/deletion permissions, and the exact first/last-row boundary at normal display scaling.

## Authoritative reference principles

- Frappe users and permissions: https://docs.frappe.io/framework/user/en/basics/users-and-permissions
- Frappe REST API: https://docs.frappe.io/framework/user/en/api/rest
- Frappe rate limiting: https://docs.frappe.io/framework/user/en/rate-limiting
- Odoo security (ACLs, record rules, field access): https://www.odoo.com/documentation/19.0/developer/reference/backend/security.html
- Odoo external JSON-2 API security: https://www.odoo.com/documentation/19.0/developer/reference/external_api.html
- ASP.NET Core policy authorization: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies
- ASP.NET Core antiforgery: https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery
- ASP.NET Core rate limiting: https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit
- ASP.NET Core health checks: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks

## Final recommendation

Keep TexTrack's production logic, garment model, keyboard workflow, and current voucher-entry identity. Adopt from mature ERPs the controls that users do not see directly: identity, permissions, record isolation, API discipline, durable jobs, observability, and recovery. That produces a system that remains recognizably TexTrack while becoming safe and supportable at ERP scale.

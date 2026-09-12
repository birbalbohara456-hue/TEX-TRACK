# A07 — User-administration audit events

Date: 8 September 2026

## Scope and behavior

Changed `Services/UserIdentityService.cs`; added `Tests/TexTrack.Web.IntegrationTests/UserAdministrationAuditTests.cs`. No UI, voucher logic, SQL migrations, database privileges, or deployment configuration changed. Existing working-tree changes are preserved. No live server restart, business-data maintenance, commit, or push performed.

User creation, administrative password reset, and deactivation now write `UserAdministration` audit events with action, target ID, UTC time, success, and a persisted-session-validated actor ID. User creation includes the validated initial role codes and company IDs. These are account-wide events; CompanyId is intentionally null rather than misleadingly attributing multi-company administration to the current company.

Creation writes the event inside its existing explicit transaction after obtaining the generated user ID. Reset/deactivation add the event alongside the account changes in the same EF SaveChanges transaction. Audit insertion failure prevents the account change from committing. Password reset/deactivation continue rotating the security stamp; existing A01 session validation remains in force.

Authorization and validation rejections write separate failure events with fixed descriptions and no account mutation. A valid but unauthorized session retains its verified actor ID; absent/invalid sessions are labeled Anonymous, not attributed using untrusted claims. Unsuccessful creation has no generated target ID. Database outages, cancellation, and database-write exceptions are not guaranteed to produce persisted failure events: the database is also the event sink. An independent operational security log is outside this fix.

Bootstrap creation shares the atomic creation path but uses the explicit actor `System:IdentityBootstrap`. This does not introduce any new bootstrap endpoint or authority bypass. The added tests concentrate on public administration methods; bootstrap event attribution is source-reviewed.

## Secret handling

Events use allowlisted metadata, never serialized request/user objects or exception messages. No submitted password, password hash, security stamp, arbitrary submitted username/display name, or reset credential is included. No historical audit records are backfilled or invented.

## Behavioral verification

Six new cases cover:

1. Create/reset/deactivate events with correct target, actor, timestamps, initial roles, and secret exclusion; events remain readable after deactivation.
2. User creation rolls back if the audit insert fails, including the previously saved account and its memberships.
3. Password reset rolls back if the audit insert fails.
4. Deactivation rolls back if the audit insert fails.
5. Unauthorized create/reset/deactivate and invalid-password rejection produce failure events without changing the target account or logging submitted secrets.
6. An unvalidated principal claiming Developer authority is rejected and is not recorded as a trusted actor.

Failure injection adds a rejecting CHECK constraint only within a disposable test schema. It never alters the application's real migration bundle or live business schema.

## Independent review and limits

Review atomicity, actor validation, fixed failure descriptions, and account-wide event scope. Retest authorization and credential revocation with the live UI before release. This is not full voucher snapshot history, an append-only database permission boundary, a backup solution, or a regulatory-compliance claim. Future role/membership administration must receive equivalent event coverage when introduced.

Final full regression run: **205 passed, zero failed, zero skipped**, duration 1 minute 13 seconds. Build completed with no reported compiler warnings/errors. Evidence: `Tests/.a01-artifacts/results/a07-full-final.trx`.

Command: `dotnet test Tests/TexTrack.Web.IntegrationTests/TexTrack.Web.IntegrationTests.csproj --artifacts-path Tests/.a01-artifacts --logger "trx;LogFileName=a07-full-final.trx" --results-directory Tests/.a01-artifacts/results`.

The first full run passed 204/205 tests, including all six new A07 cases. The existing `Twenty_simultaneous_reservations_are_unique_and_gap_free` test failed because its hand-built schema contained only `voucher_sequences`, whereas the current allocator also reads the `vouchers` high-water mark. Added a minimal empty `vouchers` table with the four queried columns to that disposable fixture in `VoucherSequenceAllocatorTests.cs`. Its concurrency assertion is unchanged, and no allocator/runtime logic was changed. This fixture correction is separate from the A07 application change.

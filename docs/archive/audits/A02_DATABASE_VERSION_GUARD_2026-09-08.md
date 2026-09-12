# A02 — database-version compatibility guard

Date: 8 September 2026

## Finding and fix

The bootstrapper previously checked only migration versions found in the application package. A database containing an additional, newer applied version could therefore be incorrectly marked current by an older build.

The bootstrapper now checks for recorded versions outside the package's supported range, after validating the package's contiguous migration sequence and while holding the existing PostgreSQL advisory migration lock. This check runs before any pending migration SQL executes. A fresh database with no applied versions remains supported.

An unsupported version throws `DatabaseVersionCompatibilityException`. The bootstrapper marks database status unavailable, logs an actionable compatibility message, releases its migration lock through the existing `finally` block, and rethrows. `Program.cs` already awaits initialization before starting the web host; the exception therefore stops startup rather than merely leaving an incompatible application running behind a warning. Program and the UI were not changed for A02.

The message identifies the unsupported database version and highest packaged version. It tells the operator to use a matching or newer compatible build and not to rerun database setup, delete migration history, or attempt an automatic downgrade.

If explicit lock cleanup itself fails after an initialization error, that cleanup failure is logged without replacing the original exception. This preserves the fatal incompatibility path. Normal lock release is integration-tested; the cleanup-failure branch was source-reviewed, not fault-injected against PostgreSQL.

## Scope

Changed application code: `Data/DatabaseBootstrapper.cs` only.

Changed tests: `Tests/TexTrack.Web.IntegrationTests/DatabaseBootstrapperTests.cs`.

No SQL migrations, migration checksums, compatibility-manifest exceptions, entities, voucher calculations, authentication rules, Razor, CSS, or JavaScript were changed. Claude's UI work and all unrelated working-tree changes were preserved. No live database operation, app restart, commit, or GitHub push was performed.

The existing applied-file/checksum validation, reviewed legacy identity handling, transaction-per-migration behavior, package-gap checks, and advisory lock remain in place. This is not a schema-downgrade feature or a guarantee against manual schema changes that bypass migration history. Other startup failure behavior is unchanged.

## Behavioral tests

Four new cases cover:

1. Upgrade a disposable database using a real temporary version-28 migration that creates and populates a table. Remove that migration from the temporary application package to simulate an older build. Confirm fatal refusal, unavailable status (even if previously connected), unchanged migration identity/count, preserved data, and release of the migration lock. Restore the matching migration file and confirm successful reopening without duplicate execution.
2. Add unsupported version 99 to disposable migration history while version 28 is pending in the package. Confirm no pending migration side effect or history insert occurs.
3. Repeat the preflight refusal with invalid version 0.
4. Confirm that an ordinary supported forward upgrade from 27 to 28 still applies exactly once and remains restartable.

The existing bootstrapper tests still cover clean startup, idempotence, checksum tampering, failed-migration rollback, simultaneous startup serialization, package gaps, and preservation of approved legacy migration identities.

## Verification evidence

- Targeted bootstrapper tests: **10 passed, zero failed, zero skipped**.
- Build completed without compiler warnings or errors.
- Final full regression suite, including the cleanup-error safeguard: **199 passed, zero failed, zero skipped**, in 1 minute 35 seconds. Evidence: `Tests/.a01-artifacts/results/a02-full-final.trx`. The preceding full run also passed all 199 tests.
- `git diff --check` on the two changed code/test files: passed.

All integration tests use disposable PostgreSQL schemas and temporary migration-package directories through the existing test harness. The version-28 test files are not added to the real `Data/Migrations` directory. Outputs remain isolated in the locally ignored `Tests/.a01-artifacts` directory used for A01 verification.

Commands from the repository root:

```powershell
dotnet test Tests/TexTrack.Web.IntegrationTests/TexTrack.Web.IntegrationTests.csproj --artifacts-path Tests/.a01-artifacts --filter "FullyQualifiedName~DatabaseBootstrapperTests" --logger "trx;LogFileName=a02-bootstrap.trx" --results-directory Tests/.a01-artifacts/results
dotnet test Tests/TexTrack.Web.IntegrationTests/TexTrack.Web.IntegrationTests.csproj --artifacts-path Tests/.a01-artifacts --logger "trx;LogFileName=a02-full-final.trx" --results-directory Tests/.a01-artifacts/results
```

## Independent review and release check

Review the preflight's placement before the migration loop, its execution under the migration lock, and the dedicated catch/rethrow before the generic setup-error handler. Confirm that the hosting entry point continues to await bootstrap initialization before serving requests.

A full packaged-executable deployment smoke test remains a release check; these tests exercise the real bootstrapper, database changes, and exception behavior, not a live deployment. Do not test a downgrade against a customer's working database. Use a restored disposable copy and verify that the old build refuses startup while the correct build still starts.

This closes the implemented A02 behavior, not the entire production-readiness audit. A01's handoff and browser smoke-test requirement remain documented separately in `A01_SESSION_REVOCATION_FIX_2026-09-08.md`.

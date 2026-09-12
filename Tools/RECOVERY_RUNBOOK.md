# TexTrack database backup and recovery drill

Status: local database recovery tooling; not a complete production disaster-recovery system.

## Safety boundaries

- Requires Windows PowerShell **7.2+** and PostgreSQL client/server tools. Defaults to PostgreSQL 18 under Program Files. Use the same major version as the source for this initial drill.
- Backup reads the configured database; it never changes the application's database, roles, permissions, or server configuration.
- Restore creates a new private directory and independent local PostgreSQL cluster, with a random temporary password and a loopback-only ephemeral port. It never restores into the working database, never installs a service, and never uses `--clean` against an existing database.
- Only restore trusted backups. PostgreSQL archives can contain executable database objects. A SHA-256 checksum detects accidental modification against the manifest; it is not proof of authenticity if both files can be changed.
- Recovery directories restrict inherited access to the current Windows account before sensitive files are written. They are **not encrypted**, and administrators remain a separate trust boundary. Do not upload them to GitHub or share them in chat.
- The stopped restored cluster remains on disk for authorized inspection; no automatic recursive deletion or retention policy runs.

## Take a backup

Configure PostgreSQL authentication locally using a restricted `PGPASSFILE`/pgpass or temporary `PGPASSWORD` in the invoking process. Never put passwords into command arguments, source code, transcripts, or this document. The script does not scrape credentials or assume administrator access. During the verified developer drill, the already configured TexTrack connection was used without printing its credential.

From the repository root in PowerShell 7:

```powershell
./Tools/Backup-TexTrack.ps1 -Server localhost -Port 5432 -Database textrack_dev -DatabaseUser textrack_app
```

It prints the newly created directory under `%LOCALAPPDATA%\TexTrack\Recovery`. Keep `database.dump` and `manifest.json` together. A directory without a completed manifest is not a successful backup. Check command success and warnings; do not assume a nonempty dump is valid.

The database dump and table fingerprints share an exported read-only repeatable-read snapshot. Each row is converted to canonical JSONB, hashed with SHA-256, sorted by that hash, and streamed into a table fingerprint with a row count. This checks all ordinary user tables, including database-backed photos, migration history, vouchers, stock postings, and audit events. It may also include existing diagnostic schemas: this is a full database backup, not a company-only export.

Fingerprinting can use database sorting resources and holds a snapshot open. Schedule around heavy workloads and schema migrations. It is a restore-verification baseline, not a low-cost continuous backup or a WAL/PITR system. PostgreSQL sequence state is not MVCC snapshot data; sequence-next-value and posting behavior require application-level checks too.

## Restore and compare

```powershell
./Tools/Test-TexTrackRestore.ps1 -BackupDirectory 'C:\absolute\path\to\the\backup-directory'
```

This checks archive integrity before starting any server, restores with `--single-transaction --exit-on-error --no-owner --no-privileges`, and compares every table count and fingerprint. Beyond that byte-for-byte content check, it also runs two read-only sequence-integrity checks against the restored copy - neither ever calls `nextval()`/`setval()`, which would mutate sequence state, and the actual per-sequence increment is read from `pg_sequence` rather than assumed to be 1:

1. **Fails the drill** - every column backed by a `bigserial`/identity sequence: the sequence's next issuable value (correctly computed from its real increment, whatever positive value that is) must be strictly greater than the highest ID actually present in its table. A violation means the very next insert into that table would collide with existing data, and nothing in the application reconciles this automatically. A sequence with a zero or negative increment is genuinely unsupported (the "ahead of max id" check doesn't apply to a descending sequence) and **also fails the drill** rather than being silently skipped - a passing `verification.json` must never claim a check was verified when it wasn't actually evaluated.
2. **Reported only, never fails the drill** - `voucher_sequences.last_number` must not be below the highest `vouchers.sequence_number` actually used in the same scope, following `Services/VoucherSequenceAllocator.cs`'s own scope exactly: a voucher type with `reset_period = 'Never'` is intended to keep one counter row regardless of which financial year a voucher lands in, but Migration 007 originally created `voucher_sequences` rows grouped by financial year even for types later switched to Never - so a never-reset type can legitimately still carry multiple historical counter rows. This check takes the effective counter as `MAX(last_number)` across all of a never-reset type's rows (matching what the allocator itself uses) and compares that single effective value against the highest voucher number across every financial year for that (company, type), rather than testing each historical row independently, which would misreport a stale-but-harmless row as drift even while the true (maximum) counter is current. Any other reset period is compared within the matching financial year only. A violation here is real drift worth surfacing (and may already exist in the source, not just be restore-introduced) but does **not** mean the next voucher would collide - the allocator reconciles `last_number` against the actual max voucher on every reservation and self-heals on next use.

It also reports (not enforces) restored-vs-source server version, encoding, and collation/ctype in `verification.json`'s `RestoredCompatibility` object - a locale difference is expected by design in this drill, not a failure.

It writes `verification.json` only after all of the above succeeds. Its `finally` block stops the isolated cluster; verify command success and absence of `cluster\postmaster.pid`. A startup/control failure must be investigated rather than described as a successful drill.

The isolated test uses a recovery-owner role and C locale. It deliberately does not reproduce production ACLs, original collation, service identity, or login/session/key behavior. For the real recovery environment, match the source locale/provider/version and restore reviewed roles/privileges with a DBA. Do not point the regular launcher at a restored database without explicitly checking its connection and disabling unintended integrations. Restoring original global roles and privileges is a separate, larger workstream (global-role capture, role mapping, and safe handling of password-hash data) deferred until the production tenancy and service-account model is decided - this drill intentionally does not attempt it.

Run guard checks without a database:

```powershell
./Tests/PowerShell/Recovery.Guards.Tests.ps1
```

They reject relative recovery roots, malformed snapshot IDs, missing tools, unsupported manifests, and archive-hash mismatches before connecting or starting a server.

## What a database dump does not protect

1. PostgreSQL global roles, tablespaces, and operational permissions. These need a separately controlled DBA recovery plan. Do not grant database-owner powers to ordinary runtime users to simplify restore.
2. Release artifact, exact migration bundle, connection configuration, and credential custody. Keep matching releases and deployment configuration securely outside the backup archive.
3. ASP.NET Data Protection keys and their OS/service-identity protection. `Program.cs` currently uses platform defaults. Confirm the actual key location, ACLs and recoverability under the eventual service identity; copying protected files alone may not permit decryption on another machine.
4. `%LOCALAPPDATA%\TexTrack\assistant-settings.protected`. It is outside PostgreSQL and depends on Data Protection. Re-enter provider settings/credentials after recovery if key recovery is unavailable. This tool does not copy or expose those credentials.
5. TLS certificates/private keys and any future external attachment storage. Current stock photos are stored in database rows and are included in the table verification.

## Production acceptance still required

Agree a recovery-point objective (maximum acceptable lost work) and recovery-time objective. Choose encrypted off-machine storage, key custody, backup frequency and retention; monitor failures and run regular restore drills. This local recovery folder is not a substitute for an independent backup copy.

On a separately restored production-equivalent environment, verify login, user/company isolation, photos, audit access, latest postings, stock quantities/values, voucher numbering, and a representative JWO/MO/MI workflow. Compare business reports as well as rows. Test under the actual service identity and prove external keys/configuration recovery. No successful database drill alone closes these requirements or establishes regulatory compliance.

References: [PostgreSQL pg_dump](https://www.postgresql.org/docs/18/app-pgdump.html), [PostgreSQL pg_restore](https://www.postgresql.org/docs/18/app-pgrestore.html).

# A03 — Local database recovery evidence

Database backup taken 8 September 2026; successful isolated restore verified 9 September 2026.

## Implemented

- `Tools/Recovery.Common.ps1`: restricted artifact directories, safely separated process arguments, PostgreSQL process helpers, snapshot-bound streaming SHA-256 table fingerprints.
- `Tools/Backup-TexTrack.ps1`: custom-format database dump plus matching snapshot inventory and archive checksum.
- `Tools/Test-TexTrackRestore.ps1`: fresh independent loopback-only PostgreSQL cluster, random temporary credential, transactional restore, all-table comparison, server shutdown in finally.
- `Tests/PowerShell/Recovery.Guards.Tests.ps1`: five runnable rejection tests.
- `Tools/RECOVERY_RUNBOOK.md`: procedure, trust boundaries, exclusions and remaining production requirements.

No Razor/CSS/JS, voucher calculations, entities, migration files, runtime database permissions, or live connection settings changed. No application restart, commit, push or live database writes. The source application's existing credential was used only for read-only backup. Its role remains without CREATEDB or superuser privileges. The temporary cluster requires neither privilege on the working server.

## Measured result

- Source: PostgreSQL 18.4; UTF8; English_India.1252 collation/ctype.
- Backup: 57.86 seconds; archive 28,808,667 bytes.
- Restored table inventory: **233 tables, 2,264,524 rows**, all counts and SHA-256 content fingerprints matched.
- Restore plus inventory comparison: **96.52 seconds**. This is not a production RTO guarantee.
- Five guard tests passed, without database connections/server startup.
- Temporary server shutdown confirmed in its log. No automatic deletion of backup or stopped cluster performed.

Evidence, restricted to the local Windows account:

`C:\Users\VICTUS\AppData\Local\TexTrack\Recovery\backup-20260908T112143Z-ae6155b0a94049ec941218892eca5835\manifest.json`

`C:\Users\VICTUS\AppData\Local\TexTrack\Recovery\restore-drill-20260909T055331Z-178cbbe0f03348c29b5bea20f7d85143\verification.json`

The full database includes any existing diagnostic schemas as well as application tables; totals must not be described as the count of business vouchers.

## Failed first attempt and correction

The first attempt started its private server but stalled before restore because Windows background PostgreSQL inherited redirected pipe handles. Waiting for EOF after pg_ctl returned therefore waited for server shutdown. Stopped that exact temporary cluster, confirmed the first run failed, and changed pg_ctl handling to wait for its exit (bounded at 45 seconds), not inherited stream EOF. The second attempt completed successfully. The first failed cluster remains stopped under the earlier `restore-drill-20260908T112331Z-7531545b0f384bf6b9a1afdf4519f6c7` directory; it is not valid restore evidence.

## Remaining A03 scope — do not mark fully closed

This proves local database restoration and row-content preservation, not the entire recovery acceptance matrix. The drill uses a recovery-owner role, strips original ACL/ownership on restore, and uses C locale. It does not yet verify production-equivalent collation/permissions, Windows service identity, login/UI, new posting/sequence behavior, Data Protection recovery, assistant-settings recovery, TLS/private keys or external configuration.

The files have restrictive ACLs but are not encrypted. Off-machine encrypted storage, retention, monitoring, key custody and agreed recovery-point/recovery-time objectives remain decisions and deployment work. There is no scheduled backup, WAL/PITR, full voucher history implementation, or regulatory-compliance claim here. No production deployment switch was introduced.

Independent reviewer: inspect snapshot lifetime, duplicate-preserving sorted row fingerprints, error handling, archive checksum refusal, unique cluster path and loopback binding. Repeat the test on a trusted restored environment and complete application-level recovery checks before declaring A03 closed.

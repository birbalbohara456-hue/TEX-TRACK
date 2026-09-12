#requires -Version 7.2
<#
Executable tests for Get-SequenceIntegrityReport and Get-VoucherSequenceIntegrityReport
(Tools/Recovery.Common.ps1), against a disposable, isolated PostgreSQL cluster created
solely for this run. This NEVER connects to, or even knows the connection details of,
the configured TexTrack development database - the cluster below is a brand-new
initdb'd directory on a freshly-allocated loopback port, exactly like
Tools/Test-TexTrackRestore.ps1's own isolation pattern, just built directly here
instead of via a backup/restore round-trip.

Run: pwsh Tests/PowerShell/Recovery.SequenceIntegrity.Tests.ps1
#>
[CmdletBinding()]
param(
    [string]$PgBin = 'C:\Program Files\PostgreSQL\18\bin'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $root 'Tools/Recovery.Common.ps1')

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "ASSERTION FAILED: $Message" }
}

function Invoke-Setup([string]$Psql, [string[]]$Connection, [string]$Sql) {
    $null = Invoke-PgTool $Psql ($Connection + @('-X','-w','-v','ON_ERROR_STOP=1')) $Sql
}

$testRoot = New-PrivateRecoveryDirectory (Join-Path $env:LOCALAPPDATA 'TexTrack\Recovery') 'sequence-integrity-tests'
$cluster = Join-Path $testRoot 'cluster'
$passwordFile = Join-Path $testRoot 'temporary-password.txt'
$pgCtl = Join-Path $PgBin 'pg_ctl.exe'
$psql = Join-Path $PgBin 'psql.exe'
$temporaryPassword = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$savedPassword = $env:PGPASSWORD
$started = $false
$passed = 0
try {
    # New, independent cluster - never the configured application's data directory or port.
    [IO.File]::WriteAllText($passwordFile, $temporaryPassword + "`n", [Text.UTF8Encoding]::new($false))
    $null = Invoke-PgTool (Join-Path $PgBin 'initdb.exe') @('-D',$cluster,'-U','recovery_owner','--auth=scram-sha-256',"--pwfile=$passwordFile",'--encoding=UTF8','--no-locale')
    Remove-Item -LiteralPath $passwordFile
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $listener.Stop()
    $null = Invoke-PgTool $pgCtl @('-D',$cluster,'-l',(Join-Path $testRoot 'postgres.log'),'-o',"-h 127.0.0.1 -p $port",'-w','-t','30','start')
    $started = $true
    $env:PGPASSWORD = $temporaryPassword
    $connection = @('-h','127.0.0.1','-p',"$port",'-U','recovery_owner')
    $null = Invoke-PgTool (Join-Path $PgBin 'createdb.exe') ($connection + @('-w','--template=template0','sequence_integrity_test'))
    $connection += @('-d','sequence_integrity_test')

    # --- Identity/serial sequence scenarios -------------------------------------------------

    Invoke-Setup $psql $connection 'CREATE TABLE test_seq_empty(id bigserial PRIMARY KEY, name text);'
    Invoke-Setup $psql $connection 'CREATE TABLE test_seq_healthy(id bigserial PRIMARY KEY, name text);'
    Invoke-Setup $psql $connection "INSERT INTO test_seq_healthy(name) SELECT 'row-' || g FROM generate_series(1,5) g;"
    Invoke-Setup $psql $connection 'CREATE TABLE test_seq_behind(id bigserial PRIMARY KEY, name text);'
    # Explicit high id, deliberately bypassing nextval() - mirrors how a seeded/migrated row
    # can leave a serial column's backing sequence behind the data already present.
    Invoke-Setup $psql $connection "INSERT INTO test_seq_behind(id, name) VALUES (1000, 'seeded-with-explicit-id');"
    # Positive, non-default increment: must be read from pg_sequence and evaluated correctly,
    # not assumed to be 1 and not rejected merely for being non-default.
    Invoke-Setup $psql $connection 'CREATE TABLE test_seq_custom_increment_healthy(id bigserial PRIMARY KEY, name text);'
    Invoke-Setup $psql $connection "ALTER SEQUENCE test_seq_custom_increment_healthy_id_seq INCREMENT BY 5;"
    Invoke-Setup $psql $connection "INSERT INTO test_seq_custom_increment_healthy(name) SELECT 'row-' || g FROM generate_series(1,3) g;"
    Invoke-Setup $psql $connection 'CREATE TABLE test_seq_custom_increment_behind(id bigserial PRIMARY KEY, name text);'
    Invoke-Setup $psql $connection "ALTER SEQUENCE test_seq_custom_increment_behind_id_seq INCREMENT BY 5;"
    Invoke-Setup $psql $connection "INSERT INTO test_seq_custom_increment_behind(id, name) VALUES (1000, 'seeded-with-explicit-id');"
    # Non-positive increment (descending sequence): genuinely unsupported - the "ahead of max
    # id" check does not apply, and must be reported as unsupported rather than evaluated wrong.
    Invoke-Setup $psql $connection 'CREATE TABLE test_seq_negative_increment(id bigserial PRIMARY KEY, name text);'
    # RESTART WITH is required, not just START WITH: START only sets the value a future bare
    # RESTART would use, it does not itself reposition the sequence's current internal value,
    # which otherwise stays at its bigserial default (1) - now outside the new MAXVALUE (-1).
    Invoke-Setup $psql $connection "ALTER SEQUENCE test_seq_negative_increment_id_seq INCREMENT BY -1 MINVALUE -1000000 MAXVALUE -1 START WITH -1 RESTART WITH -1;"
    Invoke-Setup $psql $connection "INSERT INTO test_seq_negative_increment(id, name) VALUES (-1, 'row-1');"

    $sequenceReport = @(Get-SequenceIntegrityReport $psql $connection)

    $empty = $sequenceReport | Where-Object Table -eq 'public.test_seq_empty'
    Assert-True ($empty.Count -eq 1) 'Scenario 1 (empty, never-called sequence): expected exactly one report row.'
    Assert-True (-not $empty[0].Behind) 'Scenario 1: an empty table must never be reported as behind.'
    Assert-True $empty[0].Supported 'Scenario 1: default-increment sequence must be marked supported.'
    Write-Output 'PASS: Scenario 1 - empty, never-called sequence is not flagged'
    $passed++

    $healthy = $sequenceReport | Where-Object Table -eq 'public.test_seq_healthy'
    Assert-True ($healthy.Count -eq 1) 'Scenario 2 (healthy populated sequence): expected exactly one report row.'
    Assert-True (-not $healthy[0].Behind) 'Scenario 2: a normally-populated serial column must not be reported as behind.'
    Assert-True ($healthy[0].MaxId -eq 5) "Scenario 2: expected MaxId 5, got $($healthy[0].MaxId)."
    Write-Output 'PASS: Scenario 2 - healthy populated sequence is not flagged'
    $passed++

    $behind = $sequenceReport | Where-Object Table -eq 'public.test_seq_behind'
    Assert-True ($behind.Count -eq 1) 'Scenario 3 (deliberately behind sequence): expected exactly one report row.'
    Assert-True $behind[0].Behind 'Scenario 3: a sequence behind an explicitly-seeded high id must be flagged as behind.'
    Assert-True ($behind[0].MaxId -eq 1000) "Scenario 3: expected MaxId 1000, got $($behind[0].MaxId)."
    Write-Output 'PASS: Scenario 3 - deliberately behind sequence is correctly flagged'
    $passed++

    # Scenario 4: a positive, non-default increment must be read from pg_sequence and evaluated
    # correctly (not assumed to be 1, and not rejected merely for being non-default).
    $customHealthy = $sequenceReport | Where-Object Table -eq 'public.test_seq_custom_increment_healthy'
    Assert-True ($customHealthy.Count -eq 1) 'Scenario 4a (healthy, increment 5): expected exactly one report row.'
    Assert-True $customHealthy[0].Supported 'Scenario 4a: a positive non-1 increment must still be supported, not rejected.'
    Assert-True ($customHealthy[0].Increment -eq 5) "Scenario 4a: expected the real increment (5) read from pg_sequence, got $($customHealthy[0].Increment)."
    Assert-True ($customHealthy[0].MaxId -eq 11) "Scenario 4a: expected MaxId 11 (3 inserts at increment 5 starting from 1: 1,6,11), got $($customHealthy[0].MaxId)."
    Assert-True ($customHealthy[0].NextIssuableValue -eq 16) "Scenario 4a: expected next issuable value 16 (11 + increment 5), got $($customHealthy[0].NextIssuableValue)."
    Assert-True (-not $customHealthy[0].Behind) 'Scenario 4a: a healthy increment-5 sequence must not be flagged as behind.'
    Write-Output 'PASS: Scenario 4a - positive non-default increment is read from pg_sequence and evaluated correctly'
    $passed++

    $customBehind = $sequenceReport | Where-Object Table -eq 'public.test_seq_custom_increment_behind'
    Assert-True ($customBehind.Count -eq 1) 'Scenario 4b (behind, increment 5): expected exactly one report row.'
    Assert-True $customBehind[0].Supported 'Scenario 4b: a positive non-1 increment must still be supported.'
    Assert-True $customBehind[0].Behind 'Scenario 4b: an increment-5 sequence left uncalled behind an explicitly-seeded high id must be flagged as behind.'
    Write-Output 'PASS: Scenario 4b - a behind sequence is correctly flagged even with a non-default increment'
    $passed++

    # Scenario 4c: a non-positive increment is genuinely unsupported - the ascending "next value
    # exceeds max id" check does not apply to a descending sequence, and must say so rather than
    # silently computing (or omitting) an answer.
    $negativeIncrement = $sequenceReport | Where-Object Table -eq 'public.test_seq_negative_increment'
    Assert-True ($negativeIncrement.Count -eq 1) 'Scenario 4c (negative increment): expected exactly one report row.'
    Assert-True (-not $negativeIncrement[0].Supported) 'Scenario 4c: a non-positive increment must be reported as unsupported.'
    Assert-True (-not $negativeIncrement[0].Behind) 'Scenario 4c: an unsupported row must not also claim Behind (that would be an unverified assertion).'
    Assert-True ($negativeIncrement[0].Increment -eq -1) "Scenario 4c: expected the real increment (-1) read from pg_sequence, got $($negativeIncrement[0].Increment)."
    Write-Output 'PASS: Scenario 4c - non-positive increment is explicitly rejected as unsupported, not evaluated incorrectly'
    $passed++

    # --- Voucher-sequence scenarios ----------------------------------------------------------
    # Minimal slice of the real schema - only the columns Get-VoucherSequenceIntegrityReport
    # and VoucherSequenceAllocator.cs actually touch.
    Invoke-Setup $psql $connection @'
CREATE TABLE voucher_types(id bigint PRIMARY KEY, reset_period varchar(30) NOT NULL);
CREATE TABLE vouchers(id bigserial PRIMARY KEY, company_id bigint NOT NULL, financial_year_id bigint NOT NULL, voucher_type_id bigint NOT NULL, sequence_number integer NOT NULL);
CREATE TABLE voucher_sequences(company_id bigint NOT NULL, financial_year_id bigint NOT NULL, voucher_type_id bigint NOT NULL, last_number integer NOT NULL, modified_at_utc timestamptz NOT NULL, modified_by varchar(100) NOT NULL, PRIMARY KEY(company_id, financial_year_id, voucher_type_id));
'@

    # Scenario 5: annual-reset voucher type, drift within one financial year - must still be
    # caught (this is the pre-existing, already-correct code path for a non-Never type).
    Invoke-Setup $psql $connection "INSERT INTO voucher_types(id, reset_period) VALUES (3, 'FinancialYear');"
    Invoke-Setup $psql $connection "INSERT INTO vouchers(company_id, financial_year_id, voucher_type_id, sequence_number) SELECT 1, 1, 3, g FROM generate_series(1,4) g;"
    Invoke-Setup $psql $connection "INSERT INTO voucher_sequences(company_id, financial_year_id, voucher_type_id, last_number, modified_at_utc, modified_by) VALUES (1, 1, 3, 2, now(), 'test');"

    # Scenario 6: Never-reset type spanning two financial years. last_number=3 is AHEAD of
    # financial_year_id=1's own max (2) but BEHIND the true max across all financial years for
    # this (company, type) (5, in financial_year_id=2). The buggy, financial-year-scoped query
    # would compare only against FY 1 and wrongly report no drift; the corrected, allocator-
    # matching scope must compare across every financial year for a Never-reset type and catch it.
    Invoke-Setup $psql $connection "INSERT INTO voucher_types(id, reset_period) VALUES (2, 'Never');"
    Invoke-Setup $psql $connection "INSERT INTO vouchers(company_id, financial_year_id, voucher_type_id, sequence_number) VALUES (1, 1, 2, 1), (1, 1, 2, 2), (1, 2, 2, 3), (1, 2, 2, 4), (1, 2, 2, 5);"
    Invoke-Setup $psql $connection "INSERT INTO voucher_sequences(company_id, financial_year_id, voucher_type_id, last_number, modified_at_utc, modified_by) VALUES (1, 1, 2, 3, now(), 'test');"

    # Scenario 7: "faithful restore containing legacy voucher-counter drift" - a second,
    # independent company/type combination representing drift that already existed in the
    # source before any restore, to prove the check surfaces pre-existing inconsistencies (not
    # only restore-introduced ones) and that detecting it never throws from the report function
    # itself - only the caller (Test-TexTrackRestore.ps1) decides to warn instead of fail.
    Invoke-Setup $psql $connection "INSERT INTO voucher_types(id, reset_period) VALUES (9, 'FinancialYear');"
    Invoke-Setup $psql $connection "INSERT INTO vouchers(company_id, financial_year_id, voucher_type_id, sequence_number) SELECT 2, 7, 9, g FROM generate_series(1,10) g;"
    Invoke-Setup $psql $connection "INSERT INTO voucher_sequences(company_id, financial_year_id, voucher_type_id, last_number, modified_at_utc, modified_by) VALUES (2, 7, 9, 1, now(), 'test');"

    # Scenario 8: Never-reset type with TWO historical voucher_sequences rows (Migration 007
    # originally grouped these rows by financial year even for Never-reset types, so restored
    # data can legitimately carry more than one). One row (FY 1, last_number=2) is stale; the
    # other (FY 2, last_number=5) already equals the true max voucher across every financial
    # year for this (company, type). The effective counter is MAX(last_number)=5 across both
    # rows, which is NOT behind the max voucher (5) - expect no diagnostic at all. Testing each
    # historical row independently would wrongly flag the stale FY-1 row (2 < 5).
    Invoke-Setup $psql $connection "INSERT INTO voucher_types(id, reset_period) VALUES (5, 'Never');"
    Invoke-Setup $psql $connection "INSERT INTO vouchers(company_id, financial_year_id, voucher_type_id, sequence_number) VALUES (1, 1, 5, 1), (1, 1, 5, 2), (1, 2, 5, 3), (1, 2, 5, 4), (1, 2, 5, 5);"
    Invoke-Setup $psql $connection "INSERT INTO voucher_sequences(company_id, financial_year_id, voucher_type_id, last_number, modified_at_utc, modified_by) VALUES (1, 1, 5, 2, now(), 'test'), (1, 2, 5, 5, now(), 'test');"

    # Scenario 9: Never-reset type with two historical voucher_sequences rows where EVERY row is
    # behind the true max voucher (7). The effective counter is MAX(last_number)=4 (FY 2, the
    # larger of 2 and 4) - still behind 7, so expect exactly ONE diagnostic row carrying that
    # effective maximum (4), not two independent rows for FY 1 and FY 2.
    Invoke-Setup $psql $connection "INSERT INTO voucher_types(id, reset_period) VALUES (6, 'Never');"
    Invoke-Setup $psql $connection "INSERT INTO vouchers(company_id, financial_year_id, voucher_type_id, sequence_number) VALUES (1, 1, 6, 1), (1, 1, 6, 2), (1, 2, 6, 3), (1, 2, 6, 4), (1, 2, 6, 5), (1, 2, 6, 6), (1, 2, 6, 7);"
    Invoke-Setup $psql $connection "INSERT INTO voucher_sequences(company_id, financial_year_id, voucher_type_id, last_number, modified_at_utc, modified_by) VALUES (1, 1, 6, 2, now(), 'test'), (1, 2, 6, 4, now(), 'test');"

    $voucherReport = @(Get-VoucherSequenceIntegrityReport $psql $connection)

    $scenario5 = @($voucherReport | Where-Object { $_.CompanyId -eq 1 -and $_.VoucherTypeId -eq 3 })
    Assert-True ($scenario5.Count -eq 1) 'Scenario 5 (annual-reset drift): expected exactly one diagnostic row.'
    Assert-True ($scenario5[0].ResetPeriod -eq 'financialyear') "Scenario 5: expected reset_period 'financialyear', got '$($scenario5[0].ResetPeriod)'."
    Assert-True ($scenario5[0].MaxActualSequenceNumber -eq 4) "Scenario 5: expected max actual sequence_number 4, got $($scenario5[0].MaxActualSequenceNumber)."
    Write-Output 'PASS: Scenario 5 - annual-reset voucher type drift is detected within its financial year'
    $passed++

    $scenario6 = @($voucherReport | Where-Object { $_.CompanyId -eq 1 -and $_.VoucherTypeId -eq 2 })
    Assert-True ($scenario6.Count -eq 1) 'Scenario 6 (never-reset spanning two financial years): expected exactly one diagnostic row - the corrected cross-financial-year scope must catch this drift that a financial-year-scoped comparison would miss.'
    Assert-True ($scenario6[0].ResetPeriod -eq 'never') "Scenario 6: expected reset_period 'never', got '$($scenario6[0].ResetPeriod)'."
    Assert-True ($scenario6[0].MaxActualSequenceNumber -eq 5) "Scenario 6: expected max actual sequence_number 5 (across both financial years), got $($scenario6[0].MaxActualSequenceNumber)."
    Write-Output 'PASS: Scenario 6 - never-reset voucher type drift is detected across financial years, matching VoucherSequenceAllocator scope'
    $passed++

    $scenario7 = @($voucherReport | Where-Object { $_.CompanyId -eq 2 -and $_.VoucherTypeId -eq 9 })
    Assert-True ($scenario7.Count -eq 1) 'Scenario 7 (legacy drift already present in source): expected exactly one diagnostic row.'
    Assert-True ($scenario7[0].MaxActualSequenceNumber -eq 10) "Scenario 7: expected max actual sequence_number 10, got $($scenario7[0].MaxActualSequenceNumber)."
    Write-Output 'PASS: Scenario 7 - pre-existing (legacy) voucher-counter drift is surfaced as diagnostic data, not thrown as an exception'
    $passed++

    Assert-True (@($voucherReport | Where-Object { $_.CompanyId -eq 1 -and $_.VoucherTypeId -eq 5 }).Count -eq 0) 'Scenario 8 (never-reset, multiple historical counter rows, effective max is current): expected no diagnostic row - the stale FY-1 row must not be tested independently of the current FY-2 row.'
    Write-Output 'PASS: Scenario 8 - a never-reset type with multiple historical voucher_sequences rows is not flagged when the effective (maximum) counter is current'
    $passed++

    $scenario9 = @($voucherReport | Where-Object { $_.CompanyId -eq 1 -and $_.VoucherTypeId -eq 6 })
    Assert-True ($scenario9.Count -eq 1) 'Scenario 9 (never-reset, multiple historical counter rows, all behind): expected exactly one diagnostic row aggregating both historical rows, not one per row.'
    Assert-True ($scenario9[0].ResetPeriod -eq 'never') "Scenario 9: expected reset_period 'never', got '$($scenario9[0].ResetPeriod)'."
    Assert-True ($scenario9[0].LastNumber -eq 4) "Scenario 9: expected the effective (maximum) counter 4 across both historical rows, got $($scenario9[0].LastNumber)."
    Assert-True ($scenario9[0].MaxActualSequenceNumber -eq 7) "Scenario 9: expected max actual sequence_number 7, got $($scenario9[0].MaxActualSequenceNumber)."
    Write-Output 'PASS: Scenario 9 - a never-reset type with multiple historical voucher_sequences rows is flagged exactly once, using the effective (maximum) counter'
    $passed++

    # A company/type pair with no drift at all must never appear in the report - confirms the
    # query is a pure violations-only diagnostic, not an unconditional full dump.
    Assert-True (@($voucherReport | Where-Object { $_.CompanyId -eq 1 -and $_.VoucherTypeId -eq 2 -and $_.FinancialYearId -eq 2 }).Count -eq 0) 'A financial_year_id that only ever holds a Never-reset type''s later vouchers (no voucher_sequences row of its own) must not spuriously appear.'

    Write-Output "PASS: all $passed sequence-integrity scenarios verified against an isolated, disposable PostgreSQL cluster. No connection to the TexTrack development database was made."
} finally {
    $env:PGPASSWORD = $savedPassword
    $temporaryPassword = $null
    if (Test-Path -LiteralPath $passwordFile) { Remove-Item -LiteralPath $passwordFile }
    if ($started -or (Test-Path -LiteralPath (Join-Path $cluster 'postmaster.pid'))) {
        $null = Invoke-PgTool $pgCtl @('-D',$cluster,'-m','fast','-w','-t','30','stop')
    }
    # Preserve the stopped cluster for inspection, matching the other recovery tools' convention.
}

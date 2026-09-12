#requires -Version 7.2
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function New-PrivateRecoveryDirectory([string]$Parent, [string]$Prefix) {
    if (-not [IO.Path]::IsPathFullyQualified($Parent)) { throw 'Recovery root must be an absolute path.' }
    $null = New-Item -ItemType Directory -Path $Parent -Force
    $path = Join-Path $Parent ($Prefix + '-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $path
    # Restrict BEFORE writing database contents, which include password hashes and audit data.
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $rule = [Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $path -AclObject $acl
    return $path
}

function Start-PgProcess([string]$Executable, [string[]]$Arguments) {
    if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) { throw "PostgreSQL tool missing: $Executable" }
    $info = [Diagnostics.ProcessStartInfo]::new($Executable)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = [Text.Encoding]::UTF8
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $info.Environment['PGCLIENTENCODING'] = 'UTF8'
    $info.Environment['PGCONNECT_TIMEOUT'] = '15'
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    $null = $process.Start()
    return $process
}

function Invoke-PgTool([string]$Executable, [string[]]$Arguments, [string]$InputText = '') {
    $process = Start-PgProcess $Executable $Arguments
    try {
        $output = $process.StandardOutput.ReadToEndAsync()
        $errors = $process.StandardError.ReadToEndAsync()
        if ($InputText) { $process.StandardInput.WriteLine($InputText) }
        $process.StandardInput.Close()
        $isServerControl = (Split-Path $Executable -Leaf) -eq 'pg_ctl.exe'
        if ($isServerControl) {
            if (-not $process.WaitForExit(45000)) { throw 'PostgreSQL server control timed out; inspect the isolated cluster.' }
            # The background Windows server can inherit pipe handles after pg_ctl exits.
            # Never wait for stream EOF (which then arrives only when the server stops).
            $stdout = if ($output.IsCompleted) { $output.GetAwaiter().GetResult() } else { '' }
            $stderr = if ($errors.IsCompleted) { $errors.GetAwaiter().GetResult() } else { '' }
        } else {
            $process.WaitForExit()
            $stdout = $output.GetAwaiter().GetResult()
            $stderr = $errors.GetAwaiter().GetResult()
        }
        # Do not echo raw server errors, SQL contents, or connection configuration.
        if ($process.ExitCode -ne 0) { throw "$(Split-Path $Executable -Leaf) failed (exit $($process.ExitCode)); inspect locally with an authorized operator." }
        if ($stderr.Trim()) { Write-Warning "$(Split-Path $Executable -Leaf) emitted diagnostic output; no credentials or raw server messages are echoed." }
        return $stdout.TrimEnd()
    } finally { $process.Dispose() }
}

function Test-TableExists([string]$Psql, [string[]]$Connection, [string]$TableIdent) {
    # A bare boolean column (not cast to ::text, which renders the full words 'true'/'false')
    # prints as psql's native 't'/'f' in unaligned tuples-only mode.
    $sql = "SELECT (to_regclass('$TableIdent') IS NOT NULL);"
    $result = Invoke-PgTool $Psql ($Connection + @('-X','-w','-qAt','-v','ON_ERROR_STOP=1')) $sql
    return $result -eq 't'
}

function Get-SequenceIntegrityReport([string]$Psql, [string[]]$Connection) {
    # Read-only: never calls nextval()/setval(), which would mutate sequence state.
    # Reading last_value/is_called directly off the sequence relation is a plain SELECT, and
    # the actual increment comes from the pg_sequence catalog - never assumed to be 1. A
    # column identifier is returned already quoted (format('%I', ...)) so it's safe to embed
    # directly into MAX(...) below without a second quoting step.
    $prefix = "BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY; SET LOCAL timezone='UTC';"
    $discoverSql = @"
SELECT format('%I.%I',n.nspname,c.relname) || E'\t' || format('%I',a.attname) || E'\t' || pg_get_serial_sequence(format('%I.%I',n.nspname,c.relname), a.attname)
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
WHERE c.relkind = 'r' AND n.nspname NOT IN ('pg_catalog','information_schema') AND n.nspname NOT LIKE 'pg_toast%'
  AND pg_get_serial_sequence(format('%I.%I',n.nspname,c.relname), a.attname) IS NOT NULL
ORDER BY n.nspname COLLATE "C", c.relname COLLATE "C", a.attnum;
"@
    $rows = Invoke-PgTool $Psql ($Connection + @('-X','-w','-qAt','-v','ON_ERROR_STOP=1')) ($prefix + $discoverSql + ' COMMIT;')
    $result = @()
    foreach ($line in ($rows -split "`r?`n" | Where-Object { $_ })) {
        $parts = $line -split "`t"
        if ($parts.Count -ne 3) { throw 'Unexpected sequence discovery output.' }
        $table, $column, $sequence = $parts
        if ($table -match '[\r\n;]' -or $column -match '[\r\n;]' -or $sequence -match '[\r\n;]') { throw 'Unsupported identifier in sequence discovery.' }
        $maxIdText = Invoke-PgTool $Psql ($Connection + @('-X','-w','-qAt','-v','ON_ERROR_STOP=1')) ($prefix + "SELECT COALESCE(MAX($column),0) FROM $table; COMMIT;")
        # seqincrement comes from pg_sequence via the sequence's own OID (cast from the already
        # schema-qualified identifier pg_get_serial_sequence returned) - not string-matched by name.
        $seqStateText = Invoke-PgTool $Psql ($Connection + @('-X','-w','-qAt','-F',"`t",'-v','ON_ERROR_STOP=1')) ($prefix + "SELECT last_value, is_called, (SELECT seqincrement FROM pg_sequence WHERE seqrelid = '$sequence'::regclass) FROM $sequence; COMMIT;")
        $seqParts = $seqStateText -split "`t"
        if ($seqParts.Count -ne 3) { throw 'Unexpected sequence state output.' }
        $maxId = [long]$maxIdText
        $lastValue = [long]$seqParts[0]
        $isCalled = $seqParts[1] -eq 't'
        $increment = [long]$seqParts[2]
        if ($increment -le 0) {
            # A zero or negative increment makes "ahead of max id" mean something entirely
            # different (a descending sequence's safety condition is being behind the MIN id,
            # not ahead of the MAX) - genuinely unsupported, not just non-default. Every other
            # positive increment is evaluated correctly below using its real value, not assumed.
            $result += [pscustomobject]@{
                Table = $table; Column = $column; Sequence = $sequence
                MaxId = $maxId; Increment = $increment
                Supported = $false; Behind = $false
                Note = "Non-positive increment ($increment) - ascending next-value calculation does not apply to this sequence."
            }
            continue
        }
        $nextIssuable = if ($isCalled) { $lastValue + $increment } else { $lastValue }
        $result += [pscustomobject]@{
            Table = $table; Column = $column; Sequence = $sequence
            MaxId = $maxId; Increment = $increment; NextIssuableValue = $nextIssuable
            Supported = $true; Behind = ($nextIssuable -le $maxId)
        }
    }
    return $result
}

function Get-VoucherSequenceIntegrityReport([string]$Psql, [string[]]$Connection) {
    # Read-only DIAGNOSTIC, not a pass/fail gate: Services/VoucherSequenceAllocator.cs
    # reconciles voucher_sequences.last_number against the actual max voucher in scope on
    # every reservation (GREATEST(last_number + 1, actual-max + 1)), so a behind counter here
    # is real drift worth surfacing but does NOT mean the next voucher would collide - the
    # allocator self-heals on next use. Callers should report this, never fail on it.
    #
    # Scope matches the allocator exactly (see VoucherSequenceAllocator.ReserveAsync): a
    # voucher type with reset_period = 'Never' is INTENDED to store one voucher_sequences row
    # (keyed to the company's earliest financial year), but Migration 007 originally created
    # these rows grouped by financial year even for types later switched to Never - so a
    # never-reset type can legitimately still have multiple historical counter rows in restored
    # data. The allocator takes the MAX(last_number) across all of them as the effective
    # counter, so this check must too: aggregate every never-reset row for a (company, type)
    # down to the single one holding the largest last_number (DISTINCT ON below) before
    # comparing it against the highest voucher.sequence_number across EVERY financial year for
    # that (company, type) - never testing each historical row independently, which would flag
    # a stale-but-harmless row even while the effective (maximum) counter is current. Any other
    # reset period compares within the matching financial year only, as before.
    if (-not (Test-TableExists $Psql $Connection 'voucher_sequences')) { return @() }
    if (-not (Test-TableExists $Psql $Connection 'vouchers')) { return @() }
    if (-not (Test-TableExists $Psql $Connection 'voucher_types')) { return @() }
    $prefix = "BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY; SET LOCAL timezone='UTC';"
    $checkSql = @"
WITH never_effective_row AS (
    SELECT DISTINCT ON (vs.company_id, vs.voucher_type_id)
           vs.company_id, vs.voucher_type_id, vs.financial_year_id, vs.last_number
    FROM voucher_sequences vs
    JOIN voucher_types vt ON vt.id = vs.voucher_type_id
    WHERE lower(vt.reset_period) = 'never'
    ORDER BY vs.company_id, vs.voucher_type_id, vs.last_number DESC, vs.financial_year_id ASC
),
never_check AS (
    SELECT ner.company_id, ner.financial_year_id, ner.voucher_type_id, ner.last_number,
           COALESCE(MAX(v.sequence_number),0) AS max_actual, 'never'::text AS reset_period
    FROM never_effective_row ner
    LEFT JOIN vouchers v ON v.company_id = ner.company_id AND v.voucher_type_id = ner.voucher_type_id
    GROUP BY ner.company_id, ner.financial_year_id, ner.voucher_type_id, ner.last_number
    HAVING ner.last_number < COALESCE(MAX(v.sequence_number),0)
),
other_check AS (
    SELECT vs.company_id, vs.financial_year_id, vs.voucher_type_id, vs.last_number,
           COALESCE(MAX(v.sequence_number),0) AS max_actual, lower(vt.reset_period) AS reset_period
    FROM voucher_sequences vs
    JOIN voucher_types vt ON vt.id = vs.voucher_type_id
    LEFT JOIN vouchers v
      ON v.company_id = vs.company_id AND v.voucher_type_id = vs.voucher_type_id AND v.financial_year_id = vs.financial_year_id
    WHERE lower(vt.reset_period) <> 'never'
    GROUP BY vs.company_id, vs.financial_year_id, vs.voucher_type_id, vs.last_number, vt.reset_period
    HAVING vs.last_number < COALESCE(MAX(v.sequence_number),0)
)
SELECT company_id || E'\t' || financial_year_id || E'\t' || voucher_type_id || E'\t' || last_number || E'\t' || max_actual || E'\t' || reset_period
FROM (SELECT * FROM never_check UNION ALL SELECT * FROM other_check) combined
ORDER BY company_id, financial_year_id, voucher_type_id;
"@
    $rows = Invoke-PgTool $Psql ($Connection + @('-X','-w','-qAt','-F',"`t",'-v','ON_ERROR_STOP=1')) ($prefix + $checkSql + ' COMMIT;')
    $result = @()
    foreach ($line in ($rows -split "`r?`n" | Where-Object { $_ })) {
        $parts = $line -split "`t"
        if ($parts.Count -ne 6) { throw 'Unexpected voucher-sequence check output.' }
        $result += [pscustomobject]@{
            CompanyId = [long]$parts[0]; FinancialYearId = [long]$parts[1]; VoucherTypeId = [long]$parts[2]
            LastNumber = [long]$parts[3]; MaxActualSequenceNumber = [long]$parts[4]; ResetPeriod = $parts[5]
        }
    }
    return $result
}

function Get-DatabaseCompatibilityIdentity([string]$Psql, [string[]]$Connection) {
    $sql = "SELECT json_build_object('Database',current_database(),'ServerVersion',current_setting('server_version'),'Encoding',pg_encoding_to_char(encoding),'Collation',datcollate,'Ctype',datctype)::text FROM pg_database WHERE datname=current_database();"
    $json = Invoke-PgTool $Psql ($Connection + @('-X','-w','-qAt','-v','ON_ERROR_STOP=1')) $sql
    return $json | ConvertFrom-Json
}

function Get-RecoveryInventory([string]$Psql, [string[]]$Connection, [string]$Snapshot = '') {
    $prefix = "BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;"
    if ($Snapshot) {
        if ($Snapshot -notmatch '^[0-9A-Fa-f]+-[0-9A-Fa-f]+-[0-9]+$') { throw 'Invalid exported snapshot identity.' }
        $prefix += " SET TRANSACTION SNAPSHOT '$Snapshot';"
    }
    $prefix += " SET LOCAL timezone='UTC'; SET LOCAL datestyle='ISO, YMD'; SET LOCAL intervalstyle='postgres'; SET LOCAL extra_float_digits=3;"
    $tableSql = @"
SELECT format('%I.%I', n.nspname, c.relname)
FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE c.relkind='r' AND n.nspname NOT IN ('pg_catalog','information_schema') AND n.nspname NOT LIKE 'pg_toast%'
ORDER BY n.nspname COLLATE "C", c.relname COLLATE "C";
"@
    $tables = Invoke-PgTool $Psql ($Connection + @('-X','-w','-qAt','-v','ON_ERROR_STOP=1')) ($prefix + $tableSql + ' COMMIT;')
    $result = @()
    foreach ($table in ($tables -split "`r?`n" | Where-Object { $_ })) {
        # Identifiers come from PostgreSQL format(%I); reject control characters before embedding.
        if ($table -match '[\r\n]') { throw 'Unsupported table identifier.' }
        $process = Start-PgProcess $Psql ($Connection + @('-X','-w','-qAt','-v','ON_ERROR_STOP=1'))
        $hasher = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
        try {
            $errors = $process.StandardError.ReadToEndAsync()
            # Sort fixed-length row hashes in PostgreSQL, then stream the result. No complete
            # table or user data is materialized in PowerShell or printed to the console.
            $process.StandardInput.WriteLine($prefix + " COPY (SELECT h FROM (SELECT encode(sha256(convert_to(to_jsonb(t)::text,'UTF8')),'hex') AS h FROM $table t) r ORDER BY h COLLATE ""C"") TO STDOUT; COMMIT;")
            $process.StandardInput.Close()
            $count = 0L
            while ($null -ne ($line = $process.StandardOutput.ReadLine())) {
                if ($line -notmatch '^[0-9a-f]{64}$') { throw 'Unexpected inventory output.' }
                $hasher.AppendData([Text.Encoding]::UTF8.GetBytes($line + "`n"))
                $count++
            }
            $process.WaitForExit()
            $null = $errors.GetAwaiter().GetResult()
            if ($process.ExitCode -ne 0) { throw 'Table inventory failed; recovery verification is incomplete.' }
            $result += [pscustomobject]@{ Table=$table; Rows=$count; Sha256=[Convert]::ToHexString($hasher.GetHashAndReset()) }
        } finally { $hasher.Dispose(); $process.Dispose() }
    }
    return $result
}

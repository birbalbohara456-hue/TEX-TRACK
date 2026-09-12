#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Database,
    [Parameter(Mandatory)][string]$DatabaseUser,
    [string]$Server = 'localhost',
    [ValidateRange(1,65535)][int]$Port = 5432,
    [string]$PgBin = 'C:\Program Files\PostgreSQL\18\bin',
    [string]$RecoveryRoot = (Join-Path $env:LOCALAPPDATA 'TexTrack\Recovery')
)
. (Join-Path $PSScriptRoot 'Recovery.Common.ps1')
$psql = Join-Path $PgBin 'psql.exe'
$connection = @('-h',$Server,'-p',"$Port",'-U',$DatabaseUser,'-d',$Database)
$directory = New-PrivateRecoveryDirectory $RecoveryRoot 'backup'
$timer = [Diagnostics.Stopwatch]::StartNew()
$snapshotProcess = $null
try {
    $source = Get-DatabaseCompatibilityIdentity $psql $connection
    # Keep one read-only transaction open so pg_dump and every table fingerprint use
    # precisely the same MVCC snapshot, even while the application posts new vouchers.
    $snapshotProcess = Start-PgProcess $psql ($connection + @('-X','-w','-qAt','-v','ON_ERROR_STOP=1'))
    $snapshotErrors = $snapshotProcess.StandardError.ReadToEndAsync()
    $snapshotProcess.StandardInput.WriteLine('BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY; SELECT pg_export_snapshot();')
    $snapshotRead = $snapshotProcess.StandardOutput.ReadLineAsync()
    if (-not $snapshotRead.Wait(30000)) { throw 'Timed out opening a backup snapshot.' }
    $snapshot = $snapshotRead.GetAwaiter().GetResult()
    if ($snapshot -notmatch '^[0-9A-Fa-f]+-[0-9A-Fa-f]+-[0-9]+$') { throw 'Could not establish a backup snapshot.' }
    $archive = Join-Path $directory 'database.dump'
    $dumpVersion = Invoke-PgTool (Join-Path $PgBin 'pg_dump.exe') @('--version')
    $null = Invoke-PgTool (Join-Path $PgBin 'pg_dump.exe') ($connection + @('-w','--format=custom',"--snapshot=$snapshot",'--lock-wait-timeout=30s',"--file=$archive"))
    $inventory = @(Get-RecoveryInventory $psql $connection $snapshot)
    $snapshotProcess.StandardInput.WriteLine('ROLLBACK;')
    $snapshotProcess.StandardInput.Close()
    $snapshotProcess.WaitForExit()
    if ($snapshotProcess.ExitCode -ne 0) { throw 'Backup snapshot session failed.' }
    $manifest = [ordered]@{
        FormatVersion=1; Status='BackupVerifiedNotYetRestored'; CreatedUtc=[DateTime]::UtcNow.ToString('o')
        Source=$source; DumpTool=$dumpVersion; ArchiveSha256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
        Tables=$inventory; ElapsedSeconds=[Math]::Round($timer.Elapsed.TotalSeconds,2)
        Limits='Database only. Global roles, external files, application configuration, TLS and Data Protection keys are not included. Sequence state is not MVCC snapshot data.'
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $directory 'manifest.json') -Encoding utf8
    Write-Output $directory
} finally {
    if ($snapshotProcess) {
        if (-not $snapshotProcess.HasExited) { $snapshotProcess.Kill($true); $snapshotProcess.WaitForExit() }
        $snapshotProcess.Dispose()
    }
    # Failed/partial directories are retained for diagnosis, without a success manifest.
}

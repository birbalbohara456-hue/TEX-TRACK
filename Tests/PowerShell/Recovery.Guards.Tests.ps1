#requires -Version 7.2
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $root 'Tools/Recovery.Common.ps1')
$testRoot = New-PrivateRecoveryDirectory (Join-Path $env:LOCALAPPDATA 'TexTrack\Recovery') 'guard-tests'
function Assert-Rejected([scriptblock]$Action, [string]$Expected) {
    try { & $Action; throw 'Expected rejection did not occur.' }
    catch { if ($_.Exception.Message -notlike "*$Expected*") { throw } }
}
Assert-Rejected { New-PrivateRecoveryDirectory 'relative-path' 'test' } 'absolute path'
Assert-Rejected { Get-RecoveryInventory 'missing-psql.exe' @() "bad'; SELECT 1;--" } 'Invalid exported snapshot'
Assert-Rejected { Invoke-PgTool (Join-Path $testRoot 'missing.exe') @() } 'tool missing'
$restore = Join-Path $root 'Tools/Test-TexTrackRestore.ps1'
$badBackup = Join-Path $testRoot 'bad-backup'
$null = New-Item -ItemType Directory -Path $badBackup
[IO.File]::WriteAllBytes((Join-Path $badBackup 'database.dump'), [byte[]](1,2,3))
@{ FormatVersion=99; Status='BackupVerifiedNotYetRestored' } | ConvertTo-Json | Set-Content (Join-Path $badBackup 'manifest.json')
Assert-Rejected { & $restore -BackupDirectory $badBackup -PgBin $testRoot } 'Unsupported/incomplete'
@{ FormatVersion=1; Status='BackupVerifiedNotYetRestored'; ArchiveSha256='INVALID' } | ConvertTo-Json | Set-Content (Join-Path $badBackup 'manifest.json')
Assert-Rejected { & $restore -BackupDirectory $badBackup -PgBin $testRoot } 'Archive hash mismatch'
Write-Output 'PASS: 5 recovery guard tests. No database connection or server startup occurred.'

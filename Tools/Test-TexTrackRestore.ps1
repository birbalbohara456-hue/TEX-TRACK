#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BackupDirectory,
    [string]$PgBin = 'C:\Program Files\PostgreSQL\18\bin',
    [string]$RecoveryRoot = (Join-Path $env:LOCALAPPDATA 'TexTrack\Recovery')
)
. (Join-Path $PSScriptRoot 'Recovery.Common.ps1')
$manifest = Get-Content -LiteralPath (Join-Path $BackupDirectory 'manifest.json') -Raw | ConvertFrom-Json
$archive = (Resolve-Path -LiteralPath (Join-Path $BackupDirectory 'database.dump')).Path
if ($manifest.FormatVersion -ne 1 -or $manifest.Status -ne 'BackupVerifiedNotYetRestored') { throw 'Unsupported/incomplete backup manifest.' }
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $manifest.ArchiveSha256) { throw 'Archive hash mismatch. Restore refused.' }
$directory = New-PrivateRecoveryDirectory $RecoveryRoot 'restore-drill'
$cluster = Join-Path $directory 'cluster'
$passwordFile = Join-Path $directory 'temporary-password.txt'
$pgCtl = Join-Path $PgBin 'pg_ctl.exe'
$timer = [Diagnostics.Stopwatch]::StartNew()
$savedPassword = $env:PGPASSWORD
$started = $false
$temporaryPassword = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
try {
    # New independent cluster, never the configured application's data directory.
    [IO.File]::WriteAllText($passwordFile, $temporaryPassword + "`n", [Text.UTF8Encoding]::new($false))
    $null = Invoke-PgTool (Join-Path $PgBin 'initdb.exe') @('-D',$cluster,'-U','recovery_owner','--auth=scram-sha-256',"--pwfile=$passwordFile",'--encoding=UTF8','--no-locale')
    Remove-Item -LiteralPath $passwordFile
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $listener.Stop()
    # Port races cause startup failure, not connection to another server. No service installed.
    $null = Invoke-PgTool $pgCtl @('-D',$cluster,'-l',(Join-Path $directory 'postgres.log'),'-o',"-h 127.0.0.1 -p $port",'-w','-t','30','start')
    $started = $true
    $env:PGPASSWORD = $temporaryPassword
    $connection = @('-h','127.0.0.1','-p',"$port",'-U','recovery_owner')
    $null = Invoke-PgTool (Join-Path $PgBin 'createdb.exe') ($connection + @('-w','--template=template0','textrack_restore'))
    $connection += @('-d','textrack_restore')
    $null = Invoke-PgTool (Join-Path $PgBin 'pg_restore.exe') ($connection + @('-w','--exit-on-error','--single-transaction','--no-owner','--no-privileges',$archive))
    $psqlExe = Join-Path $PgBin 'psql.exe'
    $actual = @(Get-RecoveryInventory $psqlExe $connection)
    $expected = @($manifest.Tables)
    if ($actual.Count -ne $expected.Count) { throw 'Restored table inventory differs.' }
    foreach ($table in $expected) {
        $restored = @($actual | Where-Object Table -CEQ $table.Table)
        if ($restored.Count -ne 1 -or $restored[0].Rows -ne $table.Rows -or $restored[0].Sha256 -cne $table.Sha256) {
            throw 'Restored row counts or content fingerprints differ; recovery is NOT verified.'
        }
    }

    # Read-only post-restore sanity checks, beyond byte-for-byte row fidelity already proven
    # above. Neither of these calls nextval()/setval() - both would mutate sequence state.
    #
    # Generic identity/serial sequences have no application-level reconciliation: if one is
    # genuinely behind the max id already in its table, the very next INSERT collides on the
    # primary key. This one fails the drill loudly, same as a table-content mismatch.
    $sequenceReport = @(Get-SequenceIntegrityReport $psqlExe $connection)
    $behindSequences = @($sequenceReport | Where-Object { $_.Supported -and $_.Behind })
    if ($behindSequences.Count -gt 0) {
        $names = ($behindSequences | ForEach-Object { "$($_.Sequence) (next issuable $($_.NextIssuableValue) <= max id $($_.MaxId) in $($_.Table).$($_.Column))" }) -join '; '
        throw "Identity sequence(s) behind restored table data - next insert would collide: $names"
    }
    # An unsupported sequence (non-positive increment) was never actually checked, ascending or
    # not. Writing a passing verification.json anyway would claim integrity that was never
    # verified - fail the drill instead of merely warning and continuing.
    $unsupportedSequences = @($sequenceReport | Where-Object { -not $_.Supported })
    if ($unsupportedSequences.Count -gt 0) {
        $names = ($unsupportedSequences | ForEach-Object { "$($_.Sequence): $($_.Note)" }) -join '; '
        throw "Identity sequence integrity could not be verified for: $names"
    }

    # voucher_sequences drift is diagnostic ONLY, never a failure: VoucherSequenceAllocator
    # reconciles against the actual max voucher on every reservation (see
    # Services/VoucherSequenceAllocator.cs), so a behind counter here does not mean the next
    # voucher would collide - it self-heals on next use. Surfaced for visibility, not gated.
    $voucherSequenceReport = @(Get-VoucherSequenceIntegrityReport $psqlExe $connection)
    if ($voucherSequenceReport.Count -gt 0) {
        $names = ($voucherSequenceReport | ForEach-Object { "company $($_.CompanyId)/FY $($_.FinancialYearId)/type $($_.VoucherTypeId) ($($_.ResetPeriod)): last_number=$($_.LastNumber) but max used sequence_number=$($_.MaxActualSequenceNumber)" }) -join '; '
        Write-Warning "voucher_sequences drift detected (not a failure - VoucherSequenceAllocator self-reconciles on next reservation): $names"
    }

    # Informational only - a locale/collation difference from the portable C-locale drill is
    # expected by design, not a failure. Reported so an operator sees it, not enforced here.
    $restoredIdentity = Get-DatabaseCompatibilityIdentity $psqlExe $connection
    $compatibility = [ordered]@{
        SourceServerVersion=$manifest.Source.ServerVersion; RestoredServerVersion=$restoredIdentity.ServerVersion
        SourceEncoding=$manifest.Source.Encoding; RestoredEncoding=$restoredIdentity.Encoding
        SourceCollation=$manifest.Source.Collation; RestoredCollation=$restoredIdentity.Collation
        SourceCtype=$manifest.Source.Ctype; RestoredCtype=$restoredIdentity.Ctype
        EncodingMatches=($manifest.Source.Encoding -eq $restoredIdentity.Encoding)
        CollationMatches=($manifest.Source.Collation -eq $restoredIdentity.Collation -and $manifest.Source.Ctype -eq $restoredIdentity.Ctype)
    }

    [ordered]@{
        Status='DatabaseRestoreAndTableContentVerified'; VerifiedUtc=[DateTime]::UtcNow.ToString('o')
        ArchiveSha256=$manifest.ArchiveSha256; Tables=$actual.Count
        Rows=($actual | Measure-Object Rows -Sum).Sum; ElapsedSeconds=[Math]::Round($timer.Elapsed.TotalSeconds,2)
        SequenceIntegrity=$sequenceReport; VoucherSequenceIntegrity=$voucherSequenceReport
        RestoredCompatibility=$compatibility
        Limits='Isolated local cluster with recovery owner and no original ACLs; not production-role, service identity, application login, external-key, or off-machine disaster-recovery verification. Test cluster uses C locale by design (see RestoredCompatibility for the actual difference from source); production collation must be matched separately in a dedicated drill.'
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $directory 'verification.json') -Encoding utf8
    Write-Output $directory
} finally {
    $env:PGPASSWORD = $savedPassword
    $temporaryPassword = $null
    if (Test-Path -LiteralPath $passwordFile) { Remove-Item -LiteralPath $passwordFile }
    if ($started -or (Test-Path -LiteralPath (Join-Path $cluster 'postmaster.pid'))) {
        $null = Invoke-PgTool $pgCtl @('-D',$cluster,'-m','fast','-w','-t','30','stop')
    }
    # Preserve the stopped cluster and evidence. Never automatically delete a database directory.
}

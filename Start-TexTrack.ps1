$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$userSecretsId = 'TexTrack-Web-20260828'
$projectSecretFile = Join-Path $projectRoot 'TexTrack.local.json'
$standardSecretFile = Join-Path $env:APPDATA "Microsoft\UserSecrets\$userSecretsId\secrets.json"
$localSecretFile = Join-Path $env:LOCALAPPDATA 'TexTrackERP\secrets.json'
$secretFile = if (Test-Path -LiteralPath $projectSecretFile) {
    $projectSecretFile
} elseif (Test-Path -LiteralPath $localSecretFile) {
    $localSecretFile
} else {
    $standardSecretFile
}

if (-not (Test-Path -LiteralPath $secretFile)) {
    throw "TexTrack local secrets were not found. Checked: $projectSecretFile, $localSecretFile and $standardSecretFile"
}

$secrets = Get-Content -LiteralPath $secretFile -Raw | ConvertFrom-Json
$connectionString = [string]$secrets.'ConnectionStrings:TexTrackDatabase'

if ([string]::IsNullOrWhiteSpace($connectionString)) {
    throw "ConnectionStrings:TexTrackDatabase is missing from the TexTrack local secrets."
}

if ($connectionString -notmatch '(?i)(Password|Pwd)\s*=\s*[^;]+') {
    throw "The saved TexTrack database connection does not contain a PostgreSQL password."
}

$postgresService = Get-Service -Name 'postgresql-x64-18' -ErrorAction SilentlyContinue
if ($null -ne $postgresService -and $postgresService.Status -ne 'Running') {
    throw "PostgreSQL service postgresql-x64-18 is not running. Start it as Administrator, then run this launcher again."
}

$env:ConnectionStrings__TexTrackDatabase = $connectionString
$env:ASPNETCORE_ENVIRONMENT = 'Development'

Set-Location -LiteralPath $projectRoot
Write-Host 'Starting the current TexTrack build at http://localhost:5075 ...' -ForegroundColor Green
dotnet run --no-restore

if ($LASTEXITCODE -ne 0) {
    throw "TexTrack stopped with exit code $LASTEXITCODE."
}

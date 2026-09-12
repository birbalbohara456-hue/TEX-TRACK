$ErrorActionPreference = "Stop"

function Find-PostgreSqlTool([string]$ToolName) {
    $command = Get-Command $ToolName -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $root = "C:\Program Files\PostgreSQL"
    if (Test-Path $root) {
        $candidate = Get-ChildItem $root -Directory |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName "bin\$ToolName.exe" } |
            Where-Object { Test-Path $_ } |
            Select-Object -First 1
        if ($candidate) { return $candidate }
    }

    return $null
}

Write-Host ""
Write-Host "TexTrack ERP - PostgreSQL Development Database Setup" -ForegroundColor Cyan
Write-Host "This creates only the local TexTrack development database." -ForegroundColor DarkGray
Write-Host ""

$psql = Find-PostgreSqlTool "psql"
$createdb = Find-PostgreSqlTool "createdb"

if (-not $psql -or -not $createdb) {
    Write-Host "PostgreSQL command-line tools were not found." -ForegroundColor Red
    Write-Host "Install PostgreSQL for Windows, including Command Line Tools, then run this setup again."
    exit 1
}

$server = Read-Host "PostgreSQL server [localhost]"
if ([string]::IsNullOrWhiteSpace($server)) { $server = "localhost" }

$port = Read-Host "PostgreSQL port [5432]"
if ([string]::IsNullOrWhiteSpace($port)) { $port = "5432" }

$adminUser = Read-Host "PostgreSQL administrator user [postgres]"
if ([string]::IsNullOrWhiteSpace($adminUser)) { $adminUser = "postgres" }

$securePassword = Read-Host "Password for $adminUser" -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
try {
    $adminPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
}

$env:PGPASSWORD = $adminPassword
$appUser = "textrack_app"
$randomBytes = New-Object byte[] 32
[Security.Cryptography.RandomNumberGenerator]::Fill($randomBytes)
$appPassword = [Convert]::ToBase64String($randomBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
$database = "textrack_dev"

try {
    Write-Host "Checking PostgreSQL connection..."
    & $psql -h $server -p $port -U $adminUser -d postgres -v ON_ERROR_STOP=1 -tAc "SELECT 1;" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Unable to connect to PostgreSQL with the supplied administrator credentials." }

    $roleResult = @(& $psql -h $server -p $port -U $adminUser -d postgres -tAc "SELECT 1 FROM pg_roles WHERE rolname='$appUser';")
    if ($LASTEXITCODE -ne 0) { throw "Unable to check whether the TexTrack PostgreSQL login exists." }
    $roleExists = ($roleResult -join "").Trim()
    if ($roleExists -ne "1") {
        Write-Host "Creating TexTrack development login..."
        & $psql -h $server -p $port -U $adminUser -d postgres -v ON_ERROR_STOP=1 -c "CREATE ROLE $appUser LOGIN PASSWORD '$appPassword';" | Out-Null
    }
    else {
        Write-Host "Refreshing TexTrack development login password..."
        & $psql -h $server -p $port -U $adminUser -d postgres -v ON_ERROR_STOP=1 -c "ALTER ROLE $appUser WITH LOGIN PASSWORD '$appPassword';" | Out-Null
    }

    $databaseResult = @(& $psql -h $server -p $port -U $adminUser -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$database';")
    if ($LASTEXITCODE -ne 0) { throw "Unable to check whether the TexTrack development database exists." }
    $databaseExists = ($databaseResult -join "").Trim()
    if ($databaseExists -ne "1") {
        Write-Host "Creating database $database..."
        & $createdb -h $server -p $port -U $adminUser -O $appUser $database
        if ($LASTEXITCODE -ne 0) { throw "Database creation failed." }
    }
    else {
        Write-Host "Database $database already exists. Existing data will be preserved."
        & $psql -h $server -p $port -U $adminUser -d postgres -v ON_ERROR_STOP=1 -c "ALTER DATABASE $database OWNER TO $appUser;" | Out-Null
    }

    & $psql -h $server -p $port -U $adminUser -d $database -v ON_ERROR_STOP=1 -c "GRANT ALL ON SCHEMA public TO $appUser;" | Out-Null

    $env:PGPASSWORD = $appPassword
    & $psql -h $server -p $port -U $appUser -d $database -v ON_ERROR_STOP=1 -tAc "SELECT current_database();" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "TexTrack login test failed after database creation." }

    $projectRoot = Split-Path $PSScriptRoot -Parent
    $projectFile = Join-Path $projectRoot 'TexTrack.Web.csproj'
    $connectionString = "Host=$server;Port=$port;Database=$database;Username=$appUser;Password=$appPassword;Include Error Detail=true"
    & dotnet user-secrets set 'ConnectionStrings:TexTrackDatabase' $connectionString --project $projectFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Unable to save the TexTrack database connection in .NET User Secrets." }

    Write-Host ""
    Write-Host "PostgreSQL setup completed successfully." -ForegroundColor Green
    Write-Host "Database: $database"
    Write-Host "Application user: $appUser"
    Write-Host "Existing TexTrack data was preserved."
    Write-Host "Now run RUN_TEXTRACK.bat."
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    $adminPassword = $null
    $connectionString = $null
}

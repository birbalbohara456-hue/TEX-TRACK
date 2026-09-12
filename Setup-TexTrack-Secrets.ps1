$ErrorActionPreference = 'Stop'

$projectFile = Join-Path $PSScriptRoot 'TexTrack.Web.csproj'
if (-not (Test-Path -LiteralPath $projectFile)) {
    throw "TexTrack.Web.csproj was not found beside this setup script."
}

$securePassword = Read-Host 'Enter the PostgreSQL password for textrack_app' -AsSecureString
$passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)

try {
    $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    if ([string]::IsNullOrWhiteSpace($plainPassword)) {
        throw 'The PostgreSQL password cannot be blank.'
    }

    $connectionString = "Host=localhost;Port=5432;Database=textrack_dev;Username=textrack_app;Password=$plainPassword;Include Error Detail=true"
    dotnet user-secrets set 'ConnectionStrings:TexTrackDatabase' $connectionString --project $projectFile
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet user-secrets failed with exit code $LASTEXITCODE."
    }

    Write-Host 'TexTrack database access has been saved for this Windows user.' -ForegroundColor Green
    Write-Host 'You can now close this window and run RUN_TEXTRACK.bat.' -ForegroundColor Green
}
finally {
    if ($passwordPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    }
    $plainPassword = $null
    $connectionString = $null
}

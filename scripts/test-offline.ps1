param([string]$PackageCache = 'C:/Users/bales/.nuget/packages')
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/environment.ps1"
$env:NUGET_PACKAGES = $PackageCache
Push-Location "$PSScriptRoot/.."
try {
    dotnet build tools/StarComms.OfflineRunner -p:OfflineTransport=true -p:NuGetAudit=false -m:1
    if ($LASTEXITCODE) { throw 'Offline test compilation failed' }
    dotnet tools/StarComms.OfflineRunner/bin/Debug/net10.0/StarComms.OfflineRunner.dll
    if ($LASTEXITCODE) { throw 'Offline assertions failed' }
} finally { Pop-Location }

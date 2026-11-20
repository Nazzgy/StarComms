$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/environment.ps1"
Push-Location "$PSScriptRoot/.."
try {
    dotnet restore StarComms.slnx -m:1
    if ($LASTEXITCODE) { throw 'Initial restore failed. Do not use incomplete lock files as verified dependency locks.' }
    Write-Output 'Restore succeeded. Review and retain generated packages.lock.json files; subsequent builds use locked mode.'
} finally { Pop-Location }

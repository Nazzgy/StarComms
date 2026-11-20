param([string]$BuildId = (Get-Date).ToUniversalTime().ToString('yyyyMMdd.HHmmss'))
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/environment.ps1"
Push-Location "$PSScriptRoot/.."
try {
    dotnet restore StarComms.slnx --locked-mode -m:1
    if ($LASTEXITCODE) { throw 'Restore failed' }
    dotnet build StarComms.slnx -c Release --no-restore -m:1 "-p:BuildId=$BuildId"
    if ($LASTEXITCODE) { throw 'Build failed' }
    dotnet test tests/StarComms.Tests/StarComms.Tests.csproj -c Release --no-build --logger trx
    if ($LASTEXITCODE) { throw 'Tests failed' }
} finally { Pop-Location }

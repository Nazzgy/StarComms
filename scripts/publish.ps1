$ErrorActionPreference = 'Stop'
$buildId = (Get-Date).ToUniversalTime().ToString('yyyyMMdd.HHmmss')
& "$PSScriptRoot/build.ps1" -BuildId $buildId
. "$PSScriptRoot/environment.ps1"
Push-Location "$PSScriptRoot/.."
try {
    dotnet publish src/StarComms.App/StarComms.App.csproj -c Release -r win-x64 --self-contained true --no-restore -m:1 "-p:BuildId=$buildId" -o obj/publish/win-x64
    if ($LASTEXITCODE) { throw 'Publish failed' }
    $files = @(Get-ChildItem obj/publish/win-x64 -File)
    if ($files.Count -ne 1 -or $files[0].Name -ne 'StarComms.exe') { throw 'Distribution must contain exactly StarComms.exe' }
    $actual = (Get-Item obj/publish/win-x64/StarComms.exe).VersionInfo.ProductVersion
    $expected = (Get-Content VERSION -Raw).Trim() + '+' + $buildId
    if ($actual -ne $expected) { throw "Version mismatch: $actual" }
    New-Item -ItemType Directory -Force artifacts | Out-Null
    Copy-Item obj/publish/win-x64/StarComms.exe artifacts/StarComms.exe -Force
    Get-FileHash artifacts/StarComms.exe -Algorithm SHA256
} finally { Pop-Location }

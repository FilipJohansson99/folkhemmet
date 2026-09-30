<#
.SYNOPSIS
    Rebuilds the custom mod (ValheimServerTweaks) and copies the dll into game-files so
    start-server.ps1 / Play.bat deploy it.
    Needs the .NET SDK 8+ (https://dotnet.microsoft.com/download) and BepInEx installed in Valheim
    (run Play.bat or start-server.ps1 once first).
#>
param(
    [string]$ValheimDir = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim',
    [string]$ServerDir = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server'
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$proj = [IO.Path]::Combine($here, 'ValheimServerTweaks', 'ValheimServerTweaks.csproj')

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'dotnet not found - install the .NET SDK from https://dotnet.microsoft.com/download' }

$bepCore = $null
foreach ($d in @($ValheimDir, $ServerDir)) {
    $c = [IO.Path]::Combine($d, 'BepInEx', 'core')
    if (Test-Path -LiteralPath (Join-Path $c 'BepInEx.dll')) { $bepCore = $c; break }
}
if (-not $bepCore) { throw 'BepInEx not found in Valheim or the dedicated server - run Play.bat or start-server.ps1 once first.' }

$out = Join-Path $here 'bin'
& dotnet build $proj -c Release "-p:ValheimDir=$ValheimDir" "-p:BepInExCoreDir=$bepCore" -o $out
if ($LASTEXITCODE -ne 0) { throw 'Build failed - see the errors above.' }

$dest = [IO.Path]::Combine($here, '..', 'game-files', 'common', 'BepInEx', 'plugins', 'ValheimServerTweaks')
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item -LiteralPath (Join-Path $out 'ValheimServerTweaks.dll') -Destination $dest -Force
Write-Host 'Built and copied to game-files\common\BepInEx\plugins\ValheimServerTweaks.' -ForegroundColor Green
Write-Host 'Run start-server.ps1 to deploy it to the server and publish it to players.' -ForegroundColor Green

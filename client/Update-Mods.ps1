<#
.SYNOPSIS
    Installs / updates the server's mods in your Valheim, then (with -Launch) starts the game.

.DESCRIPTION
    Players normally just double-click Play.bat, which runs:  Update-Mods.ps1 -Launch
    - Finds your Valheim folder (Steam) or asks for it once.
    - Downloads the mod list the server published and installs exactly those versions
      (BepInEx, Thunderstore mods, the server's custom mod, configs and textures).
    - Removes mods that were dropped from the server's list.
    - Keeps these scripts up to date.

.PARAMETER Launch     Start Valheim through Steam when done.
.PARAMETER Uninstall  Remove everything this script installed and disable BepInEx.
.PARAMETER GamePath   Use this Valheim folder (saved for next time).
#>
[CmdletBinding()]
param(
    [switch]$Launch,
    [switch]$Uninstall,
    [switch]$NoSelfUpdate,
    [string]$GamePath
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$Here = $PSScriptRoot

# ModSync.ps1 is next to this script in the player download, or in ..\lib inside the server project (host mode).
$ProjectRoot = $null
$lib = Join-Path $Here 'ModSync.ps1'
if (-not (Test-Path -LiteralPath $lib)) {
    $lib = [IO.Path]::Combine($Here, '..', 'lib', 'ModSync.ps1')
    $ProjectRoot = [IO.Path]::GetFullPath([IO.Path]::Combine($Here, '..'))
}
. $lib
Initialize-ModSync

$ConfigFile = Join-Path $Here 'client-config.json'
$CacheDir = Join-Path $Here 'cache'
if ($ProjectRoot) { $CacheDir = Join-Path $ProjectRoot '.cache' }   # host mode: share the server's download cache

function Get-ClientConfig {
    $c = Read-JsonFile $ConfigFile
    if (-not $c) { $c = [pscustomobject]@{} }
    foreach ($p in 'ManifestUrl', 'GamePath') {
        if (-not ($c.PSObject.Properties.Name -contains $p)) { $c | Add-Member -NotePropertyName $p -NotePropertyValue '' }
    }
    if (-not ($c.PSObject.Properties.Name -contains 'KeepLocalConfigs')) { $c | Add-Member -NotePropertyName KeepLocalConfigs -NotePropertyValue @() }
    if (-not ($c.PSObject.Properties.Name -contains 'DesktopShortcut')) { $c | Add-Member -NotePropertyName DesktopShortcut -NotePropertyValue $false }
    return $c
}

function Save-ClientConfig($c) {
    # KeepLocalConfigs: config file names (e.g. "seneaL.valheim.ui.cfg") you want to keep your own version of.
    Write-JsonFile $ConfigFile ([ordered]@{
        ManifestUrl      = [string]$c.ManifestUrl
        GamePath         = [string]$c.GamePath
        KeepLocalConfigs = @($c.KeepLocalConfigs | Where-Object { $_ })
        DesktopShortcut  = [bool]$c.DesktopShortcut
    })
}

function New-PlayShortcuts($c, [string]$Name) {
    # Play.bat can't have its own icon, so we make "<server>.lnk" shortcuts with the icon (kit folder + desktop, once).
    if ($env:OS -ne 'Windows_NT') { return }
    $ico = Join-Path $Here 'Folkhemmet.ico'
    $bat = Join-Path $Here 'Play.bat'
    if (-not (Test-Path -LiteralPath $ico) -or -not (Test-Path -LiteralPath $bat)) { return }
    if (-not $Name) { $Name = 'Folkhemmet' }
    $Name = ($Name -replace '[\\/:*?"<>|]', '').Trim()
    try {
        $shell = New-Object -ComObject WScript.Shell
        $targets = @(Join-Path $Here "$Name.lnk")
        if (-not $c.DesktopShortcut) { $targets += Join-Path ([Environment]::GetFolderPath('Desktop')) "$Name.lnk" }
        # Start menu: <name>\<name>  and  <name>\Remove <name> mods
        $menu = Join-Path ([Environment]::GetFolderPath('Programs')) $Name
        New-Directory $menu
        $targets += Join-Path $menu "$Name.lnk"
        foreach ($t in $targets) {
            $lnk = $shell.CreateShortcut($t)
            $lnk.TargetPath = $bat
            $lnk.WorkingDirectory = $Here
            $lnk.IconLocation = "$ico,0"
            $lnk.Description = "Update the mods and play on $Name"
            $lnk.Save()
        }
        $remove = Join-Path $Here 'Remove Mods.bat'
        if (Test-Path -LiteralPath $remove) {
            $lnk = $shell.CreateShortcut((Join-Path $menu "Remove $Name mods.lnk"))
            $lnk.TargetPath = $remove
            $lnk.WorkingDirectory = $Here
            $lnk.Description = "Remove the $Name mods from Valheim"
            $lnk.Save()
        }
        if (-not $c.DesktopShortcut) {
            $c.DesktopShortcut = $true
            Save-ClientConfig $c
            Write-Good "Added a '$Name' shortcut to your desktop."
        }
    } catch {
        Write-Info "Could not create the shortcut ($($_.Exception.Message))."
    }
}

function Find-ValheimFolder {
    $steamRoots = New-Object System.Collections.ArrayList
    foreach ($key in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
        try {
            $p = Get-ItemProperty -Path $key -ErrorAction Stop
            foreach ($name in 'SteamPath', 'InstallPath') {
                if ($p.PSObject.Properties.Name -contains $name -and $p.$name) { [void]$steamRoots.Add(([string]$p.$name).Replace('/', '\')) }
            }
        } catch { }
    }
    [void]$steamRoots.Add('C:\Program Files (x86)\Steam')
    [void]$steamRoots.Add('C:\Program Files\Steam')
    $candidates = New-Object System.Collections.ArrayList
    foreach ($sr in ($steamRoots | Select-Object -Unique)) {
        [void]$candidates.Add([IO.Path]::Combine($sr, 'steamapps', 'common', 'Valheim'))
        $vdf = [IO.Path]::Combine($sr, 'steamapps', 'libraryfolders.vdf')
        if (Test-Path -LiteralPath $vdf) {
            foreach ($m in [regex]::Matches([IO.File]::ReadAllText($vdf), '"path"\s+"([^"]+)"')) {
                [void]$candidates.Add([IO.Path]::Combine($m.Groups[1].Value.Replace('\\', '\'), 'steamapps', 'common', 'Valheim'))
            }
        }
    }
    foreach ($c in ($candidates | Select-Object -Unique)) {
        if (Test-Path -LiteralPath (Join-Path $c 'valheim.exe')) { return $c }
    }
    return $null
}

function Resolve-GameFolder($c) {
    $path = $GamePath
    if (-not $path) { $path = [string]$c.GamePath }
    if ($path -and -not (Test-Path -LiteralPath (Join-Path $path 'valheim.exe'))) {
        Write-Warn "valheim.exe not found in $path"
        $path = $null
    }
    if (-not $path) { $path = Find-ValheimFolder }
    while (-not $path) {
        Write-Host ''
        Write-Host 'Could not find Valheim automatically.' -ForegroundColor Yellow
        Write-Host 'In Steam: right-click Valheim -> Manage -> Browse local files, copy that folder path and paste it here.'
        $answer = (Read-Host 'Valheim folder').Trim().Trim('"')
        if ($answer -and (Test-Path -LiteralPath (Join-Path $answer 'valheim.exe'))) { $path = $answer }
        else { Write-Warn 'That folder does not contain valheim.exe.' }
    }
    $path = [IO.Path]::GetFullPath($path)
    if ($c.GamePath -ne $path) { $c.GamePath = $path; Save-ClientConfig $c }
    return $path
}

function Wait-ValheimClosed {
    while (Get-Process -Name 'valheim' -ErrorAction SilentlyContinue) {
        Write-Warn 'Valheim is running. Close the game, then press Enter.'
        [void](Read-Host)
    }
}

function Get-BaseUrl([string]$ManifestUrl) {
    $i = $ManifestUrl.LastIndexOf('client-pack/')
    if ($i -lt 0) { throw "ManifestUrl should end with client-pack/manifest.json: $ManifestUrl" }
    return $ManifestUrl.Substring(0, $i)
}

function Get-SourceUrl([string]$Base, [string]$Source) {
    $escaped = ($Source.Split('/') | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/'
    return $Base + $escaped
}

function Update-Scripts($Manifest, [string]$Base) {
    # Returns $true when Update-Mods.ps1 / ModSync.ps1 changed (caller restarts itself).
    $restart = $false
    foreach ($s in @($Manifest.clientScripts)) {
        $local = Join-Path $Here $s.name
        if ((Get-Sha256 $local) -eq ([string]$s.sha256).ToUpperInvariant()) { continue }
        $tmp = "$local.download"
        Save-Url (Get-SourceUrl $Base $s.source) $tmp
        if ((Get-Sha256 $tmp) -ne ([string]$s.sha256).ToUpperInvariant()) { Remove-PathSafe $tmp; Write-Warn "Could not verify new $($s.name), keeping the old one."; continue }
        if ($s.name -eq 'Play.bat') {
            # The running Play.bat must not be overwritten - it swaps the new one in on its next start.
            Move-Item -LiteralPath $tmp -Destination "$local.new" -Force
        } else {
            Move-Item -LiteralPath $tmp -Destination $local -Force
            if ($s.name -like '*.ps1') { $restart = $true }
        }
        Write-Good "Updated $($s.name)"
    }
    return $restart
}

function Uninstall-Mods([string]$Game) {
    $state = Read-SyncState $Game
    foreach ($m in $state.mods) { foreach ($d in @($m.dirs)) { Remove-PathSafe (Join-Path $Game (ConvertTo-NativePath $d)) } }
    foreach ($f in $state.files) { Remove-PathSafe (Join-Path $Game (ConvertTo-NativePath $f.target)) }
    foreach ($n in 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version') { Remove-PathSafe (Join-Path $Game $n) }
    Remove-PathSafe (Get-SyncStatePath $Game)
    Write-Good 'Mods removed and BepInEx disabled. Valheim is vanilla again (run Play.bat to reinstall).'
}

# =====================================================================================

Write-Host ''
Write-Host '  VALHEIM MODPACK' -ForegroundColor Cyan

$cfg = Get-ClientConfig
$game = Resolve-GameFolder $cfg
Write-Info "Valheim: $game"
Wait-ValheimClosed

if ($Uninstall) {
    Uninstall-Mods $game
    return
}

# ---- manifest -------------------------------------------------------------------------
Write-Step 'Getting the server mod list'
$base = $null
if ($ProjectRoot) {
    $manifestPath = [IO.Path]::Combine($ProjectRoot, 'client-pack', 'manifest.json')
    $manifest = Read-JsonFile $manifestPath
    if (-not $manifest) { throw "No $manifestPath yet - run start-server.ps1 once (it creates the player pack)." }
    Write-Info 'Host mode: using the files in the server project folder.'
} else {
    if (-not $cfg.ManifestUrl) { throw "No ManifestUrl in $ConfigFile. Ask the server host for a fresh download." }
    $base = Get-BaseUrl ([string]$cfg.ManifestUrl)
    $manifest = Invoke-JsonRequest ("$($cfg.ManifestUrl)?t=" + [DateTime]::UtcNow.Ticks)
    if (-not $NoSelfUpdate -and (Update-Scripts $manifest $base)) {
        Write-Info 'Restarting with the updated script...'
        $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $Here 'Update-Mods.ps1'), '-NoSelfUpdate')
        if ($Launch) { $argList += '-Launch' }
        if ($GamePath) { $argList += @('-GamePath', $GamePath) }
        & (Get-Process -Id $PID).Path @argList
        exit $LASTEXITCODE
    }
}
if ($manifest.serverName) { Write-Info "Server: $($manifest.serverName)" }

# ---- mods -----------------------------------------------------------------------------
Write-Step 'Installing / updating mods'
$bep = [pscustomobject]@{ FullName = $manifest.bepinex.fullName; Version = $manifest.bepinex.version; DownloadUrl = $manifest.bepinex.url }
$mods = @($manifest.mods | Where-Object { $_ } | ForEach-Object { [pscustomobject]@{ FullName = $_.fullName; Version = $_.version; DownloadUrl = $_.url } })
Sync-GameMods -GameDir $game -BepInEx $bep -Mods $mods -CacheDir $CacheDir

Write-Step 'Installing mod settings, custom mod and textures'
$files = @($manifest.files | Where-Object { $_ })
$keep = @($cfg.KeepLocalConfigs | Where-Object { $_ } | ForEach-Object { "BepInEx/config/$_" })
if ($keep.Count -gt 0) { Write-Info "Keeping your own: $(@($cfg.KeepLocalConfigs) -join ', ')" }
if ($ProjectRoot) {
    Sync-GameFiles -GameDir $game -Files $files -Keep $keep -Fetch { param($f, $dest) [IO.File]::Copy((Join-Path $ProjectRoot (ConvertTo-NativePath $f.source)), $dest, $true) }
} else {
    Sync-GameFiles -GameDir $game -Files $files -Keep $keep -Fetch { param($f, $dest) Save-Url (Get-SourceUrl $base $f.source) $dest }
}

# Keep the download cache small: only the zips of the current versions (the host's cache is managed by start-server.ps1).
if (-not $ProjectRoot -and (Test-Path -LiteralPath $CacheDir)) {
    $keep = @($mods + $bep | ForEach-Object { "$($_.FullName)-$($_.Version).zip".ToLowerInvariant() })
    Get-ChildItem -LiteralPath $CacheDir -Filter '*.zip' | Where-Object { $keep -notcontains $_.Name.ToLowerInvariant() } | ForEach-Object { Remove-PathSafe $_.FullName }
}

New-PlayShortcuts $cfg ([string]$manifest.serverName)

Write-Step 'All mods are in sync with the server'
if ($Launch) {
    Write-Info 'Starting Valheim through Steam...'
    Start-Process 'steam://rungameid/892970'
}

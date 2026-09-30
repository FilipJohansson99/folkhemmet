<#
.SYNOPSIS
    Updates the mods and starts the Valheim dedicated server.

.DESCRIPTION
    1. Reads server-config.psd1 (asks for server name + password the first time)
    2. Checks Thunderstore for the newest versions of everything in mods.txt (+ dependencies)
    3. Installs/updates BepInEx and the mods in the dedicated server folder
    4. Copies game-files\common + game-files\server (configs, custom mod) into the server
    5. Writes the player pack (client-pack\) and pushes it to GitHub so friends get the same mods
    6. Checks Windows Firewall + router port forwarding (opens the ports via UPnP when possible)
    7. Backs up the world and starts the server (stop it with Ctrl+C - it saves first),
       then checks in the background that the server can be reached from the internet

.PARAMETER SkipModUpdate
    Don't contact Thunderstore; start with the mods that are already installed.
.PARAMETER NoLaunch
    Do everything except starting the server (useful to just publish mod changes).
.PARAMETER NoPublish
    Don't commit/push the player pack to GitHub this time.
.PARAMETER SkipPortCheck
    Don't check the firewall / router ports this time.
.PARAMETER NonInteractive
    Never ask questions (used by Publish-Folkhemmet.ps1); fails if the config is incomplete.
.PARAMETER ConfigPath
    Use another config file (default: server-config.psd1 next to this script).
#>
[CmdletBinding()]
param(
    [switch]$SkipModUpdate,
    [switch]$NoLaunch,
    [switch]$NoPublish,
    [switch]$SkipPortCheck,
    [switch]$NonInteractive,
    [string]$ConfigPath
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$Root = $PSScriptRoot
. ([IO.Path]::Combine($Root, 'lib', 'ModSync.ps1'))
. ([IO.Path]::Combine($Root, 'lib', 'NetworkCheck.ps1'))
Initialize-ModSync

$ExampleConfig = Join-Path $Root 'server-config.example.psd1'
if (-not $ConfigPath) { $ConfigPath = Join-Path $Root 'server-config.psd1' }
$ModsFile = Join-Path $Root 'mods.txt'
$CacheDir = Join-Path $Root '.cache'
$PackDir = Join-Path $Root 'client-pack'
$ManifestPath = Join-Path $PackDir 'manifest.json'
$KitZipName = 'kit.zip'                 # payload the installer downloads
$FriendZipName = 'Folkhemmet.zip'        # what friends download (Install-Folkhemmet.bat inside)

# =====================================================================================
# Config
# =====================================================================================

function Import-DataFile([string]$Path) {
    # Like Import-PowerShellDataFile, but reads UTF-8 (so names like 'Asgard' with a-ring work in PowerShell 5.1).
    $text = [IO.File]::ReadAllText($Path)
    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseInput($text, [ref]$tokens, [ref]$errors)
    if ($errors -and $errors.Count -gt 0) {
        throw "Syntax error in ${Path} (line $($errors[0].Extent.StartLineNumber)): $($errors[0].Message)"
    }
    $hashAst = $ast.Find({ param($n) $n -is [System.Management.Automation.Language.HashtableAst] }, $false)
    if (-not $hashAst) { throw "$Path must contain a @{ ... } block." }
    return $hashAst.SafeGetValue()
}

function Set-ConfigString([string]$Path, [string]$Key, [string]$Value) {
    $text = [IO.File]::ReadAllText($Path)
    $quoted = "'" + $Value.Replace("'", "''") + "'"
    $pattern = "(?m)^(\s*$Key\s*=\s*)'(?:[^'\r\n]|'')*'"
    if (-not [regex]::IsMatch($text, $pattern)) { throw "Could not find '$Key' in $Path" }
    $text = ([regex]$pattern).Replace($text, '${1}' + $quoted.Replace('$', '$$'), 1)
    Write-TextFile $Path $text -Bom
}

function Set-ConfigArray([string]$Path, [string]$Key, [string[]]$Values) {
    $text = [IO.File]::ReadAllText($Path)
    $items = ($Values | ForEach-Object { "'" + $_.Replace("'", "''") + "'" }) -join ', '
    $pattern = "(?m)^(\s*$Key\s*=\s*)@\([^)\r\n]*\)"
    $text = ([regex]$pattern).Replace($text, '${1}' + ('@(' + $items + ')').Replace('$', '$$'), 1)
    Write-TextFile $Path $text -Bom
}

function Get-PasswordProblem([string]$Password, [string]$WorldName, [string]$ServerName) {
    if (-not $Password -or $Password -eq 'CHANGE ME') { return 'Password is not set.' }
    if ($Password.Length -lt 5) { return 'Password must be at least 5 characters.' }
    if ($Password.Contains('"')) { return 'Password cannot contain double quotes (").' }
    if ($WorldName -and $WorldName.Contains($Password)) { return 'Password cannot be part of the world name.' }
    if ($ServerName -and $ServerName.Contains($Password)) { return 'Password cannot be part of the server name.' }
    return ''
}

function Read-Answer([string]$Prompt, [string]$Default) {
    $suffix = ''
    if ($Default) { $suffix = " [$Default]" }
    $a = Read-Host "$Prompt$suffix"
    if (-not $a) { $a = $Default }
    return $a
}

function Get-HostSteamId {
    # SteamID64 of the account last logged in to Steam on this PC (from Steam's loginusers.vdf).
    try {
        $steam = $null
        foreach ($key in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam') {
            $p = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
            if ($p -and $p.PSObject.Properties.Name -contains 'SteamPath' -and $p.SteamPath) { $steam = ([string]$p.SteamPath).Replace('/', '\'); break }
            if ($p -and $p.PSObject.Properties.Name -contains 'InstallPath' -and $p.InstallPath) { $steam = [string]$p.InstallPath; break }
        }
        if (-not $steam) { $steam = 'C:\Program Files (x86)\Steam' }
        $vdf = [IO.Path]::Combine($steam, 'config', 'loginusers.vdf')
        if (-not (Test-Path -LiteralPath $vdf)) { return $null }
        $text = [IO.File]::ReadAllText($vdf)
        $first = $null
        foreach ($m in [regex]::Matches($text, '"(7656\d{13})"\s*\{([^}]*)\}')) {
            if (-not $first) { $first = $m.Groups[1].Value }
            if ($m.Groups[2].Value -match '"MostRecent"\s*"1"') { return $m.Groups[1].Value }
        }
        return $first
    } catch { return $null }
}

function Initialize-Config {
    if (-not (Test-Path -LiteralPath $ConfigPath)) {
        Write-Step 'First start - creating server-config.psd1'
        Write-TextFile $ConfigPath ([IO.File]::ReadAllText($ExampleConfig)) -Bom
    }
    $cfg = Import-DataFile $ConfigPath

    # Fill in settings added to the example later (keeps old configs working).
    $defaults = Import-DataFile $ExampleConfig
    foreach ($k in $defaults.Keys) { if (-not $cfg.ContainsKey($k)) { $cfg[$k] = $defaults[$k] } }

    $asked = $false
    if (-not $cfg.ServerName -or $cfg.ServerName -eq 'CHANGE ME') {
        if ($NonInteractive) { throw "ServerName is not set in $ConfigPath." }
        $asked = $true
        do { $name = (Read-Answer 'Server name (shown in the server list)' '').Trim().Replace('"', '') } while (-not $name)
        Set-ConfigString $ConfigPath 'ServerName' $name
        $cfg.ServerName = $name
        $world = (Read-Answer 'World name' $cfg.WorldName).Trim()
        Set-ConfigString $ConfigPath 'WorldName' $world
        $cfg.WorldName = $world
    }
    $problem = Get-PasswordProblem $cfg.Password $cfg.WorldName $cfg.ServerName
    if ($problem -and $cfg.Password -and $cfg.Password -ne 'CHANGE ME') { Write-Warn "Password in the config: $problem" }
    if ($problem -and $NonInteractive) { throw "Server password problem in ${ConfigPath}: $problem" }
    while ($problem) {
        $asked = $true
        $cfg.Password = Read-Answer 'Server password (min 5 characters, share it with your friends)' ''
        $problem = Get-PasswordProblem $cfg.Password $cfg.WorldName $cfg.ServerName
        if ($problem) { Write-Warn $problem } else { Set-ConfigString $ConfigPath 'Password' $cfg.Password }
    }
    # Make the host admin automatically (needed for devcommands / Infinity Hammer) - no questions asked.
    $adminMarker = Join-Path $CacheDir 'admin-detected'
    if (@($cfg.Admins).Count -eq 0 -and -not (Test-Path -LiteralPath $adminMarker)) {
        Write-TextFile $adminMarker 'done'
        $id = Get-HostSteamId
        if ($id) {
            Set-ConfigArray $ConfigPath 'Admins' @($id)
            $cfg.Admins = @($id)
            Write-Good "Made your Steam account ($id) server admin. More admins: Admins in $ConfigPath"
        } else {
            Write-Info "Tip: add your SteamID64 to Admins in $ConfigPath to get admin rights."
        }
    }
    if ($asked) { Write-Good "Saved to $ConfigPath (edit it any time)." }
    return $cfg
}

function Resolve-ProjectPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return $Path }
    return [IO.Path]::GetFullPath((Join-Path $Root $Path))
}

# =====================================================================================
# Files that get copied into the game (game-files\..., textures\)
# =====================================================================================

function Get-LocalFileEntries {
    # Each source: @{ Dir = 'game-files/common'; Prefix = '' }. Later sources win when the target is the same.
    param([object[]]$Sources)
    $map = [ordered]@{}
    foreach ($src in $Sources) {
        $dir = Join-Path $Root (ConvertTo-NativePath $src.Dir)
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        foreach ($f in Get-FileTree $dir) {
            if ($f.Name.StartsWith('.') -or $f.Name -like 'README*') { continue }
            $rel = $f.Rel
            $target = $rel
            if ($src.Prefix) { $target = $src.Prefix.TrimEnd('/') + '/' + $rel }
            $map[$target.ToLowerInvariant()] = [pscustomobject]@{
                target    = $target
                source    = $src.Dir.TrimEnd('/') + '/' + $rel
                sha256    = Get-Sha256 $f.Full
                localPath = $f.Full
            }
        }
    }
    return @($map.Values)
}

$ServerFileSources = @(
    @{ Dir = 'game-files/common'; Prefix = '' },
    @{ Dir = 'game-files/server'; Prefix = '' }
)
$ClientFileSources = @(
    @{ Dir = 'game-files/common'; Prefix = '' },
    @{ Dir = 'game-files/client'; Prefix = '' },
    @{ Dir = 'textures'; Prefix = 'BepInEx/plugins/ValheimServerTweaks/textures' }
)
$ClientScripts = @(
    @{ Name = 'Play.bat'; Source = 'client/Play.bat' },
    @{ Name = 'Remove Mods.bat'; Source = 'client/Remove Mods.bat' },
    @{ Name = 'Update-Mods.ps1'; Source = 'client/Update-Mods.ps1' },
    @{ Name = 'ModSync.ps1'; Source = 'lib/ModSync.ps1' },
    @{ Name = 'Folkhemmet.ico'; Source = 'client/Folkhemmet.ico' }
)

# =====================================================================================
# Player pack (manifest.json + kit zip) and GitHub publishing
# =====================================================================================

function Invoke-Git([string[]]$GitArgs) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = & git -C $Root @GitArgs 2>&1 | ForEach-Object { "$_" }
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $old
    }
    return [pscustomobject]@{ Code = $code; Output = (@($out) -join "`n").Trim() }
}

function Get-GitHubInfo {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { return $null }
    if (-not (Test-Path -LiteralPath (Join-Path $Root '.git'))) { return $null }
    $remote = Invoke-Git @('remote', 'get-url', 'origin')
    if ($remote.Code -ne 0 -or $remote.Output -notmatch 'github\.com[:/]+([^/]+)/([^/\s]+?)(?:\.git)?/?$') { return $null }
    $owner = $Matches[1]
    $repo = $Matches[2]
    $b = Invoke-Git @('symbolic-ref', '--short', 'HEAD')   # works before the first commit too
    $branch = 'main'
    if ($b.Code -eq 0 -and $b.Output -and $b.Output -notmatch '\s') { $branch = $b.Output }
    return [pscustomobject]@{
        RepoUrl     = "https://github.com/$owner/$repo"
        ManifestUrl = "https://raw.githubusercontent.com/$owner/$repo/$branch/client-pack/manifest.json"
        KitUrl      = "https://github.com/$owner/$repo/raw/$branch/client-pack/$FriendZipName"
        KitPayloadUrl = "https://raw.githubusercontent.com/$owner/$repo/$branch/client-pack/$KitZipName"
        Branch      = $branch
    }
}

function Write-ClientManifest($BepInEx, $Mods, $Files) {
    $manifest = [ordered]@{
        schema        = 1
        serverName    = [string]$cfg.ServerName
        bepinex       = [ordered]@{ fullName = $BepInEx.FullName; version = $BepInEx.Version; url = $BepInEx.DownloadUrl }
        mods          = @($Mods | Sort-Object FullName | ForEach-Object { [ordered]@{ fullName = $_.FullName; version = $_.Version; url = $_.DownloadUrl } })
        files         = @($Files | Sort-Object target | ForEach-Object { [ordered]@{ target = $_.target; source = $_.source; sha256 = $_.sha256 } })
        clientScripts = @($ClientScripts | ForEach-Object {
                [ordered]@{ name = $_.Name; source = $_.Source; sha256 = (Get-Sha256 (Join-Path $Root (ConvertTo-NativePath $_.Source))) } })
    }
    $json = ConvertTo-Json -InputObject $manifest -Depth 10
    $old = $null
    if (Test-Path -LiteralPath $ManifestPath) { $old = [IO.File]::ReadAllText($ManifestPath) }
    if ($old -and ($old.Replace("`r`n", "`n").Trim() -eq $json.Replace("`r`n", "`n").Trim())) {
        Write-Info 'Player pack unchanged.'
        return
    }
    Write-TextFile $ManifestPath $json
    Write-Good "Player pack updated: $(@($Mods).Count) mods, $(@($Files).Count) files."
}

function Get-InstallName {
    $n = ([string]$cfg.ServerName -replace '[^A-Za-z0-9 _-]', '').Trim()
    if (-not $n) { $n = 'Folkhemmet' }
    return $n
}

$InstallerTemplate = @'
@echo off
setlocal
title __NAME__ - install
echo.
echo   Installing the __NAME__ modpack for Valheim - no account or login needed...
echo.
set "FH_DEST=%LOCALAPPDATA%\__NAME__"
set "FH_URL=__KIT_URL__"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072 } catch {}; $d = $env:FH_DEST; [void][IO.Directory]::CreateDirectory($d); $z = Join-Path $env:TEMP 'fh-kit.zip'; Invoke-WebRequest -UseBasicParsing -Uri $env:FH_URL -OutFile $z; Add-Type -AssemblyName System.IO.Compression.FileSystem; $zip = [IO.Compression.ZipFile]::OpenRead($z); try { foreach ($e in $zip.Entries) { $n = ($e.FullName -replace '\\','/') -replace '^[^/]+/',''; if (-not $n -or $n.EndsWith('/')) { continue }; $t = Join-Path $d $n; if ($n -eq 'client-config.json' -and (Test-Path -LiteralPath $t)) { continue }; [void][IO.Directory]::CreateDirectory((Split-Path $t)); [IO.Compression.ZipFileExtensions]::ExtractToFile($e, $t, $true) } } finally { $zip.Dispose() }; Remove-Item -LiteralPath $z -Force"
if errorlevel 1 (
    echo.
    echo   Download failed - check your internet connection and try again.
    pause
    exit /b 1
)
call "%FH_DEST%\Play.bat"
'@

function Write-ClientKit($GitHub) {
    # Friends download client-pack\Folkhemmet.zip (just Install-Folkhemmet.bat + a readme) - no account, no login.
    # Install-Folkhemmet.bat downloads client-pack\kit.zip (Play.bat, updater, icon) into %LOCALAPPDATA%\<name>,
    # installs the mods, adds a desktop shortcut and starts Valheim. After that Play.bat keeps everything up to date.
    $name = Get-InstallName
    $kitPath = Join-Path $PackDir $KitZipName
    $friendPath = Join-Path $PackDir $FriendZipName
    $hashPath = Join-Path $PackDir 'kit.sha256'
    $installer = $InstallerTemplate.Replace('__NAME__', $name).Replace('__KIT_URL__', $GitHub.KitPayloadUrl)
    $readme = @"
$($cfg.ServerName.ToUpper()) - VALHEIM MODPACK
=============================================

1. Double-click Install-Folkhemmet.bat (you can run it straight from this zip).
   No GitHub account, no login, nothing else to install.
   If Windows says "Windows protected your PC": click More info -> Run anyway.
2. It installs the mods into your Valheim, puts a "$name" shortcut on your desktop
   and starts the game.
3. In Valheim: Join Game -> find "$($cfg.ServerName)" (or Join IP) -> type the password from the host.

From then on start the game with the "$name" desktop shortcut - it updates the mods first.
To remove the mods: Start menu -> $name -> Remove $name mods.
"@
    $clientConfig = ConvertTo-Json -InputObject ([ordered]@{ ManifestUrl = $GitHub.ManifestUrl; GamePath = '' })
    $parts = @($GitHub.ManifestUrl, $GitHub.KitPayloadUrl, $readme, $installer)
    foreach ($s in $ClientScripts) { $parts += (Get-Sha256 (Join-Path $Root (ConvertTo-NativePath $s.Source))) }
    $sha = [Security.Cryptography.SHA256]::Create()
    $hash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($parts -join '|')))).Replace('-', '')
    if ((Test-Path -LiteralPath $kitPath) -and (Test-Path -LiteralPath $friendPath) -and (Test-Path -LiteralPath $hashPath) -and
        ([IO.File]::ReadAllText($hashPath).Trim() -eq $hash)) { return }

    $stage = New-TempDirectory
    try {
        # kit.zip - what the installer unpacks
        $kitStage = Join-Path $stage 'kit'
        $kit = Join-Path $kitStage $name
        New-Directory $kit
        foreach ($s in $ClientScripts) { [IO.File]::Copy((Join-Path $Root (ConvertTo-NativePath $s.Source)), (Join-Path $kit $s.Name), $true) }
        Write-TextFile (Join-Path $kit 'client-config.json') $clientConfig
        Remove-PathSafe $kitPath
        [IO.Compression.ZipFile]::CreateFromDirectory($kitStage, $kitPath)

        # Folkhemmet.zip - what friends download
        $friendStage = Join-Path $stage 'friend'
        New-Directory $friendStage
        Write-TextFile (Join-Path $friendStage 'Install-Folkhemmet.bat') ($installer.Replace("`r`n", "`n").Replace("`n", "`r`n"))
        Write-TextFile (Join-Path $friendStage 'READ ME.txt') ($readme.Replace("`r`n", "`n").Replace("`n", "`r`n"))
        Remove-PathSafe $friendPath
        [IO.Compression.ZipFile]::CreateFromDirectory($friendStage, $friendPath)
        Remove-PathSafe (Join-Path $PackDir 'ValheimModpack.zip')   # old name
        Write-TextFile $hashPath $hash
        Write-Good "Player download rebuilt: client-pack\$FriendZipName"
    } finally {
        Remove-PathSafe $stage
    }
}

function Publish-ToGit {
    $paths = @('mods.txt', 'client-pack', 'game-files', 'textures', 'client', 'lib', 'custom-mod', 'README.md',
               'start-server.ps1', 'Start Server.bat', 'Publish-Folkhemmet.ps1', 'Publish-Folkhemmet.bat',
               'server-config.example.psd1', '.gitignore', '.gitattributes')
    $paths = @($paths | Where-Object { Test-Path -LiteralPath (Join-Path $Root $_) })
    $add = Invoke-Git (@('add', '--') + $paths)
    if ($add.Code -ne 0) { Write-Warn "git add failed: $($add.Output)"; return }
    $staged = Invoke-Git @('diff', '--cached', '--name-only')
    if (-not $staged.Output) { Write-Info 'GitHub is already up to date.'; return }
    $commit = Invoke-Git @('commit', '-m', "Modpack update $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
    if ($commit.Code -ne 0) { Write-Warn "git commit failed: $($commit.Output)"; return }
    Write-Info 'Pushing to GitHub...'
    $push = Invoke-Git @('push', '-u', 'origin', 'HEAD')
    if ($push.Code -ne 0) { Write-Warn "git push failed (friends won't get the update until it works): $($push.Output)"; return }
    Write-Good 'Published - friends get the changes next time they run Play.bat.'
}

function Import-ServerConfigs([string]$ServerDir) {
    # Every mod writes its .cfg on the server the first time it runs. New ones are copied to
    # game-files\common\BepInEx\config so they are synced to every player (and edited in one place).
    $srcDir = Join-PathParts $ServerDir 'BepInEx' 'config'
    if (-not (Test-Path -LiteralPath $srcDir)) { return }
    $known = @{}
    foreach ($side in 'common', 'server', 'client') {
        $d = Join-Path $Root (ConvertTo-NativePath "game-files/$side/BepInEx/config")
        if (Test-Path -LiteralPath $d) { Get-ChildItem -LiteralPath $d -Filter '*.cfg' | ForEach-Object { $known[$_.Name.ToLowerInvariant()] = $true } }
    }
    $dest = Join-Path $Root (ConvertTo-NativePath 'game-files/common/BepInEx/config')
    $added = @()
    foreach ($f in @(Get-ChildItem -LiteralPath $srcDir -Filter '*.cfg' | Where-Object { -not $_.PSIsContainer })) {
        $n = $f.Name.ToLowerInvariant()
        if ($n -eq 'bepinex.cfg' -or $known.ContainsKey($n)) { continue }
        New-Directory $dest
        [IO.File]::Copy($f.FullName, (Join-Path $dest $f.Name), $false)
        $added += $f.Name
    }
    if ($added.Count -gt 0) { Write-Good "Mod configs now synced to players: $($added -join ', ')" }
}

# =====================================================================================
# World helpers
# =====================================================================================

function Backup-World([string]$SaveDir, [string]$World, [int]$Keep) {
    $worlds = Join-Path $SaveDir 'worlds_local'
    if (-not (Test-Path -LiteralPath $worlds)) { return }
    $files = @(Get-ChildItem -LiteralPath $worlds -Force | Where-Object { -not $_.PSIsContainer -and $_.Name.StartsWith("$World.") -and $_.Name -notlike '*.old' })
    if ($files.Count -eq 0) { return }
    $backupRoot = Join-Path $SaveDir 'startup-backups'
    $dest = Join-Path $backupRoot ("$World-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Directory $dest
    foreach ($f in $files) { [IO.File]::Copy($f.FullName, (Join-Path $dest $f.Name), $true) }
    Write-Info "World backed up to $dest"
    $all = @(Get-ChildItem -LiteralPath $backupRoot -Directory | Where-Object { $_.Name.StartsWith("$World-") } | Sort-Object Name -Descending)
    if ($Keep -gt 0 -and $all.Count -gt $Keep) { $all | Select-Object -Skip $Keep | ForEach-Object { Remove-PathSafe $_.FullName } }
}

function Import-ExistingWorld([string]$SaveDir, [string]$World) {
    # Offer to copy a world with the same name from the normal Valheim save folder (first start only).
    if (-not $env:USERPROFILE) { return }
    $mine = Join-Path (Join-Path $SaveDir 'worlds_local') "$World.db"
    if (Test-Path -LiteralPath $mine) { return }
    $default = [IO.Path]::Combine($env:USERPROFILE, 'AppData', 'LocalLow', 'IronGate', 'Valheim', 'worlds_local')
    $theirs = Join-Path $default "$World.db"
    if (-not (Test-Path -LiteralPath $theirs)) { return }
    $a = Read-Answer "A world called '$World' exists in your normal Valheim saves. Copy it to the server? (y/n)" 'n'
    if ($a -notmatch '^[yY]') { return }
    $target = Join-Path $SaveDir 'worlds_local'
    New-Directory $target
    Get-ChildItem -LiteralPath $default -Force | Where-Object { $_.Name.StartsWith("$World.") } | ForEach-Object { [IO.File]::Copy($_.FullName, (Join-Path $target $_.Name), $true) }
    Write-Good "Copied $World to $target"
}

function Update-AdminList([string]$SaveDir, $Admins) {
    $ids = @($Admins | Where-Object { $_ } | ForEach-Object { ([string]$_).Trim() })
    if ($ids.Count -eq 0) { return }
    $file = Join-Path $SaveDir 'adminlist.txt'
    $lines = @()
    if (Test-Path -LiteralPath $file) { $lines = @([IO.File]::ReadAllLines($file)) } else { $lines = @('// List admin players ID  ONE per line') }
    $added = $false
    foreach ($id in $ids) {
        if (-not ($lines | Where-Object { $_.Trim() -eq $id })) { $lines += $id; $added = $true }
    }
    if ($added) { Write-TextFile $file (($lines -join "`r`n") + "`r`n"); Write-Info "Admin list updated ($file)." }
}

function Test-SteamServerUpdate([string]$ServerDir) {
    try {
        $steamapps = Split-Path -Parent (Split-Path -Parent $ServerDir)
        $acf = Join-Path $steamapps 'appmanifest_896660.acf'
        if (-not (Test-Path -LiteralPath $acf)) { return }
        $txt = [IO.File]::ReadAllText($acf)
        if ($txt -match '"StateFlags"\s+"(\d+)"' -and [int]$Matches[1] -ne 4) {
            Write-Warn "Steam says 'Valheim Dedicated Server' needs an update. Update it in your Steam library (Tools),"
            Write-Warn 'otherwise players on the newest Valheim version cannot join.'
        }
    } catch { }
}

# =====================================================================================
# Main
# =====================================================================================

Write-Host ''
Write-Host '  VALHEIM SERVER' -ForegroundColor Cyan
Write-Host "  $Root" -ForegroundColor DarkGray

$cfg = Initialize-Config
Write-Host "  $($cfg.ServerName)  (world: $($cfg.WorldName), port $($cfg.Port))" -ForegroundColor Cyan
$ServerDir = [string]$cfg.ServerDir
$SaveDir = (Resolve-ProjectPath ([string]$cfg.SaveDir)).TrimEnd('\', '/')
$ServerExe = Join-Path $ServerDir 'valheim_server.exe'

if (-not (Test-Path -LiteralPath $ServerDir)) {
    throw "Dedicated server not found at '$ServerDir'. Install 'Valheim Dedicated Server' from Steam (Library -> Tools) or fix ServerDir in $ConfigPath."
}
if (-not $NoLaunch -and -not (Test-Path -LiteralPath $ServerExe)) { throw "valheim_server.exe not found in $ServerDir" }
if (Get-Process -Name 'valheim_server' -ErrorAction SilentlyContinue) {
    throw 'The Valheim server is already running. Stop it (Ctrl+C in its window) before starting it again.'
}
Test-SteamServerUpdate $ServerDir

# ---- mods ------------------------------------------------------------------------------
$packages = $null
if ($SkipModUpdate -or -not $cfg.UpdateModsOnStart) {
    Write-Step 'Skipping the mod update check (using the installed mods)'
} else {
    Write-Step 'Checking Thunderstore for mod updates'
    try {
        $entries = Read-ModList $ModsFile
        $packages = @(Resolve-ModList $entries)
        Write-Info "$($packages.Count) packages (mods.txt + dependencies)."
    } catch {
        Write-Warn "Could not check for mod updates: $($_.Exception.Message)"
        Write-Warn 'Starting with the mods that are already installed.'
        $packages = $null
    }
}

$bepinex = $null
$mods = @()
if ($packages) {
    $bepinex = $packages | Where-Object { $_.FullName -eq $script:BepInExPackName } | Select-Object -First 1
    if (-not $bepinex) { $bepinex = Get-ThunderstorePackage 'denikson' 'BepInExPack_Valheim' '' }
    $mods = @($packages | Where-Object { $_.FullName -ne $script:BepInExPackName })

    Write-Step 'Installing mods on the server'
    Sync-GameMods -GameDir $ServerDir -BepInEx $bepinex -Mods @($mods | Where-Object { $_.Side -ne 'client' }) -CacheDir $CacheDir

    # Drop downloads of old versions from the cache.
    $keep = @(@($bepinex) + $mods | ForEach-Object { "$($_.FullName)-$($_.Version).zip".ToLowerInvariant() })
    Get-ChildItem -LiteralPath $CacheDir -Filter '*.zip' -ErrorAction SilentlyContinue |
        Where-Object { $keep -notcontains $_.Name.ToLowerInvariant() } | ForEach-Object { Remove-PathSafe $_.FullName }
} elseif (-not (Test-BepInExInstalled $ServerDir)) {
    Write-Warn 'BepInEx is not installed on the server yet, so no mods will load. Run again with internet access.'
}

Write-Step 'Copying configs and the custom mod to the server'
Import-ServerConfigs $ServerDir
$serverFiles = Get-LocalFileEntries -Sources $ServerFileSources
Sync-GameFiles -GameDir $ServerDir -Files $serverFiles -Fetch { param($file, $dest) [IO.File]::Copy($file.localPath, $dest, $true) }

# ---- player pack -------------------------------------------------------------------------
if ($bepinex) {
    Write-Step 'Updating the player pack (what Play.bat installs)'
    $clientFiles = Get-LocalFileEntries -Sources $ClientFileSources
    Write-ClientManifest $bepinex @($mods | Where-Object { $_.Side -ne 'server' }) $clientFiles
}

$github = Get-GitHubInfo
if ($github) {
    Write-ClientKit $github
    if ($cfg.PublishToGit -and -not $NoPublish) {
        Write-Step 'Publishing to GitHub'
        Publish-ToGit
    }
    Write-Info "Friends download (first time): $($github.KitUrl)"
} else {
    Write-Step 'GitHub not set up yet'
    Write-Info 'Run "Publish-Folkhemmet.bat" once so friends can install the mods (see README.md).'
}

# ---- world + launch ------------------------------------------------------------------------
if ($NoLaunch) {
    Write-Step 'Done (-NoLaunch: server not started)'
    return
}

New-Directory $SaveDir
Import-ExistingWorld $SaveDir $cfg.WorldName
Update-AdminList $SaveDir $cfg.Admins
Backup-World $SaveDir $cfg.WorldName ([int]$cfg.StartupBackupsToKeep)

$publicFlag = '0'
if ($cfg.Public) { $publicFlag = '1' }
$serverArgs = @(
    '-nographics', '-batchmode',
    '-name', [string]$cfg.ServerName,
    '-port', [string]$cfg.Port,
    '-world', [string]$cfg.WorldName,
    '-password', [string]$cfg.Password,
    '-public', $publicFlag,
    '-savedir', $SaveDir,
    '-saveinterval', [string]$cfg.SaveIntervalSeconds,
    '-backups', [string]$cfg.Backups,
    # World modifiers come from the config on every start:
    '-resetmodifiers'
)
if ($cfg.Preset) { $serverArgs += @('-preset', [string]$cfg.Preset) }
if ($cfg.Modifiers) { foreach ($k in $cfg.Modifiers.Keys) { $serverArgs += @('-modifier', [string]$k, [string]$cfg.Modifiers[$k]) } }
if ($cfg.FoodRatePercent -and [int]$cfg.FoodRatePercent -ne 100) { $serverArgs += @('-setkey', "foodrate $([int]$cfg.FoodRatePercent)") }
foreach ($k in @($cfg.SetKeys)) { if ($k) { $serverArgs += @('-setkey', [string]$k) } }
if ($cfg.Crossplay) { $serverArgs += '-crossplay' }
foreach ($a in @($cfg.ExtraArgs)) { if ($a) { $serverArgs += [string]$a } }

$net = $null
if ($cfg.CheckPorts -and -not $SkipPortCheck) {
    try {
        $net = Invoke-PortCheck -Port ([int]$cfg.Port) -ServerExe $ServerExe -ServerName ([string]$cfg.ServerName) `
                                -Crossplay ([bool]$cfg.Crossplay) -AutoForward ([bool]$cfg.AutoPortForward)
    } catch {
        Write-Warn "Port check failed: $($_.Exception.Message)"
    }
}

$logPath = Join-PathParts $ServerDir 'BepInEx' 'LogOutput.log'
Write-Step "Starting '$($cfg.ServerName)' (world '$($cfg.WorldName)', port $($cfg.Port))"
Write-Info 'Wait for "Game server connected". Stop the server with Ctrl+C (it saves the world first).'
Write-Info "Logs: $logPath"
Write-Host ''

$watcher = $null
if ($net -and $net.PublicIp -and -not $cfg.Crossplay) {
    $watcher = Start-ReachabilityWatcher -LogPath $logPath -PublicIp $net.PublicIp -Port ([int]$cfg.Port) -Public ([bool]$cfg.Public)
}
$env:SteamAppId = '892970'
Push-Location -LiteralPath $ServerDir
try {
    & $ServerExe @serverArgs
} finally {
    Pop-Location
    Stop-ReachabilityWatcher $watcher
    # Mods create their configs on the first run - pick them up now (published on the next start).
    try { Import-ServerConfigs $ServerDir } catch { }
}
Write-Step 'Server stopped'

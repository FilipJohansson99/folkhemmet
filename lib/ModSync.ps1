# ModSync.ps1 - shared helpers used by start-server.ps1 (server) and Update-Mods.ps1 (players).
# Installs BepInEx + Thunderstore mods into a Valheim folder and keeps them in sync.
# Compatible with Windows PowerShell 5.1 and PowerShell 7. Keep this file ASCII only.

$script:ThunderstoreApi = 'https://thunderstore.io/api/experimental/package'
$script:BepInExPackName = 'denikson-BepInExPack_Valheim'
$script:PackageCache = @{}
$script:UserAgent = 'ValheimServerModSync/1.0'

# ---------------------------------------------------------------- output

function Write-Step([string]$Text) { Write-Host ''; Write-Host "==> $Text" -ForegroundColor Cyan }
function Write-Info([string]$Text) { Write-Host "    $Text" }
function Write-Good([string]$Text) { Write-Host "    $Text" -ForegroundColor Green }
function Write-Warn([string]$Text) { Write-Host "    WARNING: $Text" -ForegroundColor Yellow }

# ---------------------------------------------------------------- setup / small helpers

function Initialize-ModSync {
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    } catch { }
    Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
}

function Join-PathParts {
    # Join-Path with any number of parts (PowerShell 5.1 only takes two).
    $result = $args[0]
    for ($i = 1; $i -lt $args.Count; $i++) { $result = [IO.Path]::Combine($result, $args[$i]) }
    return $result
}

function ConvertTo-NativePath([string]$RelativePath) {
    return $RelativePath.Replace('/', [IO.Path]::DirectorySeparatorChar).Replace('\', [IO.Path]::DirectorySeparatorChar)
}

function New-Directory([string]$Path) {
    [void][IO.Directory]::CreateDirectory($Path)
}

function Remove-PathSafe([string]$Path) {
    if ($Path -and (Test-Path -LiteralPath $Path)) { Remove-Item -LiteralPath $Path -Recurse -Force }
}

function New-TempDirectory {
    $path = Join-Path ([IO.Path]::GetTempPath()) ('valheim-modsync-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Directory $path
    return $path
}

function Get-Sha256([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Write-TextFile([string]$Path, [string]$Text, [switch]$Bom) {
    New-Directory (Split-Path -Parent $Path)
    $enc = New-Object System.Text.UTF8Encoding($Bom.IsPresent)
    [IO.File]::WriteAllText($Path, $Text, $enc)
}

function Read-JsonFile([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $text = [IO.File]::ReadAllText($Path)
    if (-not $text.Trim()) { return $null }
    return ($text | ConvertFrom-Json)
}

function Write-JsonFile([string]$Path, $Object) {
    Write-TextFile $Path (ConvertTo-Json -InputObject $Object -Depth 12)
}

function Get-FileTree([string]$Root) {
    # All files below $Root as objects { Full; Rel } (Rel uses '/'). Built by walking the folders,
    # so it works the same with short (8.3) temp paths, long names and odd characters.
    $result = New-Object System.Collections.ArrayList
    if (-not [IO.Directory]::Exists($Root)) { return $result }
    $stack = New-Object System.Collections.Stack
    $stack.Push(@([IO.Path]::GetFullPath($Root), ''))
    while ($stack.Count -gt 0) {
        $pair = $stack.Pop()
        foreach ($f in [IO.Directory]::GetFiles($pair[0])) {
            $name = [IO.Path]::GetFileName($f)
            $rel = $name
            if ($pair[1]) { $rel = $pair[1] + '/' + $name }
            [void]$result.Add([pscustomobject]@{ Full = $f; Rel = $rel; Name = $name })
        }
        foreach ($d in [IO.Directory]::GetDirectories($pair[0])) {
            $name = [IO.Path]::GetFileName($d)
            $rel = $name
            if ($pair[1]) { $rel = $pair[1] + '/' + $name }
            $stack.Push(@($d, $rel))
        }
    }
    return $result
}

function Copy-Tree {
    # Copies every file under $Source into $Destination (merging). $KeepExisting = relative paths never overwritten.
    param([string]$Source, [string]$Destination, [string[]]$KeepExisting = @(), [string[]]$Skip = @())
    $keep = @($KeepExisting | ForEach-Object { $_.Replace('\', '/').ToLowerInvariant() })
    $skipList = @($Skip | ForEach-Object { $_.Replace('\', '/').ToLowerInvariant() })
    foreach ($f in Get-FileTree $Source) {
        $relKey = $f.Rel.ToLowerInvariant()
        $skipped = $false
        foreach ($s in $skipList) { if ($relKey -eq $s -or $relKey.StartsWith($s + '/')) { $skipped = $true } }
        if ($skipped) { continue }
        $dest = Join-Path $Destination (ConvertTo-NativePath $f.Rel)
        if (($keep -contains $relKey) -and (Test-Path -LiteralPath $dest)) { continue }
        New-Directory (Split-Path -Parent $dest)
        [IO.File]::Copy($f.Full, $dest, $true)
    }
}

# ---------------------------------------------------------------- web

function Get-HttpStatus($ErrorRecord) {
    try {
        if ($ErrorRecord.Exception.Response) { return [int]$ErrorRecord.Exception.Response.StatusCode }
    } catch { }
    return 0
}

function Invoke-JsonRequest([string]$Url) {
    $attempt = 0
    while ($true) {
        try {
            $resp = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 60 -Headers @{ 'User-Agent' = $script:UserAgent }
            $content = $resp.Content
            if ($content -is [byte[]]) { $content = [Text.Encoding]::UTF8.GetString($content) }
            return ($content | ConvertFrom-Json)
        } catch {
            $attempt++
            $status = Get-HttpStatus $_
            if ($status -eq 404 -or $attempt -ge 3) { throw }
            Start-Sleep -Seconds (2 * $attempt)
        }
    }
}

function Save-Url([string]$Url, [string]$Path) {
    New-Directory (Split-Path -Parent $Path)
    $part = "$Path.part"
    $attempt = 0
    while ($true) {
        try {
            Invoke-WebRequest -Uri $Url -OutFile $part -UseBasicParsing -TimeoutSec 600 -Headers @{ 'User-Agent' = $script:UserAgent }
            Move-Item -LiteralPath $part -Destination $Path -Force
            return
        } catch {
            $attempt++
            Remove-PathSafe $part
            if ((Get-HttpStatus $_) -eq 404 -or $attempt -ge 3) { throw }
            Start-Sleep -Seconds (3 * $attempt)
        }
    }
}

# ---------------------------------------------------------------- zip

function Test-Zip([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    try { $z = [IO.Compression.ZipFile]::OpenRead($Path); $z.Dispose(); return $true } catch { return $false }
}

function Expand-ZipSafe([string]$ZipPath, [string]$Destination) {
    # Like Expand-Archive, but handles zips made with '\' separators and refuses paths escaping $Destination.
    New-Directory $Destination
    $sep = [IO.Path]::DirectorySeparatorChar
    $root = [IO.Path]::GetFullPath($Destination).TrimEnd($sep) + $sep
    $zip = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        foreach ($entry in $zip.Entries) {
            $rel = $entry.FullName.Replace('\', '/')
            if ($rel.EndsWith('/')) { continue }
            $parts = @($rel.Split('/') | Where-Object { $_ -ne '' -and $_ -ne '.' })
            if ($parts.Count -eq 0) { continue }
            if ($parts -contains '..') { throw "Unsafe path in zip ${ZipPath}: $rel" }
            $target = [IO.Path]::GetFullPath($root + ($parts -join $sep))
            if (-not $target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe path in zip ${ZipPath}: $rel" }
            New-Directory ([IO.Path]::GetDirectoryName($target))
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        }
    } finally {
        $zip.Dispose()
    }
}

# ---------------------------------------------------------------- mods.txt

function Read-ModList([string]$Path) {
    # One mod per line. Accepted forms:
    #   https://thunderstore.io/c/valheim/p/Author/ModName/        (latest version)
    #   https://thunderstore.io/c/valheim/p/Author/ModName/v/1.2.3/ (pinned version)
    #   Author-ModName          Author-ModName-1.2.3 (pinned)
    # Optional flag after the mod:  server-only  |  client-only   (default: both)
    # '#' starts a comment.
    if (-not (Test-Path -LiteralPath $Path)) { throw "Mod list not found: $Path" }
    $entries = New-Object System.Collections.ArrayList
    $lineNo = 0
    foreach ($raw in [IO.File]::ReadAllLines($Path)) {
        $lineNo++
        $line = ($raw -replace '(^|\s)#.*$', '').Trim()
        if (-not $line) { continue }
        $tokens = @($line -split '\s+')
        $target = $tokens[0]
        $side = 'both'
        for ($i = 1; $i -lt $tokens.Count; $i++) {
            switch ($tokens[$i].ToLowerInvariant()) {
                'server-only' { $side = 'server' }
                'server'      { $side = 'server' }
                'client-only' { $side = 'client' }
                'client'      { $side = 'client' }
                'both'        { $side = 'both' }
                default       { Write-Warn "mods.txt line ${lineNo}: unknown option '$($tokens[$i])' (use server-only or client-only)" }
            }
        }
        $e = [pscustomobject]@{ Line = $lineNo; Text = $target; Side = $side; Kind = 'unknown'; Namespace = $null; Name = $null; Version = $null }
        if ($target -match 'nexusmods\.com') {
            $e.Kind = 'nexus'
        } elseif ($target -match 'thunderstore\.io/package/download/([^/]+)/([^/]+)/([^/?#]+)') {
            $e.Kind = 'thunderstore'; $e.Namespace = $Matches[1]; $e.Name = $Matches[2]; $e.Version = $Matches[3]
        } elseif ($target -match 'thunderstore\.io/(?:c/[^/]+/)?(?:p|package)/([^/?#]+)/([^/?#]+)(?:/v/([^/?#]+))?') {
            $e.Kind = 'thunderstore'; $e.Namespace = $Matches[1]; $e.Name = $Matches[2]
            if ($Matches.Count -gt 3 -and $Matches[3]) { $e.Version = $Matches[3] }
        } elseif ($target -match '^([A-Za-z0-9_]+)-([A-Za-z0-9_]+)(?:-(\d+(?:\.\d+)+))?$') {
            $e.Kind = 'thunderstore'; $e.Namespace = $Matches[1]; $e.Name = $Matches[2]
            if ($Matches.Count -gt 3 -and $Matches[3]) { $e.Version = $Matches[3] }
        }
        [void]$entries.Add($e)
    }
    return $entries
}

# ---------------------------------------------------------------- Thunderstore

function Get-ThunderstorePackage([string]$Namespace, [string]$Name, [string]$Version) {
    $key = "$Namespace-$Name-$Version".ToLowerInvariant()
    if ($script:PackageCache.ContainsKey($key)) { return $script:PackageCache[$key] }
    $deprecated = $false
    if ($Version) {
        $v = Invoke-JsonRequest "$script:ThunderstoreApi/$Namespace/$Name/$Version/"
    } else {
        $p = Invoke-JsonRequest "$script:ThunderstoreApi/$Namespace/$Name/"
        $v = $p.latest
        $deprecated = [bool]$p.is_deprecated
    }
    $pkg = [pscustomobject]@{
        FullName     = "$($v.namespace)-$($v.name)"
        Namespace    = [string]$v.namespace
        Name         = [string]$v.name
        Version      = [string]$v.version_number
        DownloadUrl  = [string]$v.download_url
        Dependencies = @($v.dependencies)
        Deprecated   = $deprecated
    }
    $script:PackageCache[$key] = $pkg
    return $pkg
}

function Merge-Side([string]$A, [string]$B) {
    if (-not $A) { return $B }
    if ($A -eq $B) { return $A }
    return 'both'
}

function Resolve-ModList($Entries) {
    # Resolves mods.txt entries + all their dependencies (latest versions unless pinned).
    # Returns objects: FullName, Namespace, Name, Version, DownloadUrl, Side, Explicit.
    $resolved = @{}
    $order = New-Object System.Collections.ArrayList
    $queue = New-Object System.Collections.Queue
    foreach ($e in $Entries) {
        if ($e.Kind -eq 'nexus') {
            Write-Warn "mods.txt line $($e.Line): Nexus links can't be downloaded automatically (Nexus needs a login). Use the mod's Thunderstore page instead: $($e.Text)"
            continue
        }
        if ($e.Kind -ne 'thunderstore') {
            Write-Warn "mods.txt line $($e.Line): not a Thunderstore mod, skipped: $($e.Text)"
            continue
        }
        $queue.Enqueue([pscustomobject]@{ Namespace = $e.Namespace; Name = $e.Name; Version = $e.Version; Side = $e.Side; Explicit = $true; Parent = "mods.txt line $($e.Line)" })
    }
    while ($queue.Count -gt 0) {
        $item = $queue.Dequeue()
        $key = "$($item.Namespace)-$($item.Name)".ToLowerInvariant()
        if ($resolved.ContainsKey($key)) {
            $existing = $resolved[$key]
            if ($item.Explicit -and $existing.Explicit) { Write-Warn "$($existing.FullName) is listed more than once in mods.txt ($($item.Parent) ignored)." }
            $merged = Merge-Side $existing.Side $item.Side
            if ($merged -ne $existing.Side) {
                $existing.Side = $merged
                foreach ($d in $existing.Dependencies) {
                    if ($d -match '^(.+)-([^-]+)-[^-]+$') {
                        $queue.Enqueue([pscustomobject]@{ Namespace = $Matches[1]; Name = $Matches[2]; Version = $null; Side = $merged; Explicit = $false; Parent = $existing.FullName })
                    }
                }
            }
            continue
        }
        try {
            $pkg = Get-ThunderstorePackage $item.Namespace $item.Name $item.Version
        } catch {
            if ((Get-HttpStatus $_) -eq 404) {
                $what = "$($item.Namespace)-$($item.Name)"
                if ($item.Version) { $what += " $($item.Version)" }
                if ($item.Explicit) { throw "Mod not found on Thunderstore: $what ($($item.Parent)). Check the link in mods.txt." }
                Write-Warn "Dependency $what of $($item.Parent) not found on Thunderstore, skipped."
                continue
            }
            throw
        }
        $entry = [pscustomobject]@{
            FullName     = $pkg.FullName
            Namespace    = $pkg.Namespace
            Name         = $pkg.Name
            Version      = $pkg.Version
            DownloadUrl  = $pkg.DownloadUrl
            Dependencies = $pkg.Dependencies
            Side         = $item.Side
            Explicit     = $item.Explicit
        }
        $resolved[$key] = $entry
        [void]$order.Add($entry)
        if ($pkg.Deprecated) { Write-Warn "$($pkg.FullName) is marked as deprecated on Thunderstore - consider replacing it." }
        foreach ($d in $pkg.Dependencies) {
            if ($d -match '^(.+)-([^-]+)-[^-]+$') {
                $queue.Enqueue([pscustomobject]@{ Namespace = $Matches[1]; Name = $Matches[2]; Version = $null; Side = $item.Side; Explicit = $false; Parent = $pkg.FullName })
            }
        }
    }
    return @($order)
}

function Get-PackageZip($Package, [string]$CacheDir) {
    New-Directory $CacheDir
    $zip = Join-Path $CacheDir "$($Package.FullName)-$($Package.Version).zip"
    if (-not (Test-Zip $zip)) {
        Write-Info "Downloading $($Package.FullName) $($Package.Version)..."
        Save-Url $Package.DownloadUrl $zip
        if (-not (Test-Zip $zip)) { Remove-PathSafe $zip; throw "Download of $($Package.FullName) is not a valid zip." }
    }
    return $zip
}

# ---------------------------------------------------------------- install into a game folder

function Install-BepInExPack($Package, [string]$GameDir, [string]$CacheDir) {
    $zip = Get-PackageZip $Package $CacheDir
    $tmp = New-TempDirectory
    try {
        Expand-ZipSafe $zip $tmp
        $src = Join-Path $tmp 'BepInExPack_Valheim'
        if (-not (Test-Path -LiteralPath $src)) {
            $hit = Get-ChildItem -LiteralPath $tmp -Recurse -Filter 'winhttp.dll' | Select-Object -First 1
            if (-not $hit) { throw "Unexpected BepInExPack layout in $zip" }
            $src = $hit.DirectoryName
        }
        Copy-Tree -Source $src -Destination $GameDir -KeepExisting @('BepInEx/config/BepInEx.cfg') `
                  -Skip @('doorstop_libs', 'start_game_bepinex.sh', 'start_server_bepinex.sh', 'changelog.txt')
        New-Directory (Join-PathParts $GameDir 'BepInEx' 'plugins')
    } finally {
        Remove-PathSafe $tmp
    }
}

function Install-ModPackage($Package, [string]$GameDir, [string]$CacheDir) {
    # Same layout rules as r2modman: plugins -> BepInEx/plugins/<Author-Mod>/, config -> BepInEx/config (never overwritten) ...
    # Returns the folders (relative to $GameDir) that belong to this mod.
    $zip = Get-PackageZip $Package $CacheDir
    $bep = Join-Path $GameDir 'BepInEx'
    $pluginRel = "BepInEx/plugins/$($Package.FullName)"
    $patcherRel = "BepInEx/patchers/$($Package.FullName)"
    $pluginDir = Join-Path $GameDir (ConvertTo-NativePath $pluginRel)
    $patcherDir = Join-Path $GameDir (ConvertTo-NativePath $patcherRel)
    Remove-PathSafe $pluginDir
    Remove-PathSafe $patcherDir
    $ignoreFiles = @('manifest.json', 'icon.png', 'readme.md', 'changelog.md', 'license', 'license.md', 'license.txt')
    $tmp = New-TempDirectory
    try {
        Expand-ZipSafe $zip $tmp
        foreach ($item in Get-ChildItem -LiteralPath $tmp -Force) {
            $n = $item.Name.ToLowerInvariant()
            if (-not $item.PSIsContainer) {
                if ($ignoreFiles -contains $n) { continue }
                New-Directory $pluginDir
                [IO.File]::Copy($item.FullName, (Join-Path $pluginDir $item.Name), $true)
                continue
            }
            switch ($n) {
                'bepinex' {
                    foreach ($sub in Get-ChildItem -LiteralPath $item.FullName -Force) {
                        $s = $sub.Name.ToLowerInvariant()
                        if (-not $sub.PSIsContainer) { New-Directory $pluginDir; [IO.File]::Copy($sub.FullName, (Join-Path $pluginDir $sub.Name), $true); continue }
                        if ($s -eq 'plugins') { Copy-Tree $sub.FullName $pluginDir }
                        elseif ($s -eq 'patchers') { Copy-Tree $sub.FullName $patcherDir }
                        elseif ($s -eq 'config') { Copy-Tree -Source $sub.FullName -Destination (Join-Path $bep 'config') -KeepExisting (Get-RelativeFileList $sub.FullName) }
                        else { Copy-Tree $sub.FullName (Join-Path $bep $sub.Name) }
                    }
                }
                'plugins'  { Copy-Tree $item.FullName $pluginDir }
                'patchers' { Copy-Tree $item.FullName $patcherDir }
                'config'   { Copy-Tree -Source $item.FullName -Destination (Join-Path $bep 'config') -KeepExisting (Get-RelativeFileList $item.FullName) }
                'core'     { Copy-Tree $item.FullName (Join-Path $bep 'core') }
                default    { Copy-Tree $item.FullName (Join-Path $pluginDir $item.Name) }
            }
        }
    } finally {
        Remove-PathSafe $tmp
    }
    $dirs = @()
    if (Test-Path -LiteralPath $pluginDir) { $dirs += $pluginRel }
    if (Test-Path -LiteralPath $patcherDir) { $dirs += $patcherRel }
    return $dirs
}

function Get-RelativeFileList([string]$Dir) {
    return @(Get-FileTree $Dir | ForEach-Object { $_.Rel })
}

# ---------------------------------------------------------------- sync state

function Get-SyncStatePath([string]$GameDir) { return Join-PathParts $GameDir 'BepInEx' 'modsync-state.json' }

function Read-SyncState([string]$GameDir) {
    $s = $null
    try { $s = Read-JsonFile (Get-SyncStatePath $GameDir) } catch { Write-Warn "Could not read the mod sync state, starting fresh." }
    if (-not $s) { $s = [pscustomobject]@{ bepinex = ''; mods = @(); files = @() } }
    return [pscustomobject]@{
        bepinex = [string]$s.bepinex
        mods    = @($s.mods | Where-Object { $_ })
        files   = @($s.files | Where-Object { $_ })
    }
}

function Save-SyncState([string]$GameDir, $State) {
    Write-JsonFile (Get-SyncStatePath $GameDir) ([ordered]@{ bepinex = $State.bepinex; mods = @($State.mods); files = @($State.files) })
}

function Test-BepInExInstalled([string]$GameDir) {
    return (Test-Path -LiteralPath (Join-Path $GameDir 'winhttp.dll')) -and
           (Test-Path -LiteralPath (Join-PathParts $GameDir 'BepInEx' 'core' 'BepInEx.Preloader.dll'))
}

function Sync-GameMods {
    # Makes $GameDir contain exactly BepInEx + $Mods (only touches mods it installed itself).
    param([string]$GameDir, $BepInEx, $Mods, [string]$CacheDir)
    $state = Read-SyncState $GameDir
    $changes = 0

    if ($BepInEx) {
        if (-not (Test-BepInExInstalled $GameDir) -or $state.bepinex -ne $BepInEx.Version) {
            Write-Info "Installing BepInEx $($BepInEx.Version)..."
            Install-BepInExPack $BepInEx $GameDir $CacheDir
            $state.bepinex = $BepInEx.Version
            $changes++
        }
    }

    $wanted = @{}
    foreach ($m in $Mods) { $wanted[$m.FullName.ToLowerInvariant()] = $m }
    $old = @{}
    foreach ($o in $state.mods) { $old[([string]$o.fullName).ToLowerInvariant()] = $o }

    foreach ($o in $state.mods) {
        if (-not $wanted.ContainsKey(([string]$o.fullName).ToLowerInvariant())) {
            foreach ($d in @($o.dirs)) { Remove-PathSafe (Join-Path $GameDir (ConvertTo-NativePath $d)) }
            Write-Good "Removed $($o.fullName)"
            $changes++
        }
    }

    $newState = @()
    foreach ($m in $Mods) {
        $prev = $old[$m.FullName.ToLowerInvariant()]
        $intact = $false
        if ($prev -and $prev.version -eq $m.Version) {
            $intact = $true
            foreach ($d in @($prev.dirs)) { if (-not (Test-Path -LiteralPath (Join-Path $GameDir (ConvertTo-NativePath $d)))) { $intact = $false } }
        }
        if ($intact) {
            $newState += [pscustomobject]@{ fullName = $m.FullName; version = $m.Version; dirs = @($prev.dirs) }
            continue
        }
        $dirs = Install-ModPackage $m $GameDir $CacheDir
        if ($prev) { Write-Good "Updated   $($m.FullName)  $($prev.version) -> $($m.Version)" }
        else       { Write-Good "Installed $($m.FullName)  $($m.Version)" }
        $newState += [pscustomobject]@{ fullName = $m.FullName; version = $m.Version; dirs = @($dirs) }
        $changes++
    }
    $state.mods = $newState
    Save-SyncState $GameDir $state
    Test-DuplicatePlugins $GameDir $state
    if ($changes -eq 0) { Write-Info 'Mods are up to date.' }
}

function Test-DuplicatePlugins([string]$GameDir, $State) {
    # Warn about hand-installed copies of mods we manage (BepInEx would load the mod twice).
    $pluginsRoot = Join-PathParts $GameDir 'BepInEx' 'plugins'
    if (-not (Test-Path -LiteralPath $pluginsRoot)) { return }
    $managedRoots = @($State.mods | ForEach-Object { @($_.dirs) } | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $GameDir (ConvertTo-NativePath $_))) })
    $managedDlls = @{}
    $unmanaged = @()
    foreach ($dll in Get-ChildItem -LiteralPath $pluginsRoot -Recurse -Filter '*.dll' -Force) {
        $isManaged = $false
        foreach ($r in $managedRoots) { if ($dll.FullName.StartsWith($r, [StringComparison]::OrdinalIgnoreCase)) { $isManaged = $true } }
        if ($isManaged) { $managedDlls[$dll.Name.ToLowerInvariant()] = $true } else { $unmanaged += $dll }
    }
    foreach ($dll in $unmanaged) {
        if ($managedDlls.ContainsKey($dll.Name.ToLowerInvariant())) {
            Write-Warn "Duplicate mod dll (installed by hand?): $($dll.FullName) - delete it, the synced copy is used instead."
        }
    }
}

function Sync-GameFiles {
    # Copies/downloads managed files into $GameDir; removes files this tool installed earlier but no longer lists.
    # $Files: objects with target (relative, '/'), sha256. $Fetch: scriptblock($file, $destPath) that puts the file there.
    # $Keep: targets (e.g. a config) the player wants to keep as-is once it exists locally.
    param([string]$GameDir, $Files, [scriptblock]$Fetch, [string[]]$Keep = @())
    $state = Read-SyncState $GameDir
    $keepSet = @{}
    foreach ($k in $Keep) { if ($k) { $keepSet[$k.Replace('\', '/').ToLowerInvariant()] = $true } }
    $wanted = @{}
    foreach ($f in $Files) { $wanted[([string]$f.target).ToLowerInvariant()] = $f }
    $changes = 0
    foreach ($o in $state.files) {
        $t = [string]$o.target
        if ($keepSet.ContainsKey($t.ToLowerInvariant())) { continue }
        if (-not $wanted.ContainsKey($t.ToLowerInvariant())) {
            $p = Join-Path $GameDir (ConvertTo-NativePath $t)
            if (Test-Path -LiteralPath $p -PathType Leaf) { Remove-Item -LiteralPath $p -Force; Write-Good "Removed $t"; $changes++ }
        }
    }
    $newState = @()
    foreach ($f in $Files) {
        $dest = Join-Path $GameDir (ConvertTo-NativePath ([string]$f.target))
        $kept = $keepSet.ContainsKey(([string]$f.target).ToLowerInvariant()) -and (Test-Path -LiteralPath $dest)
        if (-not $kept -and (Get-Sha256 $dest) -ne ([string]$f.sha256).ToUpperInvariant()) {
            New-Directory (Split-Path -Parent $dest)
            & $Fetch $f $dest
            $got = Get-Sha256 $dest
            if ($got -ne ([string]$f.sha256).ToUpperInvariant()) { Write-Warn "$($f.target) does not match the expected checksum (was it changed after publishing?)." }
            $changes++
        }
        $newState += [pscustomobject]@{ target = [string]$f.target; sha256 = ([string]$f.sha256).ToUpperInvariant() }
    }
    $state.files = $newState
    Save-SyncState $GameDir $state
    if ($changes -gt 0) { Write-Good "$changes file(s) updated." } else { Write-Info 'Files are up to date.' }
}

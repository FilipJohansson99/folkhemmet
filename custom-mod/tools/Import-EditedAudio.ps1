<# Copy only edited workshop clips into the automatically distributed audio pack. #>
[CmdletBinding()]
param(
    [switch]$Publish,
    [string]$WorkshopPath,
    [string]$DestinationRoot
)
$ErrorActionPreference = 'Stop'
$projectDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $WorkshopPath) { $WorkshopPath = Join-Path $projectDir 'asset-workshop' }
if (-not $DestinationRoot) { $DestinationRoot = $projectDir }
if ($Publish -and [IO.Path]::GetFullPath($DestinationRoot) -ne $projectDir) { throw 'Publishing requires the actual project destination.' }
$workRoot = [IO.Path]::GetFullPath($WorkshopPath).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$destRoot = [IO.Path]::GetFullPath($DestinationRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
function Resolve-Inside([string]$Root, [string]$Relative) {
    $full = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if (-not $full.StartsWith($Root, [StringComparison]::OrdinalIgnoreCase)) { throw "Path escapes intended folder: $Relative" }
    return $full
}
function Hash([string]$Path) {
    if (Test-Path -LiteralPath $Path -PathType Leaf) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
    return ''
}
$indexFile = Join-Path $workRoot 'index.json'
if (-not (Test-Path -LiteralPath $indexFile)) { throw 'Extract the editable game assets first (asset-workshop/index.json is missing).' }
$index = Get-Content -LiteralPath $indexFile -Raw | ConvertFrom-Json
if ($index.schema -ne 1) { throw 'Unsupported extraction index format.' }
$ffmpeg = Get-Command ffmpeg -ErrorAction Stop
$stateFile = Join-Path $workRoot 'import-state.json'
$state = @{}
if (Test-Path -LiteralPath $stateFile) {
    foreach ($record in @(Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json)) { $state[$record.target] = $record }
}
$count = 0
$seenTargets = @{}
foreach ($entry in $index.audio) {
    if ($entry.target -notmatch '^audio/(sfx|music/clips)/[^/\\:]+\.ogg$') { throw "Unexpected audio target: $($entry.target)" }
    $original = Resolve-Inside $workRoot $entry.file
    $base = [IO.Path]::Combine([IO.Path]::GetDirectoryName($original), [IO.Path]::GetFileNameWithoutExtension($original))
    $candidates = @()
    foreach ($ext in @('.ogg', '.mp3', '.wav')) {
        $candidate = $base + $ext
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        if ($candidate -eq $original -and (Hash $candidate) -eq $entry.originalHash) { continue }
        $candidates += $candidate
    }
    if (-not $candidates.Count) { continue }
    if ($candidates.Count -gt 1) { throw "Multiple edited versions of $($entry.file). Keep just one edited OGG, MP3 or WAV." }
    $inputFile = $candidates[0]
    $inputHash = Hash $inputFile
    if ($seenTargets.ContainsKey($entry.target) -and $seenTargets[$entry.target] -ne $inputHash) {
        throw "Conflicting edits share the same game clip: $($entry.clipName)"
    }
    $seenTargets[$entry.target] = $inputHash
    $destination = Resolve-Inside $destRoot $entry.target
    if ($state.ContainsKey($entry.target) -and $state[$entry.target].inputHash -eq $inputHash -and
        $state[$entry.target].outputHash -eq (Hash $destination)) { continue }
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
    $pending = $destination + '.download'
    try {
        # Always make a real Vorbis OGG regardless of source extension/encoding.
        $ffmpegArgs = @('-hide_banner', '-loglevel', 'error', '-nostdin', '-y', '-i', $inputFile,
            '-vn', '-c:a', 'libvorbis', '-q:a', '6', '-threads', '1', '-f', 'ogg', $pending)
        & $ffmpeg.Source @ffmpegArgs
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $pending)) { throw "Audio conversion failed: $inputFile" }
        if (Test-Path -LiteralPath $destination) {
            $backup = Resolve-Inside $workRoot ('backups/' + [IO.Path]::GetFileNameWithoutExtension($destination) + '-' + [Guid]::NewGuid().ToString('N') + '.ogg')
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backup))
            Copy-Item -LiteralPath $destination -Destination $backup
        }
        Move-Item -LiteralPath $pending -Destination $destination -Force
    } finally {
        if (Test-Path -LiteralPath $pending) { Remove-Item -LiteralPath $pending -Force }
    }
    $state[$entry.target] = [pscustomobject]@{ target = $entry.target; inputHash = $inputHash; outputHash = Hash $destination }
    $count++
    Write-Host "Imported: $($entry.file)"
}
$records = @($state.Values | Sort-Object target)
[IO.File]::WriteAllText($stateFile, (ConvertTo-Json -InputObject $records -Depth 5), (New-Object Text.UTF8Encoding($false)))
Write-Host "$count edited audio file(s) imported. Unchanged originals were not added to the player pack."
if ($Publish) { & (Join-Path $projectDir 'start-server.ps1') -PublishClientOnly -NonInteractive }

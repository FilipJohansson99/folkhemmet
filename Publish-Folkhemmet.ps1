<#
.SYNOPSIS
    For the HOST only: puts Folkhemmet on GitHub so friends can install it without any account or login.
    One click, no questions:
      1. installs Git if it is missing (Windows asks for permission once),
      2. signs in to GitHub with Git's normal sign-in window (first time only - just click "Sign in with your browser"),
      3. creates the public repository "folkhemmet" on your GitHub account,
      4. builds the player pack and publishes it,
      5. copies the download link for your friends to the clipboard.
    After this, every Start Server.bat publishes mod changes automatically.
    Safe to run again. server-config.psd1 (the password) and the world saves are never uploaded (.gitignore).
#>
$ErrorActionPreference = 'Continue'   # git writes progress to stderr; exit codes are checked instead
$ProgressPreference = 'SilentlyContinue'
$Root = $PSScriptRoot
Set-Location -LiteralPath $Root
try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch { }
$RepoNames = @('folkhemmet', 'folkhemmet-valheim', 'folkhemmet-server')
$GitHubApi = 'https://api.github.com'
if ($env:FOLKHEMMET_GITHUB_API) { $GitHubApi = $env:FOLKHEMMET_GITHUB_API }   # testing only

function Stop-WithMessage([string]$Text) {
    Write-Host ''
    Write-Host $Text -ForegroundColor Yellow
    exit 1
}
function Write-Ok([string]$Text) { Write-Host "  $Text" -ForegroundColor Green }
function Write-Step([string]$Text) { Write-Host ''; Write-Host "==> $Text" -ForegroundColor Cyan }

function Invoke-Git([string[]]$GitArgs, [string]$InputText) {
    if ($PSBoundParameters.ContainsKey('InputText')) { $out = $InputText | & git @GitArgs 2>&1 | ForEach-Object { "$_" } }
    else { $out = & git @GitArgs 2>&1 | ForEach-Object { "$_" } }
    return [pscustomobject]@{ Code = $LASTEXITCODE; Output = (@($out) -join "`n").Trim() }
}

function Get-GitValue([string[]]$GitArgs) {
    # Output of a git query, or '' when git reports an error (errors must not be mistaken for values).
    $r = Invoke-Git $GitArgs
    if ($r.Code -eq 0) { return $r.Output }
    return ''
}

function Update-SessionPath {
    $paths = @([Environment]::GetEnvironmentVariable('Path', 'Machine'), [Environment]::GetEnvironmentVariable('Path', 'User'))
    foreach ($p in @("$env:ProgramFiles\Git\cmd", "${env:ProgramFiles(x86)}\Git\cmd", "$env:LOCALAPPDATA\Programs\Git\cmd")) {
        if ($p -and (Test-Path -LiteralPath $p)) { $paths += $p }
    }
    $env:Path = ($paths | Where-Object { $_ }) -join ';'
}

function Install-Git {
    Write-Step 'Installing Git (Windows may ask for permission)'
    $done = $false
    if (Get-Command winget -ErrorAction SilentlyContinue) {
        winget install --id Git.Git -e --source winget --silent --accept-package-agreements --accept-source-agreements
        Update-SessionPath
        $done = [bool](Get-Command git -ErrorAction SilentlyContinue)
    }
    if (-not $done) {
        $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/git-for-windows/git/releases/latest' -Headers @{ 'User-Agent' = 'Publish-Folkhemmet' }
        $asset = @($release.assets | Where-Object { $_.name -match '^Git-[\d\.]+-64-bit\.exe$' }) | Select-Object -First 1
        if (-not $asset) { Stop-WithMessage 'Could not download Git. Install it from https://git-scm.com/download/win and run this again.' }
        $installer = Join-Path ([IO.Path]::GetTempPath()) $asset.name
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $installer -UseBasicParsing
        Start-Process -FilePath $installer -ArgumentList '/VERYSILENT', '/NORESTART', '/SP-', '/SUPPRESSMSGBOXES' -Wait
        Remove-Item -LiteralPath $installer -Force -ErrorAction SilentlyContinue
        Update-SessionPath
    }
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Stop-WithMessage 'Git was installed but Windows has not picked it up yet. Close this window and run Publish-Folkhemmet.bat again.'
    }
    Write-Ok "Installed $(git --version)"
}

function Get-GitHubCredential {
    # Git Credential Manager (comes with Git for Windows) shows GitHub's sign-in window the first time and remembers it.
    $helper = Get-GitValue @('config', '--get-all', 'credential.helper')
    if (-not $helper) {
        if ((Invoke-Git @('credential-manager', '--version')).Code -ne 0) {
            Stop-WithMessage 'Git Credential Manager is missing. Reinstall Git from https://git-scm.com/download/win (default options) and run Publish-Folkhemmet.bat again.'
        }
        [void](Invoke-Git @('config', '--global', 'credential.helper', 'manager'))
    }
    Write-Host '  Signing in to GitHub - if a window opens, choose "Sign in with your browser" (first time only).'
    $r = Invoke-Git @('credential', 'fill') "protocol=https`nhost=github.com`n"
    $user = $null
    $token = $null
    foreach ($line in $r.Output -split "`n") {
        if ($line -match '^username=(.*)$') { $user = $Matches[1].Trim() }
        if ($line -match '^password=(.*)$') { $token = $Matches[1].Trim() }
    }
    if ($r.Code -ne 0 -or -not $token) {
        Stop-WithMessage 'GitHub sign-in did not complete. Run Publish-Folkhemmet.bat again (you need a free account at https://github.com).'
    }
    return [pscustomobject]@{ User = $user; Token = $token }
}

function Invoke-GitHub([string]$Method, [string]$Path, $Token, $Body) {
    $h = @{ Authorization = "Bearer $Token"; 'User-Agent' = 'Publish-Folkhemmet'; Accept = 'application/vnd.github+json' }
    $args2 = @{ Uri = "$GitHubApi$Path"; Method = $Method; Headers = $h; TimeoutSec = 60 }
    if ($null -ne $Body) { $args2.Body = (ConvertTo-Json -InputObject $Body); $args2.ContentType = 'application/json' }
    return Invoke-RestMethod @args2
}

function Get-HttpCode($ErrorRecord) {
    try { return [int]$ErrorRecord.Exception.Response.StatusCode } catch { return 0 }
}

# =====================================================================================

Write-Host ''
Write-Host '  FOLKHEMMET - publish to GitHub (host only)' -ForegroundColor Cyan

# 1. Git
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { Update-SessionPath }
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { Install-Git }

if (-not (Test-Path -LiteralPath (Join-Path $Root '.git'))) {
    [void](Invoke-Git @('init'))
    [void](Invoke-Git @('symbolic-ref', 'HEAD', 'refs/heads/main'))
    Write-Ok 'Created the local repository.'
}

# 2 + 3. GitHub sign-in and repository
$remote = Get-GitValue @('remote', 'get-url', 'origin')
$needIdentity = -not (Get-GitValue @('config', 'user.email'))
if (-not $remote -or $needIdentity) {
    Write-Step 'GitHub'
    $cred = Get-GitHubCredential
    try {
        $me = Invoke-GitHub 'GET' '/user' $cred.Token $null
    } catch {
        Stop-WithMessage "GitHub did not accept the sign-in ($($_.Exception.Message)). Run Publish-Folkhemmet.bat again."
    }
    $login = [string]$me.login
    Write-Ok "Signed in as $login"
    # Remember the sign-in for the pushes that follow.
    [void](Invoke-Git @('credential', 'approve') "protocol=https`nhost=github.com`nusername=$($cred.User)`npassword=$($cred.Token)`n")

    if ($needIdentity) {
        $name = [string]$me.name
        if (-not $name) { $name = $login }
        [void](Invoke-Git @('config', 'user.name', $name))
        [void](Invoke-Git @('config', 'user.email', "$($me.id)+$login@users.noreply.github.com"))
    }

    if (-not $remote) {
        $repo = $null
        foreach ($name in $RepoNames) {
            try {
                $existing = Invoke-GitHub 'GET' "/repos/$login/$name" $cred.Token $null
            } catch {
                if ((Get-HttpCode $_) -ne 404) { Stop-WithMessage "Could not check GitHub: $($_.Exception.Message)" }
                $existing = $null
            }
            if (-not $existing) {
                $repo = Invoke-GitHub 'POST' '/user/repos' $cred.Token ([ordered]@{
                        name = $name; private = $false; has_issues = $false; has_wiki = $false; has_projects = $false
                        description = 'Folkhemmet - modded Valheim server. Players: download client-pack/Folkhemmet.zip and run Install-Folkhemmet.bat (no login needed).' })
                Write-Ok "Created the public repository $($repo.html_url)"
                break
            }
            # Re-use an earlier Folkhemmet repo (empty, or one that already holds this project).
            $ours = ($existing.size -eq 0)
            if (-not $ours) {
                try { [void](Invoke-GitHub 'GET' "/repos/$login/$name/contents/start-server.ps1" $cred.Token $null); $ours = $true } catch { }
            }
            if ($ours) {
                if ($existing.private) {
                    $existing = Invoke-GitHub 'PATCH' "/repos/$login/$name" $cred.Token @{ private = $false }
                    Write-Ok "Made $name public (Play.bat downloads without logging in)."
                }
                $repo = $existing
                Write-Ok "Using the existing repository $($repo.html_url)"
                break
            }
        }
        if (-not $repo) { Stop-WithMessage "Repositories named $($RepoNames -join ', ') already exist on your account for other things." }
        [void](Invoke-Git @('remote', 'add', 'origin', [string]$repo.clone_url))
    }
}
$remote = Get-GitValue @('remote', 'get-url', 'origin')
if (-not $remote) { Stop-WithMessage 'The GitHub repository could not be connected. Run Publish-Folkhemmet.bat again.' }
Write-Ok "Repository: $remote"

# 4. Build + publish (no questions)
Write-Step 'Building the player pack and publishing it'
& (Join-Path $Root 'start-server.ps1') -NoLaunch -NonInteractive

# 5. Link for friends
if ($remote -match 'github\.com[:/]+([^/]+)/([^/\s]+?)(?:\.git)?/?$') {
    $branch = Get-GitValue @('symbolic-ref', '--short', 'HEAD')
    if (-not $branch) { $branch = 'main' }
    $link = "https://github.com/$($Matches[1])/$($Matches[2])/raw/$branch/client-pack/Folkhemmet.zip"
    try { Set-Clipboard -Value $link } catch { }
    Write-Host ''
    Write-Host '  DONE. Send your friends this link (it is already copied - just paste it):' -ForegroundColor Green
    Write-Host "  $link" -ForegroundColor White
    Write-Host '  ...and the server password. They open the zip and double-click Install-Folkhemmet.bat.' -ForegroundColor Green
    Write-Host '  No GitHub account or login needed on their side.' -ForegroundColor Green
}

# NetworkCheck.ps1 - makes sure friends can reach the server (used by start-server.ps1).
#   Before start: Windows Firewall rules, router port forwarding (automatic via UPnP when the router allows it),
#                 carrier-grade NAT detection, local + internet address.
#   After start:  a background check that the server answers from the internet (query port + Steam server list).
# Windows PowerShell 5.1 compatible. Keep this file ASCII only.

$script:FirewallRuleName = 'Valheim dedicated server (start-server.ps1)'

function Test-IsWindowsHost {
    return ($PSVersionTable.PSEdition -eq 'Desktop') -or ((Get-Variable -Name IsWindows -ErrorAction SilentlyContinue) -and $IsWindows)
}

function Get-LocalIPv4 {
    # The address of the network card used for the internet (what the router must forward to).
    try {
        $s = New-Object System.Net.Sockets.Socket([Net.Sockets.AddressFamily]::InterNetwork, [Net.Sockets.SocketType]::Dgram, [Net.Sockets.ProtocolType]::Udp)
        $s.Connect('8.8.8.8', 53)
        $ip = $s.LocalEndPoint.Address.ToString()
        $s.Close()
        return $ip
    } catch { return $null }
}

function Get-PublicIPv4 {
    foreach ($u in 'https://api.ipify.org', 'https://ipv4.icanhazip.com', 'https://ifconfig.me/ip') {
        try {
            $ip = ([string](Invoke-WebRequest -Uri $u -UseBasicParsing -TimeoutSec 10).Content).Trim()
            if ($ip -match '^\d{1,3}(\.\d{1,3}){3}$') { return $ip }
        } catch { }
    }
    return $null
}

function Test-NonPublicIPv4([string]$Ip) {
    if (-not $Ip -or $Ip -notmatch '^(\d+)\.(\d+)\.\d+\.\d+$') { return $false }
    $a = [int]$Matches[1]; $b = [int]$Matches[2]
    return ($a -eq 10) -or ($a -eq 172 -and $b -ge 16 -and $b -le 31) -or ($a -eq 192 -and $b -eq 168) -or ($a -eq 100 -and $b -ge 64 -and $b -le 127)
}

# ------------------------------------------------------------------------ Windows Firewall

function Test-PortInRange([string[]]$Ranges, [int]$Port) {
    foreach ($r in $Ranges) {
        if ($r -eq 'Any') { return $true }
        if ($r -match '^(\d+)-(\d+)$') { if ($Port -ge [int]$Matches[1] -and $Port -le [int]$Matches[2]) { return $true } }
        elseif ($r -match '^\d+$' -and [int]$r -eq $Port) { return $true }
    }
    return $false
}

function Test-ServerFirewall([string]$Exe, [int]$Port) {
    if (-not (Get-Command Get-NetFirewallRule -ErrorAction SilentlyContinue)) { return }
    $exeFull = [IO.Path]::GetFullPath($Exe)
    $allow = 0
    $block = 0
    try {
        # Rules for the program (Windows creates these from the "allow access" popup - Cancel creates BLOCK rules).
        $filters = @(Get-NetFirewallApplicationFilter -ErrorAction Stop | Where-Object {
                $_.Program -and ([Environment]::ExpandEnvironmentVariables($_.Program) -ieq $exeFull) })
        foreach ($f in $filters) {
            foreach ($r in @($f | Get-NetFirewallRule -ErrorAction SilentlyContinue)) {
                if ("$($r.Enabled)" -ne 'True' -or "$($r.Direction)" -ne 'Inbound') { continue }
                if ("$($r.Action)" -eq 'Block') { $block++ } elseif ("$($r.Action)" -eq 'Allow') { $allow++ }
            }
        }
        if ($allow -eq 0) {
            # Port rules (ours, or ones made by hand).
            foreach ($pf in @(Get-NetFirewallPortFilter -Protocol UDP -ErrorAction SilentlyContinue)) {
                if (-not (Test-PortInRange @($pf.LocalPort) $Port)) { continue }
                foreach ($r in @($pf | Get-NetFirewallRule -ErrorAction SilentlyContinue)) {
                    if ("$($r.Enabled)" -eq 'True' -and "$($r.Direction)" -eq 'Inbound' -and "$($r.Action)" -eq 'Allow') { $allow++ }
                }
            }
        }
    } catch {
        Write-Info "Could not read the Windows Firewall rules ($($_.Exception.Message))."
        return
    }

    if ($block -eq 0 -and $allow -gt 0) { Write-Good 'Windows Firewall: the server is allowed.'; return }
    if ($block -gt 0) { Write-Warn "Windows Firewall BLOCKS valheim_server.exe ($block rule(s) - made when the firewall popup was cancelled)." }
    else { Write-Warn 'Windows Firewall: no rule allows the server yet.' }

    $a = Read-Host '    Fix the Windows Firewall now? Windows will ask for admin permission (Y/n)'
    if ($a -match '^[nN]') { Write-Info 'Skipped. Friends outside your PC probably cannot connect.'; return }
    $name = $script:FirewallRuleName
    $cmd = @"
`$exe = '$exeFull'
Get-NetFirewallApplicationFilter | Where-Object { `$_.Program -and ([Environment]::ExpandEnvironmentVariables(`$_.Program) -ieq `$exe) } |
    Get-NetFirewallRule | Where-Object { "`$(`$_.Direction)" -eq 'Inbound' -and "`$(`$_.Action)" -eq 'Block' } | Remove-NetFirewallRule
Get-NetFirewallRule -DisplayName '$name*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName '$name - UDP $Port-$($Port + 2)' -Direction Inbound -Protocol UDP -LocalPort '$Port-$($Port + 2)' -Action Allow -Profile Any | Out-Null
New-NetFirewallRule -DisplayName '$name - program' -Direction Inbound -Program `$exe -Action Allow -Profile Any | Out-Null
"@
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($cmd))
    try {
        $p = Start-Process -FilePath 'powershell.exe' -Verb RunAs -Wait -PassThru -WindowStyle Hidden -ArgumentList "-NoProfile -ExecutionPolicy Bypass -EncodedCommand $encoded"
        if ($p.ExitCode -eq 0) { Write-Good 'Windows Firewall fixed: UDP ports and valheim_server.exe are allowed.' }
        else { Write-Warn "The firewall change did not finish (exit code $($p.ExitCode))." }
    } catch {
        Write-Warn 'The firewall change was cancelled.'
    }
}

# ------------------------------------------------------------------------ Router (UPnP)

function Invoke-UPnPForward([int[]]$Ports, [string]$LocalIp, [string]$Description, [bool]$AutoForward) {
    # Returns $null when UPnP isn't usable, otherwise @{ ExternalIp; Ok (ports forwarded to this PC) }.
    $col = $null
    try {
        $nat = New-Object -ComObject HNetCfg.NATUPnP
        $col = $nat.StaticPortMappingCollection
    } catch { return $null }
    if ($null -eq $col) { return $null }

    $existing = @()
    try { foreach ($m in $col) { $existing += $m } } catch { }
    $external = $null
    $ok = @()
    foreach ($p in $Ports) {
        $m = $existing | Where-Object { $_.ExternalPort -eq $p -and "$($_.Protocol)" -eq 'UDP' } | Select-Object -First 1
        if ($m) { $external = $m.ExternalIPAddress }
        if ($m -and $m.InternalClient -eq $LocalIp -and $m.InternalPort -eq $p -and $m.Enabled) {
            Write-Good "Router: UDP $p is forwarded to this PC ($LocalIp)."
            $ok += $p
            continue
        }
        if (-not $AutoForward) {
            if ($m) { Write-Warn "Router: UDP $p is forwarded to $($m.InternalClient), not to this PC ($LocalIp)." }
            else { Write-Warn "Router: UDP $p is not forwarded." }
            continue
        }
        try {
            if ($m) { $col.Remove($p, 'UDP') | Out-Null }
            $new = $col.Add($p, 'UDP', $p, $LocalIp, $true, $Description)
            if ($new -and $new.ExternalIPAddress) { $external = $new.ExternalIPAddress }
            Write-Good "Router: forwarded UDP $p to this PC ($LocalIp) automatically (UPnP)."
            $ok += $p
        } catch {
            Write-Warn "Router refused to forward UDP $p automatically."
        }
    }
    return [pscustomobject]@{ ExternalIp = $external; Ok = $ok }
}

# ------------------------------------------------------------------------ before start

function Invoke-PortCheck {
    param([int]$Port, [string]$ServerExe, [string]$ServerName, [bool]$Crossplay, [bool]$AutoForward)
    Write-Step 'Checking that friends can reach the server (ports / firewall)'
    $localIp = Get-LocalIPv4
    $publicIp = Get-PublicIPv4
    Write-Info "This PC on your network: $localIp    Your internet address: $publicIp"

    if ($Crossplay) {
        Write-Info 'Crossplay is on: traffic goes through a relay, no port forwarding needed.'
        return [pscustomobject]@{ LocalIp = $localIp; PublicIp = $publicIp }
    }
    if (-not (Test-IsWindowsHost)) {
        Write-Info 'Firewall/router checks only run on Windows.'
        return [pscustomobject]@{ LocalIp = $localIp; PublicIp = $publicIp }
    }

    Test-ServerFirewall $ServerExe $Port

    $ports = @($Port, ($Port + 1))
    $upnp = $null
    if ($localIp) { $upnp = Invoke-UPnPForward $ports $localIp "Valheim $ServerName" $AutoForward }
    if (-not $upnp) {
        Write-Info "Router: automatic forwarding (UPnP) is not available, so it can't be checked from here."
        Write-Info "Make sure your router forwards UDP $Port-$($Port + 1) to $localIp (the check after start tells you if it works)."
    } elseif (@($upnp.Ok).Count -lt $ports.Count) {
        Write-Warn "Forward UDP $Port-$($Port + 1) to $localIp in your router's settings (look for 'Port forwarding' / 'Virtual server')."
    }

    # Carrier-grade NAT / double router: forwarding on your router can't help then.
    $routerIp = $null
    if ($upnp) { $routerIp = $upnp.ExternalIp }
    if ((Test-NonPublicIPv4 $publicIp) -or (Test-NonPublicIPv4 $routerIp) -or ($routerIp -and $publicIp -and $routerIp -ne $publicIp)) {
        Write-Warn "Your router's internet address ($routerIp) is not your real internet address ($publicIp)."
        Write-Warn 'That means a second router or your internet provider (CGNAT) sits in between - port forwarding on your router'
        Write-Warn 'alone will not work. Ask your provider for a public IP, or set Crossplay = $true (relay; the shared map mod stops working).'
    }
    if ($publicIp) { Write-Info "Friends can use Join IP: ${publicIp}:$Port" }
    return [pscustomobject]@{ LocalIp = $localIp; PublicIp = $publicIp }
}

# ------------------------------------------------------------------------ after start (background)

function Start-ReachabilityWatcher {
    # Runs next to the server in the same window: waits for "Game server connected", then checks from the outside.
    param([string]$LogPath, [string]$PublicIp, [int]$Port, [bool]$Public)
    if (-not $PublicIp) { return $null }
    $ps = [powershell]::Create()
    [void]$ps.AddScript({
        param($LogPath, $PublicIp, $Port, $Public, $Since)
        $ProgressPreference = 'SilentlyContinue'
        try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch { }
        function Say([string]$Text, [string]$Color) {
            $old = [Console]::ForegroundColor
            if ($Color) { [Console]::ForegroundColor = [ConsoleColor]$Color }
            [Console]::WriteLine($Text)
            [Console]::ForegroundColor = $old
        }
        function Test-Query([string]$Ip, [int]$QueryPort) {
            # Steam A2S_INFO query - the server answers on game port + 1.
            $udp = New-Object Net.Sockets.UdpClient
            try {
                $udp.Client.ReceiveTimeout = 3000
                [byte[]]$pkt = @(0xFF, 0xFF, 0xFF, 0xFF, 0x54) + @([Text.Encoding]::ASCII.GetBytes('Source Engine Query')) + @(0)
                [void]$udp.Send($pkt, $pkt.Length, $Ip, $QueryPort)
                $ep = New-Object Net.IPEndPoint([Net.IPAddress]::Any, 0)
                $reply = $udp.Receive([ref]$ep)
                return ($reply.Length -gt 0)
            } catch { return $false } finally { $udp.Close() }
        }
        $deadline = (Get-Date).AddMinutes(20)
        $ready = $false
        while ((Get-Date) -lt $deadline -and -not $ready) {
            Start-Sleep -Seconds 5
            try {
                if ((Test-Path -LiteralPath $LogPath) -and ((Get-Item -LiteralPath $LogPath).LastWriteTime -gt $Since)) {
                    $fs = [IO.File]::Open($LogPath, 'Open', 'Read', 'ReadWrite')
                    $sr = New-Object IO.StreamReader($fs)
                    $ready = $sr.ReadToEnd().Contains('Game server connected')
                    $sr.Close()
                }
            } catch { }
        }
        if (-not $ready) { return }
        Start-Sleep -Seconds 5
        $tag = '[Folkhemmet check]'
        Say '' ''
        Say "$tag Server is up - testing whether it can be reached from the internet..." 'Cyan'
        if (Test-Query $PublicIp ($Port + 1)) {
            Say "$tag OK: the server answers on your internet address ${PublicIp}:$Port. Friends can join." 'Green'
            return
        }
        if (-not $Public) {
            Say "$tag Could not confirm from here (Public = `$false, so Steam doesn't list it). Ask a friend to try Join IP ${PublicIp}:$Port." 'Yellow'
            return
        }
        for ($i = 0; $i -lt 12; $i++) {
            try {
                $r = Invoke-RestMethod -Uri "https://api.steampowered.com/ISteamApps/GetServersAtAddress/v1/?addr=$PublicIp&format=json" -TimeoutSec 20
                $hit = @($r.response.servers) | Where-Object { $_ -and $_.appid -eq 892970 -and [int]$_.gameport -eq $Port }
                if ($hit) {
                    Say "$tag OK: Steam sees the server from the internet at ${PublicIp}:$Port. Friends can join." 'Green'
                    return
                }
            } catch { }
            Start-Sleep -Seconds 15
        }
        Say "$tag WARNING: the server can't be seen from the internet after 3 minutes." 'Yellow'
        Say "$tag Check: router forwards UDP $Port-$($Port + 1) to this PC, Windows Firewall allows valheim_server.exe," 'Yellow'
        Say "$tag and your provider gives you a public IP (no CGNAT). Players on your own network can still join." 'Yellow'
    })
    [void]$ps.AddArgument($LogPath).AddArgument($PublicIp).AddArgument($Port).AddArgument($Public).AddArgument((Get-Date))
    $handle = $ps.BeginInvoke()
    return [pscustomobject]@{ PowerShell = $ps; Handle = $handle }
}

function Stop-ReachabilityWatcher($Watcher) {
    if (-not $Watcher) { return }
    try { $Watcher.PowerShell.Stop() } catch { }
    try { $Watcher.PowerShell.Dispose() } catch { }
}

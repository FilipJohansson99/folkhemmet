# Server settings for start-server.ps1
# start-server.ps1 copies this file to server-config.psd1 on the first run and asks for the name and password.
# server-config.psd1 is NOT uploaded to GitHub (it holds the password) - edit that one.
@{
    # --- Basics -----------------------------------------------------------------
    ServerName = 'Folkhemmet'         # Shown in the server browser
    Password   = 'CHANGE ME'          # 5+ characters, must not be part of the world name
    WorldName  = 'Folkhemmet'         # Created on first start if it doesn't exist
    Port       = 2456                 # Forward UDP 2456-2457 in your router for friends outside your network
    Public     = $true                # $true = listed in the community server browser

    # Crossplay (Xbox/Game Pass players). Leave $false: the shared map mod (ServerSideMap) does not work with crossplay.
    Crossplay  = $false

    # --- Network checks (every start) -----------------------------------------------
    CheckPorts      = $true           # before start: Windows Firewall + router ports; after start: reachable from the internet?
    AutoPortForward = $true           # open UDP 2456-2457 on the router automatically (UPnP) when the router allows it

    # --- Folders ------------------------------------------------------------------
    # Steam's dedicated server install (BepInEx and the mods are installed into it).
    ServerDir  = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server'
    # Worlds, admin/ban lists. Relative paths are relative to this project folder.
    SaveDir    = 'data'

    # --- World rules (vanilla world modifiers, applied on every start) -------------
    # Hunger: 33 = food lasts 3x longer (hunger drains at 33% of normal speed). 100 = vanilla.
    FoodRatePercent = 33
    # Optional preset: Normal, Casual, Easy, Hard, Hardcore, Immersive, Hammer ('' = none)
    Preset     = ''
    # Optional modifiers, e.g. @{ Combat = 'hard'; Raids = 'less'; Resources = 'more' }
    Modifiers  = @{}
    # Optional checkbox keys, e.g. @('nobuildcost', 'passivemobs')
    SetKeys    = @()

    # --- Admins --------------------------------------------------------------------
    # SteamID64s that get admin (needed for devcommands / Infinity Hammer), e.g. @('76561198000000000')
    # Find yours in-game with F2, or in the server window when you connect.
    Admins     = @()

    # --- Saving / backups ------------------------------------------------------------
    SaveIntervalSeconds = 1800        # vanilla default 1800 (30 min)
    Backups             = 4           # vanilla automatic backups kept
    StartupBackupsToKeep = 10         # copies of the world made by start-server.ps1 before each start

    # --- Mods ---------------------------------------------------------------------------
    UpdateModsOnStart = $true         # check Thunderstore for new versions on every start
    PublishToGit      = $true         # commit + push the player pack (client-pack/) so friends get updates

    # Extra arguments passed straight to valheim_server.exe
    ExtraArgs  = @()
}

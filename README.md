# Folkhemmet - Valheim Server

Modded Valheim dedicated server with shared map, 3x map reveal, slower hunger, monster-proof buildings and custom
textures (troll in a tracksuit, custom sails).

---

## For players: how to join

No GitHub account, no login, nothing else to install.

1. **Download `client-pack/Folkhemmet.zip`** from this repo (click it, then *Download raw file*) - or use the link the
   host sent you.
2. **Open the zip and double-click `Install-Folkhemmet.bat`.** It installs BepInEx and every mod - in exactly the
   versions and with the same settings as the server - into your Valheim, puts a **Folkhemmet** shortcut (troll icon)
   on your desktop and in the Start menu, and starts the game.
   If Windows says *Windows protected your PC*: click *More info* -> *Run anyway*.
3. In Valheim: **Join Game** -> find **Folkhemmet** (or *Join IP*) -> enter the password you got from the host.

From then on start the game with the **Folkhemmet** desktop shortcut: it updates the mods, their settings and itself
first. To go back to vanilla Valheim: Start menu -> Folkhemmet -> *Remove Folkhemmet mods*.

---

## For the host

### Start the server

Double-click **`Start Server.bat`** (or run `.\start-server.ps1` in PowerShell).

Server name, world and password live in `server-config.psd1` (never uploaded). The first time it asks for your
SteamID64 for admin rights (optional). Every start then:

1. checks Thunderstore for new versions of everything in `mods.txt` (plus dependencies) and installs them into the
   dedicated server (`C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server`),
2. copies `game-files\common` + `game-files\server` (mod configs, the custom mod) into the server,
3. writes the player pack (`client-pack\`) and pushes it to GitHub,
4. checks the Windows Firewall (offers to fix it) and the router: forwards UDP 2456-2457 automatically via UPnP when
   the router allows it, and warns about carrier-grade NAT,
5. backs up the world (`data\startup-backups\`) and starts the server,
6. once it's up, checks in the background that it can be reached from the internet (`[Folkhemmet check]` lines).

Wait for `Game server connected`. **Stop with Ctrl+C** (the server saves first). Options:
`-SkipModUpdate` (offline / keep current versions), `-NoLaunch` (update + publish only), `-NoPublish`, `-SkipPortCheck`.

**Router:** friends outside your network need **UDP 2456-2457** forwarded to this PC. The start script does this
automatically when the router supports UPnP; otherwise it tells you the PC's address to forward to. Keep *Valheim
Dedicated Server* updated in Steam (Library -> Tools) - the script warns if Steam says it needs an update.

### Share with friends (one-time, one click)

Friends get the mods from a public GitHub repo made from this folder. Only you need a (free) GitHub account:

1. Double-click **`Publish-Folkhemmet.bat`** (host only). No questions: it installs Git if it's missing, opens GitHub's
   sign-in once (*Sign in with your browser*), creates the public repository `folkhemmet` and publishes everything.
2. The friends' link is copied to your clipboard (`.../raw/main/client-pack/Folkhemmet.zip`) - send it with the
   server password. Friends need no account and never log in.

After that, every `Start Server.bat` publishes changes automatically. Your password (`server-config.psd1`) and the
world saves (`data\`) are in `.gitignore` and never uploaded.

**You play too?** Run `client\Play.bat` from this folder once (it adds the Folkhemmet desktop shortcut). It uses the local files directly (no GitHub needed).

---

## What's set up

| Feature | How |
|---|---|
| Quick start script | `Start Server.bat` -> `start-server.ps1` |
| Mods auto-update on every start | `mods.txt` -> Thunderstore API, exact versions written to `client-pack/manifest.json` |
| Easy mod install/update for players | `Install-Folkhemmet.bat` (in `Folkhemmet.zip`, no login) -> Folkhemmet shortcut / `Play.bat` syncs to the manifest, removes dropped mods, updates itself |
| Server name + password | `server-config.psd1` (asked on first start) |
| Shared map exploration + shared pins | [ServerSideMap](https://thunderstore.io/c/valheim/p/Mydayyy/ServerSideMap/), pin sharing enabled in `game-files/common/BepInEx/config/eu.mydayyy.plugins.serversidemap.cfg` |
| All players always visible on the map | Custom mod, enforced by the server (`AlwaysShowPlayersOnMap`) |
| Map reveal 3x | Custom mod, `ExploreRadiusMultiplier = 3` (radius 100 m -> 300 m), pushed from the server to players |
| Hunger at 33% speed | Vanilla world modifier `foodrate 33` (`FoodRatePercent` in `server-config.psd1`) - food lasts 3x longer |
| Buildings indestructible from enemies | Custom mod, `[Buildings] ProtectFromEnemies` - monsters do no damage to built pieces (ships/carts optional) |
| Bigger stacks | Custom mod, `[Items] StackSizeMultiplier = 2` - every stackable item stacks twice as high |
| More stone and wood | Custom mod, `[Drops] StoneMultiplier = 3`, `WoodMultiplier = 2` (rocks, ore deposits, trees, logs, stumps, ground pickups - not deconstructing) |
| Custom textures (trolls, sails, ...) | Custom mod, PNGs in `textures\` (troll + the sails of every ship) |
| Port forwarding check | `lib\NetworkCheck.ps1`: firewall, UPnP forwarding, CGNAT, internet reachability |

### Mods (`mods.txt`)

One Thunderstore link (or `Author-ModName`) per line. Add `/v/1.2.3/` to a link (or `Author-ModName-1.2.3`) to stay on
a version; add `server-only` / `client-only` after a link to install it on one side only. Dependencies are automatic.
Nexus links can't be automated (Nexus needs a login) - use the mod's Thunderstore page; both of yours were on
Thunderstore already (SeneaL UI, Plant Everything).

Notes on the current list:
- **Better Networking**: `CW_Jesse`'s original was last updated in 2023 and is outdated for Valheim 1.0, so the
  maintained fork `SimplifyDave-BetterNetworking_Valheim` is used instead (the original is kept as a comment).
- **Riverheim** changes world generation: create the world with it installed and never remove it. It can't load
  vanilla worlds (and vice versa). Everyone needs the same version - Play.bat takes care of that.
- **ServerSideMap** doesn't support crossplay, so `Crossplay = $false` (Steam players only).

### Server settings (`server-config.psd1`)

Name, password, world, port, public listing, save folder, world modifiers (`FoodRatePercent`, `Preset`, `Modifiers`,
`SetKeys` - applied fresh on every start), admins, backups, and `UpdateModsOnStart` / `PublishToGit`.
Admins (`Admins = @('7656...')`) are written to `data\adminlist.txt`; you need admin for devcommands/Infinity Hammer.

### Mod configs (`game-files\`) - synced to everyone

Every mod writes its `.cfg` on the server the first time it runs; `start-server.ps1` then copies each new one into
`game-files\common\BepInEx\config\` automatically, and from there it goes to the server **and every player** on each
start / Play.bat. So all mod settings are the same for everyone - **edit them in `game-files\`**, not in the game
folders (those copies get overwritten). `server\` / `client\` hold files for one side only.

A player who wants to keep their own version of a purely personal config (e.g. UI layout) can add its file name to
`KeepLocalConfigs` in their `client-config.json`, e.g. `"KeepLocalConfigs": ["seneaL.valheim.ui.cfg"]`.

---

## Custom mod: ValheimServerTweaks

Source in `custom-mod\`, built dll in `game-files\common\BepInEx\plugins\ValheimServerTweaks\`,
settings in `game-files\common\BepInEx\config\filip.valheim.servertweaks.cfg`.

**Buildings** - monsters can't damage anything built (walls, doors, workbenches...). Players still can, and weather,
falling trees, Ashlands fire etc. work as normal. `AlsoProtectShipsAndCarts = true` extends it to ships and carts.

**Textures** - `textures\<PrefabName>\<MaterialName>.png` replaces that material's main texture
(`<MaterialName>._BumpMap.png` etc. for other slots). In game, open the console (F5):

| Command | Does |
|---|---|
| `tex_find troll` | lists prefab names containing "troll" |
| `tex_dump Troll` | saves the original textures + `materials.txt` to `<Valheim>\BepInEx\plugins\ValheimServerTweaks\texture-dumps\Troll\` |
| `tex_reload` | re-reads the textures without restarting |

Workflow: `tex_dump Troll` -> copy the PNG into `textures\Troll\` (same name) -> paint -> `Start Server.bat` publishes
it -> everyone gets it with Play.bat. Included now: `Troll\troll.png` and the sails of `Raft`, `Karve`,
`VikingShip` (longship), `VikingShip_Ashlands` (drakkar) and `Trailership` (its sail material is shared with the
frozen shipwrecks). Sails are 1024x1024; the picture reads correctly from behind the ship. Changing a material also changes other objects that share
it (e.g. the troll ragdoll - which is usually what you want).

**Rebuild** after changing the C# code: install the .NET SDK, run `custom-mod\build.ps1`, then `Start Server.bat`.

---

## Troubleshooting

- **Logs:** server `...\Valheim dedicated server\BepInEx\LogOutput.log`, players `...\Valheim\BepInEx\LogOutput.log`.
- **Friends can't find/join:** check the port forward (UDP 2456-2457), that they started with Play.bat, and that
  Steam has updated both Valheim and the dedicated server.
- **"Could not check for mod updates":** Thunderstore unreachable - the server starts with the installed mods.
- **Mods in a weird state:** delete the `BepInEx\plugins\<Author-Mod>` folder, run again - it reinstalls.
- **Move an existing world in:** copy `<World>.db` + `<World>.fwl` from
  `%USERPROFILE%\AppData\LocalLow\IronGate\Valheim\worlds_local` into `data\worlds_local` (the first start offers this).
  Note: an existing vanilla world won't load with Riverheim.

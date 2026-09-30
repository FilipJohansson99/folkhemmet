Files copied into the Valheim folder (same layout as the game folder).
  common\  -> server AND every player      (configs everyone must share, the custom mod)
  server\  -> only the dedicated server     (server-only configs)
  client\  -> only players                  (client-only configs)
These files are enforced: edit them here, not in the game folders (they are overwritten on every start / Play.bat).
To manage a mod's settings: copy its .cfg from the server's BepInEx\config folder into common\BepInEx\config\ and edit it here.

CUSTOM TEXTURES
===============
Put replacement textures here. Play.bat copies them to every player (the server doesn't need them).

Layout:   textures\<PrefabName>\<MaterialName>.png
          textures\<PrefabName>\<MaterialName>._BumpMap.png    (other texture slots, rarely needed)

How to make one (in game, press F5 for the console):
  1. tex_find troll          -> lists prefab names, e.g. Troll
  2. tex_dump Troll          -> saves the original textures + materials.txt to
                                <Valheim>\BepInEx\plugins\ValheimServerTweaks\texture-dumps\Troll\
  3. Copy the PNG you want into textures\Troll\ here (keep the file name) and paint over it.
  4. Run start-server.ps1 (publishes it) and Play.bat.
     To preview while the game is running: also drop the PNG into
     <Valheim>\BepInEx\plugins\ValheimServerTweaks\textures\Troll\ and type tex_reload in the console.

Useful prefab names: Troll, VikingShip (longship), Karve, VikingShip_Ashlands (drakkar), Raft.
Sails are on the ship prefabs - tex_dump VikingShip and look for the sail material in materials.txt.
Folders starting with _ are ignored (use them for drafts).

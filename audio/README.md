# Custom mob sounds and music

Requested targets: sea serpent (`Serpent`), necks (`Neck`), Ashlands creatures, and Fader (displayed as **Jimpan**).
Use `audio_find serpent`, `audio_find neck`, `audio_find charred`, `audio_find asksvin`, `audio_find morgen`,
`audio_find volture`, `audio_find bonemaw`, and `audio_find fader` to discover related effect objects/clips.
Some effect names differ from the creature's display name, so use more specific clip searches if needed.
Current replacements (mod 1.3.4):

- All 8 extracted neck clips and all 181 charred clips use `sfx/perfekt_jimpan.ogg`, converted from the supplied `perfekt_jimpan.mp3`.
- `encounters/serpent_aggro.ogg` is the supplied **Tjuge Pågar I Bastun (1).mp3**, converted to mono Ogg Vorbis for spatial playback.
  It plays while a living, aggroed sea serpent or bonemaw is within 100 metres of the local player. One source follows the nearest
  eligible creature; other serpents and attacks do not restart or stack the song. It loops during longer fights and stops when
  there are no nearby aggroed serpents, on player death/logout, or when custom audio is disabled. It uses the game's ambience/SFX
  mixer and distance fading. It adds a continuous encounter sound; the creatures' ordinary combat effects remain.
  Aggro is read from the game's existing replicated AI state, so this works with the server still on its current mod.

The short phrase is shared using `sfx-aliases.cfg` (`original clip name=replacement clip name`). It is decoded once, rather than
loading 189 identical recordings. A direct `sfx/<original clip name>.ogg` takes priority over an alias, so importing later edits
from the workshop can still customize individual sounds. Change `sfx/perfekt_jimpan.ogg` to update the shared phrase, or
`encounters/serpent_aggro.ogg` to update the aggro song, then run **Publish Client Assets.bat**. Long encounter audio streams
from disk. The workshop baseline originals and the supplied source MP3s remain local.

This folder is delivered automatically by the Folkhemmet launcher. Players do not need another ZIP or manual mod installation.

## Replace the extracted files

The original requested creature sounds and the full music catalogue are extracted into the local `asset-workshop/` folder.
Listen to the `.ogg` files, then overwrite whichever ones you want to change, keeping their filenames.
Alternatively, put an MP3 or WAV beside an unchanged OGG with the same base name (for example, `MenuMusic.mp3` beside `MenuMusic.ogg`).
Only one edited version of each clip should be present. Then double-click **Publish Edited Audio.bat**.
It converts your edits to game-compatible Ogg Vorbis, copies only changed clips into the folders below and publishes them.
Unchanged original game recordings are not uploaded. The workshop and its index must stay together; the index records
original checksums and maps filenames to their game clips. The server can keep running.

Deletion in the workshop is ignored. To undo a published replacement, remove the corresponding file from `audio/sfx/`
or `audio/music/clips/`, then use **Publish Client Assets.bat**. Old custom recordings overwritten by imports are backed up
in `asset-workshop/backups/`.

Put replacement WAV, Ogg Vorbis or MP3 files in:

- `audio/sfx/<original AudioClip name>.ogg`: replaces that clip wherever Valheim uses ZSFX effects, including mob sounds.
- `audio/music/<music group name>.ogg`: replaces the tracks in that music group while preserving its volume and playback rules.
- `audio/music/clips/<original AudioClip name>.ogg`: replaces just that track, preserving other tracks in the group.

Use just one file per name; names match without regard to capitalization. WAV, OGG and MP3 extensions are interchangeable.
Use Ogg Vorbis for long music files to keep downloads small. Audio is decoded into memory on launch, so keep files reasonably sized.
Shared original clips affect every effect that uses them. This does not replace sounds played through unrelated audio systems.

After entering a world with the updated mod, open the F5 console and use `audio_find troll` (or another search term) to see
effect objects and their clip names. `audio_music` lists music group names; `audio_music forest` filters them.
If the console is unavailable, add `-console` to Valheim's Steam launch options.

Once the files are ready, run `Publish Client Assets.bat` while the server stays running. It refreshes the manifest and
publishes to GitHub, preserving the currently published Thunderstore mod versions. Players must fully exit Valheim and
launch through Folkhemmet/Play.bat to receive the update. Already-running clients keep their current audio until they relaunch.
Sound replacements do not need a server restart. The custom mod DLL must be rebuilt with `custom-mod/build.ps1` only when
its code changes; simply editing/replacing audio files does not need a rebuild.

Sounds without a direct replacement or an entry in `sfx-aliases.cfg` retain their original audio.

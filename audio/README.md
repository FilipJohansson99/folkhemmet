# Custom mob sounds and music

Requested targets: sea serpent (`Serpent`), necks (`Neck`), Ashlands creatures, and Fader (displayed as **Jimpan**).
Use `audio_find serpent`, `audio_find neck`, `audio_find charred`, `audio_find asksvin`, `audio_find morgen`,
`audio_find volture`, `audio_find bonemaw`, and `audio_find fader` to discover related effect objects/clips.
Some effect names differ from the creature's display name, so use more specific clip searches if needed.
The host will supply the MP3 recordings later; no audio content has been chosen or generated.

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

No replacement sounds are included yet. The original game audio remains until edited files are imported and published.

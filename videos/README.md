# Startup intro

`intro.mp4` is the converted version of the host's `Movies/intro.mov`.
The supplied source is a silent portrait video about 7.4 seconds long. Its proportions are preserved with
FitInside (side bars on wide screens), rather than stretching or cropping it.

The mod replaces Valheim's startup cinematic with this local file. Other cinematics and the new-character
flight sequence keep their normal behavior. Playback is skippable with the game's usual controls, and
completion/errors return to the normal menu. The custom intro is shown on a normal fresh game launch even
when the original intro was disabled, without changing the saved skip preference. Direct join/invite launches
still follow the game's own startup rules.

Publish with `Publish Client Assets.bat`. Players fully close Valheim and use the Folkhemmet shortcut to
download the MP4 and updated mod automatically. The dedicated server can stay running.

`[Video] ReplaceStartupIntro = false` disables the replacement; `ShowOnStartup = false` respects the saved
vanilla skip-intro choice. Missing local video files leave the original cinematic in place.

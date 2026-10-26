# Custom sky background - artwork pending

The host will provide PNG artwork later. A complete 360-degree background is ideally a 2:1 equirectangular panorama
(for example, 4096 x 2048), or six square cubemap faces labelled front/back/left/right/up/down.
A normal rectangular image can be adapted later, but may stretch or show a seam if simply wrapped around the sky.

The client asset publisher already includes this folder, so finished sky assets will use the same automatic updates
as textures and audio. Players will not need another ZIP.

No sky override is enabled in the mod yet. Once the artwork is supplied, its projection and rendering must be wired
and checked in-game. Valheim uses its own procedural sky shader, so changing the stars texture alone is insufficient
for an arbitrary custom background. Weather, world lighting, visibility and day/night behavior should be reviewed
when the actual background is integrated.

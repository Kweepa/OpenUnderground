# Unity Underground

A wrapper for a well-known dungeon crawler, with gamepad-driven input or mouse and keyboard, and with 3d models for the objects and creatures.

**You need to have the data files from a GoG installation of the original game** - the game will search for them in standard locations. If it's the ISO version (`game.gog`), the game extracts the data files into a `LooseData` folder under your user save-data path (same area as save games, e.g. AppData\LocalLow on Windows), not next to the GOG install.

To sync from github, you will need to have git LFS (large file storage) installed, since the character textures are stored in LFS. If you are using GitHub desktop it should be installed already automatically.

The game is built with Unity 6000.0.68f1. The root scene is GameDirectoryDialog. Once you have run that scene once you can skip to using World.

I removed a number of store-bought assets before pushing this to github, so it may not work out of the box. Some things I removed were: some audio (a lot of interface clicks); some particles (water splashes mostly I think); and some minor textures and models. I think it should still run, but it might sound sparse and show some pink textures until those are replaced.

**Bug reports and comments are both very welcome.** A bug is easiest to act on as a GitHub issue, where it can be tracked and closed; for anything else - thoughts, questions, or something you would like to see - the [itch.io page](https://kweepa.itch.io/unity-underground) is the friendlier place, and it is where most of the conversation happens anyway.

**Attempts to be gameplay-accurate while adding some modern conveniences**
  - full audio
  - 3d models for critters and objects
  - better lighting and particles
  - some tidied-up portraits
  - achievements
  - gamepad support
  - three ratings added to the inventory panel: attack by the weapon hand, defence and armour by the shield hand
  - clearer attack feedback: the charge cursor stays dark until a release would actually land a swing, then lights up and fills
  - a shrine will not take a skill point when nothing it could raise can rise, and it names only the skills that actually improved


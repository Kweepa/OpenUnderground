# Unity Underground

This project is a wrapper for the well-known 1992 dungeon crawler by Blue Sky Productions and Origin Systems: fully playable from start to finish, with a modern look, gamepad or mouse-and-keyboard controls, and 3D models for the objects and creatures.

![Two skeletons and a chest in a room of the Abyss](Docs/images/room.jpg)

## What's new

**Looks and sound**

  - 3D models for critters and objects
  - better lighting and particles
  - full audio
  - supports external MIDI devices, such as a real MT-32 or the Munt emulator

![Two ghosts by a grave](Docs/images/ghosts.jpg)

**Modern conveniences**

  - gamepad support, with Xbox or PlayStation button prompts, and mouse and keyboard
  - every key and pad button can be changed, with a Classic keys preset for the original's layout; the Controls tab draws the keyboard and the pad with what each key and button does
  - autosave at the beginning of each Abyss level and five quicksave slots, rolling in BG3 style
  - achievements
  - sprinting, bound to the Acrobat skill (be warned: dashing through the level can make you miss secret details)
  - four ratings on the inventory panel: attack and damage by the weapon hand, defence and armour by the shield hand
  - clearer attack feedback: the charge cursor stays dark until a release would actually land a swing, then lights up and fills
  - every few seconds the Search skill looks for you, and points out possible secrets and trapped objects in front of you
  - every few seconds the Track skill tells you of creatures nearby, and in which direction
  - a history of the last hundred messages: Page Up and Page Down, or the d-pad, scroll back through what you missed
  - a list of the spells you know beside the magic panel, with their runes: those in the manual from the start, the others as you find them
  - a locked door unlocks by itself when you use it with the right key in your pack
  - display settings: monitor, display mode and resolution, field of view, camera bob, and how much the interface grows on large screens; it also fits small ones such as a Steam Deck
  - the gameplay helpers, such as automatic Search, Traps and Track reports, sprinting, and items hidden in debris, can be turned off in the game menu if you prefer the original experience

**Faithful to the original**

![A conversation, with the inventory open beside it](Docs/images/conversation.jpg)

  - the levels, objects, conversations, music and cutscenes come from your own copy of the original game
  - its rules as they were: combat, experience, skills, magic, traps and how the creatures behave follow the original

Every change is listed in the [CHANGELOG](CHANGELOG.md).

## Download and play

Get the latest build from the [itch.io page](https://kweepa.itch.io/unity-underground). It runs on Windows, and on Linux or a Steam Deck through Proton.

**You need the original game's data files**: the `game.gog` file of a GoG copy, the `game.bin` file of a Steam copy, or the `CRIT`, `CUTS`, `DATA` and `SOUND` folders of an installed original copy. GoG is the version it is tested with; the original DOS version should work too. The game looks for them in the usual places, and if it does not find them you can point it to the folder. If your copy is the ISO version (`game.gog` or `game.bin`), the game extracts the data into a `LooseData` folder next to your saves (in AppData\LocalLow on Windows), not into the GOG install.

You can play with a gamepad or with mouse and keyboard, and the game switches between them as you use them. Every control is listed in [CONTROLS.md](CONTROLS.md).

Your saves are in `%USERPROFILE%\AppData\LocalLow\Kweepa\UnityUnderground\Saves`.

## Linux and Steam Deck

The Windows build runs through Proton or Wine. It usually runs well; if the frame rate drops badly, add `PROTON_USE_WINED3D=1` to the launch options - in Steam, `PROTON_USE_WINED3D=1 %command%`.

## Bugs and comments

**Bug reports and comments are both very welcome.** A bug is easiest to act on as a GitHub issue, where it can be tracked and closed, and a saved game attached to it helps a lot: one made just before the bug happens, and where the bug leaves a mark on the game, one made after it as well. For anything else - thoughts, questions, or something you would like to see - the [itch.io page](https://kweepa.itch.io/unity-underground) is the friendlier place, and it is where most of the conversation happens anyway.

## For developers

To sync from github, you will need to have git LFS (large file storage) installed, since the character textures are stored in LFS. If you are using GitHub desktop it should be installed already automatically.

The game is built with Unity 6000.0.68f1. The root scene is GameDirectoryDialog. Once you have run that scene once you can skip to using World.

I removed a number of store-bought assets before pushing this to github, so it may not work out of the box. Some things I removed were: some audio (a lot of interface clicks); some particles (water splashes mostly I think); and some minor textures and models. I think it should still run, but it might sound sparse and show some pink textures until those are replaced.

If you would like to contribute, [CONTRIBUTING.md](CONTRIBUTING.md) explains how.

# Contributing

Thanks for wanting to help. This is a small project, so the aim here is to keep a pull request
cheap to review: the less time it takes to see what a change does and why, the sooner it can go
in.

## Getting it running

You need Unity **6000.0.68f1**, git **LFS** (the character textures are stored there), and the data
files from a GOG installation of the original game. No game data is in this repository and none
ever will be: the game reads the originals from disk at runtime.

The root scene is `GameDirectoryDialog`. Run that once so the data files are found, then you can
open `World` directly.

Some store-bought assets present in the released builds were removed before this was published on
GitHub: interface sounds, some particle effects, some minor textures and models. The project
builds and runs without them; the interface is just silent where a click would be. **Please do not
commit replacements if you don't have the right to redistribute them.**

## Pull requests

**One pull request per change, and one commit per change.** A branch that carries a single change
can be taken or turned down on its own; three changes sharing a branch have to be judged together,
and one problem holds up the other two. Branch from `main`, not from another feature branch.

Anyone can open a pull request; that is the door in. People with push access may commit small
changes straight to `main` - docs, tooling, typos, an obvious one-line fix. Anything that changes
how the game plays goes through a pull request even then: not to ask permission, but so that the
reasoning stays somewhere findable, and so that two people working on the same file can see each
other coming. How the maintainer works in his own repository is his call, not this file's.

Please keep a pull request to the files it needs. A few things produce large diffs by accident:

- **Scenes.** Opening a scene is safe, saving it is not: Unity rewrites the file and the diff is
  enormous. If a change needs a value that lives in a scene or a prefab, it is usually better to
  set it from code.
- **`ProjectSettings/`.** Unity likes to touch these on its own. Only include them when the change
  is about them.
- **`Library/`, `Temp/`, `Logs/`, build output.** These are not in the repository and should stay
  out.

## Commit messages

The commit message is what fills in the pull request description, so it is worth writing once,
properly:

```
Area or class: what changes

A short summary: what was wrong, what changes, why. Then the visible
symptom, if there is one.

Details
-------

Everything else: numbers, tables, cases covered, effects on saved games,
what is deliberately left out.
```

For example: `Combat: restore the original's armour and to-hit numbers`.

Keep the subject line **under 72 characters** - GitHub fills the pull request title from it and
cuts it there, moving the rest into the description. Wrap the body at 72-80 columns, which is what
`git log` leaves room for once it has indented the message by four spaces.

The summary should be readable in ten seconds. Put the detail below it, where it gets read only if
someone wants it.

## Changing how the game plays

This is a remake of a specific game, so "what does the original do?" is usually the question that
settles an argument. When a change restores something from the original, saying **where that was
checked** - the data files, the manual, a routine in the executable - is worth more than any
amount of reasoning, and it lets the next person verify it without repeating the work. Put it in a
comment next to the code, not only in the pull request, because pull requests do not travel with
the repository.

Changes that are conveniences rather than restorations are welcome too, but they are easier to
accept when they are optional, or at least when the original behaviour stays available.

## Code

Follow the style of the file you are editing. Name a method with its class when you talk about it -
`Critter.Update()`, not `Update()`, since half a dozen classes have one.

The same goes for the Markdown files: GitHub reflows them when it renders, so how wide the source
lines are is a matter of matching the file you are in, not a rule to apply to the others.

## Reporting bugs

Open a GitHub issue with everything that matters: what happened, what you were doing, the level,
and a save file if you have one. `Player.log` lives next to the saves, under your user save-data
path.

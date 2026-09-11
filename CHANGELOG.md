# Changelog

## Unreleased

### Fixed

- How long a weapon takes to wind up now depends on the weapon alone, as the original has it, and not on the character's skills as well: a trained fighter used to charge a battle axe in under a second, and it now takes the same two it takes a beginner. Every swing also opens with a short raise that no weapon skips and that a release cuts short, so the distance between the lightest weapon and the heaviest is the two to one the original asks for instead of the near-parity it drifted into.

- Creatures now wear the armour their own data gives them, which nothing had been reading, so a blow that lands loses the protection covering the part it struck and one that cannot get through does no harm at all. Fights are harder and longer for it, and closer to the original: most creatures shrug off one to five points of every hit, and a headless turns out to be best protected exactly where it has no head.

- Maximum hit points now grow with the character's level, as the original's do, instead of gaining a fixed step each time. A character with Strength 14 reached level 16 twelve points short - a sixth of the total - and the shortfall grew with every level.

- A projectile the player fires no longer does extra damage for the character's level, which the original adds neither to missiles nor to melee. It was a quarter of the level, so at level 13 a sling stone hit for five where the stone itself is worth two.

- Mantras at a shrine no longer hand out an extra point on the seven skills governed by Strength, the way the original withholds it there and nowhere else. The mana ceiling now uses the game's own formula rather than one from a wiki, and saying the mana mantra no longer tops the pool back up.

- Every skill roll drew from a range one value short of the original's, so every check in the game - to hit, to pick a lock, to cast - was slightly harder than it should have been. Where the difficulty sat a point above the skill, a critical success was not merely rare but impossible.

- The original's game.gog is now unpacked once and reused, instead of being extracted again on every trip through the game-directory dialog. Starting the game while another copy is already running no longer fails on the locked data files.

- Armour no longer makes the player nearly impossible to hit. Creatures marked as elite keep the attack and damage bonus.

- Creatures now choose among their three attacks the way the original does, weighted rather than at random, and the damage of a blow scales with how long it was charged. A goblin's swing is slightly weaker, a mongbat's is nearly twice as strong.

- Armour and rings enchanted with Resist Blows, Thick Skin or Iron Flesh now give the protection they promise. The three never add up - the strongest one wins - which is also how the original behaves.

- Levers, buttons and switches on a wall no longer attach themselves to the face of a door that shares their tile but sits on a different wall. The clearest case was a lever near a secret door on level 3.

- Paying a smith to repair an item now actually repairs it. The item was taken, the money was spent and the item came back untouched.

## First public release

Release UUBUild14Aug2026.zip

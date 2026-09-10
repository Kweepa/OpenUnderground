# Changelog

## Unreleased

### Fixed

- Every skill roll drew from a range one value short of the original's, so every check in the game - to hit, to pick a lock, to cast - was slightly harder than it should have been. Where the difficulty sat a point above the skill, a critical success was not merely rare but impossible.

- The original's game.gog is now unpacked once and reused, instead of being extracted again on every trip through the game-directory dialog. Starting the game while another copy is already running no longer fails on the locked data files.

- Armour no longer makes the player nearly impossible to hit. Creatures marked as elite keep the attack and damage bonus.

- Creatures now choose among their three attacks the way the original does, weighted rather than at random, and the damage of a blow scales with how long it was charged. A goblin's swing is slightly weaker, a mongbat's is nearly twice as strong.

- Armour and rings enchanted with Resist Blows, Thick Skin or Iron Flesh now give the protection they promise. The three never add up - the strongest one wins - which is also how the original behaves.

- Levers, buttons and switches on a wall no longer attach themselves to the face of a door that shares their tile but sits on a different wall. The clearest case was a lever near a secret door on level 3.

- Paying a smith to repair an item now actually repairs it. The item was taken, the money was spent and the item came back untouched.

## First public release

Release UUBUild14Aug2026.zip

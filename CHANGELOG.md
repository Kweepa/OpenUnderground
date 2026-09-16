# Changelog

## Current release

- A blow that lands on a back now counts for more, the way the original counts it: up to four points on the chance to hit and four on the damage, tailing off to nothing when the two of you are face to face. It works both ways, so something that catches you looking elsewhere hits harder, and so do you when you come up behind something that has not turned round yet.

- Enchanted items keep working when the game text is in another language. A saved game used to remember an enchantment by the name it printed, and the game recognised it by reading that name back, so putting a translated strings file in the data folder left every potion, wand and enchanted ring inert - which is what players running the French text were seeing.

- Garamon's empty grave is named as his in any language, not only in English.

- Potions found in debris no longer merge into one pile. Picking up two with different effects used to leave you with two of the first kind, and the second one gone.

- Quicksaving, on F5 or by holding the right stick click on a gamepad. Five quicksaves are kept and the sixth takes the place of the oldest, so saving before you open a suspicious door never costs you the save from an hour ago.

- The game now saves by itself the first time you set foot on a level, a new character's first level included. Rows read "Autosave - Cabirus - Lvl 3" and "Quick 01 - Cabirus - Lvl 4", lined up in a column, so a glance down the list tells you where each save came from and how deep you were. Quicksaving never overwrites a level's own save.

- The save and load lists show every save you have, newest first, and scroll with the wheel, so eight saves is no longer the limit. Journey Onward opens on your most recent game with the pointer already on it, which is almost always the one you want.

- The load screen is laid out properly. The list of saves and the preview of the selected one sit side by side, starting on the same line, centred on the screen, and they keep their shape and their proportions at any window size. The preview used to sit low and to the right, half over the list, and it drifted as the window changed.

- A new character starts at level 1 again. Rolling one up without closing the game down first left him at whatever level the character before had reached, and with the hit points and the experience thresholds that go with it.

- The armour rating in the pack now counts each piece once for every part of you it covers, so a shield counts for the torso and the arms both instead of once. It also carries the shield spells, which used to swell the defence figure next to it instead.

- Resist Blows, Thick Skin and Iron Flesh take points off the damage instead of making you harder to hit, and they are worth what the original pays for them: 2, 3 and 5. Iron Flesh used to make two swings in three miss outright, which is not a shield spell so much as a suit of invisible plate.

- A repair that goes badly wrong now destroys the item, as it does in the original. Taking a worn long sword to an anvil with no skill at it is close to throwing it away - eight attempts in ten ended with the sword gone - where before the bad roll did nothing at all.

- Repairing takes time. A battered item in unskilled hands costs well over an hour of it, so an evening spent at the anvil leaves you hungry and tired, and a spare weapon is worth carrying again.

- The anvil reads a weapon's toughness from the weapon table instead of the armour one. Nine melee weapons had the wrong repair difficulty - a broadsword was the easiest thing in the game to mend and is now among the hardest - and bows, crossbows and slings, which never wear out, are no longer offered for repair at all.

- The anvil no longer turns away an item just because it is still in fair condition - only one at full quality, where a repair could do nothing but cost you the item.

- The anvil tells you what you are getting into. Its five words used to cover uneven slices of the odds, so "hard" meant anything from a fair bet to one try in thirty, and a broadsword and a long sword got the same word while one was three times likelier to survive. There are six words now, cut on the actual chance of success, and the worst of them is "next to impossible" because some repairs are. It also warns you when you might ruin the item, which it never did before - a battered broadsword in unskilled hands now reads as "next to impossible and very risky", which is exactly what it is.

- Common coins and gold coins can be told apart at last: the common ones are silver now, in the pack and on the floor. Both used to be drawn with the same two pictures and the same gold model, so a purse of silver and a purse of gold looked the same either way.

- With auto jump turned on the jump now goes off when you press the key rather than when you let it go, and the edge of a gap still takes you over by itself - running at it is enough now, where before you had to be holding the key down as well.

- Keys now carry the level they were found on in their name. A dozen of them look alike by the middle of the game, and the level is what tells you which ones belong to doors you are never going back to.

- Create Food no longer turns half of everything it makes into fish. All seven foods are equally likely, the way the original draws them, and what it makes is as fresh as food gets.

- A creature's eyes only glow as far as your own light reaches, and a couple of paces past it, since eyes shine by throwing light back. They used to shine right down an unlit corridor, so a pair of them gave a creature away long before anything else could, and putting your torch out made no difference. A creature that is a light in its own right, like the fire elemental, still shows.

- A door will not close on you. Standing in a doorway while something shuts it - a lever, a sleeping spell, a creature squeezing past - now pushes it open again, which is what the original does. The room with the tomb on level four has both its switches out in the corridor, so a door that shut behind you there left you with nothing to open it with.

- The game pauses when its window loses focus, by opening the save screen the way Escape does. Alt-tabbing used to leave hunger, fatigue, poison and every creature running while you were somewhere else.

- The magic panel closes when a spell is cast, and the inventory when a conversation ends. And C casts with the panel open, which it used not to: the one screen showing the runes you are about to cast was the one place the cast key did nothing. Bartering used to leave the pack open, and the panel used to sit over the half of the screen you needed to aim a fireball at.

- The runes laid out ready to cast survive a save and a reload. You used to find the shelf empty and have to lay the spell out again.

- How long you can sprint, and how quickly your wind comes back, now depend on the Acrobat skill: eight seconds of running and twenty to recover for an untrained character, forty and ten for a trained one, where it used to be ten and twenty for everyone. Acrobat had nothing to its name but the fall it saves you from.

- The flute plays an octave higher, in the register a flute actually sings in, and the whole scale is now one instrument. Four of the ten notes were overblown takes, thin and reedy, so a tune sounded like several players taking turns.

- The exploring music no longer runs wall to wall. A stretch of quiet is left between one track and the next, and after a fight, so the next track lands instead of just carrying on. Options sets how long that quiet can get, and setting it to zero puts the music back the way it was.

- The title music is no longer cut off the moment you start playing. It plays out first, whether you made a new character or carried an old one on, and the exploring music takes over when it ends. That is what the original does with it.

- If you also own the sequel, its ambient tracks can join the rotation. They live inside `game.gog`, which despite the extension is a plain ISO9660 image that 7-Zip opens directly; the tracks are under `UW2\SOUND`. Copy both the `UWA` and the `UWR` version of each track into a folder called `UW2` inside `%USERPROFILE%\AppData\LocalLow\Kweepa\UnityUnderground\LooseData\UW\Sound`. Only these ten are recommended: 01, 05, 11, 12, 13, 14, 15, 16, 17 and 30.

- The automap now leaves the music playing rather than cutting in with its own track, which also stops the exploring track restarting from the top on the way out. Its track joins the exploring rotation instead: nothing else ever plays it, so it was the one piece of the score you never heard. Options has a switch for the old behaviour.

- The music now follows how a fight is going. Two tracks from the original that nothing ever played are back: one for when you have something down to its last quarter, one for when you are. The plain combat track keeps the middle, and the score only changes every few seconds so it does not flap from blow to blow.

- Something noticing you no longer sounds the same as something fighting you. A creature that hears you through a wall now starts a warning, and the combat music waits until one of you actually swings. Holding your weapon up keeps the warning going, which is what the original used that track for.

- The track that used to play when you gained a level is the original's combat music, so it has gone back where it belongs. Gaining a level plays the victory music instead.

- Sleeping and talking have their own music again, the track the original plays for both. It is the one this remake already used for the automap, and that stays as it is.

- A victory fanfare is no longer cut off when the fight is not over. Killing one of three used to chop it in half; now it finishes, and the music goes back to the fight rather than out to the corridors.

- A piece *of Protection* now makes you harder to hit on the part it covers, instead of soaking damage there, which is what the original does with it. Ten pieces in the game carry the enchantment, from the buckler of Minor Protection on level two to the breastplate of Tremendous Protection on level eight. The panel follows: they leave the armour number, since they no longer stop damage, and show up in the defence one instead, averaged over the four body parts because that is where they now act.

- A piece *of Toughness* now soaks damage on the part it covers, as the original has it. It used to do nothing in a fight at all, only making the piece itself wear out more slowly, so the enchantment never showed. Eight pieces in the game carry it, up to the leather cap of Unsurpassed Toughness on level eight, which is worth eight points off every blow that lands on your head.

- A blow now picks where it lands once. It used to choose a body part for the attack and then choose again for the armour, so the piece that was rolled against was not always the piece that took the hit.

- The weapon hand of the inventory panel now shows two numbers instead of one, the way the other hand already shows defence and armour: on the left the score your swing is rolled with, on the right the largest damage that swing can do. The single number it used to show was the damage.

- Putting the pointer on a hand now names its two numbers at the foot of the panel: attack and max damage for the weapon hand, defence and armour for the other.

- The inventory numbers no longer give away enchantments you have not identified. An enchanted weapon, ring or piece of armour works from the moment you wear it, but its bonus now stays out of the figures until a Lore roll names the enchantment, so the panel no longer tells you what your Lore has not.

- The jeweled sword on level six no longer ends up in the lava when the level loads, so it can be picked up instead of being lost.

- The three group mantras at a shrine now advance as many skills as the original advances them: three for SUMM RA and four for OM CAH, where every group mantra used to advance two. OM CAH is once more the most generous thing a shrine offers, instead of being worth less than chanting a single skill's mantra.

- A shrine no longer says it has improved skills that it has not, and it will not take the point when there is nothing in the group left to raise. MU AHM now leans towards Mana while Mana is still low, as the original has it.

- A new character starts with one skill point instead of three, which is what the original grants.

- A punch now takes its damage from the Unarmed skill, which had no effect on it at all. A trained brawler's fist is worth three and a half times what it was, an untrained one half again as much, so bare hands are a way to fight rather than a last resort.

- Casting a spell now rolls the way the original rolls it: five points are added to the skill, and the roll is against twice the spell's circle rather than against its mana cost. Spells go off far more often - an eighth-circle spell at full Casting never fails now, where it failed a third of the time - and the cheapest spells can no longer backfire at any skill, which in the original they never could.

- A spell no longer costs half its mana when the roll comes out a critical success. The original charges the full price however well the roll went, and at high Casting nearly every cast is a critical success, so most of a practised mage's spells were going off at half price.

- Landing a critical hit now doubles the damage half the time, the way a critical hit from a creature already did. Against anything you comfortably outclass most of your blows are critical, so for a trained character this reads as about half again on melee damage rather than an occasional flourish.

- A missile now does the damage its own row in the game's data gives it, instead of a hand-written number, and the Missile skill multiplies that damage rather than being added to it. Archery is a little weaker at the top and a spell is much stronger everywhere: a fireball averaged seven points for a new mage and fourteen for a practised one, where the original asks for sixteen and a half from both.

- Spells no longer get a damage bonus from the caster's skill, which the original gives to neither spells nor arrows. A fireball is a fireball whoever throws it; what the skill buys is the chance of throwing one at all.

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

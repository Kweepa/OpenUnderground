using System.Collections.Generic;

public class ObjectsData
{
    public struct MeleeData
    {
        //0000   Int8   damage modifier for Slash attack
        //0001   Int8   damage modifier for Bash attack
        //0002   Int8   damage modifier for Stab attack
        //0003   int8   used in attack charge calculations  (min charge?)
        //0004   int8   attackSpeed - how quickly an attack charge builds up.
        //0005   int8   used in attack charge calculations  (max charge?)
        //0006   Int8   skill type (3: sword, 4: axe, 5: mace, 6: unarmed)
        //0007   Int8   durability	
        public short Slash;
        public short Bash;
        public short Stab;
        public short MinCharge;
        public short WeaponSpeed;
        public short MaxCharge;
        public short Skill;
        public short Durability;
    };

    public struct RangedData
    {
        public int ammo;
        public int unk;
        public int damage;
    }

    public struct ArmourData
    {
        /*0000   Int8   protection
        0001   Int8   durability
        0002   Int8   unknown
        0003   Int8   category:
        00: shield
        01: body armour
        03: leggings
        04: gloves
        05: boots
        08: hat
        09: ring*/
        public short protection;
        public short durability;
        public short unk;
        public short category;
    }

    public struct ContainerData
    {
        /** Containers table (0x0080-0x008f)

        0000   Int8   capacity in 0.1 stones
        0001   Int8   objects accepted; 0: runes, 1: arrows, 2: scrolls, 3: edibles, 0xFF: any
        0002   Int8   number of slots available?; 2: , -1: any
        */

        public int capacity;
        public int objectsMask;
        public int slots;
    }

    public struct LightSourceData
    {
        //* Light source table (0x0090-0x009f)
        public int brightness;
        public int duration;
        //0000   Int8   light brightness (max. is 4; 0 means unlit)
        //0001   Int8   duration (00: doesn't go out, e.g. taper of sacrifice)
    }


    public class CritterData
    {
        /*
00h 	1 	uint8 	Level: 	Level of the creature.
01h 	3 	 ?? 	 ?? 	 ??
04h 	1 	uint8 	HitPoints 	Average hit points. <is this meant to be uint8
05h     1    uint8 Strength  - for the player these values are copied at save load into the table.
06h 	1 	uint8 	Dexterity
07h 	1 	 uint8	 Intelligence
08h 	1 	uint8 	FluidAndRemains 	A combination of remains after death and the type of blood splatters this produces.
Mask 0x0F is the splatter type, 0 for dust, 8 for red blood. 
Mask 0xF0 is the remains; Nothing = 0x00, RotwormCorpse = 0x20, Rubble = 0x40, WoodChips = 0x60, Bones = 0x80, GreenBloodPool = 0xA0, RedBloodPool = 0xC0, RedBloodPoolGiantSpider = 0xE0.
09h 	1 	uint8 	GeneralType 	An index into the strings on page 8, offset 370. This string is the generic name for the creature, like "a creature" for "a goblin" or "a rat" for "a giant rat".
0Ah 	1 	uint8 	Passiveness 	Relative passiveness. 255 will never take a swing at you, even if you kill them.
0Bh 	1 	magic related to the critter having extra/specific spells.   ?? 	 ?? 	 ??
0Ch 	1 	uint8 	MovementSpeed 	Speed of movement; 0 is immobile, maxes out at 12 for vampire bat.
0Dh 	2 	 ?? 	 ?? 	 ??
0Fh 	1 	uint8 	PoisonDamage 	Amount of poison damage this is capable of on attack.
10h 	1 	uint8 	Category 	Ethereal = 0x00 (Ethereal critters like ghosts, wisps, and shadow beasts), Humanoid = 0x01 (Humanlike non-thinking forms like lizardmen, trolls, ghouls, and mages), Flying = 0x02 (Flying critters like bats and imps), Swimming = 0x03 (Swimming critters like lurkers), Creeping = 0x04 (Creeping critters like rats and spiders), Crawling = 0x05 (Crawling critters like slugs, worms, reapers (!), and fire elementals (!!)), EarthGolem = 0x11 (Only used for the earth golem), Human = 0x51 (Humanlike thinking forms like goblins, skeletons, mountainmen, fighters, outcasts, and stone and metal golems).
11h 	1 	uint8 	EquipmentDamage 	Amount of equipment damage this is capable of on attack.
12h 	1 	 ?? 	 ?? 	 ??
13h 	9 	Probability[3] 	Probabilities 	Each has the form (uint16 value, uint8 percent). What this means is unknown.
1Ch 	12 	 ?? 	 ?? 	 ??
28h 	2 	uint16 	Experience: 	Experience provided when killed.
2Ah 	5 	 ?? 	 ?? 	 ?? list of spells. Looks like 3 values?
2Dh     Some sort of value (magic users related)
2Fh 	1 	uint8 	 ?? 	Always 73.
*/
        public int Level;
        public byte unk01;
        public byte unk02;
        public byte unk03;
        public short AvgHit;//Is this defence?????
        public int Strength;
        public int Dexterity;
        public int Intelligence;
        public int Remains;
        public int Blood;
        public int Race;
        public int Passive;
        public int Speed;
        public byte unk0c;
        public int TradeLevel;
        public int TradeAppraisal;
        public int TradeThreshold;
        public int TradePatience;
        public int Poison;
        public int Category;
        public int EquipDamage;
        public int Defence;
        //public int ProbValue1;
        public int[] AttackChanceToHit; // What defence rolls against to save against this attack
        public int[] AttackDamage; // the damage value of the chosen attack.
        public int[] AttackProbability; // Probability of which attack/animation to execute

        public byte DetectionRange;
        public byte unk1d;
        public byte unk1e;
        public byte TheftDetectionRange;
        public byte unk1f;

        public List<EObjectType> Loot;  // A list of item ids that the Npc drops on death or uses in bartering

        public byte treasureLoot;
        public byte foodLoot;

        public int Exp;

        // Spells this critter can cast. -1 is no spell.
        public int[] Spells;

        public byte unk2D;
        public byte unk2E;
        public byte unk2f;
    };

    public struct NutritionData
    {
        public int FoodValue;
    }
    
    public MeleeData[] weaponStats = new MeleeData[16];
    public RangedData[] rangedStats = new RangedData[8];
    public ArmourData[] armourStats = new ArmourData[32];
    public ContainerData[] containerStats = new ContainerData[16];
    public LightSourceData[] lightSourceStats = new LightSourceData[8];
    public CritterData[] critterStats = new CritterData[64];

    public NutritionData[] nutritionStats = new NutritionData[16];

    public ObjectsData(string filename)
    {
        Stream stream = new Stream(filename);

        stream.Skip(2); // skip header

        for (int i = 0; i < weaponStats.Length; ++i)
        {
            weaponStats[i].Slash = stream.GetByte();
            weaponStats[i].Bash = stream.GetByte();
            weaponStats[i].Stab = stream.GetByte();
            weaponStats[i].MinCharge = stream.GetByte();
            weaponStats[i].WeaponSpeed = stream.GetByte();
            weaponStats[i].MaxCharge = stream.GetByte();
            weaponStats[i].Skill = stream.GetByte();
            weaponStats[i].Durability = stream.GetByte();
        }
        
        for (int i = 0; i < rangedStats.Length; ++i)
        {
            rangedStats[i].damage = stream.GetByte();
            stream.Skip(2);
        }

        for (int i = 0; i < rangedStats.Length; ++i)
        {
            rangedStats[i].damage = stream.GetByte();
            rangedStats[i].unk = stream.GetByte();
            rangedStats[i].ammo = stream.GetByte() + 16; // index into ranged table
        }

        for (int i = 0; i < armourStats.Length; ++i)
        {
            armourStats[i].protection = stream.GetByte();
            armourStats[i].durability = stream.GetByte();
            armourStats[i].unk = stream.GetByte();
            armourStats[i].category = stream.GetByte();
        }

        for (int i = 0; i < critterStats.Length; ++i)
        {
            critterStats[i] = new CritterData();
            critterStats[i].Level = stream.GetByte();
            critterStats[i].unk01 = stream.GetByte();
            critterStats[i].unk02 = stream.GetByte();
            critterStats[i].unk03 = stream.GetByte();
            critterStats[i].AvgHit = stream.GetByte();//Average Hitpoints - changed from uint16 to uint8

            critterStats[i].Strength = stream.GetByte(); //Base damage calculations
            critterStats[i].Dexterity = stream.GetByte();// attackscore calculations
            critterStats[i].Intelligence = stream.GetByte(); //need to id usages. probably magic spell attacks

            int remainsAndBlood = stream.GetByte();
            critterStats[i].Remains = (remainsAndBlood & 0xE0) >> 5;//Remains body
            critterStats[i].Blood = remainsAndBlood & 0x1F;//Remains blood

            critterStats[i].Race = stream.GetByte();//Uwformats calls this General Type

            critterStats[i].Passive = stream.GetByte();//Passiveness
            critterStats[i].Defence = stream.GetByte();//Defence
            critterStats[i].Speed = stream.GetByte();//Speed
            int tradeLevelAndAppraisal = stream.GetByte();
            critterStats[i].TradeLevel = tradeLevelAndAppraisal & 0xf;
            critterStats[i].TradeAppraisal = tradeLevelAndAppraisal >> 4;
            int tradeThresholdAndPatience = stream.GetByte();
            critterStats[i].TradeThreshold = tradeThresholdAndPatience & 0xf;
            critterStats[i].TradePatience = tradeThresholdAndPatience >> 4;
            critterStats[i].Poison = stream.GetByte() & 0xF;//Poison Damage
            critterStats[i].Category = stream.GetByte();//& 0x1F);//Category
            critterStats[i].EquipDamage = stream.GetByte();//Equipment damage
            critterStats[i].Defence = stream.GetByte();

            critterStats[i].AttackChanceToHit = new int[3];
            critterStats[i].AttackDamage = new int[3];
            critterStats[i].AttackProbability = new int[3];

            for (int j = 0; j < 3; ++j)
            {
                critterStats[i].AttackChanceToHit[j] = stream.GetByte();
                critterStats[i].AttackDamage[j] = stream.GetByte();
                critterStats[i].AttackProbability[j] = stream.GetByte();
            }

            critterStats[i].DetectionRange = stream.GetByte();
            critterStats[i].unk1d = stream.GetByte();
            critterStats[i].unk1e = stream.GetByte();
            critterStats[i].TheftDetectionRange = (byte)(critterStats[i].unk1e >> 4);
            critterStats[i].unk1f = stream.GetByte();

            critterStats[i].Loot = new List<EObjectType>();

            for (int j = 0; j < 2; ++j)
            {
                int byte1 = stream.GetByte();
                if ((byte1 & 1) == 1)
                {
                    critterStats[i].Loot.Add((EObjectType)(byte1 >> 1));
                }
            }

            for (int j = 0; j < 2; ++j)
            {
                int byte1 = stream.GetUShort();
                if (byte1 != 0)
                {
                    critterStats[i].Loot.Add((EObjectType)(byte1 >> 4));
                }
            }

            critterStats[i].treasureLoot = stream.GetByte();
            critterStats[i].foodLoot = stream.GetByte();

            critterStats[i].Exp = stream.GetUShort();
            
            critterStats[i].Spells = new int[8];

            for (int k = 0; k < 3; ++k)
            {
                critterStats[i].Spells[k] = stream.GetByte();
            }

            critterStats[i].unk2D = stream.GetByte();
            critterStats[i].unk2E = stream.GetByte();
            critterStats[i].unk2f = stream.GetByte();
        }
        
        for (int i = 0; i < containerStats.Length; ++i)
        {
            containerStats[i].capacity = stream.GetByte();
            containerStats[i].objectsMask = stream.GetByte();
            containerStats[i].slots = stream.GetByte();
        }

        for (int i = 0; i < lightSourceStats.Length; ++i)
        {
            lightSourceStats[i].duration = stream.GetByte();
            lightSourceStats[i].brightness = stream.GetByte();
        }

        stream.Seek(0xd82);

        for (int i = 0; i < nutritionStats.Length; ++i)
        {
            nutritionStats[i].FoodValue = stream.GetByte();
        }
    }
}

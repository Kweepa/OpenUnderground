using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// The Rune of Warding: a one-shot trap that a hostile creature sets off, taking a blow and warning
/// the player.
/// </summary>
/// <remarks>
/// In the original the spell leaves a move trigger and a ward trap on a tile (UW.EXE 0x84c0a), with
/// no limit on how many: nothing looks for an earlier rune. The trigger answers creatures only and
/// once (bit 12 of its first word set, bits 11 and 10 clear, 0x83b3b); the trap takes
/// 3 + rand() * Casting / 32768 hit points from the creature as a blow with no attacker, prints
/// message 245 and the direction from the player to the creature, and then both are deleted
/// (0x846e3).
///
/// Three things are ours. The rune is drawn, where the original's is invisible. It reaches 2 m all
/// round, where the original's needs the creature's centre within 3/8 of a tile of its point, about
/// 1.1 m: that small, it is walked past. And only a hostile creature sets it off, where in the
/// original any creature does, the player's allies too.
/// </remarks>
public class RuneOfWarding : UUObject
{
    /// <summary>
    /// How far from the rune a creature has to come to set it off, in metres.
    /// </summary>
    private const float disturbanceRadius = 2.0f;

    /// <summary>The fewest hit points the rune takes; the player's Casting adds 0 to Casting - 1.</summary>
    private const int minimumDamage = 3;

    private float timeBetweenChecks;

    private readonly Collider[] cachedColliders = new Collider[8];

    public Transform runeTransform;

    public override void Update()
    {
        timeBetweenChecks -= Time.deltaTime;
        if (timeBetweenChecks < 0.0f)
        {
            timeBetweenChecks += Random.Range(0.3f, 0.5f);

            int count = Physics.OverlapSphereNonAlloc(transform.position, disturbanceRadius, cachedColliders, 1 << LayerMask.NameToLayer("Characters"));
            for (int i = 0; i < count; ++i)
            {
                Collider col = cachedColliders[i];
                Critter critter = col.transform.root.gameObject.GetComponent<Critter>();
                if (critter != null && critter.hp > 0 && critter.attitude == Critter.EAttitude.Hostile && !critter.playerAlly)
                {
                    SetOff(critter);
                    return;
                }
            }
        }

        if (runeTransform != null)
        {
            runeTransform.SetLocalPositionAndRotation(0.2f * Mathf.Sin(Time.time) * Vector3.up, Quaternion.identity);
        }
    }

    private void SetOff(Critter critter)
    {
        // "Your Rune of Warding has been set off " + the creature's direction from the player
        string setOffMessage = StringLoader.GetString(1, 245);
        Vector3 off = critter.transform.position - PlayerObject.Player.transform.position;
        int octant = Utils.OffsetToOctant(off);
        setOffMessage += StringLoader.GetString(1, 36 + octant) + ".";
        Messages.Add(setOffMessage);

        int damage = minimumDamage + Random.Range(0, Skills.GetSkill(ESkill.Casting));
        critter.TryDamageWithoutAttacker(damage, Resistance.Blow);

        Utils.DestroyItem(this);
    }
}

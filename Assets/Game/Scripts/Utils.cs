using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

public enum EDamageType
{
    Damage,
    Drowning,
    Lava,
    Poison,
    Direct, // bypasses armor (eg hunger, fatigue)
}

public enum EPoisonRating
{
    Barely,
    Mildly,
    Badly,
    Seriously,
    Egregiously
}

/// <summary>
/// Layer mask for physics queries that need to hit environment geometry (floor, walls, ceiling, door frames).
/// </summary>
public static class LayerMasks
{
    public static int EnvironmentAndCeiling =>
        (1 << LayerMask.NameToLayer("Environment")) | (1 << LayerMask.NameToLayer("Ceiling"));
    
    /// <summary>
    /// Layer mask for environment without ceiling (for downward/horizontal raycasts that shouldn't hit ceiling).
    /// </summary>
    public static int EnvironmentOnly => 1 << LayerMask.NameToLayer("Environment");
}

// Smaller enum value => lower GUI.depth in Unity IMGUI => drawn on top.
public enum EGUIDepth
{
    PlayerDataDebug,
    ShowObjectsDebug,
    CritterViewer,
    CritterDebug,
    SoftwareCursorOverlay,
    Achievements,
    FrontEnd,
    Credits,
    Cutscene,
    Keyboard,
    CreateCharacter,
    Logos,
    EndGame,
    WinScreen,

    SaveLoad,
    PlayerFade,
    Map,
    Compass,
    HowMany,
    RepairDialog,
    Inventory,
    Conversation,
    CritterHealth,
    Flute,
    Shrine,
    Crosshair, // interaction
    Tutorial,
    Magic,
    Stats,
    Messages,
    KeyFloater,
    WeaponChargeGem,
}

[System.Flags]
public enum EControlMask
{
    Inventory = 1,
    Magic = 2,
    Conversation = 4,
    Map = 8,
    Cutscene = 16,
    Flute = 32,
    RoamingSight = 64,
    EnterMoongate = 128,
    HowMany = 256,
    RepairDialog = 512,
    Resting = 1024,
    SaveLoad = 2048,
    Keyboard = 4096,
}

public class Utils
{
    private static readonly Collider[] cachedColliders = new Collider[32];
    
    public static float DampedApproachUnscaledTime(float current, float target, float time)
    {
        float threshold = 0.99f; // the value will reach 99% of the 'target' in 'time'
        float factor = (time > 0.0f) ? (1.0f - Mathf.Pow(1.0f - threshold, Time.unscaledDeltaTime / time)) : 1.0f;
        return Mathf.Lerp(current, target, factor);
    }
    
    public static float DampedApproach(float current, float target, float time)
    {
        float threshold = 0.99f; // the value will reach 99% of the 'target' in 'time'
        float factor = (time > 0.0f) ? (1.0f - Mathf.Pow(1.0f - threshold, Time.deltaTime / time)) : 1.0f;
        return Mathf.Lerp(current, target, factor);
    }

    public static Vector3 DampedApproach(Vector3 current, Vector3 target, float time)
    {
        return new Vector3(DampedApproach(current.x, target.x, time), DampedApproach(current.y, target.y, time),
            DampedApproach(current.z, target.z, time));
    }

    /// <summary>
    /// Transforms a world position to local space ignoring scale, considering only position and rotation.
    /// </summary>
    /// <param name="transform">The transform to use as reference</param>
    /// <param name="worldPosition">The world space position to transform</param>
    /// <returns>The local position ignoring scale</returns>
    public static Vector3 InverseTransformPointNoScale(Transform transform, Vector3 worldPosition)
    {
        Vector3 relativePosition = worldPosition - transform.position;
        return Quaternion.Inverse(transform.rotation) * relativePosition;
    }

    private static Color[] blackColor;
    private static Texture2D black;

    public static void DrawFade(float fadeAlpha)
    {
        if (fadeAlpha > 0.0f)
        {
            if (black == null)
            {
                black = new(1, 1);
                blackColor = new[] { Color.black };
            }
            blackColor[0].a = fadeAlpha;
            black.SetPixels(blackColor);
            black.Apply();
            GUI.DrawTexture(Screen.safeArea, black, ScaleMode.StretchToFill, true);
        }
    }

    public static void RetintImages(Texture2D[] imageList, int[] indices, Color multiplier)
    {
        foreach (int i in indices)
        {
            Texture2D image = imageList[i];
            Color[] cols = image.GetPixels();
            for (int c = 0; c < cols.Length; ++c)
            {
                cols[c] *= multiplier;
            }
            image.SetPixels(cols);
            image.Apply();
        }
    }
    
    public static void DitherImages(Texture2D[] imageList, int[] indices, float multiplier)
    {
        foreach (int i in indices)
        {
            Texture2D image = imageList[i];
            Color[] cols = image.GetPixels();
            for (int y = 0; y < image.height; ++y)
            {
                for (int x = 0; x < image.width; ++x)
                {
                    int c = y * image.width + x;
                    float m = (((x ^ y) & 1) > 0) ? 1.0f - multiplier : 1.0f + multiplier;
                    cols[c].r *= m;
                    cols[c].g *= m;
                    cols[c].b *= m;
                }
            }
            image.SetPixels(cols);
            image.Apply();
        }
    }

    /// <summary>
    /// Which body part a blow arriving at <paramref name="strikeHeight"/> lands on, for a target
    /// standing between <paramref name="foot"/> and <paramref name="head"/>. The original compares
    /// the middle of the swing with the middle of what it is hitting (UW.EXE 0x2441a): below the
    /// feet is legs, above the head is head, and in between the split leans low or high depending
    /// on which side of the middle the blow arrived on. So a rotworm goes for the legs and an imp
    /// for the head, and the protection that answers is the one covering that part.
    /// One routine picks the part whoever is swinging: the melee path reaches it at 0x24a2e and
    /// the missile path at 0x25988, and both then read the protection at 0x24dc1. That is why the
    /// player and a creature ask the same question here.
    /// With <paramref name="headInReach"/> false the head is never the answer, and what would have
    /// been the head goes to the torso and the arms in their usual two to one. That is ours, not
    /// the original's: a creature small enough to be jumped over cannot reach a man's head.
    /// </summary>
    public static EBodyPart PickBodyPart(float strikeHeight, float foot, float head, bool headInReach = true)
    {
        if (strikeHeight < foot)
        {
            return EBodyPart.Legs;
        }

        if (strikeHeight > head && headInReach)
        {
            return EBodyPart.Head;
        }

        if (strikeHeight < 0.5f * (foot + head))
        {
            if (Random.Range(0, 2) == 0)
            {
                return EBodyPart.Legs;
            }
        }
        else if (headInReach && Random.Range(0, 3) == 0)
        {
            return EBodyPart.Head;
        }

        return Random.Range(0, 3) == 0 ? EBodyPart.Arms : EBodyPart.Torso;
    }

    /// <summary>
    /// How tall a melee swing is. The original sweeps a span, not a point: (2 * reach + 1) * 4
    /// units of height from the swing's base (UW.EXE 0x248a8), and the reach is 2 for every
    /// weapon, for the fist and for every creature (0x253e3 reads it from COMOBJ.DAT, 0x259f4
    /// sets it for a creature), so the span is 20 units for everyone.
    /// </summary>
    public const float SwingSpan = 20 * UUObject.yScale;

    /// <summary>
    /// The height a swing starting at <paramref name="swingBase"/> is judged at: the middle of its
    /// span, which is what the original compares with the middle of the target to pick a body part
    /// (0x2441a gets the base and the top of the span).
    /// </summary>
    public static float GetSwingMiddle(float swingBase)
    {
        return swingBase + 0.5f * SwingSpan;
    }

    /// <summary>
    /// Whether a swing starting at <paramref name="swingBase"/> reaches a body standing between
    /// <paramref name="feet"/> and <paramref name="top"/>. The original only lets a blow land on
    /// what its span overlaps in height, top above the base and bottom below the top of the span
    /// (UW.EXE 0x2934f). A creature's swing is held to it; the player's is aimed, see
    /// <see cref="AimAtBody"/>.
    /// </summary>
    public static bool SwingReaches(float swingBase, float feet, float top)
    {
        return top > swingBase && feet < swingBase + SwingSpan;
    }

    /// <summary>
    /// Where on a body the player's blow lands: below this share of its height the legs, above
    /// <see cref="AimHeadAbove"/> the head, and the torso or the arms in between. See
    /// <see cref="AimAtBody"/> and <see cref="AimArmsShare"/>.
    /// </summary>
    public const float AimLegsBelow = 0.5f;

    /// <inheritdoc cref="AimLegsBelow"/>
    public const float AimHeadAbove = 0.85f;

    /// <summary>
    /// Of the blows aimed between the legs and the head, the share that the arms take, since a body
    /// holds them in front of itself. The rest land on the torso.
    /// </summary>
    public const float AimArmsShare = 0.6f;

    /// <summary>
    /// How much of its height a body is stretched by, below the feet and above the head, for a
    /// player on a gamepad to aim at: a little help, since a stick is slower to put the crosshair
    /// on a rat than a mouse. With the mouse there is none.
    /// </summary>
    public const float AimMargin = 0.05f;

    /// <summary>
    /// Where the player's line of sight meets a body, as a share of its height: 0 at the feet, 1
    /// at the top. The line starts at <paramref name="eye"/> and runs along
    /// <paramref name="forward"/>, which is of unit length; the body is an upright cylinder around
    /// <paramref name="axis"/>, of <paramref name="radius"/>, from <paramref name="bottom"/> to
    /// <paramref name="top"/>. The line is taken where it enters the cylinder's side, or where it
    /// passes nearest to its axis if it goes by to one side, since which thing is struck is chosen
    /// by the caller; and where the side is missed above or below, where it comes in through the top
    /// or the bottom, which is what looking down on a body from above, or up at one from below,
    /// does. <paramref name="point"/> is that place. False when it passes over or under the body,
    /// stretched by <paramref name="margin"/> of its height either way: the blow then meets nothing.
    /// </summary>
    /// <remarks>
    /// This is ours. The original judges a blow from a band of height swept by the swing, set by
    /// the row of the screen the attack starts in and the tilt of the view (UW.EXE 0x24876), and
    /// lets it land only on what the band overlaps (0x2934f). The idea that a blow can pass over
    /// or under what it is aimed at is kept; the band is not.
    /// </remarks>
    public static bool AimAtBody(Vector3 eye, Vector3 forward, Vector3 axis, float radius, float bottom, float top,
                                 float margin, out float share, out Vector3 point)
    {
        share = 0.0f;
        point = eye;
        if (top <= bottom)
        {
            return false;
        }

        float height = top - bottom;
        Vector2 across = new Vector2(forward.x, forward.z);
        float across2 = across.sqrMagnitude;
        if (across2 >= 1e-4f)
        {
            Vector2 toAxis = new Vector2(axis.x - eye.x, axis.z - eye.z);
            float along = Vector2.Dot(toAxis, across) / across2;
            float miss2 = (across * along - toAxis).sqrMagnitude;
            if (miss2 < radius * radius)
            {
                along -= Mathf.Sqrt((radius * radius - miss2) / across2);
            }
            along = Mathf.Max(along, 0.0f);

            point = eye + forward * along;
            share = (point.y - bottom) / height;
            if (share >= -margin && share <= 1.0f + margin)
            {
                return true;
            }
        }

        // Over the side or under it: looking down, the line may still come in through the top,
        // and looking up through the bottom.
        if (Mathf.Abs(forward.y) < 1e-4f)
        {
            return false;
        }
        float capY = forward.y < 0.0f ? top + margin * height : bottom - margin * height;
        float down = (capY - eye.y) / forward.y;
        if (down < 0.0f)
        {
            return false;
        }
        Vector3 onCap = eye + forward * down;
        float dx = onCap.x - axis.x;
        float dz = onCap.z - axis.z;
        if (dx * dx + dz * dz > radius * radius)
        {
            return false;
        }
        point = onCap;
        share = (onCap.y - bottom) / height;
        return true;
    }

    /// <summary>
    /// The body part a blow aimed at <paramref name="share"/> of a body's height lands on.
    /// <paramref name="roll"/>, from 0 up to 1, decides between the arms and the torso.
    /// </summary>
    public static EBodyPart AimedBodyPart(float share, float roll)
    {
        if (share < AimLegsBelow)
        {
            return EBodyPart.Legs;
        }

        if (share > AimHeadAbove)
        {
            return EBodyPart.Head;
        }

        return roll < AimArmsShare ? EBodyPart.Arms : EBodyPart.Torso;
    }

    /// <summary>
    /// What a blow gains for landing on a back instead of a face: 0 face to face, 4 from straight
    /// behind, and the steps in between.
    /// </summary>
    /// <remarks>
    /// The original compares the two facings, not the direction the attacker came from, so
    /// circling something that keeps turning towards you earns nothing. It folds the difference
    /// of the two headings into 0-4 and spends it twice: on the attack score, and on the damage
    /// after the charge scale and before armour (UW.EXE 0x25158, read at 0x24ba0 and 0x24d45).
    /// The headings are three bits wide, so eighths of a turn, and they are truncated rather than
    /// rounded - the player's own is written as (angle >> 13) &amp; 7 at 0x1d08c, and a creature
    /// has nothing finer, it turns an eighth at a time (0x1ac5d). One routine works it out for
    /// whoever is swinging (0x2522c inside 0x251ac, reached from 0x255ca for the player and from
    /// 0x259ee for a creature), which is why the player gets it too. A shot does not: the missile
    /// path zeroes the same byte at 0x258e3.
    /// </remarks>
    public static int CalcFlankingBonus(Transform attacker, Transform target)
    {
        int difference = (GetHeading(target) - GetHeading(attacker) + 4) & 7;
        return difference > 4 ? 8 - difference : difference;
    }

    /// <summary>
    /// A facing the way the original keeps it: an eighth of a turn, truncated (UW.EXE 0x1d08c).
    /// </summary>
    public static int GetHeading(Transform t)
    {
        return Mathf.FloorToInt(t.eulerAngles.y / 45.0f) & 7;
    }

    /// <summary>
    /// What a critical hit does to the maximum before the dice are rolled: double it, half the
    /// time.
    /// </summary>
    /// <remarks>
    /// The original draws rand() &amp; 31, adds 48 and shifts right by 5, which is 1 for the low
    /// sixteen values and 2 for the high sixteen - an even coin flip (UW.EXE 0x24bc3). It lands
    /// on the nominal damage, before the roll and before the charge scale, and it lands there for
    /// whoever swung: one routine resolves both the player's blow and a creature's, and the
    /// critical branch sits inside it (0x24ac9, called from 0x255ca and from 0x259ee through
    /// 0x251ac).
    /// </remarks>
    public static int GetCriticalDamage(int maxDamage)
    {
        return maxDamage * Random.Range(1, 3);
    }

    public static int GetDamageRoll(int maxDamage)
    {
        int numD6 = maxDamage / 6;
        int remainder = maxDamage % 6;
        int rolledDamage = remainder > 0 ? Random.Range(1, remainder + 1) : 0;
        for (int i = 0; i < numD6; ++i)
        {
            rolledDamage += Random.Range(1, 7);
        }

        return rolledDamage;
    }
    
    public static UUObject SpawnSingleObject(EObjectType type)
    {
        UUObject obj = LevelLoader.CreateObjectOfType(type);
        
        Vector3 start = PlayerObject.Player.transform.position + Vector3.up;
        Vector3 dir = PlayerObject.Player.transform.forward;
        #if false
        int layerMask = LayerMasks.EnvironmentAndCeiling | (1 << LayerMask.NameToLayer("Objects"));
        RaycastHit hit;
        bool bam = Physics.SphereCast(start, 0.25f, dir, out hit, 4.0f, layerMask);
        start += dir * (bam ? hit.distance : 4.0f);
        bam = Physics.SphereCast(start, 0.2f, Vector3.down, out hit, 2.0f, layerMask);
        start += Vector3.down * (bam ? hit.distance : 2.0f);
        #else
        start += dir + Random.insideUnitSphere;
        #endif

        obj.transform.position = start;
        obj.PostLoadInitialize();
        LevelLoader.AddToWorld(obj);

        return obj;
    }

    public static int OffsetToOctant(Vector3 offset)
    {
        float fangle = Mathf.Atan2(offset.x, offset.z) / (2.0f * Mathf.PI);
        int octant = (int)(8.0f * fangle + 8.5f); // make all positive before rounding
        return octant & 7;
    }

    private static AudioSource CreateClip(AudioClip clip, Vector3 pos, float maxDistance, AudioRolloffMode rolloffMode)
    {
        if (clip != null)
        {
            Vector3 off = pos - PlayerObject.Player.mainCamera.transform.position;
            float distance = off.magnitude;
            if (distance < maxDistance)
            {
                GameObject go = new GameObject("UtilsPlayClip");
                AudioSource src = go.AddComponent<AudioSource>();
                src.maxDistance = maxDistance;
                src.clip = clip;
                src.transform.position = pos;
                src.rolloffMode = rolloffMode;
                src.spatialize = rolloffMode != AudioRolloffMode.Custom;
                src.spatialBlend = rolloffMode != AudioRolloffMode.Custom ? 1.0f : 0.0f;
                return src;
            }
        }
        return null;
    }

    private static float ScaleEffectsVolume(float relativeVolume = 1f) =>
        relativeVolume * PlayerInput.EffectsVolume;

    public static AudioSource PlayClip(AudioClip clip, Vector3 pos, float maxDistance, AudioRolloffMode rolloffMode)
    {
        AudioSource src = CreateClip(clip, pos, maxDistance, rolloffMode);
        if (src != null)
        {
            src.volume = ScaleEffectsVolume();
            src.Play();
            Object.Destroy(src.gameObject, src.clip.length);
        }
        return src;
    }

    public static void PlayClip(AudioClip clip, Vector3 pos, float volume = 1.0f)
    {
        AudioSource src = CreateClip(clip, pos, 24.0f, AudioRolloffMode.Linear);
        if (src != null)
        {
            src.volume = ScaleEffectsVolume(volume);
            src.Play();
            Object.Destroy(src.gameObject, src.clip.length);
        }
    }

    public static void PlayClip(AudioClip clip, Vector3 pos, float volume, float pitch)
    {
        AudioSource src = CreateClip(clip, pos, 24.0f, AudioRolloffMode.Linear);
        if (src != null)
        {
            src.volume = ScaleEffectsVolume(volume);
            src.pitch = pitch;
            float length = src.clip.length / pitch;
            src.Play();
            Object.Destroy(src.gameObject, length);
        }
    }

    public static AudioSource PlayClipOccluded(AudioClip clip, Vector3 pos, float maxDistance, AudioRolloffMode rolloffMode, float volume = 1.0f)
    {
        AudioSource src = CreateClip(clip, pos, maxDistance, rolloffMode);
        if (src != null)
        {
            float scaledVolume = ScaleEffectsVolume(volume);
            Vector3 off = pos - PlayerObject.Player.mainCamera.transform.position;
            if (Physics.Raycast(PlayerObject.Player.mainCamera.transform.position, off.normalized, off.magnitude, LayerMasks.EnvironmentAndCeiling))
            {
                src.volume = 0.4f * scaledVolume;
            }
            else
            {
                src.volume = scaledVolume;
            }
            src.Play();
            Object.Destroy(src.gameObject, src.clip.length);
        }
        return src;
    }

    public static AudioSource PlayClipOccluded(AudioClip clip, Vector3 pos, float volume = 1.0f)
    {
        return PlayClipOccluded(clip, pos, 10.0f, AudioRolloffMode.Linear, volume);
    }

    private static AudioSource CreateClip2d(AudioClip clip)
    {
        if (clip != null)
        {
            GameObject go = new GameObject("UtilsPlayClip2d");
            AudioSource src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialize = false;
            src.spatialBlend = 0.0f;
            return src;
        }
        return null;
    }

    public static AudioSource PlayClip2d(AudioClip clip, bool randomizePitch = true)
    {
        return PlayClip2d(clip, 1.0f, randomizePitch);
    }

    /// <summary>
    /// Plays a 2d clip at a fixed pitch ratio, for a sampler that transposes one recording across
    /// several notes. 1.0 is the clip as recorded; 2.0 is an octave up.
    /// </summary>
    /// <remarks>
    /// Separate from the overload below because that one randomises the pitch, which is right for
    /// a footstep and wrong for a note. The lifetime has to account for the ratio: resampling up
    /// shortens the clip and resampling down lengthens it, and destroying the object after the
    /// unscaled length would cut a low note off.
    /// </remarks>
    public static AudioSource PlayClip2dAtPitch(AudioClip clip, float pitch, float volume = 1.0f)
    {
        AudioSource src = null;
        if (clip != null && pitch > 0.0f)
        {
            src = CreateClip2d(clip);
            if (src != null)
            {
                src.pitch = pitch;
                src.volume = ScaleEffectsVolume(volume);
                src.Play();
                Object.Destroy(src.gameObject, src.clip.length / pitch);
            }
        }
        return src;
    }

    public static AudioSource PlayClip2d(AudioClip clip, float volume, bool randomizePitch = true)
    {
        AudioSource src = null;
        if (clip != null)
        {
            src = CreateClip2d(clip);
            if (src != null)
            {
                float length = src.clip.length;
                if (randomizePitch)
                {
                    float r = Random.Range(0.9f, 1.1f);
                    src.pitch = r;
                    length /= r;
                }
                src.volume = ScaleEffectsVolume(volume);
                src.Play();
                Object.Destroy(src.gameObject, length);
            }
        }
        return src;
    }

    public static AudioSource PlayClip2dWithPitch(AudioClip clip, float pitch)
    {
        AudioSource src = CreateClip2d(clip);
        if (src != null)
        {
            src.pitch = pitch;
            src.volume = ScaleEffectsVolume();
            float length = src.clip.length / pitch;
            src.Play();
            Object.Destroy(src.gameObject, length);
        }
        return src;
    }

    public static float DeadZone(float x)
    {
        if (x >= 0) return 1.25f * Mathf.Max(0.0f, x - 0.2f);
        return -DeadZone(-x);
    }

    private static readonly char[] vowels = { 'a', 'e', 'i', 'o', 'u', 'A', 'E', 'I', 'O', 'U' }; 

    public static bool IsVowel(char x)
    {
        return vowels.Contains(x);
    }

    private static EParticleType MapSplatTypeToParticleType(SplatType splatType)
    {
        switch (splatType)
        {
        case SplatType.Blood:
            return EParticleType.BloodSplat;
        case SplatType.Spark:
            return EParticleType.SparkSplat;
        case SplatType.Fire1:
        case SplatType.Fire2:
        case SplatType.Fire3:
            return EParticleType.FireSplat;
        case SplatType.Splash:
            return EParticleType.WaterSplash;
        case SplatType.Magic:
            return EParticleType.MagicSplat;
        case SplatType.Lightning:
            return EParticleType.LightningSplat;
        default:
            return EParticleType.SparkSplat;
        }
    }

    public static EParticleType MapRemainsToParticleType(int remains, int blood)
    {
        switch (remains)
        {
        case 0: // Nothing
            // probably a flying creature
            return (blood & 0xc) > 0 ? EParticleType.BloodSplat : EParticleType.SparkSplat;
        case 1: // RotwormCorpse (0x20 >> 5)
            return EParticleType.BloodSplat;
        case 2: // Rubble (0x40 >> 5)
            return EParticleType.SparkSplat;
        case 3: // WoodChips (0x60 >> 5)
            return EParticleType.WoodSplat;
        case 4: // Bones (0x80 >> 5)
            return EParticleType.SparkSplat;
        case 5: // GreenBloodPool (0xA0 >> 5)
            return EParticleType.PoisonSplat;
        case 6: // RedBloodPool (0xC0 >> 5)
            return EParticleType.BloodSplat;
        case 7: // RedBloodPoolGiantSpider (0xE0 >> 5)
            return EParticleType.BloodSplat;
        default:
            return EParticleType.SparkSplat;
        }
    }

    private static SplatType MapParticleTypeToSplatType(EParticleType particleType)
    {
        switch (particleType)
        {
        case EParticleType.BloodSplat:
            return SplatType.Blood;
        case EParticleType.SparkSplat:
            return SplatType.Spark;
        case EParticleType.WoodSplat:
            return SplatType.Spark; // Closest match
        case EParticleType.PoisonSplat:
            return SplatType.Blood; // Closest match
        case EParticleType.FireSplat:
            return SplatType.Fire1;
        case EParticleType.WaterSplash:
            return SplatType.Splash;
        case EParticleType.MagicSplat:
            return SplatType.Magic;
        case EParticleType.LightningSplat:
            return SplatType.Lightning;
        default:
            return SplatType.Spark;
        }
    }

    public static void CreateFallbackSplat(Vector3 position, SplatType splatType)
    {
        if (LevelLoader.sLevelLoader != null && LevelLoader.sLevelLoader.splat != null)
        {
            GameObject splatObj = Object.Instantiate(LevelLoader.sLevelLoader.splat.gameObject, position, Quaternion.identity);
            Splat splatComponent = splatObj.GetComponent<Splat>();
            if (splatComponent != null)
            {
                splatComponent.splatType = splatType;
            }
        }
    }

    public static void CreateSplats(Vector3 center, float spread, SplatType splatType, int damage)
    {
        if (ParticleSpawner.sParticleSpawner == null)
        {
            Debug.LogWarning("ParticleSpawner not initialized, cannot create splats");
            return;
        }

        EParticleType particleType = MapSplatTypeToParticleType(splatType);

        Vector3 off = PlayerObject.Player.mainCamera.transform.position - center;
        off.Normalize();

        int numSplats = Mathf.Min(damage, 9);
        
        for (int i = 0; i < numSplats; ++i)
        {
            Vector2 r = Random.insideUnitCircle;
            Vector3 lr = center + Quaternion.Euler(0.0f, 30.0f * r.y, 0.0f) * off;
            Vector3 pos = lr + 0.5f * spread * r.x * Vector3.up;
            
            GameObject particle = ParticleSpawner.SpawnParticle(particleType, pos);
            if (particle == null)
            {
                CreateFallbackSplat(pos, splatType);
            }
        }
    }

    public static void CreateSplats(Critter critter, SplatType splatType, int damage)
    {
        if (critter == null)
        {
            return;
        }

        if (ParticleSpawner.sParticleSpawner == null)
        {
            Debug.LogWarning("ParticleSpawner not initialized, cannot create splats");
            return;
        }

        EParticleType particleType = MapSplatTypeToParticleType(splatType);

        CharacterController characterController = critter.GetComponent<CharacterController>();
        if (characterController == null)
        {
            return;
        }

        // Get damage center
        float characterHeight = characterController.height;
        Vector3 damageCenter = critter.transform.TransformPoint(characterController.center) + 0.1f * characterHeight * Vector3.up;
        float spread = 0.5f;

        Vector3 playerPosition = PlayerObject.Player.mainCamera.transform.position;
        Vector3 playerDirection = (playerPosition - damageCenter).normalized;

        int numSplats = Mathf.Min(damage, 6);
        List<Transform> availableJoints = new List<Transform>();

        // Find all SkinnedMeshRenderers and select the one with the most bones
        SkinnedMeshRenderer[] allRenderers = critter.GetComponentsInChildren<SkinnedMeshRenderer>();
        SkinnedMeshRenderer bestRenderer = null;
        int maxBoneCount = 0;

        foreach (SkinnedMeshRenderer renderer in allRenderers)
        {
            if (renderer != null && renderer.bones != null && renderer.bones.Length > maxBoneCount)
            {
                maxBoneCount = renderer.bones.Length;
                bestRenderer = renderer;
            }
        }

        // Get joints from the best SkinnedMeshRenderer
        if (bestRenderer != null && bestRenderer.bones != null)
        {
            foreach (Transform bone in bestRenderer.bones)
            {
                if (bone != null)
                {
                    // Only include bones within half character height vertically of damage center
                    float verticalDistance = Mathf.Abs(bone.position.y - damageCenter.y);
                    if (verticalDistance <= spread)
                    {
                        availableJoints.Add(bone);
                    }
                }
            }
        }

        // If no joints found, fall back to cylinder algorithm
        if (availableJoints.Count == 0)
        {
            CreateSplats(damageCenter, spread, splatType, damage);
            return;
        }

        // Randomly select distinct joints (each used at most once)
        List<Transform> selectedJoints = new List<Transform>();
        int jointsToSelect = Mathf.Min(numSplats, availableJoints.Count);
        
        // Shuffle and select distinct joints
        List<Transform> shuffledJoints = new List<Transform>(availableJoints);
        for (int i = 0; i < shuffledJoints.Count; i++)
        {
            Transform temp = shuffledJoints[i];
            int randomIndex = Random.Range(i, shuffledJoints.Count);
            shuffledJoints[i] = shuffledJoints[randomIndex];
            shuffledJoints[randomIndex] = temp;
        }

        for (int i = 0; i < jointsToSelect; i++)
        {
            selectedJoints.Add(shuffledJoints[i]);
        }

        // Spawn particles on selected joints, offset towards player by 0.2m
        Vector3 cameraForward = PlayerObject.Player.mainCamera.transform.forward;
        foreach (Transform joint in selectedJoints)
        {
            Vector3 jointPosition = joint.position;
            Vector3 splatPosition = jointPosition + (playerDirection * 0.2f);
            
            // Calculate rotation so that splat's up axis faces away from critter center
            Vector3 directionAway = (splatPosition - damageCenter).normalized;
            Quaternion rotation = Quaternion.LookRotation(cameraForward, directionAway);
            
            GameObject particle = ParticleSpawner.SpawnParticle(particleType, splatPosition, rotation);
            if (particle == null)
            {
                CreateFallbackSplat(splatPosition, splatType);
            }
        }
    }

    /// <summary>
    /// Blood, sparks or chips where a creature was hurt, one splat a point of damage up to six, on
    /// the joints of its model near the place or, without a model, around it.
    /// With <paramref name="struckPart"/> the place is the part a blow of the player's landed on,
    /// so the blood shows where he aimed: the joints in the band of the body that part takes (below
    /// <see cref="AimLegsBelow"/> of the height for the legs, above <see cref="AimHeadAbove"/> for
    /// the head, in between for the rest), and in that middle band the ones nearest the body's
    /// middle for the torso and the ones furthest out, on both sides, for the arms. The head takes
    /// no joints: the blood goes on the middle of the body at the height struck, up to 20 cm to
    /// either side, since the joints at the top of a body can be a shoulder or a raised hand, and a
    /// creature like the headless has nothing else up there.
    /// <paramref name="struckPoint"/> is where his aim met the body. Without a part, which is every
    /// other kind of harm, the place is the middle of the body.
    /// </summary>
    public static void CreateSplats(Critter critter, EParticleType particleType, int damage,
                                    EBodyPart? struckPart = null, Vector3 struckPoint = default)
    {
        if (critter == null)
        {
            return;
        }

        if (ParticleSpawner.sParticleSpawner == null)
        {
            Debug.LogWarning("ParticleSpawner not initialized, cannot create splats");
            return;
        }

        CharacterController characterController = critter.GetComponent<CharacterController>();
        if (characterController == null)
        {
            return;
        }

        // Get damage center
        float characterHeight = characterController.height;
        Vector3 bodyMiddle = critter.transform.TransformPoint(characterController.center);
        Vector3 damageCenter = bodyMiddle + 0.1f * characterHeight * Vector3.up;
        float spread = 0.5f;

        // The band of the body the struck part takes.
        float bandLow = 0.0f;
        float bandHigh = 0.0f;
        Vector3 across = PlayerObject.Player.mainCamera.transform.right;
        if (struckPart.HasValue)
        {
            // the same box the aim was met with, WeaponBase.FindObjectToDamage()
            float boxHeight = Mathf.Max(characterHeight, 2.0f * characterController.radius);
            float bottom = bodyMiddle.y - 0.5f * boxHeight;
            bandLow = struckPart == EBodyPart.Legs ? bottom
                : struckPart == EBodyPart.Head ? bottom + AimHeadAbove * boxHeight
                : bottom + AimLegsBelow * boxHeight;
            bandHigh = struckPart == EBodyPart.Legs ? bottom + AimLegsBelow * boxHeight
                : struckPart == EBodyPart.Head ? bottom + boxHeight
                : bottom + AimHeadAbove * boxHeight;
            damageCenter = new Vector3(bodyMiddle.x, Mathf.Clamp(struckPoint.y, bandLow, bandHigh), bodyMiddle.z);
            spread = bandHigh - bandLow;
        }

        Vector3 playerPosition = PlayerObject.Player.mainCamera.transform.position;
        Vector3 playerDirection = (playerPosition - damageCenter).normalized;

        int numSplats = Mathf.Min(damage, 6);

        if (struckPart == EBodyPart.Head)
        {
            // on the front of the body's middle, a little to either side
            Vector3 towards = new Vector3(playerDirection.x, 0.0f, playerDirection.z).normalized;
            Vector3 forwardOfCamera = PlayerObject.Player.mainCamera.transform.forward;
            SplatType fallbackForHead = MapParticleTypeToSplatType(particleType);
            for (int i = 0; i < numSplats; i++)
            {
                Vector3 splatPosition = damageCenter + 0.2f * towards + Random.Range(-0.2f, 0.2f) * across
                                        + Random.Range(-0.05f, 0.05f) * Vector3.up;
                Vector3 directionAway = (splatPosition - damageCenter).normalized;
                Quaternion rotation = Quaternion.LookRotation(forwardOfCamera, directionAway);
                GameObject particle = ParticleSpawner.SpawnParticle(particleType, splatPosition, rotation);
                if (particle == null)
                {
                    CreateFallbackSplat(splatPosition, fallbackForHead);
                }
            }
            return;
        }

        List<Transform> availableJoints = new List<Transform>();

        // Find all SkinnedMeshRenderers and select the one with the most bones
        SkinnedMeshRenderer[] allRenderers = critter.GetComponentsInChildren<SkinnedMeshRenderer>();
        SkinnedMeshRenderer bestRenderer = null;
        int maxBoneCount = 0;

        foreach (SkinnedMeshRenderer renderer in allRenderers)
        {
            if (renderer != null && renderer.bones != null && renderer.bones.Length > maxBoneCount)
            {
                maxBoneCount = renderer.bones.Length;
                bestRenderer = renderer;
            }
        }

        // Get joints from the best SkinnedMeshRenderer
        if (bestRenderer != null && bestRenderer.bones != null)
        {
            foreach (Transform bone in bestRenderer.bones)
            {
                if (bone != null)
                {
                    if (struckPart.HasValue)
                    {
                        // the bones in the band of the part that was struck
                        if (bone.position.y >= bandLow && bone.position.y <= bandHigh)
                        {
                            availableJoints.Add(bone);
                        }
                        continue;
                    }

                    // Only include bones within half character height vertically of damage center
                    float verticalDistance = Mathf.Abs(bone.position.y - damageCenter.y);
                    if (verticalDistance <= spread)
                    {
                        availableJoints.Add(bone);
                    }
                }
            }
        }

        if ((struckPart is EBodyPart.Torso or EBodyPart.Arms) && availableJoints.Count >= 2)
        {
            // The torso and the arms share a band: the torso is the half of its bones nearest the
            // creature's own middle line, the arms the half furthest out to its sides, left and
            // right alike, so the blood falls on either arm at random. Its own sides and not the
            // player's view, or from the flank the arms would be its chest and back.
            Vector3 sideways = critter.transform.right;
            availableJoints.Sort((a, b) => Mathf.Abs(Vector3.Dot(a.position - bodyMiddle, sideways))
                .CompareTo(Mathf.Abs(Vector3.Dot(b.position - bodyMiddle, sideways))));
            int half = availableJoints.Count / 2;
            if (struckPart == EBodyPart.Torso)
            {
                availableJoints.RemoveRange(half, availableJoints.Count - half);
            }
            else
            {
                availableJoints.RemoveRange(0, half);
            }
        }

        // If no joints found, fall back to cylinder algorithm
        if (availableJoints.Count == 0)
        {
            SplatType fallbackSplatType = MapParticleTypeToSplatType(particleType);
            if (struckPart == EBodyPart.Arms)
            {
                // either arm at random, splat by splat, as many in all as one cloud would have
                int total = Mathf.Min(damage, 9);
                int left = 0;
                for (int i = 0; i < total; i++)
                {
                    left += Random.value < 0.5f ? 1 : 0;
                }
                Vector3 armOut = 0.8f * characterController.radius * across;
                CreateSplats(damageCenter - armOut, spread, fallbackSplatType, left);
                CreateSplats(damageCenter + armOut, spread, fallbackSplatType, total - left);
                return;
            }
            CreateSplats(damageCenter, spread, fallbackSplatType, damage);
            return;
        }

        // Randomly select distinct joints (each used at most once)
        List<Transform> selectedJoints = new List<Transform>();
        int jointsToSelect = Mathf.Min(numSplats, availableJoints.Count);
        
        // Shuffle and select distinct joints
        List<Transform> shuffledJoints = new List<Transform>(availableJoints);
        for (int i = 0; i < shuffledJoints.Count; i++)
        {
            Transform temp = shuffledJoints[i];
            int randomIndex = Random.Range(i, shuffledJoints.Count);
            shuffledJoints[i] = shuffledJoints[randomIndex];
            shuffledJoints[randomIndex] = temp;
        }

        for (int i = 0; i < jointsToSelect; i++)
        {
            selectedJoints.Add(shuffledJoints[i]);
        }

        // Spawn particles on selected joints, offset towards player by 0.2m
        Vector3 cameraForward = PlayerObject.Player.mainCamera.transform.forward;
        SplatType fallbackSplatTypeForJoint = MapParticleTypeToSplatType(particleType);
        foreach (Transform joint in selectedJoints)
        {
            Vector3 jointPosition = joint.position;
            Vector3 splatPosition = jointPosition + (playerDirection * 0.2f);
            
            // Calculate rotation so that splat's up axis faces away from critter center
            Vector3 directionAway = (splatPosition - damageCenter).normalized;
            Quaternion rotation = Quaternion.LookRotation(cameraForward, directionAway);
            
            GameObject particle = ParticleSpawner.SpawnParticle(particleType, splatPosition, rotation);
            if (particle == null)
            {
                CreateFallbackSplat(splatPosition, fallbackSplatTypeForJoint);
            }
        }
    }
    
    public static bool SafeToSleepOffDrink()
    {
        if (Music.GetMusicState() == EMusicState.Combat)
        {
            return false;
        }

        // check for enemies nearby (within 4 tiles)
        int count = Physics.OverlapSphereNonAlloc(PlayerObject.Player.mainCamera.transform.position, 4.0f * Tile.xzScale,
                     cachedColliders, 1 << LayerMask.NameToLayer("Characters"));
        for (int i = 0; i < count; i++)
        {
            Collider col = cachedColliders[i];
            if (col is CharacterController)
            {
                Critter critter = col.transform.root.gameObject.GetComponent<Critter>();
                if (critter != null && critter.attitude == Critter.EAttitude.Hostile && critter.hp > 0)
                {
                    Array.Clear(cachedColliders, 0, count);
                    return false;
                }
            }
        }
        Array.Clear(cachedColliders, 0, count);
        
        // check for trying to sleep in water, on lava, in the air, in the ethereal void, etc
        if (LevelLoader.sLevelLoader.loadedLevel == 9
            || LevelLoader.GetClosestTile(PlayerObject.Player.transform.position).GetFloorTerrain() == ETerrainType.Lava)
        {
            return false;
        }

        return true;
    }

    public static bool CanSleepHere(bool notify = false)
    {
        if (Music.GetMusicState() == EMusicState.Combat)
        {
            if (notify)
            {
                Messages.Add(1, 14);
            }

            return false;
        }

        // check for enemies nearby (within 4 tiles)
        int count = Physics.OverlapSphereNonAlloc(PlayerObject.Player.mainCamera.transform.position, 4.0f * Tile.xzScale,
                     cachedColliders, 1 << LayerMask.NameToLayer("Characters"));
        for (int i = 0; i < count; i++)
        {
            Collider col = cachedColliders[i];
            if (col is CharacterController)
            {
                Critter critter = col.transform.root.gameObject.GetComponent<Critter>();
                if (critter != null && critter.attitude == Critter.EAttitude.Hostile && critter.hp > 0)
                {
                    if (notify)
                    {
                        Messages.Add(1, 14); // there are hostile creatures nearby
                    }

                    Array.Clear(cachedColliders, 0, count);
                    return false;
                }
            }
        }
        Array.Clear(cachedColliders, 0, count);
        
        // check for trying to sleep in water, on lava, in the air, in the ethereal void, etc
        if (LevelLoader.sLevelLoader.loadedLevel == 9
            || !PlayerObject.Player.cachedCharacterController.isGrounded
            || LevelLoader.GetClosestTile(PlayerObject.Player.transform.position).GetFloorTerrain() >= ETerrainType.Water)
        {
            if (notify)
            {
                Messages.Add(1, 20); // you can't go to sleep here
            }

            return false;
        }

        return true;
    }

    public static Vector3 GetCupPos()
    {
        return new Vector3(80.0f, 10.25f, 131.0f);
    }

    public static EPoisonRating GetPoisonRating()
    {
        EPoisonRating poisonRating = (EPoisonRating) Mathf.Clamp(PlayerData.sData.poison / 6, 0, 5);
        return poisonRating;
    }

    public static void DropShadowText(string text, float x, float y, float w, float h, GUIStyle style)
    {
        Rect r = new Rect(x + 2, y + 2, w, h);
        Color textColor = style.normal.textColor;
        style.normal.textColor = Color.black;
        GUI.Label(r, text, style);
        style.normal.textColor = textColor;
        r.x -= 2;
        r.y -= 2;
        GUI.Label(r, text, style);
    }

    public static Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name)
            {
                return child;
            }

            Transform found = FindDeepChild(child, name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    public static T RandomEnum<T>() where T : System.Enum
    {
        System.Array enumValues = System.Enum.GetValues(typeof(T));
        int randomIndex = Random.Range(0, enumValues.Length);
        return (T)enumValues.GetValue(randomIndex);
    }

    public static int DiceRoll(int dieSize, int numRolls)
    {
        int roll = 0;
        for (int i = 0; i < numRolls; ++i)
        {
            roll += Random.Range(1, dieSize + 1);
        }
        return roll;
    }

    public static void DestroyItem(UUObject item)
    {
        Inventory.sInv.RemoveItemFromInventory(item);
        LevelLoader.worldObj.Remove(item);
        Object.Destroy(item.gameObject);
    }

    public static void DestroyCritter(Critter critter)
    {
        LevelLoader.worldObj.Remove(critter);
        Object.Destroy(critter.gameObject);
    }
}

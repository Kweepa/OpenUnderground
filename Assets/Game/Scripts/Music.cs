using UnityEngine;
using System.Collections;

public enum EMusicState
{
    ExploringA,
    ExploringB,
    ExploringC,
    ExploringD,
    Combat,
    Injured,
    Victory,
    LevelUp,
    Death,
    Map,

    // In coda, non in mezzo: stateMusicTracks e' indicizzato da questo enum e il prefab lo
    // serializza per posizione, quindi un valore nuovo inserito prima sposterebbe ogni traccia.
    Winning,
    Alert,
    Sleep,
    Conversation
};
    
public class Music : MonoBehaviour
{
    [EnumNamedArray(typeof(EMusicState))]
    public string[] stateMusicTracks = new string[System.Enum.GetValues(typeof(EMusicState)).Length];

    [Tooltip("Show track name, time, and voice stats on screen")]
    public bool showMusicDebugHUD = false;

    private EMusicState musicState = EMusicState.ExploringA;
    private float combatTime;

    /// <summary>Health, as a fraction of the maximum, at or below which a fight is going badly.</summary>
    /// <remarks>
    /// The original works out (hp * 64) / (maxHp + 1) on every blow and compares it against 16,
    /// which is this quarter (UW.EXE 0x1c447 for the blow the player lands, 0x1c475 for the one
    /// he takes).
    /// </remarks>
    private const float losingHealthFraction = 0.25f;

    /// <summary>Seconds before the combat music may change track again.</summary>
    /// <remarks>
    /// The original waits 0x800 units of its clock before swapping one track of the band for
    /// another, and its clock runs at 256 units to the second, so this is that wait. It is what
    /// keeps the score from flapping between winning and losing on every blow, which in a fight
    /// where both sides are near the line is every couple of seconds.
    /// </remarks>
    private const float combatSwitchDelay = 8.0f;

    private float combatSwitchCooldown;

    /// <summary>Seconds the warning music keeps playing after the last thing that called for it.</summary>
    /// <remarks>
    /// Only the tail: the countdown is refreshed every frame the weapon is up and every frame a
    /// hostile creature is still coming, so this is how long the music takes to notice that the
    /// reason has gone. Settled by ear at six seconds, timed against the trace: long enough that
    /// the warning reads as letting go rather than as a cut, short enough not to outstay the
    /// danger.
    /// </remarks>
    private const float alertDuration = 6.0f;

    private float alertTime;
    private static Music sMusic;
    private float stateTime;
    private MusicPlayer musicPlayer;
    private string currentPlayingTrack = "";
    
    // Track state before entering map screen
    private static EMusicState stateBeforeMap;
    
    private bool deferMapMusicUntilVictoryEnds;
    
    private readonly Collider[] cachedColliders = new Collider[32];
    
    void Awake()
    {
        sMusic = this;
        musicPlayer = MusicPlayer.Instance;
        
        if (musicPlayer == null)
        {
            Debug.LogError("MusicPlayer instance not found! Make sure MusicPlayer is in the scene.");
            return;
        }
        
        // Start with random exploring music
        StartExploringMusic();
    }
    
    public static bool IsInCombat() => sMusic != null && sMusic.musicState == EMusicState.Combat;

    public static void InCombat()
    {
        if (sMusic != null)
        {
            // Outside the switch: this says the fight is still going on, and that stays true
            // whatever happens to be playing over it. A victory fanfare that runs while two
            // more creatures are still swinging has to know there is a fight to go back to.
            sMusic.combatTime = 8.0f;

            switch (sMusic.musicState)
            {
            case EMusicState.ExploringA:
            case EMusicState.ExploringB:
            case EMusicState.ExploringC:
            case EMusicState.ExploringD:
            case EMusicState.Alert:
            case EMusicState.Combat:
            case EMusicState.Injured:
            case EMusicState.Winning:
                // Which of the three tracks belongs to the fight is the band's business, and
                // forcing the plain one here would cut whichever of the other two was playing
                // every time anything swung - which is to say, constantly, leaving the band
                // with nothing to do.
                if (IsCombatState(sMusic.musicState))
                {
                    break;
                }

                sMusic.musicState = EMusicState.Combat;
                // Cleared so the first reading of a new fight lands straight away: walking into
                // one already half dead should say so now, not in eight seconds.
                sMusic.combatSwitchCooldown = 0.0f;
                sMusic.StartStateMusic(EMusicState.Combat);
                break;
            }
        }
    }

    /// <summary>
    /// Something hostile has noticed the player, but has not swung at him yet.
    /// </summary>
    /// <remarks>
    /// The warning, not the fight. A creature that has heard the player through a wall is a
    /// reason to be told, not a reason for the combat music - and the combat music arriving
    /// first gave away that something was coming and then had nowhere left to go when it
    /// actually arrived. It steps aside for anything further along: a fight already under way,
    /// a victory still playing, the death music.
    /// </remarks>
    public static void Alert()
    {
        if (sMusic == null)
        {
            return;
        }

        sMusic.alertTime = alertDuration;

        switch (sMusic.musicState)
        {
        case EMusicState.ExploringA:
        case EMusicState.ExploringB:
        case EMusicState.ExploringC:
        case EMusicState.ExploringD:
        case EMusicState.Alert:
            sMusic.musicState = EMusicState.Alert;
            sMusic.StartStateMusic(EMusicState.Alert);
            break;
        }
    }

    /// <summary>
    /// The reason for the warning is still there: something is still coming.
    /// </summary>
    /// <remarks>
    /// Only refreshes the countdown, and never starts the music or changes state - that is
    /// Alert's job, once, when the creature first notices. Called every frame by any creature
    /// still on its way, so the warning stops a breath after the last one gives up or is left
    /// behind, instead of running on its own timer.
    /// </remarks>
    public static void AlertHeartbeat()
    {
        if (sMusic != null)
        {
            sMusic.alertTime = alertDuration;
        }
    }

    /// <summary>
    /// Whether anything hostile within three tiles is still in the fight.
    /// </summary>
    private static bool HostileStillFighting()
    {
        int count = Physics.OverlapSphereNonAlloc(PlayerObject.Player.mainCamera.transform.position,
                     3.0f * Tile.xzScale, sMusic.cachedColliders, 1 << LayerMask.NameToLayer("Characters"));
        for (int i = 0; i < count; i++)
        {
            Critter critter = sMusic.cachedColliders[i].transform.root.gameObject.GetComponent<Critter>();
            if (critter != null
                && critter.attitude == Critter.EAttitude.Hostile
                && critter.hp > 0
                && CritterIsAttacking(critter.state))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CritterIsAttacking(Critter.EState state)
    {
        switch (state)
        {
        case Critter.EState.Attack:
        case Critter.EState.Approach:
        case Critter.EState.TurnToApproach:
        case Critter.EState.Flinch:
        case Critter.EState.CombatTurn:
        case Critter.EState.ProjectileIdle:
        case Critter.EState.ProjectileAttack:
        case Critter.EState.CombatIdle:
        case Critter.EState.TurnToFlee:
        case Critter.EState.Flee:
            return true;
        }
        return false;
    }

    public static void EnemyDied()
    {
        // Still someone swinging nearby: not a victory yet.
        if (HostileStillFighting())
        {
            return;
        }

        switch (sMusic.musicState)
        {
        case EMusicState.ExploringA:
        case EMusicState.ExploringB:
        case EMusicState.ExploringC:
        case EMusicState.ExploringD:
        case EMusicState.Alert:
        case EMusicState.Combat:
        case EMusicState.Injured:
        case EMusicState.Winning:
            sMusic.musicState = EMusicState.Victory;
            sMusic.stateTime = 0.0f;
            sMusic.StartStateMusic(EMusicState.Victory);
            break;
        }
    }

    public static void LevelUp()
    {
        if (sMusic != null)
        {
            // Nothing is configured for this in the original: of the nine places that ask for a
            // track, none is a level up, and the nearest is the kill itself, which asks for the
            // one this remake plays on a victory. So with no track named here, say nothing at
            // all rather than cutting the music for a second of silence.
            if (string.IsNullOrEmpty(sMusic.stateMusicTracks[(int)EMusicState.LevelUp]))
            {
                return;
            }

            switch (sMusic.musicState)
            {
            case EMusicState.ExploringA:
            case EMusicState.ExploringB:
            case EMusicState.ExploringC:
            case EMusicState.ExploringD:
            case EMusicState.Combat:
            case EMusicState.Injured:
            case EMusicState.Winning:
            case EMusicState.Victory:
                sMusic.musicState = EMusicState.LevelUp;
                sMusic.stateTime = 0.0f;
                sMusic.StartStateMusic(EMusicState.LevelUp);
                break;
            }
        }
    }

    /// <summary>
    /// The player has lain down to sleep.
    /// </summary>
    /// <remarks>
    /// The original asks for its track 13 when you sleep (UW.EXE 0x81f8f), which is the file
    /// this remake had only ever used for the automap. It plays until waking, whatever was
    /// playing before: sleeping through a fight is not a thing that can happen.
    /// </remarks>
    public static void Sleep()
    {
        if (sMusic != null)
        {
            sMusic.musicState = EMusicState.Sleep;
            sMusic.StartStateMusic(EMusicState.Sleep);
        }
    }

    /// <summary>
    /// The player has started talking to somebody.
    /// </summary>
    /// <remarks>
    /// The same track as sleeping, and for the same reason: in the original this one belongs to
    /// the screens rather than to the dungeon, and it is asked for from the conversation overlay
    /// as well as from sleeping. A separate state rather than a shared one because the two are
    /// left in different ways - a conversation by closing it, a sleep by waking.
    /// </remarks>
    public static void Conversation()
    {
        if (sMusic != null)
        {
            sMusic.musicState = EMusicState.Conversation;
            sMusic.StartStateMusic(EMusicState.Conversation);
        }
    }

    public static void Dead()
    {
        sMusic.musicState = EMusicState.Death;
    }

    /// <summary>
    /// Which of the three combat tracks suits the fight right now.
    /// </summary>
    /// <remarks>
    /// The original scores a fight by who is losing it. Every blow it lands works out how much
    /// health the creature has left, and every blow it takes works out how much the player has
    /// left; a quarter or less picks a different track each way, and anything else is the plain
    /// combat track (UW.EXE 0x1c2d2). The player comes first when both are nearly down, because
    /// being about to die is the more useful thing to be told.
    /// </remarks>
    /// <summary>The three tracks a fight is scored with.</summary>
    private static bool IsCombatState(EMusicState state)
    {
        return state is EMusicState.Combat or EMusicState.Injured or EMusicState.Winning;
    }

    private static EMusicState DesiredCombatTrack()
    {
        int maxHitPoints = Mathf.Max(1, PlayerData.sData.vitality);
        if (PlayerData.sData.hp <= losingHealthFraction * maxHitPoints)
        {
            return EMusicState.Injured;
        }

        return CritterHealthDisplay.LastTargetNearlyDead ? EMusicState.Winning : EMusicState.Combat;
    }

    private void StartExploringMusic()
    {
        // Randomly pick one of the exploring states
        EMusicState[] exploringStates = new EMusicState[] 
        { 
            EMusicState.ExploringA, 
            EMusicState.ExploringB, 
            EMusicState.ExploringC, 
            EMusicState.ExploringD 
        };
        musicState = exploringStates[Random.Range(0, exploringStates.Length)];
        StartStateMusic(musicState);
    }
    
    /// <summary>
    /// Public method to resume exploring music (used by cutscenes)
    /// </summary>
    public static void ResumeExploringMusic()
    {
        if (sMusic != null)
        {
            sMusic.StartExploringMusic();
        }
    }
    
    private void StartStateMusic(EMusicState state)
    {
        if (musicPlayer == null) return;
        
        string trackFileName = stateMusicTracks[(int)state];
        if (string.IsNullOrEmpty(trackFileName))
        {
            musicPlayer.StopMusic();
            currentPlayingTrack = "";
            return;
        }
        
        if (trackFileName != currentPlayingTrack)
        {
            // The whole combat band loops, not just the middle of it: the three tracks take turns
            // for as long as the fight lasts, so any of them can be the one that is playing.
            bool loop = state is EMusicState.Map or EMusicState.Combat
                or EMusicState.Injured or EMusicState.Winning or EMusicState.Alert
                or EMusicState.Sleep or EMusicState.Conversation;
            musicPlayer.SwitchTrack(trackFileName, loop);
            currentPlayingTrack = trackFileName;
        }
    }

    void OnGUI()
    {
        if (!showMusicDebugHUD || musicPlayer == null)
        {
            return;
        }

        string track = string.IsNullOrEmpty(currentPlayingTrack) ? "(none)" : currentPlayingTrack;
        double t = musicPlayer.CurrentTime;
        int minutes = (int)(t / 60.0);
        int seconds = (int)(t % 60.0);
        int millis = (int)((t - System.Math.Floor(t)) * 1000.0);
        string playing = musicPlayer.IsPlaying ? "playing" : "stopped";
        string label =
            $"Music: {track}  {minutes}:{seconds:D2}.{millis:D3}  [{musicState}] {playing}\n" +
            $"Voices: {musicPlayer.ActiveVoiceCount}/{musicPlayer.MaximumPolyphonyCount}  " +
            $"peak {musicPlayer.PeakActiveVoices}  atCap {musicPlayer.AtCapNoteOns}  dropped {musicPlayer.DroppedNoteOns}  " +
            $"velCurve {musicPlayer.VelocityCurveAmount:F2}";

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            normal = { textColor = Color.yellow }
        };
        GUI.Label(new Rect(10, 10, Screen.width - 20, 56), label, style);
    }

    public void Update()
    {
        sMusic = this;
        if (musicPlayer == null) return;
        
        switch (musicState)
        {
        case EMusicState.ExploringA:
        case EMusicState.ExploringB:
        case EMusicState.ExploringC:
        case EMusicState.ExploringD:
            // Raising the weapon is enough on its own: the player has said he expects trouble.
            if (WeaponBase.IsRaised)
            {
                Alert();
                break;
            }

            // When exploring track ends, pick a new random track
            if (!musicPlayer.IsPlaying)
            {
                StartExploringMusic();
            }
            break;
            
        case EMusicState.Alert:
            // Holding the weapon up keeps the warning going: it is the player saying he expects
            // trouble, which is what the original's fight stance said and what this track is.
            if (WeaponBase.IsRaised)
            {
                alertTime = alertDuration;
            }

            alertTime -= Time.unscaledDeltaTime;
            if (alertTime < 0.0f)
            {
                StartExploringMusic();
            }
            break;

        case EMusicState.Combat:
        case EMusicState.Injured:
        case EMusicState.Winning:
            EMusicState desiredState = DesiredCombatTrack();
            if (desiredState != musicState && combatSwitchCooldown <= 0.0f)
            {
                musicState = desiredState;
                combatSwitchCooldown = combatSwitchDelay;
                StartStateMusic(musicState);
            }
            combatSwitchCooldown -= Time.unscaledDeltaTime;

            combatTime -= Time.unscaledDeltaTime;
            if (combatTime < 0.0f)
            {
                StartExploringMusic();
            }
            break;
            
        case EMusicState.Victory:
            stateTime += Time.unscaledDeltaTime;
            // Victory music plays once, then return to exploring (or map if map was opened during victory)
            if (stateTime > 1.0f && !musicPlayer.IsPlaying)
            {
                if (deferMapMusicUntilVictoryEnds)
                {
                    deferMapMusicUntilVictoryEnds = false;
                    musicState = EMusicState.Map;
                    stateBeforeMap = EMusicState.ExploringA;
                    StartStateMusic(EMusicState.Map);
                }
                else if (HostileStillFighting())
                {
                    // The fight outlasted the fanfare: back to it, not out to the corridors.
                    // Asked of the world and not of combatTime, which only says that something
                    // swung in the last few seconds - and after the last kill of a brawl that is
                    // always true, so the timer sent the combat music back over an empty room.
                    musicState = EMusicState.Combat;
                    combatSwitchCooldown = 0.0f;
                    StartStateMusic(EMusicState.Combat);
                }
                else
                {
                    StartExploringMusic();
                }
            }
            break;
            
        case EMusicState.LevelUp:
            stateTime += Time.unscaledDeltaTime;
            // LevelUp music plays once, then return to exploring
            if (stateTime > 1.0f && !musicPlayer.IsPlaying)
            {
                StartExploringMusic();
            }
            break;
            
        case EMusicState.Death:
            // Play death music
            string deathTrack = stateMusicTracks[(int)EMusicState.Death];
            if (currentPlayingTrack != deathTrack)
            {
                StartStateMusic(EMusicState.Death);
            }
            
            // Death music plays once and stops (no restart)
            
            if (PlayerData.sData.hp > 0) // reborn
            {
                StartExploringMusic();
            }
            break;
            
        case EMusicState.Map:
        case EMusicState.Sleep:
        case EMusicState.Conversation:
            // All three loop through the loop parameter and wait to be taken out of: the map by
            // closing it, the sleep by waking, the conversation by ending it.
            break;
        }
    }

    public static EMusicState GetMusicState()
    {
        return sMusic.musicState;
    }
    
    public static void EnterMapScreen()
    {
        if (sMusic != null)
        {
            stateBeforeMap = sMusic.musicState;
            
            if (sMusic.musicState == EMusicState.Victory)
            {
                sMusic.deferMapMusicUntilVictoryEnds = true;
                return;
            }
            
            sMusic.deferMapMusicUntilVictoryEnds = false;
            sMusic.musicState = EMusicState.Map;
            sMusic.StartStateMusic(EMusicState.Map);
        }
    }
    
    public static void ExitMapScreen()
    {
        if (sMusic != null)
        {
            if (sMusic.deferMapMusicUntilVictoryEnds && sMusic.musicState == EMusicState.Victory)
            {
                sMusic.deferMapMusicUntilVictoryEnds = false;
                return;
            }
            
            // Restore previous music state
            if (stateBeforeMap is EMusicState.ExploringA or EMusicState.ExploringB or EMusicState.ExploringC or EMusicState.ExploringD)
            {
                sMusic.StartExploringMusic();
            }
            else
            {
                sMusic.musicState = stateBeforeMap;
                sMusic.StartStateMusic(stateBeforeMap);
            }
        }
    }
}

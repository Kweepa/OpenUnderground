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
    Map
};
    
public class Music : MonoBehaviour
{
    [EnumNamedArray(typeof(EMusicState))]
    public string[] stateMusicTracks = new string[System.Enum.GetValues(typeof(EMusicState)).Length];

    [Tooltip("Show track name, time, and voice stats on screen")]
    public bool showMusicDebugHUD = false;

    private EMusicState musicState = EMusicState.ExploringA;
    private float combatTime;
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
            switch (sMusic.musicState)
            {
            case EMusicState.ExploringA:
            case EMusicState.ExploringB:
            case EMusicState.ExploringC:
            case EMusicState.ExploringD:
            case EMusicState.Victory:
            case EMusicState.Combat:
            case EMusicState.Injured:
                sMusic.musicState = EMusicState.Combat;
                sMusic.combatTime = 8.0f;
                sMusic.StartStateMusic(EMusicState.Combat);
                break;
            }
        }
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
        // check for enemies nearby (within 3 tiles)
        int count = Physics.OverlapSphereNonAlloc(PlayerObject.Player.mainCamera.transform.position, 3.0f * Tile.xzScale,
                     sMusic.cachedColliders, 1 << LayerMask.NameToLayer("Characters"));
        for (int i = 0; i < count; i++)
        {
            Collider col = sMusic.cachedColliders[i];
            Critter critter = col.transform.root.gameObject.GetComponent<Critter>();
            if (critter != null
                && critter.attitude == Critter.EAttitude.Hostile
                && critter.hp > 0
                && CritterIsAttacking(critter.state))
            {
                return;
            }
        }

        switch (sMusic.musicState)
        {
        case EMusicState.ExploringA:
        case EMusicState.ExploringB:
        case EMusicState.ExploringC:
        case EMusicState.ExploringD:
        case EMusicState.Combat:
        case EMusicState.Injured:
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
            switch (sMusic.musicState)
            {
            case EMusicState.ExploringA:
            case EMusicState.ExploringB:
            case EMusicState.ExploringC:
            case EMusicState.ExploringD:
            case EMusicState.Combat:
            case EMusicState.Injured:
            case EMusicState.Victory:
                sMusic.musicState = EMusicState.LevelUp;
                sMusic.stateTime = 0.0f;
                sMusic.StartStateMusic(EMusicState.LevelUp);
                break;
            }
        }
    }

    public static void Dead()
    {
        sMusic.musicState = EMusicState.Death;
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
            // Loop map and combat music (but not injured)
            bool loop = (state == EMusicState.Map || state == EMusicState.Combat);
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
            // When exploring track ends, pick a new random track
            if (!musicPlayer.IsPlaying)
            {
                StartExploringMusic();
            }
            break;
            
        case EMusicState.Combat:
        case EMusicState.Injured:
            // Check if player is critically injured
            bool isCriticallyInjured = (CritterHealthDisplay.sHealthDisplay?.lastDamageFraction ?? 0) == 2
                && (CritterHealthDisplay.sHealthDisplay?.enabledFraction ?? 0) > 0.0f;
            
            // Switch between combat and injured states
            EMusicState desiredState = isCriticallyInjured ? EMusicState.Injured : EMusicState.Combat;
            if (desiredState != musicState)
            {
                musicState = desiredState;
                StartStateMusic(musicState);
            }
            
            // When combat/injured track ends, restart it (loop)
            if (!musicPlayer.IsPlaying)
            {
                if (musicState == EMusicState.Injured)
                {
                    // Injured track ended - go back to regular combat
                    musicState = EMusicState.Combat;
                    StartStateMusic(EMusicState.Combat);
                }
            }
            
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
            // Map music loops automatically via the loop parameter
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

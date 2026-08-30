using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Random = UnityEngine.Random;

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

    [Tooltip("Shortest silence between two ambient tracks, in seconds. The longest is the player's own Options_AmbientSilenceMax, and this is capped by it.")]
    public float minimumSilence = 25.0f;

    [Tooltip("The one silence after the title track has played out, in seconds. Its own number, and a shorter one: nothing has been heard in the dungeon yet at that point. Capped by the player's maximum.")]
    public float openingSilence = 10.0f;

    [Tooltip("Subfolder of the sound folder scanned for extra ambient tracks, e.g. music ripped from UW2. Leave empty to disable.")]
    public string extraExploringTracksFolder = "UW2";


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
    
    // Silence countdown between two ambient tracks. Three distinct meanings:
    //    0   nothing pending - a track is playing, or one has just been started
    //   >0   silence in progress, seconds remaining
    //   -1   the silence has already been served, we are only looking for a track to play
    // The -1 sentinel is load bearing: StartStateMusic does nothing when the track it drew is
    // the one already playing, so without it every wasted draw would fall back into the "== 0"
    // branch and arm a fresh pause, chaining them forever.
    private float exploringSilenceTimer;
    
    // True from the moment the title track is left playing until the first ambient track is due.
    private bool openingSilencePending;
    
    // Ambient tracks found in extraExploringTracksFolder, as paths relative to the sound folder.
    // null = never scanned, empty = scanned and nothing found, so the disk is hit once per session.
    private string[] extraExploringTracks;
    
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
        
        // Let the title screen track finish instead of cutting it off. Music lives on the Player
        // prefab, so Awake() runs the moment the game starts - on a new character and on a load
        // alike - and the menu track is still playing. The original does not cut it either: it
        // carries on into the dungeon and the ambient rotation only takes over once it ends.
        // Clearing the loop is what lets it end at all, since the front end started it looping.
        // From there Update() does the rest: the exploring branch sees nothing playing, serves the
        // usual stretch of quiet if the player asked for it, and then draws the first track.
        if (musicPlayer.IsPlaying)
        {
            musicPlayer.LetCurrentTrackFinish();
            musicState = EMusicState.ExploringA;
            currentPlayingTrack = musicPlayer.currentTrack;
            openingSilencePending = true;
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
    /// Whether to leave a stretch of quiet between two ambient tracks.
    /// </summary>
    /// <remarks>
    /// The player sets the longest one, in seconds, and zero means none: the tracks run back to
    /// back the way they used to. A key that has never been written reads as its default, so a
    /// player carrying preferences over from an older build gets the quiet as well.
    /// </remarks>
    private bool SilenceBetweenTracks => PlayerInput.AmbientSilenceMax > 0.0f;

    /// <summary>
    /// How long the next stretch of quiet lasts, in seconds.
    /// </summary>
    /// <remarks>
    /// Anywhere from minimumSilence up to the longest the player allows, except for the first one
    /// of a session, which is openingSilence and shorter than the rest. The pause between two
    /// ambient tracks is minutes long on purpose, but the same wait right after the title track
    /// has played out lands differently: nothing has been heard in the dungeon yet, so it reads
    /// as music that is not working rather than as pacing. The two are separate numbers because
    /// they answer to different things - one to the pacing of the dungeon, the other to a player
    /// who has just arrived - and tying the opening one to the floor between tracks moves it
    /// every time that floor is retuned.
    /// Both are capped by the maximum rather than added to it, so a player who drags the maximum
    /// down to the shortest it goes gets pauses of exactly that, not a floor above the ceiling.
    /// At zero this is not called at all.
    /// </remarks>
    private float TakeSilenceLength()
    {
        float longest = PlayerInput.AmbientSilenceMax;

        if (openingSilencePending)
        {
            openingSilencePending = false;
            return Mathf.Min(openingSilence, longest);
        }

        return Random.Range(Mathf.Min(minimumSilence, longest), longest);
    }

    private static bool IsExploringState(EMusicState state)
    {
        return state is EMusicState.ExploringA or EMusicState.ExploringB
            or EMusicState.ExploringC or EMusicState.ExploringD;
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
        // Whatever brought us here, the title track is no longer the thing that just stopped, so
        // the opening pause is off the table. Getting here at all means something else has had
        // the music - a fight, the map, a level up - or that an ambient track is about to start.
        // Measured, not guessed: in the log of 17 September 2026 a fight cut the title track 76
        // seconds in, and without this line the ten seconds meant for the menu handover would
        // have been spent on the quiet after that fight instead, where minutes are wanted.
        openingSilencePending = false;

        // Arriving from combat, victory, level up or death: do not snap an ambient track back on
        // the instant the other music ends - all four of those branches in Update() call straight
        // in here. Stop, arm the same silence the exploring branch of Update() counts down, and
        // let that branch start the track when the pause is over. The result is a stretch of
        // quiet after every fight.
        // currentPlayingTrack has to be cleared as well: StartStateMusic skips its work when the
        // track it draws is the one already playing, so a stale combat track name would block the
        // next selection.
        if (musicPlayer != null && SilenceBetweenTracks && !IsExploringState(musicState))
        {
            musicPlayer.StopMusic();
            currentPlayingTrack = "";
            musicState = EMusicState.ExploringA;
            exploringSilenceTimer = TakeSilenceLength();
            return;
        }

        // Randomly pick one of the exploring states
        EMusicState[] exploringStates = new EMusicState[] 
        { 
            EMusicState.ExploringA, 
            EMusicState.ExploringB, 
            EMusicState.ExploringC, 
            EMusicState.ExploringD 
        };

        // Extra ambient tracks widen the pool without replacing anything: with no extra folder
        // present this array is empty and the draw is exactly the vanilla one. Every track,
        // built in or extra, carries the same weight.
        string[] extraTracks = GetExtraExploringTracks();
        int pick = Random.Range(0, exploringStates.Length + extraTracks.Length);
        if (pick >= exploringStates.Length && musicPlayer != null)
        {
            string track = extraTracks[pick - exploringStates.Length];
            musicPlayer.SwitchTrack(track, false);
            if (musicPlayer.IsPlaying)
            {
                // The extra tracks have no EMusicState of their own, so ExploringA stands in and
                // Update() carries on treating us as exploring.
                musicState = EMusicState.ExploringA;
                currentPlayingTrack = track;
                return;
            }
            // File unreadable or deleted since the scan: fall through to a scene track in the
            // same frame, so the player never hears a gap.
        }

        musicState = exploringStates[Random.Range(0, exploringStates.Length)];
        StartStateMusic(musicState);
    }

    /// <summary>
    /// Extra ambient tracks available right now: the contents of the extra folder, filtered to the
    /// arrangement that matches the selected soundfont, plus the map track when the map is set to
    /// keep the music playing. The folder itself is scanned only once per session.
    /// </summary>
    private string[] GetExtraExploringTracks()
    {
        extraExploringTracks ??= ScanExtraExploringTracks();

        // UW2 ships two arrangements of each track: UWA* for AdLib, UWR* for Roland MT-32. Pick
        // the set that matches the soundfont selected in the options menu, so switching soundfont
        // switches the pool immediately with no stale cache. Files matching neither prefix are
        // always eligible, so any other music dropped in the folder just works.
        string prefix = PlayerPrefs.GetString("Options_SoundFont", "Soundfonts/MT32.sf2")
            .ToUpperInvariant().Contains("OPL") ? "UWA" : "UWR";
        string rejectedPrefix = prefix == "UWA" ? "UWR" : "UWA";

        List<string> pool = new();
        foreach (string track in extraExploringTracks)
        {
            if (!Path.GetFileName(track).ToUpperInvariant().StartsWith(rejectedPrefix))
            {
                pool.Add(track);
            }
        }

        string mapTrack = GetSpareMapTrack();
        if (!string.IsNullOrEmpty(mapTrack))
        {
            pool.Add(mapTrack);
        }

        return pool.ToArray();
    }

    /// <summary>
    /// The map track, when opening the map no longer plays it and nothing else does either.
    /// </summary>
    /// <remarks>
    /// With the map keeping the music playing, the track the map used to start is the one piece of
    /// the original score that is never heard: it belongs to no other state. So it joins the
    /// ambient rotation, where it is one more track and carries the same weight as every other.
    /// Tied to the option rather than added outright, because with the option off it is still the
    /// map's own track and hearing it while walking would give the map away.
    /// The file is checked here for the same reason ShouldLeaveMusicAloneOnMap checks it: the
    /// names come from the scene and the files come from the player's own copy of the original
    /// game, so a name in the list is not a track on disk.
    /// </remarks>
    private string GetSpareMapTrack()
    {
        if (!PlayerInput.KeepMusicOnMap)
        {
            return null;
        }

        string mapTrack = stateMusicTracks[(int)EMusicState.Map];
        if (string.IsNullOrEmpty(mapTrack))
        {
            return null;
        }

        try
        {
            return File.Exists(Path.Combine(GameDataPath.GetSoundPath(), mapTrack)) ? mapTrack : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string[] ScanExtraExploringTracks()
    {
        if (string.IsNullOrEmpty(extraExploringTracksFolder))
        {
            return Array.Empty<string>();
        }

        try
        {
            string folder = Path.Combine(GameDataPath.GetSoundPath(), extraExploringTracksFolder);
            if (!Directory.Exists(folder))
            {
                return Array.Empty<string>();
            }

            string[] files = Directory.GetFiles(folder, "*.xmi");
            string[] tracks = new string[files.Length];
            for (int i = 0; i < files.Length; ++i)
            {
                // MusicPlayer.SwitchTrack resolves the name against the sound folder, so a
                // relative path naming the subfolder is all it needs.
                tracks[i] = Path.Combine(extraExploringTracksFolder, Path.GetFileName(files[i]));
            }
            return tracks;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Could not scan '{extraExploringTracksFolder}' for extra ambient tracks: {e.Message}");
            return Array.Empty<string>();
        }
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

            // When the exploring track ends, stay quiet for a while before picking a new one.
            // Wall to wall music flattens the dungeon; it is the silence that makes the next
            // track land. Uses unscaledDeltaTime like the rest of Update, so the countdown keeps
            // running while the game is paused.
            if (!musicPlayer.IsPlaying)
            {
                if (exploringSilenceTimer == 0.0f && SilenceBetweenTracks)
                {
                    exploringSilenceTimer = TakeSilenceLength();
                }
                if (exploringSilenceTimer > 0.0f)
                {
                    exploringSilenceTimer -= Time.unscaledDeltaTime;
                    if (exploringSilenceTimer > 0.0f)
                    {
                        return;
                    }
                    exploringSilenceTimer = -1.0f;
                }
                StartExploringMusic();
                return;
            }
            // A track is playing, so there is no pause pending.
            exploringSilenceTimer = 0.0f;
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
    
    /// <summary>
    /// True when opening the map should leave the music alone: either that is what the player
    /// asked for, or there is no map track that can actually be played.
    /// </summary>
    /// <remarks>
    /// Switching to a track that cannot play kills the music instead of changing it, and there
    /// are two ways to get there. With no track configured, StartStateMusic falls through to
    /// StopMusic(), which nulls the sequencer and sends All Notes Off on every channel. With a
    /// track configured but absent from the player's data files, MusicPlayer.SwitchTrack stops
    /// the current track first and only then fails its File.Exists check, which is worse: Music
    /// still records the track as playing, so the state is a lie as well as silent. The second
    /// case is real rather than theoretical, because the track names come from the scene and the
    /// files come from whichever copy of the original game the player owns.
    /// Either way the map is silent and closing it restarts a random exploring track.
    /// EnterMapScreen and ExitMapScreen must take this early return together, or the way out
    /// reads a stateBeforeMap the way in never wrote.
    /// </remarks>
    private static bool ShouldLeaveMusicAloneOnMap()
    {
        // Options_KeepMusicOnMap, on by default, so the music carries on across the map unless
        // the player asks for the map's own track. This was a public field on the component until the option
        // existed, which meant the only way to turn it on was ticking it on Music.prefab: a
        // tracked asset, so the change showed up in git status, must never reach a commit, and
        // was lost every time the derived branches were rebuilt.
        if (PlayerInput.KeepMusicOnMap)
        {
            return true;
        }

        string mapTrack = sMusic.stateMusicTracks[(int)EMusicState.Map];
        if (string.IsNullOrEmpty(mapTrack))
        {
            return true;
        }

        try
        {
            // Same folder MusicPlayer resolves track names against.
            return !File.Exists(Path.Combine(GameDataPath.GetSoundPath(), mapTrack));
        }
        catch (Exception)
        {
            return true;
        }
    }

    public static void EnterMapScreen()
    {
        if (sMusic != null)
        {
            if (ShouldLeaveMusicAloneOnMap())
            {
                return;
            }
            
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
            if (ShouldLeaveMusicAloneOnMap())
            {
                return;
            }
            
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

using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class MessageClip
{
    public string contains;
    public AudioClip clip;
}

public class Messages : MonoBehaviour
{
    public GUIStyle fontStyle;
    public AudioClip newMessage;

    public MessageClip[] clips;

    private static Messages sMessages;
    private AudioSource currentSource;

    struct SDisplayMessage
    {
        public string message;
        public float time;
    }

    private List<SDisplayMessage> messages = new ();

    struct SPendingMessage
    {
        public string message;
        public float time;
        public float delay;
    }

    /// <summary>Messages waiting for their delay to run out, in the order they were added.</summary>
    private List<SPendingMessage> pendingMessages = new ();

    /// <summary>While above zero, Add() holds every message back by this many seconds.</summary>
    private static float sAddDelay;

    protected void Start()
    {
        sMessages = this;
    }

    protected void Update()
    {
        // BeginDelay() and EndDelay() come in pairs within one call, so by now the delay is off;
        // this only matters if a spell threw in between, which would leave every message late.
        sAddDelay = 0.0f;

        // Game time, not real time: with timeScale at 0 - paused, on the map, in a conversation,
        // on the save screen - a delayed message waits too, instead of turning up behind it.
        for (int i = 0; i < pendingMessages.Count; )
        {
            SPendingMessage pending = pendingMessages[i];
            pending.delay -= Time.deltaTime;
            if (pending.delay <= 0.0f)
            {
                pendingMessages.RemoveAt(i);
                AddMessageInternal(pending.message, pending.time);
            }
            else
            {
                pendingMessages[i] = pending;
                ++i;
            }
        }

        for (int i = messages.Count - 1; i >= 0; --i)
        {
            SDisplayMessage mess = messages[i];
            mess.time -= Time.deltaTime;
            if (mess.time < 0.0f)
            {
                messages.RemoveAt(i);
            }
            else
            {
                messages[i] = mess;
            }
        }
        while (messages.Count > 4)
        {
            messages.RemoveAt(0);
        }
    }

    protected void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.Messages;

        if (Event.current.type == EventType.Repaint)
        {
            // draw the messages
            string allMessages = "";
            for (int i = 0; i < messages.Count; ++i)
            {
                allMessages += messages[i].message;
                if (!allMessages.EndsWith('\n'))
                {
                    allMessages += '\n';
                }
            }

            allMessages.Trim('\n');

            fontStyle.normal.textColor = new Color(0, 0, 0, 1.0f);
            GUI.Label(new Rect(380, Screen.height - 140, Screen.width / 2, 120), allMessages, fontStyle);
            fontStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(381, Screen.height - 141, Screen.width / 2, 120), allMessages, fontStyle);
        }
    }

    public static void Add(string messageText, float time = 7.0f)
    {
        if (sAddDelay > 0.0f)
        {
            AddLater(messageText, sAddDelay, time);
        }
        else
        {
            sMessages.AddMessageInternal(messageText, time);
        }
    }

    /// <summary>
    /// Shows a message once delay seconds of game time have passed, on screen and in its place
    /// among the others, so it can follow a line that is printed after it is added.
    /// </summary>
    public static void AddLater(string messageText, float delay, float time = 7.0f)
    {
        sMessages.pendingMessages.Add(new SPendingMessage { message = messageText, time = time, delay = delay });
    }

    /// <summary>Until EndDelay(), every message added comes out delay seconds later.</summary>
    public static void BeginDelay(float delay)
    {
        sAddDelay = delay;
    }

    public static void EndDelay()
    {
        sAddDelay = 0.0f;
    }

    public static void Add(int block, int index)
    {
        Add(StringLoader.GetString(block, index));
    }

    public static void Clear()
    {
        sMessages.messages.Clear();
        sMessages.pendingMessages.Clear();
    }

    void AddMessageInternal(string messageText, float time)
    {
        float actualTime = time;
        
        AudioClip actualClip = newMessage;
        bool randomizePitch = true;
        
        #if false
        foreach (var clip in clips)
        {
            if (messageText.Contains(clip.contains))
            {
                if (clip.clip != null)
                {
                    actualTime = clip.clip.length;
                    actualClip = clip.clip;
                    randomizePitch = false;

                    if (currentSource != null)
                    {
                        currentSource.Stop();
                        Destroy(currentSource.transform.gameObject);
                    }
                }
                else
                {
                    Debug.LogWarning($"Messages clip list has missing clip reference for '{clip.contains}'.");
                }
                break;
            }
        }
        #endif

        currentSource = Utils.PlayClip2d(actualClip, randomizePitch);
        
        SDisplayMessage mess = new SDisplayMessage { message = messageText, time = actualTime };
        messages.Add(mess);
    }
}

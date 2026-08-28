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

    protected void Start()
    {
        sMessages = this;
    }

    protected void Update()
    {
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
        sMessages.AddMessageInternal(messageText, time);
    }

    public static void Add(int block, int index)
    {
        Add(StringLoader.GetString(block, index));
    }

    public static void Clear()
    {
        sMessages.messages.Clear();
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

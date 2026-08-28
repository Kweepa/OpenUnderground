using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class Credits : MonoBehaviour
{
    public Texture2D background;
    public Texture2D font;
    public Texture2D redfont;

    public AudioClip turnPage;
    public AudioClip turnBack;
    
    public float timePerCharacter = 0.05f;
    public float minPageTime = 2.0f;

    private readonly int[] x =
    {
        1, 20, 35, 51, 70, 86, 103, 120, 139, 152, 170, 188, 210, 233, 251, 269, 288, 313, 333, 350, 367, 387, 408, 433, 454,
        476, 490, 505,
        518, 536, 557, 577, 598, 619, 639, 660, 680, 701,
        718, 740, 760
    };
    private readonly int[] w =
    {
        18, 14, 15, 17, 15, 16, 15, 17, 11, 16, 17, 20, 21, 16, 16, 17, 24, 17, 15, 16, 19, 19, 23, 18, 19,
        11, 11, 7,
        11, 15, 15, 15, 15, 15, 15, 15, 16, 15,
        20, 10, 15
    };
    private readonly string c = "abcdefghijklmnopqrstuvwxy<>.1234567890z @";

    private Texture2D[] ch;
    private Texture2D[] redCh;

    private readonly string[][] creds =
    {
        new []
        {
            "Designed by",
            "Paul Neurath",
            "",
            "Creative director",
            "Richard Garriott",
            "",
            "Producer",
            "Warren Spector",
            "",
            "Executive producer",
            "Dallas Snell",
        },
        new []
        {
            "Programming",
            "Jonathan Arnold",
            "Doug Church",
            "Jon Maiara",
            "Dan Schmidt",
            "Carlos Smith",
            "",
            "Art",
            "Carol Angell",
            "Doug Wike"
        },
        new []
        {
            "Writing",
            "Brad Freeman",
            "Dan Schmidt",
            "",
            "Manuals",
            "W.G. <Bill> Armintrout"
        },
        new []
        {
            "Composers",
            "George <the Fatman> Sanger",
            "Dave Govett",
            "",
            "Sound Effects",
            "Dan Schmidt",
            "",
            "Sound system",
            "John Miles",
            "",
            "Speech recording",
            "Randy Buck"
        },
        new []
        {
            "Voices",
            "G.P. Austin",
            "Martin Galway",
            "Richard Garriott",
            "Mary Margaret Ipser",
            "Brian Martin"
        },
        new []
        {
            "Quality assurance",
            "Michelle Bush",
            "Nick Carter",
            "Mike Chenault",
            "Jarrett Crippen",
            "Perry Finley",
            "Tim Hardy",
            "Kirk Hutcheon",
            "Robert Hill",
        },
        new []
        {
            "Quality assurance",
            "Mark Leblanc",
            "James Nance",
            "Kevin Potter",
            "Jeff Shelton",
            "Scott Shelton",
            "Tim Stellmach",
            "Perry Stokes",
            "Mark Vittek",
            "Kevin Wasserman"
        },
        new []
        {
            "Technical Consulting",
            "James Fleming",
            "Chris Green",
            "Mike Hulas",
            "Ed Nelson",
            "Matt Toschlog",
            "",
            "Special thanks",
            "James Fleming",
            "Tim Stellmach"
        },
        new []
        {
            "Unity Port",
            "Steve McCrea",
        },
        new []
        {
            "3D Objects",
            "Spherical Horse",
            "",
            "3D Characters",
            "Lil Pupinduy",
            "",
            "Visual Effects",
            "Joyce",
            "Steve",
            "",
            "New Portraits",
            "Ben Chandler",
        },
        new []
        {
            "Sound Effects",
            "hzsmith",
            "freesound.org",
            "",
            "synth",
            "meltysynth"
        },
        new []
        {
            "fonts",
            "<Avatar> by JG Ellsworth",
            "<Britannian Runes 1> by unknown",
            "<tetanus> by Tom Murphy 7",
            "<Graz> by Patrick Michael Murphy",
            "",
            "font tools",
            "Glyphr Studio", 
        },
        new []
        {
            "asset store assets",
            "casual rpg vfx...lana studio",
            "stylized water effect pack...namu",
            "stylized splashes vfx pack...malko vfx"
        },
        new []
        {
            "hacking shoulders",
            "jim <tsshp> cameron",
            "michael <vividos> fink",
            "hank morgan",
        },
        new []
        {
            "resources",
            "ultimacodex.com",
            "sircabirus.com",
            "ultima.fandom.com",
            "the underworld cluebook",
            "ttlg.com",
            "bootstrike.com",
            "",
            "Quality Assurance",
            "Basara Dragon"
        }
    };
    
    private Texture2D copy;

    private readonly Dictionary<string, int> kern = new ();

    private int GetIndex(char a)
    {
        int ww = 0;
        for (; ww < c.Length; ++ww)
        {
            if (c[ww] == a)
            {
                return ww;
            }
        }
        // space
        return c.Length - 1;
    }

    private int GetKerning(char a, char b)
    {
        string key = a.ToString() + b.ToString();
        if (kern.TryGetValue(key, out int value))
        {
            return value;
        }

        // find the distance such that the red pixels are two pixels apart
        int ai = GetIndex(a);
        int bi = GetIndex(b);

        // special case for punctuation
        if (a is '<' or '>' or '.' or ' ' || b is '<' or '>' or '.' or ' ')
        {
            int p = (a == '<' || b == '>') ? w[ai] - 4 : w[ai];
            kern[key] = p;
            return p;
        }
        
        Color[] ac = ch[ai].GetPixels();
        Color[] bc = ch[bi].GetPixels();

        int k = 0;

        for (int y = 0; y < ch[ai].height; ++y)
        {
            int ax = 0;
            for (int wx = w[ai] - 1; wx >= 0; --wx)
            {
                Color col = ac[ch[ai].width * y + wx]; 
                if (col.a > 0 && col.r > 0)
                {
                    ax = wx;
                    break;
                }
            }

            if (ax > 0)
            {
                int bx = w[bi];
                for (int wx = 0; wx < w[bi]; ++wx)
                {
                    Color col = bc[ch[bi].width * y + wx];  
                    if (col.a > 0 && col.r > 0)
                    {
                        bx = wx;
                        break;
                    }
                }

                if (bx < w[bi])
                {
                    int pk = ax + 3 - bx;
                    k = Math.Max(k, pk);
                }
            }
        }

        kern[key] = k;
        return k;
    }

    private int GetWidth(string m)
    {
        int width = 0;
        if (m.Length > 0)
        {
            char a = m[0];
            for (int i = 1; i < m.Length; ++i)
            {
                width += GetKerning(a, m[i]);
                a = m[i];
            }
            width += w[GetIndex(a)];
        }
        return width;
    }

    private int GetHeight(string[] m)
    {
        int height = 0;
        foreach (string line in m)
        {
            height += line.Length > 0 ? 30 : 15;
        }
        return height;
    }

    private void WriteCreditsOnTexture(string[] m)
    {
        int height = GetHeight(m);
        
        int yy = (400 + height) / 2 - 32;
        
        // write to a blank texture
        copy = new Texture2D(640, 400);
        Color[] cols = new Color[640 * 400];
        for (int i = 0; i < 640 * 400; ++i)
        {
            cols[i].a = 0.0f;
        }

        bool header = true;
        pageTime = 0.0f;
        foreach (string msgUpper in m)
        {
            string msg = msgUpper.ToLower();
            int width = GetWidth(msg);

            int xx = (640 - width) / 2;
            for (int k = 0; k < msg.Length; ++k)
            {
                pageTime += timePerCharacter;
                int ww = 0;
                for (; ww < c.Length; ++ww)
                {
                    if (c[ww] == msg[k])
                    {
                        break;
                    }
                }

                if (ww < c.Length)
                {
                    Texture2D let = header ? redCh[ww] : ch[ww];
                    Color[] letc = let.GetPixels();
                    for (int y = 0; y < let.height; ++y)
                    {
                        if (yy + y >= 0 && yy + y < 400)
                        {
                            int p = copy.width * (yy + y) + xx;
                            int q = let.width * y;
                            for (int wx = 0; wx < let.width; ++wx)
                            {
                                if (letc[q + wx].a >= 1.0f)
                                {
                                    cols[p + wx] = letc[q + wx];
                                }
                            }
                        }
                    }

                    if (k < msg.Length - 1)
                    {
                        xx += GetKerning(msg[k], msg[k+1]);
                    }
                }
            }

            yy -= msg.Length > 0 ? 30 : 15;

            if (header)
            {
                header = false;
            }
            else if (msg.Length == 0)
            {
                header = true;
            }
        }
        copy.SetPixels(cols);
        copy.filterMode = FilterMode.Point;
        copy.wrapMode = TextureWrapMode.Clamp;
        copy.Apply();

        pageTime = Mathf.Max(pageTime, minPageTime);

        if (!firstPage)
        {
            Utils.PlayClip2d(turnPage);
        }
        firstPage = false;
    }

    private void Start()
    {
        ch = new Texture2D[c.Length];
        redCh = new Texture2D[c.Length];
        for (int i = 0; i < ch.Length; ++i)
        {
            ch[i] = new Texture2D(w[i], font.height);
            redCh[i] = new Texture2D(w[i], font.height);
            Graphics.CopyTexture(font, 0, 0, x[i], 0, w[i], font.height, ch[i], 0, 0, 0, 0);
            Graphics.CopyTexture(redfont, 0, 0, x[i], 0, w[i], font.height, redCh[i], 0, 0, 0, 0);
            ch[i].Apply();
            redCh[i].Apply();
        }
    }

    private int page;
    private float pageTime;
    private bool firstPage;

    public void Play()
    {
        page = -1;
        pageTime = 0.0f;
        firstPage = true;
    }

    private void Update()
    {
        pageTime -= Time.deltaTime;

        if ((GameInput.CurrentGamepad?.dpad.left.wasPressedThisFrame ?? false) && page > 0)
        {
            --page;
            WriteCreditsOnTexture(creds[page]);
        }
        else if ((GameInput.CurrentGamepad?.dpad.right.wasPressedThisFrame ?? false) && page < creds.Length - 1)
        {
            ++page;
            WriteCreditsOnTexture(creds[page]);
        }
        else if (GameInput.CurrentGamepad?.bButton.wasPressedThisFrame ?? false)
        {
            Utils.PlayClip2d(turnBack);
            enabled = false;
        }
        else if (GameInput.CurrentMouse?.leftButton.wasPressedThisFrame ?? false)
        {
            Vector2 gui = GuiInput.ScreenToGuiMouse(GameInput.CurrentMouse.position.ReadValue());
            if (gui.y > Screen.height - 72f)
            {
                Utils.PlayClip2d(turnBack);
                enabled = false;
            }
            else if (gui.x < Screen.width * 0.33f && page > 0)
            {
                --page;
                WriteCreditsOnTexture(creds[page]);
            }
            else if (gui.x > Screen.width * 0.67f && page < creds.Length - 1)
            {
                ++page;
                WriteCreditsOnTexture(creds[page]);
            }
        }
        
        if (pageTime < 0.0f)
        {
            if (++page < creds.Length)
            {
                WriteCreditsOnTexture(creds[page]);
            }
            else
            {
                Utils.PlayClip2d(turnBack);
                enabled = false;
            }
        }
    }

    private void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.Credits;
        GUI.DrawTexture(Screen.safeArea, background);
        if (copy != null)
        {
            GUI.DrawTexture(Screen.safeArea, copy);
        }
    }
}

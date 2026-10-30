using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Game.Scripts
{
    public class Compass : MonoBehaviour
    {
        private static readonly int[] northOffsetsX = { 24, 16, 8, 4, 0, 0, 4, 12, 24, 32, 44, 48, 48, 44, 40, 32 };
        private static readonly int[] northOffsetsY = { 0, 2, 3, 6, 10, 14, 16, 20, 21, 19, 16, 14, 10, 5, 3, 2 };

        // these are the masks to strip out the backgrounds
        public Texture2D[] compassTexMask;
        
        // these are the stripped down textures
        private Texture2D[] compassTex;

        private bool fixedAlpha;

        private void ScrubUpTextures()
        {
            ScrubUpNorthPointerTextures();
            ScrubUpCompassBaseTextures();
        }

        private void ScrubUpNorthPointerTextures()
        {
            // remove any pixels that were originally background (not red)
            if (!fixedAlpha 
                && DataLoader.sDataLoader
                && DataLoader.sDataLoader.compassTex != null
                && DataLoader.sDataLoader.compassTex.Length > 0)
            {
                for (int i = 4; i < DataLoader.sDataLoader.compassTex.Length; ++i)
                {
                    Texture2D tex = DataLoader.sDataLoader.compassTex[i];
                    Color[] pix = tex.GetPixels();
                    for (int j = 0; j < pix.Length; ++j)
                    {
                        if (pix[j].a > 0)
                        {
                            if (pix[j].r <= pix[j].b)
                            {
                                pix[j].a = 0;
                            }
                        }
                    }
                    tex.SetPixels(pix);
                    tex.Apply();
                }

                fixedAlpha = true;
            }
        }

        private void ScrubUpCompassBaseTextures()
        {
            if (compassTex == null || compassTex.Length == 0 || compassTex[0] == null)
            {
                compassTex = new Texture2D[4];

                for (int i = 0; i < 4; ++i)
                {
                    Texture2D src = DataLoader.sDataLoader.compassTex[i];
                    Color[] srcPix = src.GetPixels();
                    Color[] mask = compassTexMask[i].GetPixels();
                    compassTex[i] = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
                    Color[] dst = compassTex[i].GetPixels();
                    for (int j = 0; j < srcPix.Length; ++j)
                    {
                        dst[j] = srcPix[j];
                        dst[j].a = mask[j].a;
                    }
                    compassTex[i].SetPixels(dst);
                    compassTex[i].wrapMode = TextureWrapMode.Clamp;
                    compassTex[i].filterMode = FilterMode.Point;
                    compassTex[i].Apply();
                }
            }
        }

        private void OnGUI()
        {
            if ((PlayerObject.Player.controlsDisabled & EControlMask.Cutscene) != 0)
            {
                return;
            }

            GUI.depth = (int)EGUIDepth.Compass;

            if (PlayerObject.HudHidden)
            {
                return;
            }

            ScrubUpTextures();
        
            int angle = (int)(PlayerObject.Player.mainCamera.transform.eulerAngles.y * 16.0f / 360.0f + 0.5f);

            int partial = angle & 3;
            int north = angle & 15;

            Texture2D partialTex = compassTex[partial];
            Texture2D northTex = DataLoader.sDataLoader.compassTex[4 + north];
            // In the HUD the compass is in reference pixels, at the interface scale. Over the map it
            // sits on the map's picture, which is stretched to the screen, so there it keeps to
            // screen pixels as the rest of the map does.
            bool onMap = (PlayerObject.Player.controlsDisabled & (EControlMask.Map)) > 0;
            float s = onMap ? 1.0f : UiScale.Factor;
            float x = s * (UiScale.Width - 260);
            float y = s * (UiScale.Height - 90);
            if (onMap)
            {
                x = Screen.width - 42 * Screen.width / 320 - 3 * partialTex.width / 2;
                y = Screen.height - 12 * Screen.height / 200 - 3 * partialTex.height / 2;
            }
            UiScale.DrawTexture(new Rect(x, y, s * 3 * partialTex.width, s * 3 * partialTex.height), partialTex);
            UiScale.DrawTexture(new Rect(x + s * 3 * northOffsetsX[north], y + s * 3 * northOffsetsY[north], s * 3 * northTex.width, s * 3 * northTex.height), northTex);
        }
    }
}

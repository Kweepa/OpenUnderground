using UnityEngine;
using System;

/// <summary>
/// Call the functions in here from a MonoBehaviour.OnGUI()
/// </summary>

public class DebugGUI
{
   public static void DrawTextOnGUI( Vector3 worldPosition, string text, Font font )
   {
      Vector3 screen = PlayerObject.Player.mainCamera.WorldToScreenPoint( worldPosition );
      if ( screen.z > 0.0f )
      {
         if ( text.Length > 0 )
         {
            DrawTextOnGUI( new Vector2( screen.x, screen.y ), text, font );
         }
      }
   }

   public delegate string GetTextDelegate();

   // DrawTextOnGUI with a delegate
   // So you don't have to generate the text unless it's actually going to be on screen.
   // Might want to update this to frustum cull the text, if it's too expensive.
   //
   // worldPosition - position of the top left of the text
   // getText - delegate to get the text string to display
   // font - the font to use to draw the text
   public static void DrawTextOnGUI(Vector3 worldPosition, GetTextDelegate getText, Font font)
   {
      Vector3 screen = PlayerObject.Player.mainCamera.WorldToScreenPoint(worldPosition);
      if (screen.z > 0.0f)
      {
         string text = getText();
         if (text.Length > 0)
         {
            DrawTextOnGUI(new Vector2(screen.x, screen.y), text, font);
         }
      }
   }

   private static void DrawTextOnGUI(Vector2 screenPosition, string text, Font font)
   {
      GUI.color = Color.white;
      GUI.contentColor = Color.white;
      GUI.backgroundColor = Color.black;
      TextMesh guiText = GetGUIText();
      if (guiText != null)
      {
         guiText.text = text;
         Rect textRect = new Rect();
         textRect.x = screenPosition.x;
         textRect.y = Screen.height - screenPosition.y;
         textRect.width = 100;
         textRect.height = 16;
         // draw the background and the drop shadow for the text
         GUIStyle style = GetBackGUIStyle();
         style.font = font;
         style.fontSize = 16;
         GUI.Label( textRect, guiText.text, style );
         // offset the text from its drop shadow
         --textRect.x;
         --textRect.y;
         // draw the text
         style = GetForeGUIStyle();
         style.font = font;
         style.fontSize = 16;
         GUI.Label( textRect, guiText.text, style );
         guiText.text = String.Empty;
      }
   }

   private static TextMesh GetGUIText()
   {
      // use the overridden operator so that if sGUIText is destroyed
      // we will create a new one. i.e., don't use != null here!
      if (sGUIText == null)
      {
         GameObject guiTextObject = new GameObject("DebugGuiText");
         sGUIText = guiTextObject.AddComponent<TextMesh>();
      }
      return sGUIText;
   }

   private static GUIStyle GetBackGUIStyle()
   {
      if (sBackGUIStyle == null)
      {
         sBackGUIStyle = new GUIStyle();
         Texture2D tex = new Texture2D(1, 1);
         tex.SetPixel(0, 0, Color.black);
         sBackGUIStyle.normal.background = tex;
         sBackGUIStyle.normal.textColor = Color.black;
      }
      return sBackGUIStyle;
   }

   private static GUIStyle GetForeGUIStyle()
   {
      if ( sForeGUIStyle == null )
      {
         sForeGUIStyle = new GUIStyle();
         sForeGUIStyle.normal.background = null;
         sForeGUIStyle.normal.textColor = Color.white;
      }
      return sForeGUIStyle;
   }

   private static TextMesh sGUIText;
   private static GUIStyle sBackGUIStyle;
   private static GUIStyle sForeGUIStyle;
}

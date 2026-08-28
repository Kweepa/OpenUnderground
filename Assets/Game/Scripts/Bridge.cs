using UnityEngine;

public class Bridge : UUObject
{
    private int GetFloorIndex()
    {
        // Read from this bridge's own level, not the currently loaded level
        // This ensures bridges initialize correctly even when loaded from save with a different level active
        if (levelIndex >= 1 && levelIndex < LevelLoader.sLevelLoader.levels.Length 
            && LevelLoader.sLevelLoader.levels[levelIndex] != null)
        {
            return LevelLoader.sLevelLoader.levels[levelIndex].floors[flags - 2];
        }
        // Fallback (shouldn't happen normally)
        return 0;
    }
    
    private Material GetFloorMatForBridge()
    {
        // Get floor material from this bridge's own level
        return LevelLoader.sLevelLoader.floorMat[GetFloorIndex()];
    }
    
   public override void PostLoadInitialize(bool restoredFromSave = false)
   {
      base.PostLoadInitialize(restoredFromSave);

      if (cachedRenderer != null)
      {
          if (flags < 2)
          {
              // 0 is wood, 1 is stone
              cachedRenderer.material = LevelLoader.sLevelLoader.GetTmObjectMaterial(30 + flags);
          }
          else
          {
              cachedRenderer.material = GetFloorMatForBridge();
          }
      }
   }
   
   /// <summary>
   /// Links this bridge to a tile, updating the tile's bridge references.
   /// This is called both during normal initialization and when restoring map data from save.
   /// </summary>
   public void LinkToTile(Tile t)
   {
       if (t == null) return;
       
       // keep track of the lowest bridge for AI pathing purposes (blocking swimmers)
       if (t.bridge == null || z < t.bridge.z)
       {
           t.upperBridge = t.bridge;
           t.bridge = this;
       }
       else if (t.bridge != null && z > t.bridge.z)
       {
           t.upperBridge = this;
       }
   }
   
   public override void WorldInitialize(Tile t, int tileX, int tileY)
   {
       base.WorldInitialize(t, tileX, tileY);
       LinkToTile(t);
   }
   
   public override float GetInteractionDistance()
   {
       // to allow you to interact from the next grid square
       return 6.0f;
   }

   public override string GetLookName()
   {
       if (levelIndex == 6 && objectIndex == 827)
       {
           return "a loose tile";
       }
       if (isEnchanted)
       {
           // we're pretending to be something else (eg some water)
           if (flags < 2)
           {
               return flags == 0 ? "wood" : "stone";
           }
           return StringLoader.GetString(10, 510 - GetFloorIndex());
       }
       return base.GetLookName();
   }
   
   public override string GetUnderCursorName()
   {
       return null;
   }

   public override void TryInteract(UUObject originator, UUObject sender, EAction action)
   {
       // prevent "You can't pick that up", unless it's the floor tile hiding the Wine
       if (sender != null || action != EAction.Use
           || (LevelLoader.sLevelLoader.loadedLevel == 6 && objectIndex == 827))
       {
           base.TryInteract(originator, sender, action);
       }
   }
}

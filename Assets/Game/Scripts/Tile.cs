using UnityEngine;

public class Tile
{
   public const float xzScale = 3.0f;
   public const float yScale = 3.0f / 4.0f;

   // derived data, from the loaded tiles
   public int type;
   public int floorHeight;
   public int floorTexture;
   public int doorFrob;
   public int wallTexture;
   public int firstObject;
   public int x;
   public int y;

   // for path finding, so transient
   public float g; // known cost to tile
   public float h; // heuristic to goal
   public ulong closedTag;
   public ulong openTag;
   public Tile parent;

   // these are set by the Door, Bridge, and stair in PostLoadInitialize
   public Door door;
   public Bridge bridge;
   public Bridge upperBridge;
   public bool isStair;

   // 
   public MovingPlatform movingPlatform;
   public int originalFloorHeight;
   public bool hidden;

   // these are set when creating level geometry
   public int decal;
   public int decalType;
   public int decalAngle;
   public int decalHeight;

   public float HeuristicTo(Tile t)
   {
      int dx = x - t.x;
      int dy = y - t.y;
      return Mathf.Sqrt(dx * dx + dy * dy);
   }

   public static int GetTileX( float x )
   {
      return (int) ( x / xzScale );
   }

   public static int GetTileY( float z )
   {
      return (int) ( z / xzScale );
   }

   /// <summary>
   /// Converts world X coordinate to sub-tile coordinate (0-7) within the tile.
   /// </summary>
   public static int GetSubTileX(float worldX)
   {
      int tileX = GetTileX(worldX);
      return EighthOfTile(worldX - xzScale * tileX);
   }

   /// <summary>
   /// Converts world Z coordinate to sub-tile coordinate (0-7) within the tile.
   /// </summary>
   public static int GetSubTileY(float worldZ)
   {
      int tileY = GetTileY(worldZ);
      return EighthOfTile(worldZ - xzScale * tileY);
   }

   /// <summary>
   /// Which eighth of the tile an offset into it falls in. The sub-tile coordinates of the level
   /// data are eighths, 0-7; this used to multiply the offset in metres by 2, which reaches only
   /// 0-5 on a tile of 3 m, so an object put back from a world position could land up to 75 cm
   /// from where it was.
   /// </summary>
   private static int EighthOfTile(float offset)
   {
      return Mathf.Clamp((int)(8.0f * offset / xzScale), 0, 7);
   }

   // gets a walkable center for pathfinding
   public Vector3 GetCenter()
   {
       switch (type)
       {
       case 2:
           return new Vector3((x + 0.75f) * xzScale, floorHeight * yScale, (y + 0.25f) * xzScale);
       case 3:
           return new Vector3((x + 0.25f) * xzScale, floorHeight * yScale, (y + 0.25f) * xzScale);
       case 4:
           return new Vector3((x + 0.75f) * xzScale, floorHeight * yScale, (y + 0.75f) * xzScale);
       case 5:
           return new Vector3((x + 0.25f) * xzScale, floorHeight * yScale, (y + 0.75f) * xzScale);
        default:
           return new Vector3((x + 0.50f) * xzScale, floorHeight * yScale, (y + 0.50f) * xzScale);
       }
   }

   // gets a center based on the grid box, ignoring diagonals
   public Vector3 GetGridCenter()
   {
       return new Vector3((x + 0.50f) * xzScale, floorHeight * yScale, (y + 0.50f) * xzScale);
   }

   public float GetFloorY(float px, float pz)
   {
       switch (type)
       {
        case 6:
            // y low to high
            {
                float yr = pz / xzScale - y;
                return Mathf.Lerp(floorHeight, floorHeight + 1, yr) * yScale;
            }
        case 7:
            // y high to low
            {
                float yr = pz / xzScale - y;
                return Mathf.Lerp(floorHeight + 1, floorHeight, yr) * yScale;
            }
        case 8:
            // x low to high
            {
                float xr = px / xzScale - x;
                return Mathf.Lerp(floorHeight, floorHeight + 1, xr) * yScale;
            }
        case 9:
            // x high to low
            {
                float xr = px / xzScale - x;
                return Mathf.Lerp(floorHeight + 1, floorHeight, xr) * yScale;
            }
        default:
            return floorHeight * yScale;
       }
   }

   public enum EDirection
   {
      North,
      East,
      South,
      West,
      NorthEast,
      SouthEast,
      SouthWest,
      NorthWest,
   }

   public bool DirectionBlocked( EDirection dir )
   {
      bool blocked = false;
      switch ( dir )
      {
      case EDirection.North:
         blocked = ( type == 2 || type == 3 );
         break;
      case EDirection.East:
         blocked = ( type == 3 || type == 4 );
         break;
      case EDirection.South:
         blocked = ( type == 4 || type == 5 );
         break;
      case EDirection.West:
         blocked = ( type == 5 || type == 2 );
         break;

      case EDirection.NorthEast:
         blocked = ( type == 3 );
         break;
      case EDirection.SouthEast:
         blocked = ( type == 5 );
         break;
      case EDirection.SouthWest:
         blocked = ( type == 4 );
         break;
      case EDirection.NorthWest:
         blocked = ( type == 2 );
         break;
      }
      return blocked;
   }

   public bool OppositeDirectionBlocked( EDirection dir )
   {
      EDirection[] opp =
      {
         EDirection.South,
         EDirection.West,
         EDirection.North,
         EDirection.East,
         EDirection.SouthWest,
         EDirection.NorthWest,
         EDirection.NorthEast,
         EDirection.SouthEast
      };

      return DirectionBlocked( opp[(int)dir] );
   }

   public ETerrainType GetFloorTerrain()
   {
       return UUTerrain.GetFloorTerrain(floorTexture);
   }

   public ETerrainType GetWallTerrain()
   {
       return UUTerrain.GetWallTerrain(wallTexture);
   }
}

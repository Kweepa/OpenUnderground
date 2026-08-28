using UnityEngine;

public enum ETerrainType
{
    Normal = 0,
    Dirt = 1, // added for planting the silver seed
    Ankh = 2,
    StairsUp = 3,
    StairsDown = 4,
    Pipe = 5,
    Grating = 6,
    Drain = 7,
    Princess = 8,
    Window = 9,
    Tapestry = 10,
    TexturedDoor = 11,
    Water = 16,
    Lava = 32,
    Waterfall = 64,
    Lavafall = 128,
};

public class UUTerrain
{
    private static ushort[] walls;
    public static ushort[] floors;
    public static void Load()
    {
        Stream stream = new Stream("../Data/terrain.dat");
        walls = stream.GetUShortArray(256);
        floors = stream.GetUShortArray(256);

        // modify so we can plant the silver seed
        foreach (int i in new[] { 5, 6, 7, 8, 9, 10, 11, 18, 19, 20, 21, 22, 28, 30, 35, 37, 38, 39, 40 })
        {
            floors[i] = (ushort) ETerrainType.Dirt;
        }
    }

    public static ETerrainType GetFloorTerrain(int textureIndex)
    {
        return (ETerrainType) floors[LevelLoader.GetLevel().floors[textureIndex]];
    }

    public static ETerrainType GetWallTerrain(int textureIndex)
    {
        int wall = LevelLoader.GetLevel().walls[textureIndex];
        if (wall == 206) // lavafall - hack since waterfall and lavafall don't seem to be marked
        {
            return ETerrainType.Lavafall;
        }
        if (wall == 198)
        {
            return ETerrainType.Waterfall;
        }
        return (ETerrainType) walls[wall];
    }
}

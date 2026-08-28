using System.Collections;
using UnityEngine;

public class Decal : UUObject
{
    public enum EDecal
    {
        Wall,
        Drain,
        Pipe,
        Grating,
        StairsUpArch,
        StairsDownArch,
        StairsUpRect,
        StairsDownRect,
        Window
    }

    public static EDecal GetDecalType(int wallIndex)
    {
        if (wallIndex is 3 or 20 or 40 or 54 or 71 or 92 or 121 or 132 or 134 or 150 or 170)
        {
            return EDecal.Drain;
        }
        switch (wallIndex)
        {
        case 137:
            return EDecal.StairsDownArch;
        case 139:
            return EDecal.StairsUpArch;
        case 140:
            return EDecal.StairsUpRect;
        case 141:
            return EDecal.StairsDownRect;
        }
        return EDecal.Wall;
    }
    
    public CutscenePlayer windowCutscene;
    public Vector3 arialOffset = new Vector3(0, 0, 0.24f);

    [EnumNamedArray(typeof(EDecal))]
    public MeshRenderer[] models;

    private int GetWallIndex()
    {
        // Read from this decal's own level, not the currently loaded level
        // This ensures decals initialize correctly even when loaded from save with a different level active
        if (levelIndex >= 1 && levelIndex < LevelLoader.sLevelLoader.levels.Length 
            && LevelLoader.sLevelLoader.levels[levelIndex] != null)
        {
            return LevelLoader.sLevelLoader.levels[levelIndex].walls[ownerIndex];
        }
        // Fallback (shouldn't happen normally)
        return 0;
    }
    
    private Material GetWallMatForDecal()
    {
        // Get wall material from this decal's own level
        return LevelLoader.sLevelLoader.wallMat[GetWallIndex()];
    }

    public override void PostLoadInitialize(bool restoredFromSave = false)
    {
        base.PostLoadInitialize(restoredFromSave);

        EDecal decalType = GetDecalType(GetWallIndex());
        MeshRenderer model = models[(int)decalType]; 
        if (model != null)
        {
            if (cachedRenderer != null && model != cachedRenderer)
            {
                cachedRenderer.gameObject.SetActive(false);
            }
            cachedRenderer = model;
            model.gameObject.SetActive(true);
            model.sharedMaterial = GetWallMatForDecal();
            if (model.sharedMaterials.Length > 1)
            {
                model.sharedMaterials[0] = model.sharedMaterial;
                model.sharedMaterials[1] = model.sharedMaterial;
            }
            
            MeshCollider col = transform.root.GetComponent<MeshCollider>();
            if (col != null)
            {
                Destroy(col);
            }
            col = model.gameObject.AddComponent<MeshCollider>();
            col.sharedMesh = model.transform.GetComponent<MeshFilter>().sharedMesh;
            col.convex = true;
        }

        // rename
        name = StringLoader.GetString(10, GetWallIndex());

        if (type != EObjectType.DecalWithCollision) // these work better without casting, eg between grave niches on level 7
        {
            SnapToWall();
        }
        
        // tidy up above the door to the slasher of veils
        if (levelIndex == 8)
        {
            switch (objectIndex)
            {
            case 528:
            case 532:
                // hide
                gameObject.SetActive(false);
                break;
            case 529:
            case 534:
                // cover the hole left by removing 528 & 532
                gameObject.transform.localScale = new Vector3(1.0f, 1.5f, 1.0f);
                break;
            }
        }
        else if (levelIndex == 7)
        {
            switch (objectIndex)
            {
            case 945:
                if (gameObject.activeInHierarchy)
                {
                    StartCoroutine(CreateArial());
                }
                else
                {
                    // Arial is restored from save as a UUObject; hide the decal so it does not reappear when level 7 is shown.
                    gameObject.SetActive(false);
                }
                break;
            }
        }
    }

    private IEnumerator CreateArial()
    {
        yield return null;

        if (!PlayerData.sData.releasedArial)
        {
            UUObject obj = LevelLoader.CreateObjectOfType(EObjectType.Arial);
            obj.transform.position = transform.position + arialOffset;
            obj.transform.rotation = Quaternion.identity;
            obj.levelIndex = LevelLoader.sLevelLoader.loadedLevel;
            obj.originalLevel = 0;
            obj.objectIndex = 0;
            LevelLoader.AddToWorld(obj);
        }
        gameObject.SetActive(false); // hide the decal
    }

    public override string GetLookName()
    {
        return StringLoader.GetString(10, GetWallIndex());
    }

    public override void TryInteract(UUObject originator, UUObject sender, EAction action)
    {
        // check to see if it's a window into the central shaft
        if (action == EAction.Use && GetWallIndex() is 127 or 142 && originator == null)
        {
            CutscenePlayer player = Instantiate(windowCutscene);
            int level = LevelLoader.sLevelLoader.loadedLevel;
            player.fixedFrame = level - 1;
        }
        else
        {
            // also run Look action as that's what some triggers accept
            // for example for the draining the pond puzzle on level 3 (to open the door and access the lever)
            // and the wall in Dantes' cell on level 7
            if (action == EAction.Use)
            {
                TryChainInteraction(EAction.Look);
            }
            TryChainInteraction(action);
        }
    }

    public override void DeleteFromTrap()
    {
        if (!name.Contains("princess"))
        {
            if (name.Contains("vine"))
            {
                ParticleSpawner.SpawnParticle(EParticleType.CollapsingPlantWall, transform.position, transform.rotation);
            }
            else if (name.Contains("wall"))
            {
                ParticleSpawner.SpawnParticle(EParticleType.CollapsingRockWall, transform.position, transform.rotation);
            }
        }
    }

    public override string GetUnderCursorName()
    {
        return null;
    }

    /// <summary>
    /// Optimized wall snapping that uses tile boundaries instead of raycasting.
    /// </summary>
    private void SnapToWall()
    {
        if (initialTile == null)
        {
            return;
        }

        Vector3 pos = transform.position;
        
        bool isDiagonalDecal = (angle & 1) == 1;
        if (isDiagonalDecal)
        {
            // Project current position onto the diagonal line perpendicular to the forward vector
            float offset = Vector3.Dot(transform.forward, pos - initialTile.GetGridCenter());
            transform.position = pos - offset * transform.forward;
        }
        else
        {
            // For non-diagonal decals, snap to the tile edge in the direction of the forward vector
            // Forward vector points into the wall, so we snap to the edge it's pointing toward
            
            switch (angle)
            {
            case 0: // North -> snap to north edge (maxZ)
                pos.z = (initialTile.y + 1) * Tile.xzScale;
                break;
            case 2: // East -> snap to east edge (maxX)
                pos.x = (initialTile.x + 1) * Tile.xzScale;
                break;
            case 4: // South -> snap to south edge (minZ)
                pos.z = initialTile.y * Tile.xzScale;
                break;
            case 6: // West -> snap to west edge (minX)
                pos.x = initialTile.x * Tile.xzScale;
                break;
            }
        
            // Preserve Y coordinate
            transform.position = pos;
        }
    }
}

using UnityEngine;

namespace Game.Scripts.CritterVariants
{
    public class Flyer : Critter
    {
        private Vector3 deathFall;
        private bool hitFloor;

        private float GetInitialHeight()
        {
            return Tile.yScale * (z / 8.0f);
        }

        public override void Initialize(ushort[] objData, byte[] critterData)
        {
            base.Initialize(objData, critterData);
        }

        // this needs to be inserted into the GetPath() function
        protected override Vector3 AdjustWanderPoint(Tile tile)
        {
            Vector3 point = tile.GetCenter();

            float initialHeight = GetInitialHeight();
            
            float flyHeightBasedOnInitialHeight = point.y + initialHeight - initialTile.GetCenter().y;
            float flyHeightBasedOnFloorHeight = point.y + 1.5f;
            float currentHeight = transform.position.y;

            float targetHeight = currentHeight;
            if (targetHeight < flyHeightBasedOnFloorHeight)
            {
                targetHeight = flyHeightBasedOnFloorHeight;
            }
            if (targetHeight < initialHeight)
            {
                targetHeight = initialHeight;
            }
            if (targetHeight > flyHeightBasedOnInitialHeight)
            {
                targetHeight = flyHeightBasedOnInitialHeight;
            }
            // keep it away from the ceiling
            if (targetHeight > 12.0f - minCeilingDistance)
            {
                targetHeight = 12.0f - minCeilingDistance;
            }
 
            point.y = targetHeight;

            return point;
        }

        protected override void SetState(EState newState)
        {
            base.SetState(newState);

            switch (state)
            {
            case EState.Die:
                deathFall = Vector3.zero;
                break;
            }
        }
        
        public override void Update()
        {
            switch (state)
            {
            default:
                base.Update();
                break;
            case EState.Die:
                if (!hitFloor)
                {
                    // make the flyer fall down, and when it hits the ground, determine whether to splash/burn or play the impact
                
                    deathFall += Time.deltaTime * 9.81f * Vector3.down;
                    CollisionFlags collisionFlags = cachedCharacterController.Move(deathFall * Time.deltaTime);
                    if (collisionFlags != CollisionFlags.None)
                    {
                        Tile t = LevelLoader.GetClosestTile(transform.position);
                        ETerrainType tt = CloseToFloor(transform.position, t) ? t.GetFloorTerrain() : ETerrainType.Normal;
                        switch (tt)
                        {
                        case ETerrainType.Lava:
                            SpawnSplash(DataLoader.sDataLoader.lavaSplash, DataLoader.sDataLoader.lavaSplashParticle, deathFall);
                            Utils.PlayClipOccluded(DataLoader.sDataLoader.lavaBurn, transform.position);
                            Utils.DestroyCritter(this);
                            break;
                        case ETerrainType.Water:
                            SpawnSplash(DataLoader.sDataLoader.splash, DataLoader.sDataLoader.waterSplashParticle, deathFall);
                            Utils.DestroyCritter(this);
                            break;
                        default:
                            cachedAnimator.CrossFade("Death_HitFloor", 0.0f);
                            hitFloor = true;
                            break;
                        }
                    }
                }
                else if (actionDone)
                {
                    SetState(EState.Dead);
                }
                break;
            }
        }
    }
}

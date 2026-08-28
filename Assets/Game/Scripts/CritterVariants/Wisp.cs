using UnityEngine;
using System.Collections.Generic;

namespace Game.Scripts.CritterVariants
{
    public class Wisp : Flyer
    {
        [Tooltip("Vertical bobbing magnitude (how high/low the Wisp bobs)")]
        public float bobMagnitude = 0.1f;
        
        [Tooltip("Vertical bobbing speed (how fast the Wisp bobs)")]
        public float bobSpeed = 1.0f;

        [Tooltip("Subobject to apply random rotation to (leave null to rotate the main object)")]
        public Transform rotationSubobject;

        [Tooltip("Minimum rotation duration in seconds")]
        public float minRotationDuration = 2.0f;

        [Tooltip("Maximum rotation duration in seconds")]
        public float maxRotationDuration = 5.0f;

        [Tooltip("Maximum rotational velocity in degrees per second")]
        public float maxRotationVelocity = 30.0f;

        [Tooltip("Number of overlapping rotations to play simultaneously")]
        public int numOverlappingRotations = 3;

        private Vector3 spawnPosition;
        private float wispBobTime;
        
        private class RotationLayer
        {
            public Vector3 axis;
            public float velocity;
            public float duration;
            public float time;
        }
        
        private List<RotationLayer> rotationLayers;

        public override void PostLoadInitialize(bool restoredFromSave = false)
        {
            base.PostLoadInitialize(restoredFromSave);
            // Store spawn position to keep Wisp stationary
            spawnPosition = transform.position;
            wispBobTime = 0.0f;
            
            // Initialize rotation system with multiple overlapping layers
            rotationLayers = new List<RotationLayer>();
            for (int i = 0; i < numOverlappingRotations; i++)
            {
                RotationLayer layer = new RotationLayer();
                GenerateNewRotation(layer);
                // Stagger start times so they don't all sync
                layer.time = Random.Range(0.0f, layer.duration);
                rotationLayers.Add(layer);
            }
        }

        private void GenerateNewRotation(RotationLayer layer)
        {
            // Generate random rotation axis
            layer.axis = new Vector3(
                Random.Range(-1.0f, 1.0f),
                Random.Range(-1.0f, 1.0f),
                Random.Range(-1.0f, 1.0f)
            ).normalized;
            
            // Generate random rotational velocity (degrees per second)
            layer.velocity = Random.Range(-maxRotationVelocity, maxRotationVelocity);
            
            // Generate random duration
            layer.duration = Random.Range(minRotationDuration, maxRotationDuration);
            
            // Reset rotation timer only (keep accumulated angle to maintain continuity)
            layer.time = 0.0f;
        }

        public override void Update()
        {
            // Call base Update for most functionality
            base.Update();
            
            // Apply gentle vertical bobbing while keeping X and Z locked to spawn position
            wispBobTime += Time.deltaTime;
            Vector3 currentPos = transform.position;
            currentPos.x = spawnPosition.x;
            currentPos.y = spawnPosition.y + bobMagnitude * Mathf.Sin(wispBobTime * bobSpeed);
            currentPos.z = spawnPosition.z;
            transform.position = currentPos;
            
            // Apply multiple overlapping rotations with S-curve fade in/out
            Transform targetTransform = rotationSubobject != null ? rotationSubobject : transform;
            Quaternion combinedRotation = Quaternion.identity;
            
            foreach (RotationLayer layer in rotationLayers)
            {
                layer.time += Time.deltaTime;
                
                if (layer.time >= layer.duration)
                {
                    // Current rotation period complete, generate new axis and velocity
                    GenerateNewRotation(layer);
                }
                
                // Calculate S-curve interpolation (smoothstep) that fades in and out
                // Goes from 0 -> 1 -> 0 over the duration
                float t = layer.time / layer.duration;
                float smoothT;
                if (t < 0.5f)
                {
                    smoothT = Mathf.SmoothStep(0, 1, 2 * t);
                }
                else
                {
                    smoothT = Mathf.SmoothStep(1, 0, (t - 0.5f) * 2.0f);
                }
                
                // Accumulate rotation angle based on rotational velocity and S-curve
                float rotationDelta = layer.velocity * Time.deltaTime * smoothT;
                
                // Create rotation quaternion for this layer
                Quaternion layerRotation = Quaternion.AngleAxis(rotationDelta, layer.axis);
                combinedRotation *= layerRotation;
            }
            
            // Apply combined rotation
            targetTransform.localRotation *= combinedRotation;
            
            // If somehow we're in a movement state, return to idle
            // This ensures Wisp stays at spawn point
            if (state == EState.Wander || state == EState.TurnToWander || 
                state == EState.Approach || state == EState.TurnToApproach ||
                state == EState.Flee || state == EState.TurnToFlee)
            {
                SetState(EState.Idle);
            }
        }

        protected override void SetState(EState newState)
        {
            // Prevent transitions to movement states to keep Wisp stationary
            if (newState == EState.Wander || newState == EState.TurnToWander || 
                newState == EState.Approach || newState == EState.TurnToApproach ||
                newState == EState.Flee || newState == EState.TurnToFlee)
            {
                // Don't allow movement states - stay idle instead
                return;
            }
            
            base.SetState(newState);
        }
    }
}

using UnityEngine;
using Unity.Profiling;
using System.Collections.Generic;
using System.Linq;

public class PhysicsPerformanceMonitor : MonoBehaviour
{
    private ProfilerRecorder physicsRecorder;
    private Queue<double> sampleBuffer = new Queue<double>();
    
    [Header("Settings")]
    public int samplesToAverage = 60; // Roughly 1 second at 60fps
    public float uiScale = 45f;

    private double smoothedMs;
    private GUIStyle labelStyle;

    void OnEnable()
    {
        physicsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Physics, "Physics.Simulate");
    }

    void OnDisable()
    {
        physicsRecorder.Dispose();
    }

    void Update()
    {
        if (physicsRecorder.Valid)
        {
            double currentSample = physicsRecorder.LastValue / 1_000_000.0;
            
            // Add new sample to the queue
            sampleBuffer.Enqueue(currentSample);

            // Remove old samples to keep the window size consistent
            if (sampleBuffer.Count > samplesToAverage)
            {
                sampleBuffer.Dequeue();
            }

            // Calculate the average of the current window
            smoothedMs = sampleBuffer.Average();
        }
    }

    void OnGUI()
    {
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = (int)uiScale;
            labelStyle.fontStyle = FontStyle.Bold;
        }

        // Color based on the smoothed value
        labelStyle.normal.textColor = (smoothedMs < 3.0) ? Color.green : 
                                     (smoothedMs < 7.0) ? Color.yellow : Color.red;

        string display = $"Avg Physics: {smoothedMs:F2}ms";
        
        // High-contrast drop shadow for the video
        GUI.color = new Color(0, 0, 0, 0.7f);
        GUI.Label(new Rect(23, 23, 800, 100), display, labelStyle);
        
        GUI.color = Color.white;
        GUI.Label(new Rect(20, 20, 800, 100), display, labelStyle);
    }
}

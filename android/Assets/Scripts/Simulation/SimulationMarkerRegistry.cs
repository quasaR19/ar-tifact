using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace ARArtifact.Simulation
{
    /// <summary>
    /// Tracks bindings between simulation-only TrackableIds and targetIds to help resolve
    /// the correct artifact when relying on XR Simulation.
    /// </summary>
    public static class SimulationMarkerRegistry
    {
#if UNITY_EDITOR
        private static readonly Dictionary<TrackableId, string> trackableToTarget = new();
        private static readonly Dictionary<string, TrackableId> targetToTrackable = new();

        public static bool TryGetTargetId(TrackableId trackableId, out string targetId)
        {
            if (trackableToTarget.TryGetValue(trackableId, out targetId))
            {
                // Debug.Log($"[SimulationMarkerRegistry] Hit: {trackableId} -> {targetId}");
                return true;
            }

            // Debug.Log($"[SimulationMarkerRegistry] Miss: {trackableId}");
            targetId = null;
            return false;
        }

        public static bool TryGetTrackableId(string targetId, out TrackableId trackableId)
        {
            return targetToTrackable.TryGetValue(targetId, out trackableId);
        }

        public static void Register(TrackableId trackableId, string targetId)
        {
            if (trackableId == TrackableId.invalidId || string.IsNullOrEmpty(targetId))
            {
                Debug.LogWarning("[SimulationMarkerRegistry] Invalid registration request");
                return;
            }

            trackableToTarget[trackableId] = targetId;
            targetToTrackable[targetId] = trackableId;
            Debug.Log($"[SimulationMarkerRegistry] Registered targetId='{targetId}' for trackableId={trackableId}. Total records: {trackableToTarget.Count}");
        }

        public static void Unregister(TrackableId trackableId, string targetId)
        {
            bool removed = false;
            if (trackableId != TrackableId.invalidId)
            {
                if (trackableToTarget.Remove(trackableId)) removed = true;
            }

            if (!string.IsNullOrEmpty(targetId))
            {
                if (targetToTrackable.Remove(targetId)) removed = true;
            }
            
            if (removed)
            {
                Debug.Log($"[SimulationMarkerRegistry] Unregistered targetId='{targetId}' / trackableId={trackableId}");
            }
        }

        public static void Clear()
        {
            Debug.Log($"[SimulationMarkerRegistry] Clearing registry. Was {trackableToTarget.Count} records.");
            trackableToTarget.Clear();
            targetToTrackable.Clear();
        }
#else
        public static bool TryGetTargetId(TrackableId trackableId, out string targetId)
        {
            targetId = null;
            return false;
        }

        public static bool TryGetTrackableId(string targetId, out TrackableId trackableId)
        {
            trackableId = TrackableId.invalidId;
            return false;
        }

        public static void Register(TrackableId trackableId, string targetId) { }
        public static void Unregister(TrackableId trackableId, string targetId) { }
        public static void Clear() { }
#endif
    }
}


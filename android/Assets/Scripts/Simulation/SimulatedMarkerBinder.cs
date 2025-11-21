using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;
#if UNITY_EDITOR
using UnityEngine.XR.Simulation;
#endif

namespace ARArtifact.Simulation
{
#if UNITY_EDITOR
    [RequireComponent(typeof(SimulatedTrackedImage))]
#endif
    public class SimulatedMarkerBinder : MonoBehaviour
    {
#if UNITY_EDITOR
        [SerializeField] private string markerId;
        private SimulatedTrackedImage trackedImage;
        private Coroutine registerRoutine;

        private void Awake()
        {
            trackedImage = GetComponent<SimulatedTrackedImage>();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying || trackedImage == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(markerId))
            {
                BeginRegistration();
            }
        }

        public void Initialize(string newMarkerId)
        {
            markerId = newMarkerId;
            Debug.Log($"[SimulatedMarkerBinder] Initialize called with markerId='{markerId}' for {gameObject.name}");

            if (!isActiveAndEnabled || trackedImage == null)
            {
                Debug.LogWarning($"[SimulatedMarkerBinder] Cannot start registration: isActiveAndEnabled={isActiveAndEnabled}, trackedImage={trackedImage}");
                return;
            }

            BeginRegistration();
        }

        private void BeginRegistration()
        {
            if (registerRoutine != null)
            {
                StopCoroutine(registerRoutine);
            }

            registerRoutine = StartCoroutine(RegisterWhenReady());
        }

        private IEnumerator RegisterWhenReady()
        {
            // Debug.Log($"[SimulatedMarkerBinder] Waiting for valid TrackableId for {markerId}...");
            // TrackableId assigned in SimulatedTrackedImage.Awake, but ensure it's ready.
            int waitFrames = 0;
            while (trackedImage.trackableId == TrackableId.invalidId)
            {
                waitFrames++;
                yield return null;
            }

            if (waitFrames > 0)
            {
                Debug.Log($"[SimulatedMarkerBinder] Waited {waitFrames} frames for TrackableId on {markerId}");
            }

            SimulationMarkerRegistry.Register(trackedImage.trackableId, markerId);
            registerRoutine = null;
        }

        private void OnDisable()
        {
            Cleanup();
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        private void Cleanup()
        {
            if (registerRoutine != null)
            {
                StopCoroutine(registerRoutine);
                registerRoutine = null;
            }

            if (trackedImage != null && !string.IsNullOrEmpty(markerId))
            {
                SimulationMarkerRegistry.Unregister(trackedImage.trackableId, markerId);
            }
        }
#else
        public void Initialize(string newMarkerId) { }
#endif
    }
}


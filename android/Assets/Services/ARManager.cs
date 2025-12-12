using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ARArtifact.Services
{
    public class ARManager : MonoBehaviour
    {
        public static ARManager Instance { get; private set; }

        [SerializeField] private ARSession arSession;
        [SerializeField] private ARTrackedImageManager trackedImageManager;

        public bool IsARAvailable { get; private set; } = false;
        public bool IsARInitializing { get; private set; } = false;
        
        public event Action<string> OnStatusChanged;
        public event Action<bool> OnARAvailabilityChanged;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
                return;
            }

#if !UNITY_EDITOR
            if (arSession == null)
            {
                arSession = FindFirstObjectByType<ARSession>();
            }
            
            if (trackedImageManager == null)
            {
                trackedImageManager = FindFirstObjectByType<ARTrackedImageManager>();
            }
#endif
        }

        public void InitializeAR(Action<bool> onComplete = null)
        {
            if (IsARInitializing)
            {
                OnStatusChanged?.Invoke("Инициализация AR уже запущена...");
                return;
            }

            StartCoroutine(InitializeARRoutine(onComplete));
        }

        private IEnumerator InitializeARRoutine(Action<bool> onComplete)
        {
            IsARInitializing = true;
            
#if UNITY_EDITOR
            OnStatusChanged?.Invoke("AR готов к работе (Simulation Mode)");
            yield return new WaitForSeconds(0.5f);
            
            InitializeMarkerLibrary();
            
            IsARAvailable = true;
            IsARInitializing = false;
            OnARAvailabilityChanged?.Invoke(true);
            onComplete?.Invoke(true);
            yield break;
#else
            OnStatusChanged?.Invoke("Проверка доступности AR...");

            if (ARSession.state == ARSessionState.None || ARSession.state == ARSessionState.CheckingAvailability)
            {
                yield return ARSession.CheckAvailability();
            }

            if (ARSession.state == ARSessionState.Unsupported)
            {
                Debug.LogError("[ARManager] AR не поддерживается на этом устройстве");
                IsARAvailable = false;
                IsARInitializing = false;
                OnStatusChanged?.Invoke("AR не поддерживается на этом устройстве.");
                OnARAvailabilityChanged?.Invoke(false);
                onComplete?.Invoke(false);
                yield break;
            }

            if (ARSession.state == ARSessionState.NeedsInstall)
            {
                OnStatusChanged?.Invoke("Установка AR сервисов...");
                yield return ARSession.Install();
                
                if (ARSession.state != ARSessionState.Ready)
                {
                    Debug.LogError($"[ARManager] Не удалось установить AR сервисы. Состояние: {ARSession.state}");
                    IsARAvailable = false;
                    IsARInitializing = false;
                    OnStatusChanged?.Invoke($"Не удалось установить AR сервисы (State: {ARSession.state})");
                    OnARAvailabilityChanged?.Invoke(false);
                    onComplete?.Invoke(false);
                    yield break;
                }
            }
            
            if (ARSession.state == ARSessionState.Ready || 
                ARSession.state == ARSessionState.SessionInitializing || 
                ARSession.state == ARSessionState.SessionTracking)
            {
                if (arSession != null)
                {
                    OnStatusChanged?.Invoke("Запуск AR сессии...");
                    
                    arSession.gameObject.SetActive(true);
                    arSession.enabled = true;
                    
                    yield return new WaitForSeconds(0.5f);
                    
                    float timeout = 10f;
                    float elapsed = 0f;
                    
                    while (ARSession.state < ARSessionState.SessionInitializing && elapsed < timeout)
                    {
                        elapsed += Time.deltaTime;
                        yield return null;
                    }

                    if (ARSession.state >= ARSessionState.SessionInitializing)
                    {
                        IsARAvailable = true;
                        OnStatusChanged?.Invoke("AR готов к работе");
                        
                        yield return new WaitForSeconds(0.3f);
                        
                        if (trackedImageManager != null && !trackedImageManager.enabled)
                        {
                            trackedImageManager.enabled = true;
                            InitializeMarkerLibrary();
                        }
                        
                        OnARAvailabilityChanged?.Invoke(true);
                        onComplete?.Invoke(true);
                    }
                    else
                    {
                        Debug.LogError($"[ARManager] Таймаут запуска AR сессии. Состояние: {ARSession.state}");
                        IsARAvailable = false;
                        OnStatusChanged?.Invoke($"Таймаут запуска AR сессии. Состояние: {ARSession.state}");
                        arSession.enabled = false;
                        arSession.gameObject.SetActive(false);
                        OnARAvailabilityChanged?.Invoke(false);
                        onComplete?.Invoke(false);
                    }
                }
                else
                {
                    Debug.LogError("[ARManager] ARSession компонент не найден!");
                    IsARAvailable = false;
                    OnStatusChanged?.Invoke("ARSession компонент не найден!");
                    OnARAvailabilityChanged?.Invoke(false);
                    onComplete?.Invoke(false);
                }
            }
            else
            {
                Debug.LogError($"[ARManager] Неподходящее состояние для запуска AR: {ARSession.state}");
                IsARAvailable = false;
                OnStatusChanged?.Invoke($"Не удалось инициализировать AR. Состояние: {ARSession.state}");
                OnARAvailabilityChanged?.Invoke(false);
                onComplete?.Invoke(false);
            }

            IsARInitializing = false;
#endif
        }

        private void InitializeMarkerLibrary()
        {
            // Ищем trackedImageManager если не найден
            if (trackedImageManager == null)
            {
                trackedImageManager = FindFirstObjectByType<ARTrackedImageManager>();
            }
            
            if (trackedImageManager == null)
            {
                Debug.LogWarning("[ARManager] ARTrackedImageManager не найден, пропускаем создание библиотеки");
                return;
            }

            var dynamicLibrary = Services.DynamicReferenceLibrary.Instance;
            if (dynamicLibrary != null)
            {
                dynamicLibrary.CreateReferenceLibrary(trackedImageManager);
            }
            else
            {
                Debug.LogWarning("[ARManager] DynamicReferenceLibrary.Instance не найден");
            }
        }
        
        public void StopAR()
        {
#if !UNITY_EDITOR
            if (arSession != null)
            {
                arSession.enabled = false;
                arSession.gameObject.SetActive(false);
            }
#endif
            IsARAvailable = false;
        }
        
        public void EnableCamera()
        {
#if !UNITY_EDITOR
            if (arSession != null && IsARAvailable)
            {
                arSession.enabled = true;
            }
            
            if (trackedImageManager != null && IsARAvailable)
            {
                trackedImageManager.enabled = true;
            }
#endif
        }
        
        public void DisableCamera()
        {
#if !UNITY_EDITOR
            if (arSession != null)
            {
                arSession.enabled = false;
            }
            
            if (trackedImageManager != null)
            {
                trackedImageManager.enabled = false;
            }
#endif
        }
    }
}


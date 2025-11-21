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
            Debug.Log("[ARManager] Awake вызван");
            
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                Debug.Log("[ARManager] Instance установлен");
            }
            else
            {
                Debug.Log("[ARManager] Дублирующийся экземпляр, уничтожаем");
                Destroy(gameObject);
                return;
            }

#if !UNITY_EDITOR
            // Только на устройстве ищем и управляем AR компонентами
            if (arSession == null)
            {
                arSession = FindFirstObjectByType<ARSession>();
                Debug.Log($"[ARManager] ARSession найден: {arSession != null}");
            }
            
            if (trackedImageManager == null)
            {
                trackedImageManager = FindFirstObjectByType<ARTrackedImageManager>();
                Debug.Log($"[ARManager] ARTrackedImageManager найден: {trackedImageManager != null}");
            }

            // Выключаем AR компоненты до контролируемой инициализации
            // if (arSession != null)
            // {
            //     Debug.Log("[ARManager] Выключаем ARSession до инициализации");
            //     arSession.enabled = false;
            //     arSession.gameObject.SetActive(false);
            // }
            // else
            // {
            //     Debug.LogWarning("[ARManager] ARSession не найден в сцене!");
            // }
#else
            Debug.Log("[ARManager] UNITY_EDITOR: Пропускаем поиск и управление AR компонентами");
#endif
        }

        public void InitializeAR(Action<bool> onComplete = null)
        {
            Debug.Log($"[ARManager] InitializeAR вызван. IsARInitializing: {IsARInitializing}");
            
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
            Debug.Log("[ARManager] InitializeARRoutine начата");
            
#if UNITY_EDITOR
            // В редакторе используется XR Simulation
            Debug.Log("[ARManager] UNITY_EDITOR: Режим симуляции");
            OnStatusChanged?.Invoke("AR готов к работе (Simulation Mode)");
            yield return new WaitForSeconds(0.5f);
            
            // Инициализируем библиотеку маркеров для симуляции
            InitializeMarkerLibrary();
            
            IsARAvailable = true;
            IsARInitializing = false;
            OnARAvailabilityChanged?.Invoke(true);
            onComplete?.Invoke(true);
            yield break;
#endif

            OnStatusChanged?.Invoke("Проверка доступности AR...");
            Debug.Log($"[ARManager] Текущее состояние ARSession: {ARSession.state}");

            // Проверяем доступность AR (без таймаута, это быстрая операция)
            if (ARSession.state == ARSessionState.None || ARSession.state == ARSessionState.CheckingAvailability)
            {
                Debug.Log("[ARManager] Вызов ARSession.CheckAvailability()");
                yield return ARSession.CheckAvailability();
                Debug.Log($"[ARManager] После CheckAvailability, состояние: {ARSession.state}");
            }

            // Проверяем результат проверки
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

            // Устанавливаем AR сервисы если нужно
            if (ARSession.state == ARSessionState.NeedsInstall)
            {
                Debug.Log("[ARManager] Требуется установка AR сервисов");
                OnStatusChanged?.Invoke("Установка AR сервисов...");
                yield return ARSession.Install();
                
                Debug.Log($"[ARManager] После установки AR сервисов, состояние: {ARSession.state}");
                
                // Проверяем результат установки
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

            Debug.Log($"[ARManager] Проверка состояния для запуска сессии: {ARSession.state}");
            
            // Включаем AR сессию
            if (ARSession.state == ARSessionState.Ready || 
                ARSession.state == ARSessionState.SessionInitializing || 
                ARSession.state == ARSessionState.SessionTracking)
            {
                if (arSession != null)
                {
                    Debug.Log("[ARManager] ARSession найден, запускаем...");
                    OnStatusChanged?.Invoke("Запуск AR сессии...");
                    
                    arSession.gameObject.SetActive(true);
                    arSession.enabled = true;
                    
                    Debug.Log("[ARManager] ARSession активирован, ждем инициализации...");
                    
                    // Ждем небольшую паузу для инициализации
                    yield return new WaitForSeconds(0.5f);
                    
                    Debug.Log($"[ARManager] После паузы, состояние: {ARSession.state}");
                    
                    // Ждем инициализации сессии с таймаутом
                    float timeout = 10f;
                    float elapsed = 0f;
                    
                    while (ARSession.state < ARSessionState.SessionInitializing && elapsed < timeout)
                    {
                        elapsed += Time.deltaTime;
                        yield return null;
                    }

                    Debug.Log($"[ARManager] После ожидания инициализации, состояние: {ARSession.state}, elapsed: {elapsed}");

                    if (ARSession.state >= ARSessionState.SessionInitializing)
                    {
                        Debug.Log("[ARManager] AR сессия успешно инициализирована!");
                        IsARAvailable = true;
                        OnStatusChanged?.Invoke("AR готов к работе");
                        
                        // Даем дополнительное время для полной инициализации
                        yield return new WaitForSeconds(0.3f);
                        
                        // Инициализируем трекинг изображений
                        if (trackedImageManager != null && !trackedImageManager.enabled)
                        {
                            Debug.Log("[ARManager] Включаем ARTrackedImageManager");
                            trackedImageManager.enabled = true;
                            InitializeMarkerLibrary();
                        }
                        
                        OnARAvailabilityChanged?.Invoke(true);
                        Debug.Log("[ARManager] Вызов callback с успехом");
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

            Debug.Log("[ARManager] InitializeARRoutine завершена");
            IsARInitializing = false;
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
                Debug.Log("[ARManager] Запуск создания динамической библиотеки маркеров");
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
#else
            Debug.Log("[ARManager] UNITY_EDITOR: Пропускаем остановку AR");
#endif
            IsARAvailable = false;
        }
    }
}


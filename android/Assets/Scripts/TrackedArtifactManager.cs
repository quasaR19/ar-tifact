using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ARArtifact.Services;
using ARArtifact.Simulation;
using ARArtifact.UI;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Управляет подменой плейсхолдеров на загруженные GLB модели при распознавании маркеров.
/// </summary>
public class TrackedArtifactManager : MonoBehaviour
{
    private const string LogPrefix = "[TrackedArtifactManager]";

    [SerializeField] private ARTrackedImageManager trackedImageManager;
    [SerializeField] private TrackedModelHost trackedModelHostPrefab;
    [SerializeField] private bool verboseLogging = true;

    private ArtifactService artifactService;
    private ModelSceneManager modelSceneManager;
    private VideoSceneManager videoSceneManager;
    private readonly Dictionary<TrackableId, TrackedArtifactInstance> trackedInstances = new();
    
    // Кеш для хостов по trackableId для оптимизации производительности
    private readonly Dictionary<TrackableId, TrackedModelHost> hostCache = new();
    
    // Отслеживание активных запросов для предотвращения дублирования
    private readonly HashSet<string> activeRequests = new HashSet<string>();
    
    // События для уведомления о распознавании таргетов
    public event System.Action<string> OnTargetRecognized; // targetId
    public event System.Action<string> OnTargetLost; // targetId
    public event System.Action<string, string> OnArtifactFound; // targetId, artifactName (legacy, для обратной совместимости)
    public event System.Action<string, string, string> OnArtifactFoundWithId; // targetId, artifactId, artifactName
    public event System.Action<string, bool> OnTargetPinStateChanged; // targetId, isPinned

    private void Awake()
    {
        if (trackedImageManager == null)
        {
            trackedImageManager = FindFirstObjectByType<ARTrackedImageManager>();
        }

        artifactService = ArtifactService.Instance;
        modelSceneManager = ARArtifact.Services.ModelSceneManager.Instance;
        videoSceneManager = ARArtifact.Services.VideoSceneManager.Instance;
    }

    private void OnEnable()
    {
        if (trackedImageManager != null)
        {
            trackedImageManager.trackablesChanged.AddListener(OnTrackedImagesChanged);
        }
    }

    private void OnDisable()
    {
        if (trackedImageManager != null)
        {
            trackedImageManager.trackablesChanged.RemoveListener(OnTrackedImagesChanged);
        }

        foreach (var kvp in trackedInstances)
        {
            if (kvp.Value.Host != null)
            {
                kvp.Value.Host.ResetToPlaceholder();
            }
        }

        trackedInstances.Clear();
    }

    private void OnTrackedImagesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
    {
        foreach (var trackedImage in args.added)
        {
            HandleTrackedImage(trackedImage);
        }

        foreach (var trackedImage in args.updated)
        {
            HandleTrackedImage(trackedImage);
        }

        foreach (var removed in args.removed)
        {
            if (trackedInstances.TryGetValue(removed.Key, out var instance))
            {
                if (!string.IsNullOrEmpty(instance.TargetId))
                {
                    OnTargetLost?.Invoke(instance.TargetId);
                }

                if (instance.Host != null)
                {
                    instance.Host.ResetToPlaceholder();
                }
                trackedInstances.Remove(removed.Key);
            }
            
            // Очищаем кеш при удалении таргета
            hostCache.Remove(removed.Key);
            targetSizeCache.Remove(removed.Key);
        }
    }

    private void HandleTrackedImage(ARTrackedImage trackedImage)
    {
        if (trackedImage == null)
        {
            return;
        }

        bool isTracking = trackedImage.trackingState == TrackingState.Tracking;
        bool isNewInstance = !trackedInstances.TryGetValue(trackedImage.trackableId, out var instance);
        bool shouldLogInfo = verboseLogging && isTracking && (isNewInstance || !instance.HasLoggedTargetInfo);

        var targetId = ResolveTargetIdFromTrackedImage(trackedImage, shouldLogInfo);
        
        if (string.IsNullOrEmpty(targetId))
        {
            return;
        }
        
        // Уведомляем о распознавании таргета
        if (isNewInstance && isTracking)
        {
            OnTargetRecognized?.Invoke(targetId);
        }

        if (isNewInstance)
        {
            instance = new TrackedArtifactInstance
            {
                TrackedImage = trackedImage,
                TargetId = targetId,
                Host = ResolveHost(trackedImage, targetId),
                HasLoggedTargetInfo = false,
                LastTrackingState = !isTracking // Initialize with opposite state to ensure SetTrackingActive is called
            };
        trackedInstances[trackedImage.trackableId] = instance;
        }
        else
        {
            instance.TrackedImage = trackedImage;
            instance.TargetId = targetId;
            if (instance.Host == null)
            {
                instance.Host = ResolveHost(trackedImage, targetId);
            }
            else
            {
                // Обновляем размер таргета при обновлении трекинга (только если изменился)
                UpdateHostTargetSizeIfNeeded(instance.Host, trackedImage);
            }
        }

        if (instance.Host == null)
        {
            return;
        }

        // Вызываем SetTrackingActive только если состояние трекинга изменилось
        // Это предотвращает ненужные вызовы и прерывание fade out корутин
        if (instance.LastTrackingState != isTracking)
        {
            instance.LastTrackingState = isTracking;
            
            // Вызываем SetTrackingActive на TrackedModelHost
            instance.Host.SetTrackingActive(isTracking);
            
            // Также вызываем SetTrackingActive на TrackedVideoHost, если он существует
            var videoHost = instance.Host.GetComponent<TrackedVideoHost>();
            if (videoHost != null)
            {
                // Обновляем размер таргета для видео хоста
                UpdateVideoHostTargetSizeIfNeeded(videoHost, trackedImage);
                videoHost.SetTrackingActive(isTracking);
            }
        }
        else
        {
            // Состояние не изменилось, но все равно обновляем размер таргета для видео хоста (если нужно)
            var videoHost = instance.Host.GetComponent<TrackedVideoHost>();
            if (videoHost != null)
            {
                UpdateVideoHostTargetSizeIfNeeded(videoHost, trackedImage);
            }
        }

        if (!isTracking)
        {
            OnTargetLost?.Invoke(targetId);
        }
        else
        {
            OnTargetRecognized?.Invoke(targetId);
        }

        if (shouldLogInfo)
        {
            instance.HasLoggedTargetInfo = true;
        }

        if (!isTracking)
        {
            return;
        }

        RequestArtifactForInstance(instance);
    }

    private TrackedModelHost ResolveHost(ARTrackedImage trackedImage, string targetId)
    {
        // Проверяем кеш
        if (hostCache.TryGetValue(trackedImage.trackableId, out var cachedHost))
        {
            if (cachedHost != null)
            {
                // Обновляем размер только если изменился
                UpdateHostTargetSizeIfNeeded(cachedHost, trackedImage);
                return cachedHost;
            }
            else
            {
                // Хост был уничтожен, удаляем из кеша
                hostCache.Remove(trackedImage.trackableId);
            }
        }
        
        // Ищем существующий хост
        var host = trackedImage.GetComponentInChildren<TrackedModelHost>();
        if (host != null)
        {
            UpdateHostTargetSizeIfNeeded(host, trackedImage);
            hostCache[trackedImage.trackableId] = host; // Кешируем
            return host;
        }

        if (trackedModelHostPrefab == null)
        {
            return null;
        }

        // Создаем новый хост
        var hostInstance = Instantiate(trackedModelHostPrefab, trackedImage.transform);
        hostInstance.name = $"TrackedModelHost_{targetId}";
        UpdateHostTargetSizeIfNeeded(hostInstance, trackedImage);
        hostCache[trackedImage.trackableId] = hostInstance; // Кешируем
        return hostInstance;
    }

    // Кеш размеров таргетов для оптимизации
    private readonly Dictionary<TrackableId, float> targetSizeCache = new();
    
    private void UpdateHostTargetSizeIfNeeded(TrackedModelHost host, ARTrackedImage trackedImage)
    {
        if (host == null || trackedImage == null)
        {
            return;
        }

        // Получаем размер таргета из ARTrackedImage
        Vector2 imageSize = trackedImage.size;
        if (imageSize.x == 0 || imageSize.y == 0)
        {
            // Если размер не определен, используем размер из referenceImage
            if (trackedImage.referenceImage != null)
            {
                imageSize = trackedImage.referenceImage.size;
            }
            else
            {
                return; // Не можем определить размер
            }
        }

        // Используем максимальный размер (диагональ) для ограничения модели
        float targetSize = Mathf.Max(imageSize.x, imageSize.y);
        
        // Проверяем кеш - обновляем только если размер изменился
        if (targetSizeCache.TryGetValue(trackedImage.trackableId, out var cachedSize))
        {
            if (Mathf.Approximately(cachedSize, targetSize))
            {
                return; // Размер не изменился, пропускаем обновление
            }
        }
        
        host.SetTargetSize(targetSize);
        targetSizeCache[trackedImage.trackableId] = targetSize; // Обновляем кеш
    }
    
    private void UpdateVideoHostTargetSizeIfNeeded(TrackedVideoHost host, ARTrackedImage trackedImage)
    {
        if (host == null || trackedImage == null)
        {
            return;
        }

        // Получаем размер таргета из ARTrackedImage
        Vector2 imageSize = trackedImage.size;
        if (imageSize.x == 0 || imageSize.y == 0)
        {
            // Если размер не определен, используем размер из referenceImage
            if (trackedImage.referenceImage != null)
            {
                imageSize = trackedImage.referenceImage.size;
            }
            else
            {
                return; // Не можем определить размер
            }
        }

        // Используем максимальный размер (диагональ) для ограничения видео
        float targetSize = Mathf.Max(imageSize.x, imageSize.y);
        
        // Проверяем кеш - обновляем только если размер изменился
        if (targetSizeCache.TryGetValue(trackedImage.trackableId, out var cachedSize))
        {
            if (Mathf.Approximately(cachedSize, targetSize))
            {
                return; // Размер не изменился, пропускаем обновление
            }
        }
        
        host.SetTargetSize(targetSize);
        targetSizeCache[trackedImage.trackableId] = targetSize; // Обновляем кеш
    }

    private string ResolveTargetIdFromTrackedImage(ARTrackedImage trackedImage, bool shouldLogInfo = false)
    {
        if (trackedImage == null)
        {
            return null;
        }

        if (SimulationMarkerRegistry.TryGetTargetId(trackedImage.trackableId, out var simulationTargetId))
        {
            return simulationTargetId;
        }

        if (trackedImage.referenceImage == null)
        {
            return null;
        }

        var referenceName = trackedImage.referenceImage.name;
        var referenceGuid = trackedImage.referenceImage.guid;
        var textureGuid = trackedImage.referenceImage.textureGuid;

        var library = DynamicReferenceLibrary.Instance;
        if (library != null)
        {
            if (library.TryGetTargetId(referenceGuid, textureGuid, referenceName, out var resolved))
            {
                return resolved;
            }
            else if (shouldLogInfo)
            {
                library.LogAllMappings();
            }
        }

        return referenceName;
    }
    
    public bool TogglePinForTarget(string targetId)
    {
        Debug.Log($"{LogPrefix} TogglePinForTarget: targetId={targetId}, trackedInstances.Count={trackedInstances.Count}");
        
        var instance = FindInstanceByTargetId(targetId);
        if (instance == null)
        {
            Debug.LogWarning($"{LogPrefix} TogglePinForTarget: Instance не найден для targetId={targetId}");
            // Выводим все доступные targetId для отладки
            foreach (var kvp in trackedInstances)
            {
                if (kvp.Value != null)
                {
                    Debug.Log($"{LogPrefix} Доступный instance: targetId={kvp.Value.TargetId}, Host={kvp.Value.Host != null}");
                }
            }
            return false;
        }
        
        if (instance.Host == null)
        {
            Debug.LogWarning($"{LogPrefix} TogglePinForTarget: Host == null для targetId={targetId}, TrackedImage={instance.TrackedImage != null}");
            return false;
        }
        
        Debug.Log($"{LogPrefix} TogglePinForTarget: Найден instance для targetId={targetId}, Host существует, текущее состояние isPinned={instance.Host.IsPinned}");
        
        bool newState = instance.Host.TogglePinned();
        OnTargetPinStateChanged?.Invoke(targetId, newState);
        return newState;
    }
    
    public bool TrySetPinState(string targetId, bool shouldPin)
    {
        var instance = FindInstanceByTargetId(targetId);
        if (instance?.Host == null)
        {
            return false;
        }
        
        bool result = instance.Host.SetPinned(shouldPin);
        OnTargetPinStateChanged?.Invoke(targetId, instance.Host.IsPinned);
        return result;
    }
    
    public bool IsTargetPinned(string targetId)
    {
        var instance = FindInstanceByTargetId(targetId);
        if (instance != null && instance.Host != null)
        {
            return instance.Host.IsPinned;
        }
        return false;
    }

    private void RequestArtifactForInstance(TrackedArtifactInstance instance)
    {
        if (artifactService == null)
        {
            return;
        }

        if (modelSceneManager == null)
        {
            // Логируем в MainScreen вместо консоли
            MainScreenController.LogToMainScreen("Ошибка: ModelSceneManager не инициализирован");
            Debug.LogWarning($"{LogPrefix} ModelSceneManager не инициализирован");
            return;
        }

        // КРИТИЧНО: Захватываем локальные копии для предотвращения race condition
        string requestedTargetId = instance.TargetId;
        TrackableId capturedTrackableId = instance.TrackedImage.trackableId;
        TrackedModelHost capturedHost = instance.Host;
        
        if (string.IsNullOrEmpty(requestedTargetId))
        {
            return;
        }

        // Проверяем, не загружена ли уже модель или видео для этого targetId
        if (capturedHost != null)
        {
            bool hasLoaded = capturedHost.HasLoadedModel;
            var videoHost = capturedHost.GetComponent<TrackedVideoHost>();
            if (videoHost != null)
            {
                hasLoaded = hasLoaded || videoHost.HasLoadedVideo;
            }
            
            if (hasLoaded)
            {
                return;
            }
        }

        // Проверяем, не выполняется ли уже запрос для этого targetId
        if (activeRequests.Contains(requestedTargetId))
        {
            Debug.Log($"{LogPrefix} Запрос для targetId={requestedTargetId} уже выполняется, пропускаем дубликат");
            return;
        }

        activeRequests.Add(requestedTargetId);

        artifactService.RequestArtifactForTarget(
            requestedTargetId,
            availability =>
            {
                // Получаем название артефакта из результата
                string artifactName = availability.DisplayName;
                if (string.IsNullOrEmpty(artifactName) && availability.Record != null)
                {
                    artifactName = availability.Record.name;
                }
                
                if (!string.IsNullOrEmpty(artifactName))
                {
                    OnArtifactFound?.Invoke(requestedTargetId, artifactName);
                    // Новое событие с artifactId
                    if (!string.IsNullOrEmpty(availability.ArtifactId))
                    {
                        OnArtifactFoundWithId?.Invoke(requestedTargetId, availability.ArtifactId, artifactName);
                    }
                }
                
                // Используем захваченный trackableId для повторного поиска актуального instance
                if (!trackedInstances.TryGetValue(capturedTrackableId, out var currentInstance))
                {
                    return;
                }

                if (currentInstance.Host == null)
                {
                    return;
                }

                if (!string.Equals(currentInstance.TargetId, requestedTargetId, StringComparison.Ordinal))
                {
                    return;
                }

                // Проверяем, не загружено ли уже медиа для этого артефакта
                bool alreadyLoaded = false;
                if (currentInstance.Host != null)
                {
                    alreadyLoaded = currentInstance.Host.HasLoadedArtifact(availability.ArtifactId);
                }
                
                var existingVideoHost = currentInstance.Host?.GetComponent<TrackedVideoHost>();
                if (existingVideoHost != null)
                {
                    alreadyLoaded = alreadyLoaded || existingVideoHost.HasLoadedArtifact(availability.ArtifactId);
                }
                
                if (alreadyLoaded)
                {
                    return;
                }

                // Захватываем актуальный хост из currentInstance
                TrackedModelHost actualHost = currentInstance.Host;
                if (actualHost == null)
                {
                    Debug.LogWarning($"{LogPrefix} ActualHost == null после повторной проверки, пропускаем");
                    return;
                }

                // Проверяем тип медиа и используем соответствующий менеджер
                if (availability.IsVideo)
                {
                    // Проверяем, не загружена ли уже 3D модель в этом хосте
                    if (actualHost.HasLoadedModel)
                    {
                        Debug.LogWarning($"{LogPrefix} [VIDEO] Пропуск размещения видео: в хосте уже загружена 3D модель для targetId={requestedTargetId}");
                        return;
                    }
                    
                    // Используем VideoSceneManager для размещения видео
                    Debug.Log($"{LogPrefix} [VIDEO] Запрос размещения видео через VideoSceneManager: artifactId={availability.ArtifactId}, targetId={requestedTargetId}");
                    
                    // Создаем или находим TrackedVideoHost
                    TrackedVideoHost videoHost = actualHost.GetComponent<TrackedVideoHost>();
                    if (videoHost == null)
                    {
                        // Создаем TrackedVideoHost на том же GameObject
                        videoHost = actualHost.gameObject.AddComponent<TrackedVideoHost>();
                        
                        // Hide model placeholder when video is loaded
                        if (actualHost != null)
                        {
                            actualHost.HidePlaceholder();
                        }
                    }
                    
                    // Обновляем размер таргета для видео хоста из trackedImage
                    if (currentInstance.TrackedImage != null)
                    {
                        UpdateVideoHostTargetSizeIfNeeded(videoHost, currentInstance.TrackedImage);
                    }
                    else
                    {
                        // Если не нашли trackedImage, используем значение по умолчанию
                        videoHost.SetTargetSize(0.1f);
                    }
                    
                    bool isYouTube = !string.IsNullOrEmpty(availability.VideoUrl) && 
                                    (availability.VideoUrl.Contains("youtube.com") || availability.VideoUrl.Contains("youtu.be"));
                    
                    // Получаем remoteUrl, mediaId и metadataJson из Record для автовосстановления и метаданных
                    string remoteUrl = null;
                    string mediaId = null;
                    string metadataJson = null;
                    if (availability.Record != null && availability.Record.media != null && availability.Record.media.Count > 0)
                    {
                        // Ищем первое видео в медиа
                        var videoMedia = availability.Record.media.FirstOrDefault(m => m.mediaType == "video");
                        if (videoMedia != null)
                        {
                            remoteUrl = videoMedia.remoteUrl;
                            mediaId = videoMedia.mediaId;
                            metadataJson = videoMedia.metadataJson; // Метаданные из кэша
                        }
                    }
                    
                    videoSceneManager.RequestVideoForHost(
                        availability.ArtifactId,
                        videoHost,
                        availability.LocalVideoPath,
                        availability.VideoUrl,
                        isYouTube,
                        () =>
                        {
                            Debug.Log($"{LogPrefix} [VIDEO] Видео успешно размещено в хосте: artifactId={availability.ArtifactId}");
                            activeRequests.Remove(requestedTargetId);
                        },
                        error =>
                        {
                            Debug.LogError($"{LogPrefix} [VIDEO] Ошибка размещения видео: artifactId={availability.ArtifactId}, error={error}");
                            activeRequests.Remove(requestedTargetId);
                        },
                        remoteUrl,
                        mediaId,
                        metadataJson);
                }
                else
                {
                    // Используем ModelSceneManager для размещения модели
                    Debug.Log($"{LogPrefix} [3D] Запрос размещения модели через ModelSceneManager: artifactId={availability.ArtifactId}, targetId={requestedTargetId}");
                    
                    // Получаем метаданные модели и remoteUrl
                    string metadataJson = null;
                    string remoteUrl = null;
                    if (availability.Record != null && availability.Record.media != null)
                    {
                        var modelMedia = availability.Record.media.FirstOrDefault(m => 
                            string.Equals(m.mediaType, "3d_model", StringComparison.OrdinalIgnoreCase));
                        if (modelMedia != null)
                        {
                            metadataJson = modelMedia.metadataJson;
                            remoteUrl = modelMedia.remoteUrl;
                        }
                    }
                    
                    // Используем actualHost как TrackedModelHost
                    if (actualHost == null)
                    {
                        string errorMessage = "ActualHost == null для размещения модели";
                        // Логируем в MainScreen вместо консоли
                        MainScreenController.LogToMainScreen($"Ошибка размещения модели: {errorMessage}", availability.ArtifactId);
                        Debug.LogWarning($"{LogPrefix} ActualHost == null для размещения модели");
                        return;
                    }
                    
                    modelSceneManager.RequestModelForHost(
                        availability.ArtifactId,
                        actualHost,
                        availability.LocalModelPath,
                        metadataJson,
                        () =>
                        {
                            Debug.Log($"{LogPrefix} [3D] Модель успешно размещена в хосте: artifactId={availability.ArtifactId}");
                            activeRequests.Remove(requestedTargetId);
                        },
                        error =>
                        {
                            // Логируем в MainScreen вместо консоли
                            MainScreenController.LogToMainScreen($"Ошибка размещения модели: {error}", availability.ArtifactId);
                            Debug.LogWarning($"{LogPrefix} [3D] Ошибка размещения модели: artifactId={availability.ArtifactId}, error={error}");
                            activeRequests.Remove(requestedTargetId);
                        },
                        remoteUrl);
                }
            },
            error =>
            {
                activeRequests.Remove(requestedTargetId);
            });
    }

    // Удалены методы ProcessModelCreationQueue, LoadModelCoroutine и CleanupOrphanedGLTFObjects
    // Теперь используется ModelSceneManager для управления размещением моделей на сцене

    private class TrackedArtifactInstance
    {
        public ARTrackedImage TrackedImage;
        public TrackedModelHost Host;
        public string TargetId;
        public bool HasLoggedTargetInfo;
        public bool LastTrackingState = false; // Track last tracking state to avoid unnecessary SetTrackingActive calls
    }
    
    private TrackedArtifactInstance FindInstanceByTargetId(string targetId)
    {
        if (string.IsNullOrEmpty(targetId))
        {
            return null;
        }
        
        foreach (var kvp in trackedInstances)
        {
            if (kvp.Value == null)
            {
                continue;
            }
            
            if (string.Equals(kvp.Value.TargetId, targetId, StringComparison.Ordinal))
            {
                return kvp.Value;
            }
        }
        
        return null;
    }
}


using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ARArtifact.Services;
using ARArtifact.Simulation;
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
    private readonly Dictionary<TrackableId, TrackedArtifactInstance> trackedInstances = new();
    
    // Кеш для хостов по trackableId для оптимизации производительности
    private readonly Dictionary<TrackableId, TrackedModelHost> hostCache = new();
    
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
    }

    private void OnEnable()
    {
        if (trackedImageManager != null)
        {
            trackedImageManager.trackablesChanged.AddListener(OnTrackedImagesChanged);
            // Debug.Log($"{LogPrefix} ARTrackedImageManager подключен, трекинг изображений активирован");
        }
        else
        {
            // Debug.LogError($"{LogPrefix} ARTrackedImageManager не назначен");
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
        // Debug.Log($"{LogPrefix} [КАМЕРА] Изменения в трекинге: добавлено={args.added.Count}, обновлено={args.updated.Count}, удалено={args.removed.Count}");
        
        foreach (var trackedImage in args.added)
        {
            // Debug.Log($"{LogPrefix} [КАМЕРА] Таргет добавлен: TrackableId={trackedImage.trackableId}, State={trackedImage.trackingState}");
            HandleTrackedImage(trackedImage);
        }

        foreach (var trackedImage in args.updated)
        {
            // Debug.Log($"{LogPrefix} [КАМЕРА] Таргет обновлен: TrackableId={trackedImage.trackableId}, State={trackedImage.trackingState}");
            HandleTrackedImage(trackedImage);
        }

        foreach (var removed in args.removed)
        {
            // Debug.Log($"{LogPrefix} [КАМЕРА] Таргет удален: TrackableId={removed.Key}");
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
            // Debug.LogWarning($"{LogPrefix} HandleTrackedImage: trackedImage == null");
            return;
        }

        bool isTracking = trackedImage.trackingState == TrackingState.Tracking;
        bool isNewInstance = !trackedInstances.TryGetValue(trackedImage.trackableId, out var instance);
        bool shouldLogInfo = verboseLogging && isTracking && (isNewInstance || !instance.HasLoggedTargetInfo);

        if (shouldLogInfo)
        {
            // Debug.Log($"{LogPrefix} [ТРЕКИНГ НАЧАТ] TrackableId={trackedImage.trackableId}");
            
            if (trackedImage.referenceImage != null)
            {
                // Debug.Log($"{LogPrefix} ReferenceImage.name: '{trackedImage.referenceImage.name}'");
                // Debug.Log($"{LogPrefix} ReferenceImage.guid: {trackedImage.referenceImage.guid}");
                // Debug.Log($"{LogPrefix} ReferenceImage.textureGuid: {trackedImage.referenceImage.textureGuid}");
            }
            else
            {
                // Debug.LogWarning($"{LogPrefix} ReferenceImage == null!");
            }
        }

        var targetId = ResolveTargetIdFromTrackedImage(trackedImage, shouldLogInfo);
        
        if (shouldLogInfo)
        {
            // Debug.Log($"{LogPrefix} [РАСПОЗНАНИЕ] Resolved targetId: '{targetId}'");
        }
        
        if (string.IsNullOrEmpty(targetId))
        {
            if (shouldLogInfo)
            {
                // Debug.LogWarning($"{LogPrefix} [РАСПОЗНАНИЕ] targetId пуст, пропускаем объект {trackedImage.trackableId}");
            }
            return;
        }
        
        // Уведомляем о распознавании таргета
        if (isNewInstance && isTracking)
        {
            // Debug.Log($"{LogPrefix} [РАСПОЗНАНИЕ] Таргет распознан: targetId='{targetId}'");
            OnTargetRecognized?.Invoke(targetId);
        }

        if (isNewInstance)
        {
            instance = new TrackedArtifactInstance
            {
                TrackedImage = trackedImage,
                TargetId = targetId,
                Host = ResolveHost(trackedImage, targetId),
                HasLoggedTargetInfo = false
            };
        trackedInstances[trackedImage.trackableId] = instance;
        }
        else
        {
            if (!string.Equals(instance.TargetId, targetId, StringComparison.Ordinal))
            {
                // Debug.LogWarning($"{LogPrefix} ⚠️ TargetId изменился! Старый: '{instance.TargetId}', Новый: '{targetId}'");
            }
            
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
            if (shouldLogInfo)
            {
                // Debug.LogWarning($"{LogPrefix} Не удалось найти TrackedModelHost для маркера {targetId}");
            }
            return;
        }

        instance.Host.SetTrackingActive(isTracking);

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
            // Debug.LogWarning($"{LogPrefix} Prefab TrackedModelHost не назначен, а существующий не найден");
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
    
    // Старый метод для обратной совместимости
    private void UpdateHostTargetSize(TrackedModelHost host, ARTrackedImage trackedImage)
    {
        UpdateHostTargetSizeIfNeeded(host, trackedImage);
    }

    private string ResolveTargetIdFromTrackedImage(ARTrackedImage trackedImage, bool shouldLogInfo = false)
    {
        if (trackedImage == null)
        {
            if (shouldLogInfo)
            {
                // Debug.LogError($"{LogPrefix} ResolveTargetIdFromTrackedImage: trackedImage == null");
            }
            return null;
        }

        if (SimulationMarkerRegistry.TryGetTargetId(trackedImage.trackableId, out var simulationTargetId))
        {
            if (shouldLogInfo)
            {
                // Debug.Log($"{LogPrefix} ✓ Resolved targetId via simulation registry: '{simulationTargetId}'");
            }
            return simulationTargetId;
        }
        else if (shouldLogInfo && Application.isEditor)
        {
             // Debug.LogWarning($"{LogPrefix} SimulationMarkerRegistry MISS for TrackableId={trackedImage.trackableId}. Fallback to library lookup.");
        }

        if (trackedImage.referenceImage == null)
        {
            if (shouldLogInfo)
            {
                // Debug.LogError($"{LogPrefix} ResolveTargetIdFromTrackedImage: referenceImage == null");
            }
            return null;
        }

        var referenceName = trackedImage.referenceImage.name;
        var referenceGuid = trackedImage.referenceImage.guid;
        var textureGuid = trackedImage.referenceImage.textureGuid;

        var library = DynamicReferenceLibrary.Instance;
        if (library == null)
        {
            if (shouldLogInfo)
            {
                // Debug.LogError($"{LogPrefix} DynamicReferenceLibrary.Instance == null, используем fallback");
            }
        }
        else
        {
            if (library.TryGetTargetId(referenceGuid, textureGuid, referenceName, out var resolved))
            {
                if (shouldLogInfo)
                {
                    // Debug.Log($"{LogPrefix} ✓ Resolved targetId via library: '{resolved}'");
                }
                return resolved;
            }
            else
            {
                if (shouldLogInfo)
                {
                    // Debug.LogWarning($"{LogPrefix} ✗ Не удалось разрешить targetId через library");
                    // Debug.Log($"{LogPrefix} Проверяем все доступные маппинги в библиотеке...");
                    library.LogAllMappings();
                }
            }
        }

        var fallback = referenceName;
        if (shouldLogInfo)
        {
            // Debug.LogWarning($"{LogPrefix} ⚠️ Используем fallback: referenceImage.name = '{fallback}'");
        }
        return fallback;
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
        // Debug.Log($"{LogPrefix} RequestArtifactForInstance: TrackableId={instance.TrackedImage.trackableId}, TargetId='{instance.TargetId}'");
        
        if (artifactService == null)
        {
            // Debug.LogError($"{LogPrefix} ArtifactService не инициализирован");
            return;
        }

        if (modelSceneManager == null)
        {
            Debug.LogError($"{LogPrefix} ModelSceneManager не инициализирован");
            return;
        }

        // КРИТИЧНО: Захватываем локальные копии для предотвращения race condition
        string requestedTargetId = instance.TargetId;
        TrackableId capturedTrackableId = instance.TrackedImage.trackableId;
        TrackedModelHost capturedHost = instance.Host;
        
        if (string.IsNullOrEmpty(requestedTargetId))
        {
            // Debug.LogWarning($"{LogPrefix} TargetId пуст при запросе артефакта");
            return;
        }

        // Проверяем, не загружена ли уже модель для этого targetId
        if (capturedHost != null && capturedHost.HasLoadedModel)
        {
            // Модель уже загружена в хосте, пропускаем запрос
            // Debug.Log($"{LogPrefix} Модель уже загружена для targetId={requestedTargetId}, пропускаем запрос");
            return;
        }

        // Debug.Log($"{LogPrefix} [БД] Запрос артефакта для targetId='{requestedTargetId}'");
        artifactService.RequestArtifactForTarget(
            requestedTargetId,
            availability =>
            {
                // Debug.Log($"{LogPrefix} [БД] Получен артефакт: targetId='{requestedTargetId}', artifactId='{availability.ArtifactId}'");
                
                // Получаем название артефакта из результата
                string artifactName = availability.DisplayName;
                if (string.IsNullOrEmpty(artifactName) && availability.Record != null)
                {
                    artifactName = availability.Record.name;
                }
                
                if (!string.IsNullOrEmpty(artifactName))
                {
                    // Debug.Log($"{LogPrefix} [БД] Название артефакта: '{artifactName}'");
                    OnArtifactFound?.Invoke(requestedTargetId, artifactName);
                    // Новое событие с artifactId
                    if (!string.IsNullOrEmpty(availability.ArtifactId))
                    {
                        OnArtifactFoundWithId?.Invoke(requestedTargetId, availability.ArtifactId, artifactName);
                    }
                }
                else
                {
                    // Debug.LogWarning($"{LogPrefix} [БД] Название артефакта не найдено для targetId='{requestedTargetId}'");
                }
                
                // Используем захваченный trackableId для повторного поиска актуального instance
                if (!trackedInstances.TryGetValue(capturedTrackableId, out var currentInstance))
                {
                    // Debug.LogWarning($"{LogPrefix} Instance не найден в trackedInstances для TrackableId={capturedTrackableId}");
                    return;
                }

                if (currentInstance.Host == null)
                {
                    // Debug.LogWarning($"{LogPrefix} Host == null, пропускаем");
                    return;
                }

                if (!string.Equals(currentInstance.TargetId, requestedTargetId, StringComparison.Ordinal))
                {
                    // Debug.LogWarning($"{LogPrefix} ⚠️ TargetId изменился! Текущий: '{currentInstance.TargetId}', Запрошенный: '{requestedTargetId}', игнорируем результат");
                    return;
                }

                if (currentInstance.Host.HasLoadedArtifact(availability.ArtifactId))
                {
                    // Debug.Log($"{LogPrefix} Модель уже загружена для targetId={requestedTargetId}, artifactId={availability.ArtifactId}");
                    return;
                }

                // Захватываем актуальный хост из currentInstance
                TrackedModelHost actualHost = currentInstance.Host;
                if (actualHost == null)
                {
                    Debug.LogWarning($"{LogPrefix} ActualHost == null после повторной проверки, пропускаем");
                    return;
                }

                // Получаем метаданные модели
                string metadataJson = null;
                if (availability.Record != null && availability.Record.media != null)
                {
                    var modelMedia = availability.Record.media.FirstOrDefault(m => 
                        string.Equals(m.mediaType, "3d_model", StringComparison.OrdinalIgnoreCase));
                    if (modelMedia != null)
                    {
                        metadataJson = modelMedia.metadataJson;
                    }
                }

                // Используем ModelSceneManager для размещения модели
                Debug.Log($"{LogPrefix} [3D] Запрос размещения модели через ModelSceneManager: artifactId={availability.ArtifactId}, targetId={requestedTargetId}");
                modelSceneManager.RequestModelForHost(
                    availability.ArtifactId,
                    actualHost,
                    availability.LocalModelPath,
                    metadataJson,
                    () =>
                    {
                        Debug.Log($"{LogPrefix} [3D] Модель успешно размещена в хосте: artifactId={availability.ArtifactId}");
                    },
                    error =>
                    {
                        Debug.LogError($"{LogPrefix} [3D] Ошибка размещения модели: artifactId={availability.ArtifactId}, error={error}");
                    });
            },
            error =>
            {
                // Debug.LogError($"{LogPrefix} [БД] Ошибка получения артефакта: targetId='{requestedTargetId}', error={error}");
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


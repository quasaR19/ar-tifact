using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using ARArtifact.Services;
using ARArtifact.Simulation;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityGLTF;

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
    private readonly Dictionary<TrackableId, TrackedArtifactInstance> trackedInstances = new();
    
    // События для уведомления о распознавании таргетов
    public event System.Action<string> OnTargetRecognized; // targetId
    public event System.Action<string, string> OnArtifactFound; // targetId, artifactName

    private void Awake()
    {
        if (trackedImageManager == null)
        {
            trackedImageManager = FindFirstObjectByType<ARTrackedImageManager>();
        }

        artifactService = ArtifactService.Instance;
    }

    private void OnEnable()
    {
        if (trackedImageManager != null)
        {
            trackedImageManager.trackablesChanged.AddListener(OnTrackedImagesChanged);
            Debug.Log($"{LogPrefix} ARTrackedImageManager подключен, трекинг изображений активирован");
        }
        else
        {
            Debug.LogError($"{LogPrefix} ARTrackedImageManager не назначен");
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
            if (kvp.Value.LoadCoroutine != null)
            {
                StopCoroutine(kvp.Value.LoadCoroutine);
            }
            kvp.Value.Host?.ResetToPlaceholder();
        }

        trackedInstances.Clear();
    }

    private void OnTrackedImagesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
    {
        Debug.Log($"{LogPrefix} [КАМЕРА] Изменения в трекинге: добавлено={args.added.Count}, обновлено={args.updated.Count}, удалено={args.removed.Count}");
        
        foreach (var trackedImage in args.added)
        {
            Debug.Log($"{LogPrefix} [КАМЕРА] Таргет добавлен: TrackableId={trackedImage.trackableId}, State={trackedImage.trackingState}");
            HandleTrackedImage(trackedImage);
        }

        foreach (var trackedImage in args.updated)
        {
            Debug.Log($"{LogPrefix} [КАМЕРА] Таргет обновлен: TrackableId={trackedImage.trackableId}, State={trackedImage.trackingState}");
            HandleTrackedImage(trackedImage);
        }

        foreach (var removed in args.removed)
        {
            Debug.Log($"{LogPrefix} [КАМЕРА] Таргет удален: TrackableId={removed.Key}");
            if (trackedInstances.TryGetValue(removed.Key, out var instance))
            {
                if (instance.LoadCoroutine != null)
                {
                    StopCoroutine(instance.LoadCoroutine);
                }
                instance.LoadCoroutine = null;
                instance.LoadingTargetId = null;
                instance.Host?.ResetToPlaceholder();
                trackedInstances.Remove(removed.Key);
            }
        }
    }

    private void HandleTrackedImage(ARTrackedImage trackedImage)
    {
        if (trackedImage == null)
        {
            Debug.LogWarning($"{LogPrefix} HandleTrackedImage: trackedImage == null");
            return;
        }

        bool isTracking = trackedImage.trackingState == TrackingState.Tracking;
        bool isNewInstance = !trackedInstances.TryGetValue(trackedImage.trackableId, out var instance);
        bool shouldLogInfo = isTracking && (isNewInstance || !instance.HasLoggedTargetInfo);

        if (shouldLogInfo)
        {
            Debug.Log($"{LogPrefix} [ТРЕКИНГ НАЧАТ] TrackableId={trackedImage.trackableId}");
            
            if (trackedImage.referenceImage != null)
            {
                Debug.Log($"{LogPrefix} ReferenceImage.name: '{trackedImage.referenceImage.name}'");
                Debug.Log($"{LogPrefix} ReferenceImage.guid: {trackedImage.referenceImage.guid}");
                Debug.Log($"{LogPrefix} ReferenceImage.textureGuid: {trackedImage.referenceImage.textureGuid}");
            }
            else
            {
                Debug.LogWarning($"{LogPrefix} ReferenceImage == null!");
            }
        }

        var targetId = ResolveTargetIdFromTrackedImage(trackedImage, shouldLogInfo);
        
        if (shouldLogInfo)
        {
            Debug.Log($"{LogPrefix} [РАСПОЗНАНИЕ] Resolved targetId: '{targetId}'");
        }
        
        if (string.IsNullOrEmpty(targetId))
        {
            if (shouldLogInfo)
            {
                Debug.LogWarning($"{LogPrefix} [РАСПОЗНАНИЕ] targetId пуст, пропускаем объект {trackedImage.trackableId}");
            }
            return;
        }
        
        // Уведомляем о распознавании таргета
        if (isNewInstance && isTracking)
        {
            Debug.Log($"{LogPrefix} [РАСПОЗНАНИЕ] Таргет распознан: targetId='{targetId}'");
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
                Debug.LogWarning($"{LogPrefix} ⚠️ TargetId изменился! Старый: '{instance.TargetId}', Новый: '{targetId}'");
            }
            
            instance.TrackedImage = trackedImage;
            instance.TargetId = targetId;
            if (instance.Host == null)
            {
                instance.Host = ResolveHost(trackedImage, targetId);
            }
            else
            {
                // Обновляем размер таргета при обновлении трекинга
                UpdateHostTargetSize(instance.Host, trackedImage);
            }
        }

        if (instance.Host == null)
        {
            if (shouldLogInfo)
            {
                Debug.LogWarning($"{LogPrefix} Не удалось найти TrackedModelHost для маркера {targetId}");
            }
            return;
        }

        instance.Host.SetTrackingActive(isTracking);

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
        var host = trackedImage.GetComponentInChildren<TrackedModelHost>();
        if (host != null)
        {
            UpdateHostTargetSize(host, trackedImage);
            return host;
        }

        if (trackedModelHostPrefab == null)
        {
            Debug.LogWarning($"{LogPrefix} Prefab TrackedModelHost не назначен, а существующий не найден");
            return null;
        }

        var hostInstance = Instantiate(trackedModelHostPrefab, trackedImage.transform);
        hostInstance.name = $"TrackedModelHost_{targetId}";
        UpdateHostTargetSize(hostInstance, trackedImage);
        return hostInstance;
    }

    private void UpdateHostTargetSize(TrackedModelHost host, ARTrackedImage trackedImage)
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
            imageSize = trackedImage.referenceImage.size;
        }

        // Используем максимальный размер (диагональ) для ограничения модели
        float targetSize = Mathf.Max(imageSize.x, imageSize.y);
        host.SetTargetSize(targetSize);
    }

    private string ResolveTargetIdFromTrackedImage(ARTrackedImage trackedImage, bool shouldLogInfo = false)
    {
        if (trackedImage == null)
        {
            if (shouldLogInfo)
            {
                Debug.LogError($"{LogPrefix} ResolveTargetIdFromTrackedImage: trackedImage == null");
            }
            return null;
        }

        if (SimulationMarkerRegistry.TryGetTargetId(trackedImage.trackableId, out var simulationTargetId))
        {
            if (shouldLogInfo)
            {
                Debug.Log($"{LogPrefix} ✓ Resolved targetId via simulation registry: '{simulationTargetId}'");
            }
            return simulationTargetId;
        }
        else if (shouldLogInfo && Application.isEditor)
        {
             Debug.LogWarning($"{LogPrefix} SimulationMarkerRegistry MISS for TrackableId={trackedImage.trackableId}. Fallback to library lookup.");
        }

        if (trackedImage.referenceImage == null)
        {
            if (shouldLogInfo)
            {
                Debug.LogError($"{LogPrefix} ResolveTargetIdFromTrackedImage: referenceImage == null");
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
                Debug.LogError($"{LogPrefix} DynamicReferenceLibrary.Instance == null, используем fallback");
            }
        }
        else
        {
            if (library.TryGetTargetId(referenceGuid, textureGuid, referenceName, out var resolved))
            {
                if (shouldLogInfo)
                {
                    Debug.Log($"{LogPrefix} ✓ Resolved targetId via library: '{resolved}'");
                }
                return resolved;
            }
            else
            {
                if (shouldLogInfo)
                {
                    Debug.LogWarning($"{LogPrefix} ✗ Не удалось разрешить targetId через library");
                    Debug.Log($"{LogPrefix} Проверяем все доступные маппинги в библиотеке...");
                    library.LogAllMappings();
                }
            }
        }

        var fallback = referenceName;
        if (shouldLogInfo)
        {
            Debug.LogWarning($"{LogPrefix} ⚠️ Используем fallback: referenceImage.name = '{fallback}'");
        }
        return fallback;
    }

    private void RequestArtifactForInstance(TrackedArtifactInstance instance)
    {
        // Debug.Log($"{LogPrefix} RequestArtifactForInstance: TrackableId={instance.TrackedImage.trackableId}, TargetId='{instance.TargetId}'");
        
        if (artifactService == null)
        {
            Debug.LogError($"{LogPrefix} ArtifactService не инициализирован");
            return;
        }

        string requestedTargetId = instance.TargetId;
        if (string.IsNullOrEmpty(requestedTargetId))
        {
            Debug.LogWarning($"{LogPrefix} TargetId пуст при запросе артефакта");
            return;
        }

        // Проверяем, не загружается ли уже модель для этого instance
        if (instance.LoadCoroutine != null)
        {
            if (string.Equals(instance.LoadingTargetId, requestedTargetId, StringComparison.Ordinal))
            {
                // Debug.Log($"{LogPrefix} Модель уже загружается для targetId={requestedTargetId}, пропускаем повторный запрос");
                return;
            }

            Debug.LogWarning($"{LogPrefix} ⚠️ Прерываем загрузку targetId={instance.LoadingTargetId} ради нового targetId={requestedTargetId}");
            StopCoroutine(instance.LoadCoroutine);
            instance.LoadCoroutine = null;
            instance.LoadingTargetId = null;
        }

        Debug.Log($"{LogPrefix} [БД] Запрос артефакта для targetId='{requestedTargetId}'");
        artifactService.RequestArtifactForTarget(
            requestedTargetId,
            availability =>
            {
                Debug.Log($"{LogPrefix} [БД] Получен артефакт: targetId='{requestedTargetId}', artifactId='{availability.ArtifactId}'");
                
                // Получаем название артефакта из результата
                string artifactName = availability.DisplayName;
                if (string.IsNullOrEmpty(artifactName) && availability.Record != null)
                {
                    artifactName = availability.Record.name;
                }
                
                if (!string.IsNullOrEmpty(artifactName))
                {
                    Debug.Log($"{LogPrefix} [БД] Название артефакта: '{artifactName}'");
                    OnArtifactFound?.Invoke(requestedTargetId, artifactName);
                }
                else
                {
                    Debug.LogWarning($"{LogPrefix} [БД] Название артефакта не найдено для targetId='{requestedTargetId}'");
                }
                
                if (!trackedInstances.TryGetValue(instance.TrackedImage.trackableId, out var currentInstance))
                {
                    Debug.LogWarning($"{LogPrefix} Instance не найден в trackedInstances для TrackableId={instance.TrackedImage.trackableId}");
                    return;
                }

                // Debug.Log($"{LogPrefix} CurrentInstance.TargetId: '{currentInstance.TargetId}'");
                // Debug.Log($"{LogPrefix} CurrentInstance.LoadingTargetId: '{currentInstance.LoadingTargetId}'");

                if (currentInstance.Host == null)
                {
                    Debug.LogWarning($"{LogPrefix} Host == null, пропускаем");
                    return;
                }

                if (!string.Equals(currentInstance.TargetId, requestedTargetId, StringComparison.Ordinal))
                {
                    Debug.LogWarning($"{LogPrefix} ⚠️ TargetId изменился! Текущий: '{currentInstance.TargetId}', Запрошенный: '{requestedTargetId}', игнорируем результат");
                    return;
                }

                if (currentInstance.Host.HasLoadedArtifact(availability.ArtifactId))
                {
                    // Debug.Log($"{LogPrefix} Модель уже загружена для targetId={instance.TargetId}, artifactId={availability.ArtifactId}");
                    return;
                }

                // Проверяем еще раз, не запущена ли уже корутина
                if (currentInstance.LoadCoroutine != null)
                {
                    Debug.LogWarning($"{LogPrefix} ⚠️ Модель уже загружается для targetId={instance.TargetId}, останавливаем предыдущую корутину");
                    StopCoroutine(currentInstance.LoadCoroutine);
                }

                Debug.Log($"{LogPrefix} [3D] Запуск загрузки модели для targetId={requestedTargetId}, artifactId={availability.ArtifactId}");
                currentInstance.LoadingTargetId = requestedTargetId;
                currentInstance.LoadCoroutine = StartCoroutine(LoadModelCoroutine(currentInstance, availability, requestedTargetId));
            },
            error =>
            {
                Debug.LogError($"{LogPrefix} [БД] Ошибка получения артефакта: targetId='{requestedTargetId}', error={error}");
                
                if (trackedInstances.TryGetValue(instance.TrackedImage.trackableId, out var currentInstance) &&
                    string.Equals(currentInstance.LoadingTargetId, requestedTargetId, StringComparison.Ordinal))
                {
                    currentInstance.LoadingTargetId = null;
                }
            });
    }

    private IEnumerator LoadModelCoroutine(TrackedArtifactInstance instance, ArtifactService.ArtifactAvailabilityResult availability, string requestTargetId)
    {
        if (instance.Host == null || string.IsNullOrEmpty(availability.LocalModelPath))
        {
            Debug.LogWarning($"{LogPrefix} [3D] Невозможно загрузить модель: Host={instance.Host != null}, Path={availability.LocalModelPath}");
            instance.LoadingTargetId = null;
            instance.LoadCoroutine = null;
            yield break;
        }

        Debug.Log($"{LogPrefix} [3D] Начало загрузки GLB: targetId={instance.TargetId}, файл={availability.LocalModelPath}");

        // Создаем loaderObject в скрытом состоянии
        var loaderObject = new GameObject($"GLTF_Loader_{availability.ArtifactId}");
        loaderObject.transform.SetParent(instance.Host.GetAttachmentRoot(), false);
        loaderObject.SetActive(false); // Скрываем loaderObject сразу

        var gltfComponent = loaderObject.AddComponent<GLTFComponent>();
        gltfComponent.GLTFUri = availability.LocalModelPath;
        gltfComponent.LoadFromStreamingAssets = false;
        gltfComponent.Multithreaded = true;
        gltfComponent.loadOnStart = false;
        gltfComponent.HideSceneObjDuringLoad = true;

        Task loadTask;
        try
        {
            loadTask = gltfComponent.Load();
        }
        catch (Exception e)
        {
            Debug.LogError($"{LogPrefix} Синхронная ошибка GLTF загрузчика: {e.Message}");
            Destroy(loaderObject);
            instance.LoadCoroutine = null;
            yield break;
        }

        while (!loadTask.IsCompleted)
        {
            yield return null;
        }

        if (loadTask.IsFaulted)
        {
            Debug.LogError($"{LogPrefix} Ошибка GLTF загрузки: {loadTask.Exception?.GetBaseException().Message}");
            Destroy(loaderObject);
            instance.LoadingTargetId = null;
            instance.LoadCoroutine = null;
            yield break;
        }

        var loadedScene = gltfComponent.LastLoadedScene;
        if (loadedScene == null)
        {
            Debug.LogWarning($"{LogPrefix} GLTF сцена не содержит объектов для targetId={instance.TargetId}");
            Destroy(loaderObject);
            instance.LoadingTargetId = null;
            instance.LoadCoroutine = null;
            yield break;
        }

        // Проверяем, не был ли instance удален или изменен во время загрузки
        if (!trackedInstances.TryGetValue(instance.TrackedImage.trackableId, out var currentInstance) || 
            currentInstance != instance || 
            currentInstance.Host == null)
        {
            Debug.LogWarning($"{LogPrefix} Instance был изменен или удален во время загрузки для targetId={instance.TargetId}");
            Destroy(loadedScene);
            Destroy(loaderObject);
            instance.LoadingTargetId = null;
            instance.LoadCoroutine = null;
            yield break;
        }

        if (!string.Equals(requestTargetId, currentInstance.TargetId, StringComparison.Ordinal))
        {
            Debug.LogWarning($"{LogPrefix} Модель для устаревшего targetId={requestTargetId}. Текущий targetId={currentInstance.TargetId}");
            Destroy(loadedScene);
            Destroy(loaderObject);
            instance.LoadingTargetId = null;
            instance.LoadCoroutine = null;
            yield break;
        }

        // Убеждаемся, что loadedScene не является дочерним объектом loaderObject
        // (GLTFComponent может создать модель внутри loaderObject)
        if (loadedScene.transform.parent == loaderObject.transform)
        {
            loadedScene.transform.SetParent(null, true); // Отсоединяем от loaderObject, сохраняя мировые координаты
        }

        // Передаем модель в Host - он сам установит parent и выровняет
        instance.Host.AttachLoadedModel(loadedScene, availability.ArtifactId);

        // Уничтожаем loaderObject после того, как модель передана
        // Это также уничтожит GLTFComponent и все его дочерние объекты (если они остались)
        Destroy(loaderObject);
        instance.LoadingTargetId = null;
        instance.LoadCoroutine = null;
        Debug.Log($"{LogPrefix} [3D] GLB успешно загружен и отображен для targetId={instance.TargetId}");
    }

    private class TrackedArtifactInstance
    {
        public ARTrackedImage TrackedImage;
        public TrackedModelHost Host;
        public Coroutine LoadCoroutine;
        public string TargetId;
        public string LoadingTargetId;
        public bool HasLoggedTargetInfo;
    }
}


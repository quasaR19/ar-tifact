using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ARArtifact.UI;

namespace ARArtifact.Services
{
    /// <summary>
    /// Управляет размещением загруженных моделей на сцене (в TrackedModelHost).
    /// Координирует работу между ModelLoaderService и TrackedModelHost.
    /// </summary>
    public class ModelSceneManager : MonoBehaviour
    {
        private const string LogPrefix = "[ModelSceneManager]";

        private static ModelSceneManager _instance;
        public static ModelSceneManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("ModelSceneManager");
                    _instance = go.AddComponent<ModelSceneManager>();
                    DontDestroyOnLoad(go);
                }

                return _instance;
            }
        }

        /// <summary>
        /// Информация о модели, размещенной на сцене
        /// </summary>
        private class SceneModelInstance
        {
            public string ArtifactId;
            public TrackedModelHost Host;
            public GameObject ModelInstance;
            public bool IsActive;
        }
        
        /// <summary>
        /// Информация об активной операции размещения модели
        /// </summary>
        private class PlacementOperation
        {
            public string OperationId;
            public string ArtifactId;
            public TrackedModelHost TargetHost;
            public bool IsCancelled;
        }

        private readonly Dictionary<string, SceneModelInstance> sceneModels = new();
        private readonly Dictionary<string, PlacementOperation> activePlacements = new(); // artifactId -> operation
        private ModelLoaderService modelLoader;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            modelLoader = ModelLoaderService.Instance;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>
        /// Запрашивает модель для размещения в хосте.
        /// Если модель уже загружена - размещает сразу.
        /// Если загружается - подписывается на завершение и размещает после загрузки.
        /// Если не загружена - запрашивает загрузку через ModelLoaderService.
        /// </summary>
        /// <param name="artifactId">ID артефакта</param>
        /// <param name="host">Хост для размещения модели</param>
        /// <param name="localPath">Локальный путь к GLB файлу</param>
        /// <param name="metadataJson">Метаданные модели</param>
        /// <param name="onSuccess">Колбэк при успешном размещении</param>
        /// <param name="onError">Колбэк при ошибке</param>
        /// <param name="remoteUrl">URL для повторной загрузки при необходимости (опционально)</param>
        public void RequestModelForHost(
            string artifactId,
            TrackedModelHost host,
            string localPath,
            string metadataJson,
            Action onSuccess,
            Action<string> onError,
            string remoteUrl = null)
        {
            if (string.IsNullOrEmpty(artifactId))
            {
                onError?.Invoke("ArtifactId пуст");
                return;
            }

            if (host == null)
            {
                onError?.Invoke("Host == null");
                return;
            }

            if (string.IsNullOrEmpty(localPath))
            {
                onError?.Invoke("LocalPath пуст");
                return;
            }

            // КРИТИЧНО: Захватываем локальные копии для предотвращения race condition
            string capturedArtifactId = artifactId;
            TrackedModelHost capturedHost = host;
            string capturedMetadataJson = metadataJson;
            
            string operationId = Guid.NewGuid().ToString();

            if (activePlacements.TryGetValue(capturedArtifactId, out var existingOp))
            {
                existingOp.IsCancelled = true;
                activePlacements.Remove(capturedArtifactId);
            }

            if (sceneModels.TryGetValue(capturedArtifactId, out var existingInstance))
            {
                if (existingInstance.Host == capturedHost && existingInstance.IsActive)
                {
                    onSuccess?.Invoke();
                    return;
                }
                else if (existingInstance.Host != capturedHost)
                {
                    Debug.LogWarning($"{LogPrefix} Модель {capturedArtifactId} размещена в другом хосте, удаляем из старого");
                    RemoveModelFromHost(capturedArtifactId, existingInstance.Host);
                }
            }

            var placementOp = new PlacementOperation
            {
                OperationId = operationId,
                ArtifactId = capturedArtifactId,
                TargetHost = capturedHost,
                IsCancelled = false
            };
            activePlacements[capturedArtifactId] = placementOp;

            if (modelLoader.TryGetLoadedModel(capturedArtifactId, out var loadedModel))
            {
                string metadata = modelLoader.GetModelMetadata(capturedArtifactId) ?? capturedMetadataJson;
                PlaceModelInHostWithValidation(placementOp, loadedModel, metadata, onSuccess, onError);
                return;
            }

            if (modelLoader.IsLoading(capturedArtifactId))
            {
                modelLoader.RequestModelLoad(
                    capturedArtifactId,
                    localPath,
                    capturedMetadataJson,
                    model =>
                    {
                        PlaceModelInHostWithValidation(placementOp, model, capturedMetadataJson, onSuccess, onError);
                    },
                    error =>
                    {
                        activePlacements.Remove(capturedArtifactId);
                        onError?.Invoke(error);
                    },
                    remoteUrl);
                return;
            }
            modelLoader.RequestModelLoad(
                capturedArtifactId,
                localPath,
                capturedMetadataJson,
                model =>
                {
                    // Модель загружена, размещаем в хосте с валидацией
                    PlaceModelInHostWithValidation(placementOp, model, capturedMetadataJson, onSuccess, onError);
                },
                error =>
                {
                    // Логируем в MainScreen вместо консоли
                    MainScreenController.LogToMainScreen($"Ошибка загрузки модели: {error}", capturedArtifactId);
                    Debug.LogWarning($"{LogPrefix} Ошибка загрузки модели {capturedArtifactId}: {error}");
                    activePlacements.Remove(capturedArtifactId);
                    onError?.Invoke(error);
                },
                remoteUrl);
        }
        
        /// <summary>
        /// Размещает модель в хосте с предварительной валидацией операции
        /// </summary>
        private void PlaceModelInHostWithValidation(
            PlacementOperation operation,
            GameObject modelInstance,
            string metadataJson,
            Action onSuccess,
            Action<string> onError)
        {
            if (operation.IsCancelled)
            {
                return;
            }
            
            if (operation.TargetHost == null)
            {
                string errorMessage = "Хост был уничтожен";
                // Логируем в MainScreen вместо консоли
                MainScreenController.LogToMainScreen($"Ошибка размещения модели: {errorMessage}", operation.ArtifactId);
                Debug.LogWarning($"{LogPrefix} Хост был уничтожен до размещения модели {operation.ArtifactId}");
                activePlacements.Remove(operation.ArtifactId);
                onError?.Invoke(errorMessage);
                return;
            }
            
            if (!activePlacements.TryGetValue(operation.ArtifactId, out var currentOp) || 
                currentOp.OperationId != operation.OperationId)
            {
                return;
            }
            
            PlaceModelInHost(operation.ArtifactId, operation.TargetHost, modelInstance, metadataJson, onSuccess, onError);
        }

        private void PlaceModelInHost(
            string artifactId,
            TrackedModelHost host,
            GameObject modelInstance,
            string metadataJson,
            Action onSuccess,
            Action<string> onError)
        {
            if (host == null)
            {
                string errorMessage = "Host == null при размещении модели";
                // Логируем в MainScreen вместо консоли
                ARArtifact.UI.MainScreenController.LogToMainScreen($"Ошибка размещения модели: {errorMessage}", artifactId);
                activePlacements.Remove(artifactId);
                onError?.Invoke(errorMessage);
                return;
            }

            if (modelInstance == null)
            {
                string errorMessage = "ModelInstance == null при размещении модели";
                // Логируем в MainScreen вместо консоли
                ARArtifact.UI.MainScreenController.LogToMainScreen($"Ошибка размещения модели: {errorMessage}", artifactId);
                activePlacements.Remove(artifactId);
                onError?.Invoke(errorMessage);
                return;
            }

            // КРИТИЧНО: Клонируем модель для размещения в хосте
            // Оригинальная модель остается в скрытом контейнере ModelLoaderService
            // Запускаем асинхронное клонирование и размещение
            StartCoroutine(CloneAndPlaceModelAsync(host, modelInstance, artifactId, metadataJson, onSuccess, onError));
        }
        
        /// <summary>
        /// Асинхронно клонирует и размещает модель, разбивая операции на кадры
        /// </summary>
        private IEnumerator CloneAndPlaceModelAsync(
            TrackedModelHost host,
            GameObject modelInstance,
            string artifactId,
            string metadataJson,
            Action onSuccess,
            Action<string> onError)
        {
            // КРИТИЧНО: Захватываем локальные копии для предотвращения race condition
            string capturedArtifactId = artifactId;
            TrackedModelHost capturedHost = host;
            string capturedMetadataJson = metadataJson;
            
            // Получаем текущую операцию для валидации
            PlacementOperation currentOperation = null;
            activePlacements.TryGetValue(capturedArtifactId, out currentOperation);
            string operationId = currentOperation?.OperationId;
            
            // Ждем кадр перед Instantiate для распределения нагрузки
            yield return null;
            
            // Проверяем, что хост все еще существует и операция не отменена
            // ВАЖНО: используем ReferenceEquals для проверки C# null, а Unity == для проверки destroyed
            bool hostDestroyed = ReferenceEquals(capturedHost, null) || capturedHost == null;
            bool operationCancelled = currentOperation != null && currentOperation.IsCancelled;
            
            if (hostDestroyed || operationCancelled)
            {
                if (hostDestroyed)
                {
                    Debug.LogWarning($"{LogPrefix} Хост уничтожен до клонирования для {capturedArtifactId}");
                }
                activePlacements.Remove(capturedArtifactId);
                if (hostDestroyed)
                {
                    onError?.Invoke("Хост был уничтожен");
                }
                yield break;
            }
            
            // Проверяем, что это все еще актуальная операция
            if (currentOperation != null && activePlacements.TryGetValue(capturedArtifactId, out var checkOp))
            {
                if (checkOp.OperationId != operationId)
                {
                    yield break;
                }
            }
            
            GameObject clonedModel = Instantiate(modelInstance);
            clonedModel.name = $"{modelInstance.name}_Instance_{Guid.NewGuid()}";

            if (clonedModel.transform.parent != null)
            {
                clonedModel.transform.SetParent(null, true);
            }
            
            yield return null;
            
            hostDestroyed = ReferenceEquals(capturedHost, null) || capturedHost == null;
            operationCancelled = currentOperation != null && currentOperation.IsCancelled;
            
            if (hostDestroyed || operationCancelled)
            {
                if (hostDestroyed)
                {
                    Debug.LogWarning($"{LogPrefix} Хост уничтожен после клонирования для {capturedArtifactId}, уничтожаем клон");
                }
                Destroy(clonedModel);
                activePlacements.Remove(capturedArtifactId);
                if (hostDestroyed)
                {
                    onError?.Invoke("Хост был уничтожен");
                }
                yield break;
            }
            
            if (currentOperation != null && activePlacements.TryGetValue(capturedArtifactId, out var checkOp2))
            {
                if (checkOp2.OperationId != operationId)
                {
                    Destroy(clonedModel);
                    yield break;
                }
            }

            // Размещаем модель в хосте (теперь асинхронно)
            bool success = false;
            string errorMessage = null;
            
            try
            {
                capturedHost.AttachLoadedModel(clonedModel, capturedArtifactId, capturedMetadataJson);

                // Сохраняем информацию о размещенной модели
                var sceneInstance = new SceneModelInstance
                {
                    ArtifactId = capturedArtifactId,
                    Host = capturedHost,
                    ModelInstance = clonedModel,
                    IsActive = true
                };
                sceneModels[capturedArtifactId] = sceneInstance;
                
                success = true;
            }
            catch (Exception e)
            {
                errorMessage = $"Ошибка размещения: {e.Message}";
                // Логируем в MainScreen вместо консоли
                ARArtifact.UI.MainScreenController.LogToMainScreen($"Ошибка размещения модели: {errorMessage}", capturedArtifactId);
                Debug.LogWarning($"{LogPrefix} Ошибка при размещении модели в хосте: {e.Message}");
                Destroy(clonedModel);
            }
            
            activePlacements.Remove(capturedArtifactId);
            
            yield return null;
            
            if (success)
            {
                if (modelLoader != null)
                {
                    modelLoader.ReleaseModelReference(capturedArtifactId);
                }
                
                onSuccess?.Invoke();
            }
            else
            {
                onError?.Invoke(errorMessage);
            }
        }

        public void RemoveModelFromHost(string artifactId, TrackedModelHost host)
        {
            if (string.IsNullOrEmpty(artifactId) || host == null)
            {
                return;
            }

            if (sceneModels.TryGetValue(artifactId, out var instance))
            {
                if (instance.Host == host)
                {
                    if (instance.ModelInstance != null)
                    {
                        Destroy(instance.ModelInstance);
                    }

                    host.ResetToPlaceholder();

                    sceneModels.Remove(artifactId);
                    
                    if (modelLoader != null)
                    {
                        modelLoader.ReleaseModelReference(artifactId);
                    }
                }
            }
        }
        
        public void CleanupInactiveModels()
        {
            var toRemove = new List<string>();
            
            foreach (var kvp in sceneModels)
            {
                if (!kvp.Value.IsActive || kvp.Value.ModelInstance == null)
                {
                    toRemove.Add(kvp.Key);
                }
            }
            
            foreach (var artifactId in toRemove)
            {
                var instance = sceneModels[artifactId];
                if (instance.ModelInstance != null)
                {
                    Destroy(instance.ModelInstance);
                }
                sceneModels.Remove(artifactId);
                
                // Освобождаем ссылку в ModelLoaderService
                if (modelLoader != null)
                {
                    modelLoader.ReleaseModelReference(artifactId);
                }
            }
        }

        /// <summary>
        /// Проверяет, размещена ли модель на сцене
        /// </summary>
        public bool IsModelInScene(string artifactId)
        {
            return sceneModels.ContainsKey(artifactId) && 
                   sceneModels[artifactId].IsActive &&
                   sceneModels[artifactId].ModelInstance != null;
        }

        /// <summary>
        /// Получает хост, в котором размещена модель
        /// </summary>
        public TrackedModelHost GetHostForModel(string artifactId)
        {
            if (sceneModels.TryGetValue(artifactId, out var instance))
            {
                return instance.Host;
            }

            return null;
        }

        /// <summary>
        /// Обновляет состояние модели при изменении состояния трекинга хоста
        /// </summary>
        public void UpdateModelTrackingState(string artifactId, bool isTracking)
        {
            if (sceneModels.TryGetValue(artifactId, out var instance))
            {
                instance.IsActive = isTracking;
            }
        }
    }
}


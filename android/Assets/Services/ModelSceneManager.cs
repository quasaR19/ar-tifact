using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
                    Debug.Log($"{LogPrefix} Создан Singleton экземпляр");
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
        public void RequestModelForHost(
            string artifactId,
            TrackedModelHost host,
            string localPath,
            string metadataJson,
            Action onSuccess,
            Action<string> onError)
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
            
            // Создаем уникальный идентификатор операции
            string operationId = Guid.NewGuid().ToString();

            Debug.Log($"{LogPrefix} Запрос модели для хоста: artifactId={capturedArtifactId}, host={capturedHost.name}, operationId={operationId}");

            // Отменяем предыдущую операцию размещения для этого артефакта, если есть
            if (activePlacements.TryGetValue(capturedArtifactId, out var existingOp))
            {
                Debug.LogWarning($"{LogPrefix} Отменяем предыдущую операцию размещения: {existingOp.OperationId}");
                existingOp.IsCancelled = true;
                activePlacements.Remove(capturedArtifactId);
            }

            // Проверяем, не размещена ли уже эта модель в этом хосте
            if (sceneModels.TryGetValue(capturedArtifactId, out var existingInstance))
            {
                if (existingInstance.Host == capturedHost && existingInstance.IsActive)
                {
                    Debug.Log($"{LogPrefix} Модель {capturedArtifactId} уже размещена в этом хосте");
                    onSuccess?.Invoke();
                    return;
                }
                else if (existingInstance.Host != capturedHost)
                {
                    // Модель размещена в другом хосте - удаляем из старого
                    Debug.LogWarning($"{LogPrefix} Модель {capturedArtifactId} размещена в другом хосте, удаляем из старого");
                    RemoveModelFromHost(capturedArtifactId, existingInstance.Host);
                }
            }

            // Создаем операцию размещения
            var placementOp = new PlacementOperation
            {
                OperationId = operationId,
                ArtifactId = capturedArtifactId,
                TargetHost = capturedHost,
                IsCancelled = false
            };
            activePlacements[capturedArtifactId] = placementOp;

            // Проверяем, не загружена ли уже модель
            if (modelLoader.TryGetLoadedModel(capturedArtifactId, out var loadedModel))
            {
                Debug.Log($"{LogPrefix} Модель {capturedArtifactId} уже загружена, размещаем в хосте");
                string metadata = modelLoader.GetModelMetadata(capturedArtifactId) ?? capturedMetadataJson;
                PlaceModelInHostWithValidation(placementOp, loadedModel, metadata, onSuccess, onError);
                return;
            }

            // Проверяем, не загружается ли модель
            if (modelLoader.IsLoading(capturedArtifactId))
            {
                Debug.Log($"{LogPrefix} Модель {capturedArtifactId} загружается, подписываемся на завершение");
                // Подписываемся на завершение загрузки
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
                        Debug.LogError($"{LogPrefix} Ошибка загрузки модели {capturedArtifactId}: {error}");
                        activePlacements.Remove(capturedArtifactId);
                        onError?.Invoke(error);
                    });
                return;
            }

            // Модель не загружена и не загружается - запрашиваем загрузку
            Debug.Log($"{LogPrefix} Модель {capturedArtifactId} не загружена, запрашиваем загрузку");
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
                    Debug.LogError($"{LogPrefix} Ошибка загрузки модели {capturedArtifactId}: {error}");
                    activePlacements.Remove(capturedArtifactId);
                    onError?.Invoke(error);
                });
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
            // Проверяем, не отменена ли операция
            if (operation.IsCancelled)
            {
                Debug.LogWarning($"{LogPrefix} Операция {operation.OperationId} отменена, пропускаем размещение");
                return;
            }
            
            // Проверяем, что хост все еще существует и не уничтожен
            if (operation.TargetHost == null)
            {
                Debug.LogError($"{LogPrefix} Хост был уничтожен до размещения модели {operation.ArtifactId}");
                activePlacements.Remove(operation.ArtifactId);
                onError?.Invoke("Хост был уничтожен");
                return;
            }
            
            // Проверяем, что это все еще актуальная операция для данного артефакта
            if (!activePlacements.TryGetValue(operation.ArtifactId, out var currentOp) || 
                currentOp.OperationId != operation.OperationId)
            {
                Debug.LogWarning($"{LogPrefix} Операция {operation.OperationId} устарела, пропускаем размещение");
                return;
            }
            
            PlaceModelInHost(operation.ArtifactId, operation.TargetHost, modelInstance, metadataJson, onSuccess, onError);
        }

        /// <summary>
        /// Размещает модель в хосте
        /// </summary>
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
                activePlacements.Remove(artifactId);
                onError?.Invoke("Host == null при размещении модели");
                return;
            }

            if (modelInstance == null)
            {
                activePlacements.Remove(artifactId);
                onError?.Invoke("ModelInstance == null при размещении модели");
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
                else
                {
                    Debug.LogWarning($"{LogPrefix} Операция отменена до клонирования для {capturedArtifactId}");
                }
                activePlacements.Remove(capturedArtifactId);
                // Не вызываем onError для отмененных операций - это нормальное поведение
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
                    Debug.LogWarning($"{LogPrefix} Операция устарела до клонирования для {capturedArtifactId}");
                    yield break;
                }
            }
            
            // Клонируем модель
            GameObject clonedModel = Instantiate(modelInstance);
            clonedModel.name = $"{modelInstance.name}_Instance_{Guid.NewGuid()}";

            // Убеждаемся, что клон отсоединен от любого родителя
            if (clonedModel.transform.parent != null)
            {
                clonedModel.transform.SetParent(null, true);
            }
            
            // Ждем кадр после Instantiate перед тяжелыми операциями
            yield return null;
            
            // Повторно проверяем хост после ожидания
            hostDestroyed = ReferenceEquals(capturedHost, null) || capturedHost == null;
            operationCancelled = currentOperation != null && currentOperation.IsCancelled;
            
            if (hostDestroyed || operationCancelled)
            {
                if (hostDestroyed)
                {
                    Debug.LogWarning($"{LogPrefix} Хост уничтожен после клонирования для {capturedArtifactId}, уничтожаем клон");
                }
                else
                {
                    Debug.LogWarning($"{LogPrefix} Операция отменена после клонирования для {capturedArtifactId}, уничтожаем клон");
                }
                Destroy(clonedModel);
                activePlacements.Remove(capturedArtifactId);
                if (hostDestroyed)
                {
                    onError?.Invoke("Хост был уничтожен");
                }
                yield break;
            }
            
            // Проверяем, что это все еще актуальная операция
            if (currentOperation != null && activePlacements.TryGetValue(capturedArtifactId, out var checkOp2))
            {
                if (checkOp2.OperationId != operationId)
                {
                    Debug.LogWarning($"{LogPrefix} Операция устарела после клонирования для {capturedArtifactId}, уничтожаем клон");
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
                Debug.LogError($"{LogPrefix} Ошибка при размещении модели в хосте: {e.Message}");
                Destroy(clonedModel);
                errorMessage = $"Ошибка размещения: {e.Message}";
            }
            
            // Удаляем операцию из активных
            activePlacements.Remove(capturedArtifactId);
            
            // Ждем кадр перед выгрузкой оригинала (вынесено из try-catch)
            yield return null;
            
            if (success)
            {
                // После успешного клонирования пытаемся выгрузить оригинал из ModelLoaderService
                // если нет других активных ссылок (ModelLoaderService проверит счетчик ссылок)
                if (modelLoader != null)
                {
                    modelLoader.ReleaseModelReference(capturedArtifactId);
                    // Модель будет автоматически выгружена ModelLoaderService, если счетчик ссылок = 0
                }
                
                onSuccess?.Invoke();
            }
            else
            {
                onError?.Invoke(errorMessage);
            }
        }

        /// <summary>
        /// Удаляет модель из хоста
        /// </summary>
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
                    Debug.Log($"{LogPrefix} Удаление модели {artifactId} из хоста {host.name}");

                    // Уничтожаем экземпляр модели на сцене
                    if (instance.ModelInstance != null)
                    {
                        Destroy(instance.ModelInstance);
                    }

                    // Сбрасываем хост к плейсхолдеру
                    host.ResetToPlaceholder();

                    sceneModels.Remove(artifactId);
                    
                    // Освобождаем ссылку в ModelLoaderService
                    if (modelLoader != null)
                    {
                        modelLoader.ReleaseModelReference(artifactId);
                    }
                    
                    Debug.Log($"{LogPrefix} Модель {artifactId} удалена из хоста");
                }
            }
        }
        
        /// <summary>
        /// Очищает неактивные модели из sceneModels
        /// </summary>
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
            
            if (toRemove.Count > 0)
            {
                Debug.Log($"{LogPrefix} Очищено {toRemove.Count} неактивных моделей");
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


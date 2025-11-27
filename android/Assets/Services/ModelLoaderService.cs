using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityGLTF;
using System.Threading.Tasks;

namespace ARArtifact.Services
{
    /// <summary>
    /// Централизованный сервис для параллельной загрузки GLB моделей из облака.
    /// Загружает модели в скрытом контейнере и предоставляет доступ к ним по запросу.
    /// </summary>
    public class ModelLoaderService : MonoBehaviour
    {
        private const string LogPrefix = "[ModelLoaderService]";
        
        // Позиция скрытого контейнера (далеко от центра сцены)
        private static readonly Vector3 HiddenContainerPosition = new Vector3(0, -1000, 0);

        private static ModelLoaderService _instance;
        public static ModelLoaderService Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("ModelLoaderService");
                    _instance = go.AddComponent<ModelLoaderService>();
                    DontDestroyOnLoad(go);
                    Debug.Log($"{LogPrefix} Создан Singleton экземпляр");
                }

                return _instance;
            }
        }

        /// <summary>
        /// Данные о загруженной модели
        /// </summary>
        private class LoadedModelData
        {
            public GameObject ModelInstance;
            public string MetadataJson;
            public DateTime LoadedAt;
            public DateTime LastAccessedAt;
            public int ReferenceCount;
        }

        /// <summary>
        /// Операция загрузки модели
        /// </summary>
        private class ModelLoadOperation
        {
            public string ArtifactId;
            public string LocalPath;
            public string MetadataJson;
            public Coroutine LoadCoroutine;
            public float Progress;
            public readonly List<Action<GameObject>> SuccessCallbacks = new();
            public readonly List<Action<string>> ErrorCallbacks = new();
            public GameObject LoaderObject;
            public bool IsCompleted;
            public bool IsFaulted;
            public string ErrorMessage;
        }

        private Transform hiddenContainer;
        private readonly Dictionary<string, ModelLoadOperation> activeLoads = new();
        private readonly Dictionary<string, LoadedModelData> loadedModels = new();
        private readonly LinkedList<string> modelAccessOrder = new(); // LRU порядок доступа
        
        // Кеш ошибок загрузки, чтобы не пытаться загружать поврежденные файлы повторно
        private readonly Dictionary<string, string> failedLoads = new();
        private readonly Dictionary<string, DateTime> failedLoadsTime = new();
        private const float FailedLoadRetryDelay = 300f; // 5 минут до повторной попытки
        
        // Настройки управления памятью
        [Header("Memory Management")]
        [SerializeField] private int maxLoadedModels = 10; // Максимум моделей в кеше
        [SerializeField] private float modelTTLMinutes = 30f; // Время жизни неиспользуемой модели в минутах
        [SerializeField] private int maxFailedLoadsCache = 50; // Максимум записей в кеше ошибок
        [SerializeField] private float cleanupIntervalSeconds = 60f; // Интервал очистки в секундах
        
        private float lastCleanupTime = 0f;

        // События для уведомления о загрузках
        public event System.Action<string> OnLoadStarted; // artifactId
        public event System.Action<string> OnLoadCompleted; // artifactId
        public event System.Action<string, string> OnLoadFailed; // artifactId, error

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            // Создаем скрытый контейнер для загруженных моделей
            var containerGO = new GameObject("HiddenModelContainer");
            containerGO.transform.position = HiddenContainerPosition;
            hiddenContainer = containerGO.transform;
            containerGO.SetActive(false); // Делаем невидимым
            
            lastCleanupTime = Time.time;
        }
        
        private void Update()
        {
            // Периодическая очистка неиспользуемых моделей и кеша ошибок
            if (Time.time - lastCleanupTime >= cleanupIntervalSeconds)
            {
                CleanupUnusedModels();
                CleanupFailedLoadsCache();
                lastCleanupTime = Time.time;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }

            // Останавливаем все активные загрузки
            StopAllCoroutines();
            
            // Очищаем загруженные модели
            foreach (var modelData in loadedModels.Values)
            {
                if (modelData.ModelInstance != null)
                {
                    Destroy(modelData.ModelInstance);
                }
            }
            loadedModels.Clear();
            modelAccessOrder.Clear();
            activeLoads.Clear();
            failedLoads.Clear();
            failedLoadsTime.Clear();
        }

        /// <summary>
        /// Результат асинхронной валидации GLB файла
        /// </summary>
        private class GLBValidationResult
        {
            public bool IsValid;
            public string Error;
        }
        
        /// <summary>
        /// Асинхронно проверяет целостность GLB файла (без блокировки основного потока)
        /// </summary>
        private IEnumerator ValidateGLBFileAsync(string filePath, GLBValidationResult result, int retryCount = 0)
        {
            result.IsValid = false;
            result.Error = null;
            
            if (string.IsNullOrEmpty(filePath))
            {
                result.Error = "Путь к файлу не задан";
                yield break;
            }

            if (!File.Exists(filePath))
            {
                result.Error = $"Файл не существует: {filePath}";
                yield break;
            }

            FileInfo fileInfo;
            try
            {
                fileInfo = new FileInfo(filePath);
                if (fileInfo.Length == 0)
                {
                    result.Error = "Файл пуст (размер 0 байт)";
                    yield break;
                }

                // GLB файлы должны быть минимум 12 байт (заголовок)
                if (fileInfo.Length < 12)
                {
                    result.Error = $"Файл слишком мал ({fileInfo.Length} байт), минимальный размер GLB: 12 байт";
                    yield break;
                }
            }
            catch (Exception e)
            {
                result.Error = $"Ошибка получения информации о файле: {e.Message}";
                yield break;
            }

            // Проверяем магическое число GLB (первые 4 байта должны быть "glTF")
            uint declaredLength = 0;
            bool readSuccess = false;
            string readError = null;
            
            try
            {
                // Используем FileShare.Read для более консервативного доступа
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var magic = new byte[4];
                    int bytesRead = fs.Read(magic, 0, 4);
                    
                    if (bytesRead < 4)
                    {
                        result.Error = "Не удалось прочитать заголовок файла";
                        yield break;
                    }
                    
                    // GLB магическое число: 0x46546C67 (little-endian "glTF")
                    if (magic[0] != 0x67 || magic[1] != 0x6C || magic[2] != 0x54 || magic[3] != 0x46)
                    {
                        result.Error = "Файл не является валидным GLB (неверное магическое число)";
                        yield break;
                    }

                    // Читаем версию (байты 4-7) и длину (байты 8-11)
                    var version = new byte[4];
                    var length = new byte[4];
                    fs.Read(version, 0, 4);
                    fs.Read(length, 0, 4);
                    
                    declaredLength = BitConverter.ToUInt32(length, 0);
                    readSuccess = true;
                }
            }
            catch (IOException ioEx)
            {
                readError = ioEx.Message;
            }
            catch (Exception e)
            {
                result.Error = $"Ошибка проверки файла: {e.Message}";
                yield break;
            }
            
            // Если файл заблокирован, ждем асинхронно и пробуем снова
            if (!readSuccess && retryCount < 5)
            {
                Debug.LogWarning($"{LogPrefix} Файл заблокирован, повторная попытка через 0.5с: {readError}");
                yield return new WaitForSeconds(0.5f);
                
                // Рекурсивно вызываем себя через новую корутину
                var retryResult = new GLBValidationResult();
                yield return StartCoroutine(ValidateGLBFileAsync(filePath, retryResult, retryCount + 1));
                result.IsValid = retryResult.IsValid;
                result.Error = retryResult.Error;
                yield break;
            }
            else if (!readSuccess)
            {
                result.Error = $"Ошибка доступа к файлу: {readError}";
                yield break;
            }
            
            // Обновляем fileInfo для получения актуального размера
            try
            {
                fileInfo.Refresh();
            }
            catch (Exception e)
            {
                result.Error = $"Ошибка обновления информации о файле: {e.Message}";
                yield break;
            }
            
            // Проверяем, что заявленная длина соответствует реальному размеру файла
            if (declaredLength != fileInfo.Length)
            {
                // Если файл еще записывается (размер меньше заявленного), и у нас есть попытки
                if (fileInfo.Length < declaredLength && retryCount < 5)
                {
                    Debug.LogWarning($"{LogPrefix} Файл еще записывается: заявлено {declaredLength} байт, реально {fileInfo.Length} байт. Повторная попытка через 0.5с...");
                    yield return new WaitForSeconds(0.5f);
                    
                    // Рекурсивно вызываем себя через новую корутину
                    var retryResult = new GLBValidationResult();
                    yield return StartCoroutine(ValidateGLBFileAsync(filePath, retryResult, retryCount + 1));
                    result.IsValid = retryResult.IsValid;
                    result.Error = retryResult.Error;
                    yield break;
                }
                
                result.Error = $"Длина файла не соответствует заголовку: заявлено {declaredLength} байт, реально {fileInfo.Length} байт";
                yield break;
            }

            result.IsValid = true;
        }

        /// <summary>
        /// Запрашивает загрузку модели. Если модель уже загружена или загружается, 
        /// подключается к существующей операции.
        /// </summary>
        /// <param name="artifactId">ID артефакта</param>
        /// <param name="localPath">Локальный путь к GLB файлу</param>
        /// <param name="metadataJson">Метаданные модели (JSON строка)</param>
        /// <param name="onSuccess">Колбэк при успешной загрузке (передает GameObject модели)</param>
        /// <param name="onError">Колбэк при ошибке</param>
        public void RequestModelLoad(
            string artifactId,
            string localPath,
            string metadataJson,
            Action<GameObject> onSuccess,
            Action<string> onError)
        {
            if (string.IsNullOrEmpty(artifactId))
            {
                onError?.Invoke("ArtifactId пуст");
                return;
            }

            if (string.IsNullOrEmpty(localPath))
            {
                onError?.Invoke("LocalPath пуст");
                return;
            }

            // Проверяем кеш ошибок
            if (failedLoads.TryGetValue(artifactId, out var cachedError))
            {
                if (failedLoadsTime.TryGetValue(artifactId, out var failTime))
                {
                    var timeSinceFail = (DateTime.Now - failTime).TotalSeconds;
                    if (timeSinceFail < FailedLoadRetryDelay)
                    {
                        // Не прошло достаточно времени для повторной попытки
                        Debug.LogWarning($"{LogPrefix} Пропускаем загрузку {artifactId}: предыдущая ошибка была {timeSinceFail:F1}с назад. Ошибка: {cachedError}");
                        onError?.Invoke($"Поврежденный файл (повторная попытка через {FailedLoadRetryDelay - timeSinceFail:F0}с): {cachedError}");
                        return;
                    }
                    else
                    {
                        // Прошло достаточно времени, удаляем из кеша ошибок для повторной попытки
                        failedLoads.Remove(artifactId);
                        failedLoadsTime.Remove(artifactId);
                        Debug.Log($"{LogPrefix} Повторная попытка загрузки {artifactId} после {timeSinceFail:F1}с");
                    }
                }
            }

            // Проверяем, не загружена ли уже модель
            if (loadedModels.TryGetValue(artifactId, out var loadedData))
            {
                if (loadedData.ModelInstance != null)
                {
                    Debug.Log($"{LogPrefix} Модель {artifactId} уже загружена, возвращаем существующую");
                    // Обновляем время последнего доступа и счетчик ссылок
                    loadedData.LastAccessedAt = DateTime.UtcNow;
                    loadedData.ReferenceCount++;
                    UpdateAccessOrder(artifactId);
                    onSuccess?.Invoke(loadedData.ModelInstance);
                    return;
                }
                else
                {
                    // Модель была уничтожена, удаляем запись
                    loadedModels.Remove(artifactId);
                    modelAccessOrder.Remove(artifactId);
                }
            }

            // Проверяем, не загружается ли уже модель
            if (activeLoads.TryGetValue(artifactId, out var existingOperation))
            {
                Debug.Log($"{LogPrefix} Модель {artifactId} уже загружается, подключаемся к существующей операции");
                existingOperation.SuccessCallbacks.Add(onSuccess);
                existingOperation.ErrorCallbacks.Add(onError);
                return;
            }

            // Создаем новую операцию загрузки
            var operation = new ModelLoadOperation
            {
                ArtifactId = artifactId,
                LocalPath = localPath,
                MetadataJson = metadataJson
            };
            operation.SuccessCallbacks.Add(onSuccess);
            operation.ErrorCallbacks.Add(onError);
            activeLoads[artifactId] = operation;

            // Запускаем загрузку с асинхронной валидацией
            operation.LoadCoroutine = StartCoroutine(ValidateAndLoadModelCoroutine(operation));
            Debug.Log($"{LogPrefix} Запущена загрузка модели: artifactId={artifactId}, path={localPath}");
            
            // Уведомляем о начале загрузки
            OnLoadStarted?.Invoke(artifactId);
        }
        
        /// <summary>
        /// Корутина для асинхронной валидации и последующей загрузки модели
        /// </summary>
        private IEnumerator ValidateAndLoadModelCoroutine(ModelLoadOperation operation)
        {
            // Асинхронно валидируем файл
            var validationResult = new GLBValidationResult();
            yield return StartCoroutine(ValidateGLBFileAsync(operation.LocalPath, validationResult));
            
            // Проверяем результат валидации
            if (!validationResult.IsValid)
            {
                Debug.LogError($"{LogPrefix} Файл не прошел валидацию: {operation.LocalPath}, ошибка: {validationResult.Error}");
                // Сохраняем ошибку в кеш
                failedLoads[operation.ArtifactId] = validationResult.Error;
                failedLoadsTime[operation.ArtifactId] = DateTime.Now;
                FailOperation(operation, $"Файл поврежден: {validationResult.Error}");
                yield break;
            }
            
            // Файл валиден, продолжаем загрузку
            yield return StartCoroutine(LoadModelCoroutine(operation));
        }

        /// <summary>
        /// Получает прогресс загрузки модели (0-1)
        /// </summary>
        public float GetModelProgress(string artifactId)
        {
            if (activeLoads.TryGetValue(artifactId, out var operation))
            {
                return operation.Progress;
            }

            // Если модель уже загружена, возвращаем 1.0
            if (loadedModels.ContainsKey(artifactId))
            {
                return 1.0f;
            }

            return 0f;
        }

        /// <summary>
        /// Проверяет, загружается ли модель в данный момент
        /// </summary>
        public bool IsLoading(string artifactId)
        {
            return activeLoads.ContainsKey(artifactId);
        }

        /// <summary>
        /// Пытается получить уже загруженную модель
        /// </summary>
        public bool TryGetLoadedModel(string artifactId, out GameObject model)
        {
            model = null;
            
            if (loadedModels.TryGetValue(artifactId, out var loadedData))
            {
                if (loadedData.ModelInstance != null)
                {
                    model = loadedData.ModelInstance;
                    // Обновляем время доступа и порядок LRU
                    loadedData.LastAccessedAt = DateTime.UtcNow;
                    loadedData.ReferenceCount++;
                    UpdateAccessOrder(artifactId);
                    return true;
                }
                else
                {
                    // Модель была уничтожена, удаляем запись
                    loadedModels.Remove(artifactId);
                    modelAccessOrder.Remove(artifactId);
                }
            }

            return false;
        }

        /// <summary>
        /// Получает метаданные загруженной модели
        /// </summary>
        public string GetModelMetadata(string artifactId)
        {
            if (loadedModels.TryGetValue(artifactId, out var loadedData))
            {
                return loadedData.MetadataJson;
            }

            return null;
        }

        /// <summary>
        /// Отменяет загрузку модели
        /// </summary>
        public void CancelLoad(string artifactId)
        {
            if (activeLoads.TryGetValue(artifactId, out var operation))
            {
                if (operation.LoadCoroutine != null)
                {
                    StopCoroutine(operation.LoadCoroutine);
                }

                if (operation.LoaderObject != null)
                {
                    Destroy(operation.LoaderObject);
                }

                // Вызываем колбэки ошибки для всех подписчиков
                foreach (var errorCallback in operation.ErrorCallbacks)
                {
                    try
                    {
                        errorCallback?.Invoke("Загрузка отменена");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"{LogPrefix} Ошибка в обработчике отмены: {e.Message}");
                    }
                }

                activeLoads.Remove(artifactId);
                Debug.Log($"{LogPrefix} Загрузка модели {artifactId} отменена");
            }
        }

        /// <summary>
        /// Удаляет загруженную модель из кеша
        /// </summary>
        public void UnloadModel(string artifactId)
        {
            if (loadedModels.TryGetValue(artifactId, out var loadedData))
            {
                if (loadedData.ModelInstance != null)
                {
                    Destroy(loadedData.ModelInstance);
                }
                loadedModels.Remove(artifactId);
                modelAccessOrder.Remove(artifactId);
                Debug.Log($"{LogPrefix} Модель {artifactId} выгружена из кеша");
            }
        }
        
        /// <summary>
        /// Уменьшает счетчик ссылок на модель. Если счетчик достигает 0, модель может быть выгружена.
        /// </summary>
        public void ReleaseModelReference(string artifactId)
        {
            if (loadedModels.TryGetValue(artifactId, out var loadedData))
            {
                loadedData.ReferenceCount = Mathf.Max(0, loadedData.ReferenceCount - 1);
                Debug.Log($"{LogPrefix} Счетчик ссылок для {artifactId}: {loadedData.ReferenceCount}");
            }
        }
        
        /// <summary>
        /// Обновляет порядок доступа для LRU кеша
        /// </summary>
        private void UpdateAccessOrder(string artifactId)
        {
            modelAccessOrder.Remove(artifactId);
            modelAccessOrder.AddLast(artifactId);
        }
        
        /// <summary>
        /// Освобождает место в кеше, выгружая наименее используемые модели
        /// </summary>
        private void EnsureCacheSpace()
        {
            while (loadedModels.Count >= maxLoadedModels && modelAccessOrder.Count > 0)
            {
                var oldestId = modelAccessOrder.First.Value;
                var oldestData = loadedModels[oldestId];
                
                // Не выгружаем модели с активными ссылками
                if (oldestData.ReferenceCount > 0)
                {
                    // Пропускаем эту модель и перемещаем в конец
                    modelAccessOrder.RemoveFirst();
                    modelAccessOrder.AddLast(oldestId);
                    continue;
                }
                
                Debug.Log($"{LogPrefix} Освобождаем место в кеше: выгружаем модель {oldestId}");
                UnloadModel(oldestId);
            }
        }
        
        /// <summary>
        /// Очищает неиспользуемые модели по TTL
        /// </summary>
        private void CleanupUnusedModels()
        {
            var now = DateTime.UtcNow;
            var modelsToRemove = new List<string>();
            
            foreach (var kvp in loadedModels)
            {
                var artifactId = kvp.Key;
                var data = kvp.Value;
                
                // Пропускаем модели с активными ссылками
                if (data.ReferenceCount > 0)
                {
                    continue;
                }
                
                var timeSinceAccess = (now - data.LastAccessedAt).TotalMinutes;
                if (timeSinceAccess >= modelTTLMinutes)
                {
                    modelsToRemove.Add(artifactId);
                }
            }
            
            foreach (var artifactId in modelsToRemove)
            {
                Debug.Log($"{LogPrefix} Выгружаем модель {artifactId} по TTL ({modelTTLMinutes} минут)");
                UnloadModel(artifactId);
            }
            
            if (modelsToRemove.Count > 0)
            {
                // Освобождаем неиспользуемые ресурсы асинхронно, чтобы не блокировать основной поток
                StartCoroutine(UnloadUnusedAssetsAsync());
            }
        }
        
        /// <summary>
        /// Асинхронно освобождает неиспользуемые ресурсы
        /// </summary>
        private IEnumerator UnloadUnusedAssetsAsync()
        {
            // Ждем несколько кадров перед вызовом, чтобы не блокировать рендеринг
            yield return new WaitForEndOfFrame();
            yield return null;
            
            // Вызываем асинхронную операцию
            var asyncOperation = Resources.UnloadUnusedAssets();
            
            // Ждем завершения асинхронно
            while (!asyncOperation.isDone)
            {
                yield return null;
            }
            
            Debug.Log($"{LogPrefix} Освобождение неиспользуемых ресурсов завершено");
        }
        
        /// <summary>
        /// Очищает кеш ошибок загрузки
        /// </summary>
        private void CleanupFailedLoadsCache()
        {
            // Ограничиваем размер кеша ошибок
            if (failedLoads.Count > maxFailedLoadsCache)
            {
                var toRemove = failedLoads.Count - maxFailedLoadsCache;
                var keysToRemove = new List<string>();
                
                // Удаляем самые старые записи
                foreach (var kvp in failedLoadsTime.OrderBy(x => x.Value).Take(toRemove))
                {
                    keysToRemove.Add(kvp.Key);
                }
                
                foreach (var key in keysToRemove)
                {
                    failedLoads.Remove(key);
                    failedLoadsTime.Remove(key);
                }
                
                Debug.Log($"{LogPrefix} Очищен кеш ошибок: удалено {keysToRemove.Count} записей");
            }
            
            // Удаляем устаревшие записи (старше FailedLoadRetryDelay)
            var now = DateTime.Now;
            var expiredKeys = new List<string>();
            
            foreach (var kvp in failedLoadsTime)
            {
                var timeSinceFail = (now - kvp.Value).TotalSeconds;
                if (timeSinceFail >= FailedLoadRetryDelay * 2) // Удаляем записи старше двойного времени повтора
                {
                    expiredKeys.Add(kvp.Key);
                }
            }
            
            foreach (var key in expiredKeys)
            {
                failedLoads.Remove(key);
                failedLoadsTime.Remove(key);
            }
        }

        /// <summary>
        /// Очищает кеш ошибок для указанного артефакта (для принудительной повторной попытки)
        /// </summary>
        public void ClearFailedLoadCache(string artifactId)
        {
            if (failedLoads.Remove(artifactId))
            {
                failedLoadsTime.Remove(artifactId);
                Debug.Log($"{LogPrefix} Кеш ошибок очищен для {artifactId}");
            }
        }

        /// <summary>
        /// Проверяет, есть ли ошибка загрузки для указанного артефакта
        /// </summary>
        public bool HasFailedLoad(string artifactId)
        {
            return failedLoads.ContainsKey(artifactId);
        }

        /// <summary>
        /// Корутина загрузки GLB модели
        /// </summary>
        private IEnumerator LoadModelCoroutine(ModelLoadOperation operation)
        {
            operation.Progress = 0f;

            // Создаем loaderObject в скрытом контейнере
            var loaderObject = new GameObject($"GLTF_Loader_{operation.ArtifactId}_{Guid.NewGuid()}");
            loaderObject.transform.SetParent(hiddenContainer, false);
            loaderObject.transform.localPosition = Vector3.zero;
            loaderObject.transform.localRotation = Quaternion.identity;
            loaderObject.transform.localScale = Vector3.one;
            loaderObject.SetActive(true); // UnityGLTF требует активный объект

            operation.LoaderObject = loaderObject;

            Debug.Log($"{LogPrefix} [Загрузка] LoaderObject создан: {loaderObject.name}");

            // Добавляем GLTFComponent
            var gltfComponent = loaderObject.AddComponent<GLTFComponent>();
            gltfComponent.GLTFUri = operation.LocalPath;
            gltfComponent.LoadFromStreamingAssets = false;
            gltfComponent.Multithreaded = true;
            gltfComponent.loadOnStart = false;
            gltfComponent.HideSceneObjDuringLoad = true;

            Debug.Log($"{LogPrefix} [Загрузка] GLTFComponent настроен: Uri={gltfComponent.GLTFUri}");

            // Запускаем загрузку
            Task loadTask;
            try
            {
                loadTask = gltfComponent.Load();
                operation.Progress = 0.1f; // Начало загрузки
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogPrefix} [Загрузка] Синхронная ошибка GLTF загрузчика: {e.Message}");
                FailOperation(operation, $"Ошибка запуска загрузки: {e.Message}");
                yield break;
            }

            // Ожидаем завершения загрузки с обновлением прогресса
            float timeout = 30f; // 30 секунд максимум
            float elapsed = 0f;
            float lastProgressUpdate = 0f;

            while (!loadTask.IsCompleted && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                
                // Обновляем прогресс (приблизительно, т.к. UnityGLTF не предоставляет точный прогресс)
                if (elapsed - lastProgressUpdate > 0.1f) // Обновляем каждые 0.1 секунды
                {
                    operation.Progress = Mathf.Lerp(0.1f, 0.9f, elapsed / timeout);
                    lastProgressUpdate = elapsed;
                }
                
                yield return null;
            }

            if (elapsed >= timeout)
            {
                Debug.LogError($"{LogPrefix} [Загрузка] ТАЙМАУТ загрузки GLB ({timeout}с): файл={operation.LocalPath}");
                FailOperation(operation, $"Таймаут загрузки ({timeout}с)");
                yield break;
            }

            operation.Progress = 0.9f;

            if (loadTask.IsFaulted)
            {
                string error = loadTask.Exception?.GetBaseException().Message ?? "Неизвестная ошибка";
                Debug.LogError($"{LogPrefix} [Загрузка] Ошибка GLTF загрузки: {error}");
                FailOperation(operation, $"Ошибка загрузки: {error}");
                yield break;
            }

            var loadedScene = gltfComponent.LastLoadedScene;
            Debug.Log($"{LogPrefix} [Загрузка] LastLoadedScene получен: {(loadedScene != null ? loadedScene.name : "NULL")}");

            if (loadedScene == null)
            {
                Debug.LogWarning($"{LogPrefix} [Загрузка] GLTF сцена не содержит объектов для artifactId={operation.ArtifactId}");
                FailOperation(operation, "Модель не содержит объектов");
                yield break;
            }

            // КРИТИЧНО: Проверяем, где находится loadedScene
            // UnityGLTF может создать модель в корне сцены, нужно переместить её в скрытый контейнер
            if (loadedScene.transform.parent == null)
            {
                Debug.LogWarning($"{LogPrefix} [Загрузка] ⚠️ GLTF модель создана в КОРНЕ СЦЕНЫ! Перемещаем в скрытый контейнер");
                loadedScene.transform.SetParent(hiddenContainer, false);
            }
            else if (loadedScene.transform.parent != loaderObject.transform)
            {
                Debug.LogWarning($"{LogPrefix} [Загрузка] ⚠️ Модель имеет неожиданный parent={loadedScene.transform.parent.name}, перемещаем в скрытый контейнер");
                loadedScene.transform.SetParent(hiddenContainer, false);
            }
            else
            {
                // Модель внутри loaderObject, перемещаем в скрытый контейнер
                loadedScene.transform.SetParent(hiddenContainer, false);
            }

            // Убеждаемся, что модель находится в скрытом контейнере
            if (loadedScene.transform.parent != hiddenContainer)
            {
                Debug.LogError($"{LogPrefix} [Загрузка] ОШИБКА: Модель не в скрытом контейнере! Принудительно перемещаем...");
                loadedScene.transform.SetParent(hiddenContainer, false);
            }

            // Устанавливаем позицию модели в скрытом контейнере (на всякий случай)
            loadedScene.transform.localPosition = Vector3.zero;
            loadedScene.transform.localRotation = Quaternion.identity;
            loadedScene.transform.localScale = Vector3.one;

            operation.Progress = 1.0f;

            // Проверяем, нужно ли освободить место для новой модели (LRU)
            EnsureCacheSpace();

            // Сохраняем загруженную модель
            var loadedData = new LoadedModelData
            {
                ModelInstance = loadedScene,
                MetadataJson = operation.MetadataJson,
                LoadedAt = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow,
                ReferenceCount = 0
            };
            loadedModels[operation.ArtifactId] = loadedData;
            modelAccessOrder.AddLast(operation.ArtifactId);

            // Уничтожаем loaderObject (модель уже в скрытом контейнере)
            if (loaderObject != null)
            {
                Destroy(loaderObject);
            }

            operation.IsCompleted = true;
            activeLoads.Remove(operation.ArtifactId);

            // Очищаем кеш ошибок при успешной загрузке
            if (failedLoads.ContainsKey(operation.ArtifactId))
            {
                failedLoads.Remove(operation.ArtifactId);
                failedLoadsTime.Remove(operation.ArtifactId);
                Debug.Log($"{LogPrefix} Кеш ошибок очищен для успешно загруженной модели {operation.ArtifactId}");
            }

            Debug.Log($"{LogPrefix} [Загрузка] Модель {operation.ArtifactId} успешно загружена и сохранена");

            // Уведомляем о завершении загрузки
            OnLoadCompleted?.Invoke(operation.ArtifactId);

            // Вызываем колбэки успеха
            CompleteOperation(operation, loadedScene);
        }

        /// <summary>
        /// Завершает операцию успешно
        /// </summary>
        private void CompleteOperation(ModelLoadOperation operation, GameObject model)
        {
            foreach (var successCallback in operation.SuccessCallbacks)
            {
                try
                {
                    successCallback?.Invoke(model);
                }
                catch (Exception e)
                {
                    Debug.LogError($"{LogPrefix} Ошибка в обработчике успешной загрузки: {e.Message}");
                }
            }
            
            // Очищаем колбэки для предотвращения утечек памяти
            operation.SuccessCallbacks.Clear();
            operation.ErrorCallbacks.Clear();
        }

        /// <summary>
        /// Завершает операцию с ошибкой
        /// </summary>
        private void FailOperation(ModelLoadOperation operation, string error)
        {
            operation.IsFaulted = true;
            operation.ErrorMessage = error;

            if (operation.LoaderObject != null)
            {
                Destroy(operation.LoaderObject);
            }

            string artifactId = operation.ArtifactId;
            activeLoads.Remove(artifactId);

            // Сохраняем ошибку в кеш, чтобы не пытаться загружать поврежденный файл повторно
            // Особенно важно для ошибок типа "File length does not match header"
            if (!string.IsNullOrEmpty(error) && 
                (error.Contains("File length does not match header") || 
                 error.Contains("поврежден") || 
                 error.Contains("damaged") ||
                 error.Contains("corrupted")))
            {
                failedLoads[artifactId] = error;
                failedLoadsTime[artifactId] = DateTime.Now;
                Debug.LogWarning($"{LogPrefix} Ошибка загрузки сохранена в кеш для {artifactId}: {error}");
            }

            // Уведомляем об ошибке загрузки
            OnLoadFailed?.Invoke(artifactId, error);

            foreach (var errorCallback in operation.ErrorCallbacks)
            {
                try
                {
                    errorCallback?.Invoke(error);
                }
                catch (Exception e)
                {
                    Debug.LogError($"{LogPrefix} Ошибка в обработчике ошибки: {e.Message}");
                }
            }
            
            // Очищаем колбэки для предотвращения утечек памяти
            operation.SuccessCallbacks.Clear();
            operation.ErrorCallbacks.Clear();
        }
    }
}




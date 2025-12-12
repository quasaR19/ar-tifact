using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityGLTF;
using System.Threading.Tasks;
using ARArtifact.UI;

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
            public string RemoteUrl;
            public string MetadataJson;
            public Coroutine LoadCoroutine;
            public float Progress;
            public readonly List<Action<GameObject>> SuccessCallbacks = new();
            public readonly List<Action<string>> ErrorCallbacks = new();
            public GameObject LoaderObject;
            public bool IsCompleted;
            public bool IsFaulted;
            public string ErrorMessage;
            public int AutoRecoveryAttempts = 0; // Количество попыток автоматического восстановления
        }

        private Transform hiddenContainer;
        private readonly Dictionary<string, ModelLoadOperation> activeLoads = new();
        private readonly Dictionary<string, LoadedModelData> loadedModels = new();
        private readonly LinkedList<string> modelAccessOrder = new(); // LRU порядок доступа
        
        /// <summary>
        /// Информация об ошибке загрузки для отслеживания повторных попыток
        /// </summary>
        private class FailedLoadInfo
        {
            public string ErrorMessage;
            public DateTime LastFailureTime;
            public int AttemptCount; // Количество неудачных попыток
        }
        
        // Кеш ошибок загрузки с отслеживанием попыток и экспоненциальным backoff
        private readonly Dictionary<string, FailedLoadInfo> failedLoads = new();
        private const float InitialRetryDelay = 5f; // Начальная задержка: 5 секунд
        private const float MaxRetryDelay = 300f; // Максимальная задержка: 5 минут (300 секунд)
        
        // Настройки автоматического восстановления
        [Header("Auto Recovery")]
        [SerializeField] private int maxAutoRecoveryAttempts = 3; // Максимум попыток автоматического восстановления
        [SerializeField] private float autoRecoveryBaseDelay = 2f; // Базовая задержка между попытками (секунды)
        
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
                // Используем FileShare.ReadWrite для разрешения одновременного доступа
                // Это позволяет читать файл, даже если он еще записывается другим процессом
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
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
                // Проверяем, является ли это sharing violation или блокировкой файла
                string errorMsg = ioEx.Message.ToLowerInvariant();
                if (errorMsg.Contains("sharing violation") || 
                    errorMsg.Contains("being used by another process") ||
                    errorMsg.Contains("cannot access") ||
                    errorMsg.Contains("access is denied"))
                {
                    readError = "Файл используется другим процессом (возможно, еще загружается). Повторная попытка...";
                }
                else
                {
                    readError = ioEx.Message;
                }
            }
            catch (UnauthorizedAccessException uaEx)
            {
                readError = $"Нет доступа к файлу: {uaEx.Message}";
            }
            catch (Exception e)
            {
                result.Error = $"Ошибка проверки файла: {e.Message}";
                yield break;
            }
            
            // Если файл заблокирован, ждем асинхронно и пробуем снова
            if (!readSuccess && retryCount < 10)
            {
                // Увеличиваем время ожидания с каждой попыткой
                float waitTime = 0.3f + (retryCount * 0.2f);
                Debug.LogWarning($"{LogPrefix} Файл заблокирован, повторная попытка {retryCount + 1}/10 через {waitTime:F1}с: {readError}");
                yield return new WaitForSeconds(waitTime);
                
                // Рекурсивно вызываем себя через новую корутину
                var retryResult = new GLBValidationResult();
                yield return StartCoroutine(ValidateGLBFileAsync(filePath, retryResult, retryCount + 1));
                result.IsValid = retryResult.IsValid;
                result.Error = retryResult.Error;
                yield break;
            }
            else if (!readSuccess)
            {
                result.Error = $"Ошибка доступа к файлу после {retryCount} попыток: {readError}";
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
                if (fileInfo.Length < declaredLength && retryCount < 15)
                {
                    // Увеличиваем время ожидания с каждой попыткой (0.5с, 1с, 1.5с, 2с...)
                    float waitTime = 0.5f + (retryCount * 0.5f);
                    
                    // Перед повторной попыткой проверяем стабильность файла
                    yield return StartCoroutine(WaitForFileStable(filePath, waitTime));
                    
                    // Рекурсивно вызываем себя через новую корутину
                    var retryResult = new GLBValidationResult();
                    yield return StartCoroutine(ValidateGLBFileAsync(filePath, retryResult, retryCount + 1));
                    result.IsValid = retryResult.IsValid;
                    result.Error = retryResult.Error;
                    yield break;
                }
                
                // Если размер файла больше заявленного - файл поврежден или неверный заголовок
                // Если размер меньше и исчерпаны попытки - файл не полностью скачан
                if (fileInfo.Length > declaredLength)
                {
                    result.Error = $"Файл поврежден: размер файла ({fileInfo.Length} байт) превышает заявленную длину ({declaredLength} байт)";
                }
                else
                {
                    result.Error = $"Файл не полностью скачан: заявлено {declaredLength} байт, реально {fileInfo.Length} байт. Попробуйте перезагрузить модель.";
                }
                yield break;
            }

            result.IsValid = true;
        }
        
        /// <summary>
        /// Автоматически восстанавливает неполностью скачанный файл
        /// </summary>
        private IEnumerator AutoRecoverFile(ModelLoadOperation operation, string originalError)
        {
            operation.AutoRecoveryAttempts++;
            int attempt = operation.AutoRecoveryAttempts;
            
            MainScreenController.LogToMainScreen($"Автоматическое восстановление модели (попытка {attempt}/{maxAutoRecoveryAttempts})...", operation.ArtifactId);
            
            if (File.Exists(operation.LocalPath))
            {
                try
                {
                    File.Delete(operation.LocalPath);
                }
                catch (Exception e)
                {
                    Debug.LogError($"{LogPrefix} [Автовосстановление] Не удалось удалить файл: {e.Message}");
                    FailOperation(operation, $"Не удалось удалить поврежденный файл: {e.Message}");
                    yield break;
                }
            }
            
            float delay = autoRecoveryBaseDelay * Mathf.Pow(2f, attempt - 1);
            yield return new WaitForSeconds(delay);
            
            bool downloadCompleted = false;
            bool downloadSuccess = false;
            string downloadError = null;
            
            string mediaId = ExtractMediaIdFromPath(operation.LocalPath) ?? operation.ArtifactId;
            
            ArtifactMediaService.Instance.DownloadModel(
                operation.ArtifactId,
                mediaId,
                operation.RemoteUrl,
                localPath =>
                {
                    downloadCompleted = true;
                    downloadSuccess = true;
                    operation.LocalPath = localPath;
                },
                error =>
                {
                    downloadCompleted = true;
                    downloadSuccess = false;
                    downloadError = error;
                    Debug.LogError($"{LogPrefix} [Автовосстановление] Ошибка перезагрузки: {error}");
                });
            
            // Ждем завершения загрузки (без таймаута)
            while (!downloadCompleted)
            {
                yield return null;
            }
            
            if (!downloadSuccess)
            {
                FailOperation(operation, $"Ошибка автоматического восстановления: {downloadError}");
                yield break;
            }
            
            // Проверяем, что файл существует
            if (!File.Exists(operation.LocalPath))
            {
                FailOperation(operation, "Файл не найден после автоматического восстановления");
                yield break;
            }
            
            // Повторяем валидацию
            Debug.Log($"{LogPrefix} [Автовосстановление] Повторная валидация файла...");
            var retryValidationResult = new GLBValidationResult();
            yield return StartCoroutine(ValidateGLBFileAsync(operation.LocalPath, retryValidationResult));
            
            if (!retryValidationResult.IsValid)
            {
                if (operation.AutoRecoveryAttempts < maxAutoRecoveryAttempts)
                {
                    yield return StartCoroutine(AutoRecoverFile(operation, retryValidationResult.Error));
                    yield break;
                }
                else
                {
                    string errorMessage = $"Автоматическое восстановление не удалось после {maxAutoRecoveryAttempts} попыток: {retryValidationResult.Error}";
                    MainScreenController.LogToMainScreen($"Ошибка: {errorMessage}", operation.ArtifactId);
                    FailOperation(operation, errorMessage);
                    yield break;
                }
            }
            
            MainScreenController.LogToMainScreen("Модель успешно восстановлена", operation.ArtifactId);
            yield return StartCoroutine(LoadModelCoroutine(operation));
        }
        
        private string ExtractMediaIdFromPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return null;
            
            try
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                // Формат: artifactId_mediaId.glb
                // Ищем последний подчеркивание, после которого идет mediaId
                int lastUnderscore = fileName.LastIndexOf('_');
                if (lastUnderscore >= 0 && lastUnderscore < fileName.Length - 1)
                {
                    return fileName.Substring(lastUnderscore + 1);
                }
            }
            catch
            {
                // Игнорируем ошибки парсинга
            }
            
            return null;
        }
        
        /// <summary>
        /// Ожидает стабилизации размера файла (файл не изменяется в течение нескольких проверок)
        /// </summary>
        private IEnumerator WaitForFileStable(string filePath, float maxWaitTime = 2f, float checkInterval = 0.2f)
        {
            if (!File.Exists(filePath))
            {
                yield break;
            }
            
            float startTime = Time.time;
            long lastSize = 0;
            int stableCount = 0;
            const int requiredStableChecks = 3; // Файл должен быть стабильным 3 проверки подряд
            
            while (Time.time - startTime < maxWaitTime)
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    fileInfo.Refresh();
                    long currentSize = fileInfo.Length;
                    
                    if (currentSize == lastSize)
                    {
                        stableCount++;
                        if (stableCount >= requiredStableChecks)
                        {
                            // Размер файла стабилен
                            yield break;
                        }
                    }
                    else
                    {
                        stableCount = 0;
                        lastSize = currentSize;
                    }
                }
                catch
                {
                    // Игнорируем ошибки доступа к файлу
                }
                
                yield return new WaitForSeconds(checkInterval);
            }
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
        /// <param name="remoteUrl">URL для повторной загрузки при необходимости (опционально)</param>
        public void RequestModelLoad(
            string artifactId,
            string localPath,
            string metadataJson,
            Action<GameObject> onSuccess,
            Action<string> onError,
            string remoteUrl = null)
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

            // Проверяем кеш ошибок с экспоненциальным backoff
            if (failedLoads.TryGetValue(artifactId, out var failedInfo))
            {
                float timeSinceFail = (float)(DateTime.Now - failedInfo.LastFailureTime).TotalSeconds;
                
                // Рассчитываем задержку: начальная 5 секунд, затем удваивается при каждой попытке
                // attemptCount=0: 5с, attemptCount=1: 10с, attemptCount=2: 20с, attemptCount=3: 40с, и т.д.
                float retryDelay = InitialRetryDelay * Mathf.Pow(2f, failedInfo.AttemptCount);
                retryDelay = Mathf.Min(retryDelay, MaxRetryDelay); // Ограничиваем максимумом
                
                if (timeSinceFail < retryDelay)
                {
                    // Не прошло достаточно времени для повторной попытки
                    float remainingTime = retryDelay - timeSinceFail;
                    Debug.LogWarning($"{LogPrefix} Пропускаем загрузку {artifactId}: предыдущая ошибка была {timeSinceFail:F1}с назад (попытка {failedInfo.AttemptCount + 1}, задержка {retryDelay:F0}с). Ошибка: {failedInfo.ErrorMessage}");
                    onError?.Invoke($"Поврежденный файл (повторная попытка через {remainingTime:F0}с): {failedInfo.ErrorMessage}");
                    return;
                }
                else
                {
                    // Прошло достаточно времени, готовы к повторной попытке
                    Debug.Log($"{LogPrefix} Повторная попытка загрузки {artifactId} после {timeSinceFail:F1}с (попытка {failedInfo.AttemptCount + 1}, задержка была {retryDelay:F0}с)");
                    // Не удаляем из кеша - счетчик попыток будет обновлен при следующей ошибке
                }
            }

            if (loadedModels.TryGetValue(artifactId, out var loadedData))
            {
                if (loadedData.ModelInstance != null)
                {
                    loadedData.LastAccessedAt = DateTime.UtcNow;
                    loadedData.ReferenceCount++;
                    UpdateAccessOrder(artifactId);
                    onSuccess?.Invoke(loadedData.ModelInstance);
                    return;
                }
                else
                {
                    loadedModels.Remove(artifactId);
                    modelAccessOrder.Remove(artifactId);
                }
            }

            if (activeLoads.TryGetValue(artifactId, out var existingOperation))
            {
                existingOperation.SuccessCallbacks.Add(onSuccess);
                existingOperation.ErrorCallbacks.Add(onError);
                return;
            }

            var operation = new ModelLoadOperation
            {
                ArtifactId = artifactId,
                LocalPath = localPath,
                RemoteUrl = remoteUrl,
                MetadataJson = metadataJson,
                AutoRecoveryAttempts = 0
            };
            operation.SuccessCallbacks.Add(onSuccess);
            operation.ErrorCallbacks.Add(onError);
            activeLoads[artifactId] = operation;

            operation.LoadCoroutine = StartCoroutine(ValidateAndLoadModelCoroutine(operation));
            
            OnLoadStarted?.Invoke(artifactId);
        }
        
        private IEnumerator ValidateAndLoadModelCoroutine(ModelLoadOperation operation)
        {
            var validationResult = new GLBValidationResult();
            yield return StartCoroutine(ValidateGLBFileAsync(operation.LocalPath, validationResult));
            
            // Проверяем результат валидации
            if (!validationResult.IsValid)
            {
                // Проверяем, можно ли автоматически восстановить файл
                bool isIncompleteDownload = validationResult.Error.Contains("не полностью скачан") || 
                                           validationResult.Error.Contains("не полностью загружен");
                
                if (isIncompleteDownload && !string.IsNullOrEmpty(operation.RemoteUrl) && 
                    operation.AutoRecoveryAttempts < maxAutoRecoveryAttempts)
                {
                    // Пытаемся автоматически восстановить файл
                    yield return StartCoroutine(AutoRecoverFile(operation, validationResult.Error));
                    yield break;
                }
                
                string errorMessage = $"Файл поврежден: {validationResult.Error}";
                // Логируем в MainScreen вместо консоли
                MainScreenController.LogToMainScreen($"Ошибка загрузки модели: {errorMessage}", operation.ArtifactId);
                Debug.LogWarning($"{LogPrefix} Файл не прошел валидацию: {operation.LocalPath}, ошибка: {validationResult.Error}");
                // Сохраняем ошибку в кеш с обновлением счетчика попыток
                RecordFailedLoad(operation.ArtifactId, errorMessage);
                FailOperation(operation, errorMessage);
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
            }
        }

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
            }
        }
        
        private void UpdateAccessOrder(string artifactId)
        {
            modelAccessOrder.Remove(artifactId);
            modelAccessOrder.AddLast(artifactId);
        }
        
        private void EnsureCacheSpace()
        {
            while (loadedModels.Count >= maxLoadedModels && modelAccessOrder.Count > 0)
            {
                var oldestId = modelAccessOrder.First.Value;
                var oldestData = loadedModels[oldestId];
                
                if (oldestData.ReferenceCount > 0)
                {
                    modelAccessOrder.RemoveFirst();
                    modelAccessOrder.AddLast(oldestId);
                    continue;
                }
                
                UnloadModel(oldestId);
            }
        }
        
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
                UnloadModel(artifactId);
            }
            
            if (modelsToRemove.Count > 0)
            {
                StartCoroutine(UnloadUnusedAssetsAsync());
            }
        }
        
        private IEnumerator UnloadUnusedAssetsAsync()
        {
            yield return new WaitForEndOfFrame();
            yield return null;
            
            var asyncOperation = Resources.UnloadUnusedAssets();
            
            while (!asyncOperation.isDone)
            {
                yield return null;
            }
        }
        
        private void CleanupFailedLoadsCache()
        {
            if (failedLoads.Count > maxFailedLoadsCache)
            {
                var toRemove = failedLoads.Count - maxFailedLoadsCache;
                var keysToRemove = new List<string>();
                
                // Удаляем самые старые записи
                foreach (var kvp in failedLoads.OrderBy(x => x.Value.LastFailureTime).Take(toRemove))
                {
                    keysToRemove.Add(kvp.Key);
                }
                
                foreach (var key in keysToRemove)
                {
                    failedLoads.Remove(key);
                }
            }
            
            var now = DateTime.Now;
            var expiredKeys = new List<string>();
            
            // Удаляем записи, которые не обновлялись более чем в 2 раза от максимальной задержки
            foreach (var kvp in failedLoads)
            {
                float timeSinceFail = (float)(now - kvp.Value.LastFailureTime).TotalSeconds;
                if (timeSinceFail >= MaxRetryDelay * 2)
                {
                    expiredKeys.Add(kvp.Key);
                }
            }
            
            foreach (var key in expiredKeys)
            {
                failedLoads.Remove(key);
            }
        }
        
        /// <summary>
        /// Записывает информацию об ошибке загрузки с обновлением счетчика попыток
        /// </summary>
        private void RecordFailedLoad(string artifactId, string errorMessage)
        {
            if (failedLoads.TryGetValue(artifactId, out var existingInfo))
            {
                // Обновляем существующую запись: увеличиваем счетчик попыток
                existingInfo.AttemptCount++;
                existingInfo.LastFailureTime = DateTime.Now;
                existingInfo.ErrorMessage = errorMessage;
            }
            else
            {
                // Создаем новую запись: первая попытка (attemptCount = 0)
                failedLoads[artifactId] = new FailedLoadInfo
                {
                    ErrorMessage = errorMessage,
                    LastFailureTime = DateTime.Now,
                    AttemptCount = 0
                };
            }
        }

        public void ClearFailedLoadCache(string artifactId)
        {
            failedLoads.Remove(artifactId);
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
                string errorMessage = $"Ошибка запуска загрузки: {e.Message}";
                // Логируем в MainScreen вместо консоли
                MainScreenController.LogToMainScreen($"Ошибка загрузки модели: {errorMessage}", operation.ArtifactId);
                Debug.LogWarning($"{LogPrefix} [Загрузка] Синхронная ошибка GLTF загрузчика: {e.Message}");
                FailOperation(operation, errorMessage);
                yield break;
            }

            // Ожидаем завершения загрузки с обновлением прогресса
            float elapsed = 0f;
            float lastProgressUpdate = 0f;
            const float progressUpdateInterval = 0.1f; // Обновляем прогресс каждые 0.1 секунды
            const float estimatedMaxTime = 120f; // Оценочное максимальное время для расчета прогресса (не таймаут!)

            while (!loadTask.IsCompleted)
            {
                elapsed += Time.deltaTime;
                
                // Обновляем прогресс (приблизительно, т.к. UnityGLTF не предоставляет точный прогресс)
                if (elapsed - lastProgressUpdate > progressUpdateInterval)
                {
                    // Прогресс от 0.1 до 0.9 на основе времени (но без таймаута)
                    // Используем логарифмическую функцию для более плавного прогресса
                    float timeProgress = Mathf.Clamp01(elapsed / estimatedMaxTime);
                    operation.Progress = Mathf.Lerp(0.1f, 0.9f, timeProgress);
                    lastProgressUpdate = elapsed;
                }
                
                yield return null;
            }

            operation.Progress = 0.9f;

            if (loadTask.IsFaulted)
            {
                string error = loadTask.Exception?.GetBaseException().Message ?? "Неизвестная ошибка";
                string errorMessage = $"Ошибка загрузки: {error}";
                // Логируем в MainScreen вместо консоли
                MainScreenController.LogToMainScreen($"Ошибка загрузки модели: {errorMessage}", operation.ArtifactId);
                Debug.LogWarning($"{LogPrefix} [Загрузка] Ошибка GLTF загрузки: {error}");
                FailOperation(operation, errorMessage);
                yield break;
            }

            var loadedScene = gltfComponent.LastLoadedScene;

            if (loadedScene == null)
            {
                string errorMessage = "Модель не содержит объектов";
                MainScreenController.LogToMainScreen($"Ошибка загрузки модели: {errorMessage}", operation.ArtifactId);
                Debug.LogWarning($"{LogPrefix} [Загрузка] GLTF сцена не содержит объектов для artifactId={operation.ArtifactId}");
                FailOperation(operation, errorMessage);
                yield break;
            }

            if (loadedScene.transform.parent == null)
            {
                loadedScene.transform.SetParent(hiddenContainer, false);
            }
            else if (loadedScene.transform.parent != loaderObject.transform)
            {
                loadedScene.transform.SetParent(hiddenContainer, false);
            }
            else
            {
                loadedScene.transform.SetParent(hiddenContainer, false);
            }

            if (loadedScene.transform.parent != hiddenContainer)
            {
                Debug.LogError($"{LogPrefix} [Загрузка] ОШИБКА: Модель не в скрытом контейнере! Принудительно перемещаем...");
                loadedScene.transform.SetParent(hiddenContainer, false);
            }

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

            // Очищаем кеш ошибок при успешной загрузке (сбрасываем счетчик попыток)
            if (failedLoads.ContainsKey(operation.ArtifactId))
            {
                failedLoads.Remove(operation.ArtifactId);
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
            
            operation.SuccessCallbacks.Clear();
            operation.ErrorCallbacks.Clear();
        }

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
                RecordFailedLoad(artifactId, error);
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




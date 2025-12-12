using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ARArtifact.Services
{
    /// <summary>
    /// Управляет размещением видео на AR сцене (в TrackedVideoHost).
    /// Координирует работу между загрузкой видео и TrackedVideoHost.
    /// </summary>
    public class VideoSceneManager : MonoBehaviour
    {
        private const string LogPrefix = "[VideoSceneManager]";

        private static VideoSceneManager _instance;
        public static VideoSceneManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("VideoSceneManager");
                    _instance = go.AddComponent<VideoSceneManager>();
                    DontDestroyOnLoad(go);
                }

                return _instance;
            }
        }

        /// <summary>
        /// Информация о видео, размещенном на сцене
        /// </summary>
        private class SceneVideoInstance
        {
            public string ArtifactId;
            public TrackedVideoHost Host;
            public GameObject VideoInstance;
            public bool IsActive;
        }
        
        /// <summary>
        /// Информация об активной операции размещения видео
        /// </summary>
        private class PlacementOperation
        {
            public string OperationId;
            public string ArtifactId;
            public TrackedVideoHost TargetHost;
            public bool IsCancelled;
            public bool IsLoading = false; // Флаг загрузки
            public string RemoteUrl; // URL для повторной загрузки при ошибке
            public string MediaId; // MediaId для повторной загрузки
            public string LocalPath; // Локальный путь к файлу
            public string MetadataJson; // Метаданные видео из БД (JSON)
            public int AutoRecoveryAttempts = 0; // Количество попыток автовосстановления
            public GameObject VideoInstance = null; // Ссылка на экземпляр видео
        }
        
        [SerializeField] private int maxAutoRecoveryAttempts = 3; // Максимум попыток автоматического восстановления
        [SerializeField] private float autoRecoveryBaseDelay = 2f; // Базовая задержка между попытками (секунды)

        private readonly Dictionary<string, SceneVideoInstance> sceneVideos = new();
        private readonly Dictionary<string, PlacementOperation> activePlacements = new(); // artifactId -> operation

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

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>
        /// Запрашивает видео для размещения в хосте.
        /// </summary>
        /// <param name="artifactId">ID артефакта</param>
        /// <param name="host">Хост для размещения видео</param>
        /// <param name="videoPath">Локальный путь к видео файлу (null для YouTube)</param>
        /// <param name="videoUrl">URL видео (для YouTube или blob)</param>
        /// <param name="isYouTube">Является ли видео YouTube</param>
        /// <param name="onSuccess">Колбэк при успешном размещении</param>
        /// <param name="onError">Колбэк при ошибке</param>
        /// <param name="remoteUrl">URL для повторной загрузки при ошибке</param>
        /// <param name="mediaId">MediaId для повторной загрузки</param>
        /// <param name="metadataJson">Метаданные видео из БД (JSON, опционально)</param>
        public void RequestVideoForHost(
            string artifactId,
            TrackedVideoHost host,
            string videoPath,
            string videoUrl,
            bool isYouTube,
            Action onSuccess,
            Action<string> onError,
            string remoteUrl = null,
            string mediaId = null,
            string metadataJson = null)
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

            if (isYouTube && string.IsNullOrEmpty(videoUrl))
            {
                onError?.Invoke("VideoUrl пуст для YouTube видео");
                return;
            }

            if (!isYouTube && string.IsNullOrEmpty(videoPath))
            {
                onError?.Invoke("VideoPath пуст для blob видео");
                return;
            }

            // КРИТИЧНО: Захватываем локальные копии для предотвращения race condition
            string capturedArtifactId = artifactId;
            TrackedVideoHost capturedHost = host;
            string capturedVideoPath = videoPath;
            string capturedVideoUrl = videoUrl;
            bool capturedIsYouTube = isYouTube;
            
            string operationId = Guid.NewGuid().ToString();

            if (sceneVideos.TryGetValue(capturedArtifactId, out var existingInstance))
            {
                if (existingInstance.Host == capturedHost && existingInstance.IsActive)
                {
                    if (existingInstance.VideoInstance != null)
                    {
                        onSuccess?.Invoke();
                        return;
                    }
                    else
                    {
                        sceneVideos.Remove(capturedArtifactId);
                    }
                }
                else if (existingInstance.Host != capturedHost)
                {
                    Debug.LogWarning($"{LogPrefix} Видео {capturedArtifactId} размещено в другом хосте, удаляем из старого");
                    RemoveVideoFromHost(capturedArtifactId, existingInstance.Host);
                }
            }

            if (activePlacements.TryGetValue(capturedArtifactId, out var existingOp))
            {
                if (existingOp.IsLoading && !existingOp.IsCancelled)
                {
                    Debug.LogWarning($"{LogPrefix} Видео {capturedArtifactId} уже загружается, отменяем предыдущую операцию");
                    existingOp.IsCancelled = true;
                    
                    // Освобождаем файл из старого экземпляра
                    if (existingOp.VideoInstance != null)
                    {
                        var oldPlayer = existingOp.VideoInstance.GetComponent<ARVideoPlayer>();
                        if (oldPlayer != null)
                        {
                            StartCoroutine(oldPlayer.ForceReleaseFileCoroutine());
                        }
                    }
                }
                activePlacements.Remove(capturedArtifactId);
            }

            var placementOp = new PlacementOperation
            {
                OperationId = operationId,
                ArtifactId = capturedArtifactId,
                TargetHost = capturedHost,
                IsCancelled = false,
                IsLoading = true,
                RemoteUrl = remoteUrl,
                MediaId = mediaId,
                LocalPath = capturedVideoPath,
                MetadataJson = metadataJson,
                VideoInstance = null
            };
            activePlacements[capturedArtifactId] = placementOp;
            
            StartCoroutine(CreateAndPlaceVideoAsync(
                placementOp,
                capturedVideoPath,
                capturedVideoUrl,
                capturedIsYouTube,
                onSuccess,
                onError,
                metadataJson));
        }
        
        private IEnumerator CreateAndPlaceVideoAsync(
            PlacementOperation operation,
            string videoPath,
            string videoUrl,
            bool isYouTube,
            Action onSuccess,
            Action<string> onError,
            string metadataJson = null)
        {
            if (operation.IsCancelled)
            {
                yield break;
            }
            
            if (operation.TargetHost == null)
            {
                Debug.LogError($"{LogPrefix} Хост был уничтожен до создания видео {operation.ArtifactId}");
                activePlacements.Remove(operation.ArtifactId);
                onError?.Invoke("Хост был уничтожен");
                yield break;
            }
            
            yield return null;
            
            string videoObjectName = $"ARVideo_{operation.ArtifactId}_{Guid.NewGuid()}";
            GameObject videoObject = new GameObject(videoObjectName);
            videoObject.SetActive(true);
            ARVideoPlayer arVideoPlayer = videoObject.AddComponent<ARVideoPlayer>();
            
            // Сохранить ссылку на экземпляр
            operation.VideoInstance = videoObject;
            
            yield return null;
            
            bool hostDestroyed = ReferenceEquals(operation.TargetHost, null) || operation.TargetHost == null;
            bool operationCancelled = operation.IsCancelled;
            
            if (hostDestroyed || operationCancelled)
            {
                if (hostDestroyed)
                {
                    Debug.LogWarning($"{LogPrefix} Хост уничтожен до размещения видео {operation.ArtifactId}, уничтожаем видео");
                }
                
                if (videoObject != null)
                {
                    DestroyImmediate(videoObject);
                }
                
                activePlacements.Remove(operation.ArtifactId);
                if (hostDestroyed)
                {
                    onError?.Invoke("Хост был уничтожен");
                }
                yield break;
            }
            
            if (activePlacements.TryGetValue(operation.ArtifactId, out var currentOp))
            {
                if (currentOp.OperationId != operation.OperationId)
                {
                    if (videoObject != null)
                    {
                        DestroyImmediate(videoObject);
                    }
                    
                    yield break;
                }
            }
            
            // Размещаем видео в хосте
            bool success = false;
            string errorMessage = null;
            bool isCorruptedVideo = false;
            
            string savedVideoObjectName = videoObject != null ? videoObject.name : videoObjectName;
            
            if (videoObject != null && !videoObject.activeSelf)
            {
                videoObject.SetActive(true);
            }
            
            // Проверяем файл перед загрузкой (только для локальных файлов)
            if (!isYouTube && !string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
            {
                try
                {
                    var fileInfo = new FileInfo(videoPath);
                    if (fileInfo.Length == 0)
                    {
                        errorMessage = $"Video file is empty (0 bytes): {videoPath}";
                        Debug.LogError($"{LogPrefix} {errorMessage}");
                        isCorruptedVideo = true;
                        
                        // Пытаемся автовосстановить
                        if (!string.IsNullOrEmpty(operation.RemoteUrl) && 
                            !string.IsNullOrEmpty(operation.MediaId) &&
                            operation.AutoRecoveryAttempts < maxAutoRecoveryAttempts)
                        {
                            Debug.LogWarning($"{LogPrefix} Обнаружен пустой файл, запускаем автовосстановление...");
                            StartCoroutine(AutoRecoverVideo(operation, errorMessage));
                        }
                        
                        if (videoObject != null)
                        {
                            DestroyImmediate(videoObject);
                        }
                        activePlacements.Remove(operation.ArtifactId);
                        onError?.Invoke(errorMessage);
                        yield break;
                    }
                    else if (fileInfo.Length < 1024)
                    {
                        errorMessage = $"Video file is too small ({fileInfo.Length} bytes), file may be corrupted: {videoPath}";
                        Debug.LogError($"{LogPrefix} {errorMessage}");
                        isCorruptedVideo = true;
                        
                        // Пытаемся автовосстановить
                        if (!string.IsNullOrEmpty(operation.RemoteUrl) && 
                            !string.IsNullOrEmpty(operation.MediaId) &&
                            operation.AutoRecoveryAttempts < maxAutoRecoveryAttempts)
                        {
                            Debug.LogWarning($"{LogPrefix} Обнаружен слишком маленький файл, запускаем автовосстановление...");
                            StartCoroutine(AutoRecoverVideo(operation, errorMessage));
                        }
                        
                        if (videoObject != null)
                        {
                            DestroyImmediate(videoObject);
                        }
                        activePlacements.Remove(operation.ArtifactId);
                        onError?.Invoke(errorMessage);
                        yield break;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{LogPrefix} Ошибка проверки файла перед загрузкой: {e.Message}");
                }
            }
            
            bool videoLoadError = false;
            string videoLoadErrorMessage = null;
            
            // Временно сохраняем операцию для доступа из колбэка
            var currentOperation = operation;
            
            // Парсим метаданные из JSON, если они есть
            VideoMetadata videoMetadata = null;
            if (!string.IsNullOrEmpty(metadataJson))
            {
                // Выводим сырой JSON до парсинга
                Debug.Log($"{LogPrefix} [CreateAndPlaceVideoAsync] Raw metadata JSON before parsing: {metadataJson}");
                
                try
                {
                    videoMetadata = JsonUtility.FromJson<VideoMetadata>(metadataJson);
                    if (videoMetadata != null)
                    {
                        Debug.Log($"{LogPrefix} [CreateAndPlaceVideoAsync] Parsed video metadata: width={videoMetadata.width}, height={videoMetadata.height}, duration={videoMetadata.duration}, filename={videoMetadata.filename}, size={videoMetadata.size}");
                        if (videoMetadata.IsValid())
                        {
                            Debug.Log($"{LogPrefix} [CreateAndPlaceVideoAsync] Valid video metadata: {videoMetadata.width}x{videoMetadata.height}, duration={videoMetadata.duration}s");
                        }
                        else
                        {
                            Debug.LogWarning($"{LogPrefix} [CreateAndPlaceVideoAsync] Invalid video metadata from JSON (width={videoMetadata.width}, height={videoMetadata.height}, duration={videoMetadata.duration}). Using 1:1 fallback.");
                            videoMetadata = null; // Будет использован фоллбэк 1:1
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"{LogPrefix} [CreateAndPlaceVideoAsync] JsonUtility.FromJson returned null for metadata JSON: {metadataJson}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{LogPrefix} [CreateAndPlaceVideoAsync] Failed to parse video metadata: {e.Message}, JSON: {metadataJson}");
                    videoMetadata = null;
                }
            }
            else
            {
                Debug.Log($"{LogPrefix} [CreateAndPlaceVideoAsync] No metadata JSON provided, will use 1:1 fallback");
            }
            
            operation.TargetHost.AttachVideo(videoObject, operation.ArtifactId, 
                isYouTube ? videoUrl : videoPath, isYouTube, videoMetadata,
                onError: error =>
                {
                    videoLoadError = true;
                    videoLoadErrorMessage = error;
                    
                    // Проверяем, является ли ошибка признаком битого файла
                    bool isCorruptedError = error.Contains("0xc00d36e6") || 
                                           error.Contains("WindowsVideoMedia error") || 
                                           error.Contains("Cannot read file") || 
                                           error.Contains("Getting duration") ||
                                           error.Contains("не имеет метаданных длины") ||
                                           error.Contains("length=0") ||
                                           error.Contains("length=NaN") ||
                                           error.Contains("Таймаут подготовки видео"); // Таймаут может быть признаком битого файла
                    
                    if (isCorruptedError)
                    {
                        isCorruptedVideo = true;
                        
                        // Проверяем, не загружается ли файл
                        bool isDownloading = false;
                        var mediaService = ARArtifact.Services.ArtifactMediaService.Instance;
                        if (mediaService != null && !string.IsNullOrEmpty(currentOperation.ArtifactId))
                        {
                            isDownloading = mediaService.IsDownloading(currentOperation.ArtifactId);
                        }
                        
                        // Пытаемся автовосстановить, если есть remoteUrl и файл не загружается
                        if (!isDownloading &&
                            !string.IsNullOrEmpty(currentOperation.RemoteUrl) && 
                            !string.IsNullOrEmpty(currentOperation.MediaId) &&
                            currentOperation.AutoRecoveryAttempts < maxAutoRecoveryAttempts)
                        {
                            Debug.LogWarning($"{LogPrefix} Обнаружено битое видео (artifactId={currentOperation.ArtifactId}, mediaId={currentOperation.MediaId}), запускаем автовосстановление...");
                            StartCoroutine(AutoRecoverVideo(currentOperation, error));
                        }
                        else if (isDownloading)
                        {
                            Debug.LogWarning($"{LogPrefix} Обнаружена ошибка видео, но файл еще загружается. Ожидаем завершения загрузки...");
                        }
                        else
                        {
                            Debug.LogError($"{LogPrefix} Обнаружено битое видео, но автовосстановление невозможно: remoteUrl={(string.IsNullOrEmpty(currentOperation.RemoteUrl) ? "NULL" : "OK")}, mediaId={(string.IsNullOrEmpty(currentOperation.MediaId) ? "NULL" : "OK")}, attempts={currentOperation.AutoRecoveryAttempts}/{maxAutoRecoveryAttempts}");
                        }
                    }
                });

            yield return new WaitForSeconds(0.5f);
            
            if (videoLoadError)
            {
                Debug.LogError($"{LogPrefix} [CreateAndPlaceVideoAsync] ОШИБКА при загрузке видео: {videoLoadErrorMessage}");
                
                if (videoObject != null)
                {
                    DestroyImmediate(videoObject);
                }
                
                errorMessage = $"Ошибка загрузки: {videoLoadErrorMessage}";
            }
            else
            {
                var sceneInstance = new SceneVideoInstance
                {
                    ArtifactId = operation.ArtifactId,
                    Host = operation.TargetHost,
                    VideoInstance = videoObject,
                    IsActive = true
                };
                sceneVideos[operation.ArtifactId] = sceneInstance;
                
                success = true;
                operation.IsLoading = false;
            }
            
            activePlacements.Remove(operation.ArtifactId);
            
            yield return null;
            
            if (success)
            {
                onSuccess?.Invoke();
            }
            else if (!isCorruptedVideo)
            {
                onError?.Invoke(errorMessage);
            }
        }
        
        private IEnumerator AutoRecoverVideo(PlacementOperation operation, string originalError)
        {
            operation.AutoRecoveryAttempts++;
            int attempt = operation.AutoRecoveryAttempts;
            
            Debug.LogWarning($"{LogPrefix} [Автовосстановление] Попытка {attempt}/{maxAutoRecoveryAttempts} для {operation.ArtifactId}");
            
            // Проверяем, не запущено ли уже восстановление для этого артефакта
            if (activePlacements.TryGetValue(operation.ArtifactId, out var existingOp))
            {
                if (existingOp.OperationId != operation.OperationId && existingOp.IsLoading)
                {
                    Debug.LogWarning($"{LogPrefix} [Автовосстановление] Восстановление уже запущено для {operation.ArtifactId}, отменяем дубликат");
                    yield break;
                }
            }
            
            // Помечаем операцию как активную для предотвращения дубликатов
            operation.IsLoading = true;
            activePlacements[operation.ArtifactId] = operation;
            
            // 1. Остановить и освободить все существующие экземпляры
            if (sceneVideos.TryGetValue(operation.ArtifactId, out var videoInstance) && videoInstance.VideoInstance != null)
            {
                var arVideoPlayer = videoInstance.VideoInstance.GetComponent<ARVideoPlayer>();
                if (arVideoPlayer != null)
                {
                    yield return StartCoroutine(arVideoPlayer.ForceReleaseFileCoroutine());
                }
                
                // Уничтожить GameObject
                if (videoInstance.VideoInstance != null)
                {
                    DestroyImmediate(videoInstance.VideoInstance);
                }
                
                sceneVideos.Remove(operation.ArtifactId);
            }
            
            // 2. Освободить файл из активной операции
            if (operation.VideoInstance != null)
            {
                var arVideoPlayer = operation.VideoInstance.GetComponent<ARVideoPlayer>();
                if (arVideoPlayer != null)
                {
                    yield return StartCoroutine(arVideoPlayer.ForceReleaseFileCoroutine());
                }
                DestroyImmediate(operation.VideoInstance);
                operation.VideoInstance = null;
            }
            
            // 3. Попытка удаления файла с увеличенными задержками
            yield return new WaitForSeconds(1.0f);
            if (!string.IsNullOrEmpty(operation.LocalPath) && File.Exists(operation.LocalPath))
            {
                bool fileDeleted = false;
                int maxDeleteAttempts = 10;
                float deleteRetryDelay = 1.0f;
                
                for (int i = 0; i < maxDeleteAttempts; i++)
                {
                    bool shouldRetry = false;
                    try
                    {
                        File.Delete(operation.LocalPath);
                        if (!File.Exists(operation.LocalPath))
                        {
                            fileDeleted = true;
                            Debug.Log($"{LogPrefix} [Автовосстановление] Файл успешно удален (попытка {i + 1})");
                            break;
                        }
                    }
                    catch (Exception e)
                    {
                        if (i < maxDeleteAttempts - 1)
                        {
                            Debug.LogWarning($"{LogPrefix} [Автовосстановление] Попытка {i + 1} удаления файла не удалась: {e.Message}. Повтор через {deleteRetryDelay}с...");
                            shouldRetry = true;
                        }
                        else
                        {
                            Debug.LogError($"{LogPrefix} [Автовосстановление] Не удалось удалить файл после {maxDeleteAttempts} попыток: {e.Message}");
                        }
                    }
                    
                    // Ожидаем перед следующей попыткой (вне блока catch)
                    if (shouldRetry && !fileDeleted)
                    {
                        yield return new WaitForSeconds(deleteRetryDelay);
                    }
                }
                
                if (!fileDeleted)
                {
                    // Использовать альтернативный путь для загрузки
                    Debug.LogWarning($"{LogPrefix} [Автовосстановление] Файл заблокирован, используем альтернативный путь");
                    string altPath = operation.LocalPath + ".tmp";
                    operation.LocalPath = altPath;
                }
            }
            
            // 4. Ждем завершения текущей загрузки, если она есть
            var mediaService = ARArtifact.Services.ArtifactMediaService.Instance;
            if (mediaService != null && !string.IsNullOrEmpty(operation.ArtifactId))
            {
                int waitCount = 0;
                const int maxWaitCount = 300; // Максимум 30 секунд (300 * 0.1s)
                while (mediaService.IsDownloading(operation.ArtifactId) && waitCount < maxWaitCount)
                {
                    yield return new WaitForSeconds(0.1f);
                    waitCount++;
                }
                
                if (waitCount >= maxWaitCount)
                {
                    Debug.LogWarning($"{LogPrefix} [Автовосстановление] Таймаут ожидания завершения загрузки для {operation.ArtifactId}");
                }
            }
            
            float delay = autoRecoveryBaseDelay * Mathf.Pow(2f, attempt - 1);
            yield return new WaitForSeconds(delay);
            
            bool downloadCompleted = false;
            bool downloadSuccess = false;
            string downloadError = null;
            string newLocalPath = null;
            
            ArtifactMediaService.Instance.DownloadVideo(
                operation.ArtifactId,
                operation.MediaId,
                operation.RemoteUrl,
                localPath =>
                {
                    downloadCompleted = true;
                    downloadSuccess = true;
                    newLocalPath = localPath;
                },
                error =>
                {
                    downloadCompleted = true;
                    downloadSuccess = false;
                    downloadError = error;
                    Debug.LogError($"{LogPrefix} [Автовосстановление] Ошибка перезагрузки: {error}");
                });
            
            // Ждем завершения загрузки
            while (!downloadCompleted)
            {
                yield return null;
            }
            
            // Ждем завершения записи файла на диск
            if (downloadSuccess && !string.IsNullOrEmpty(newLocalPath))
            {
                yield return new WaitForSeconds(0.5f); // Даем время на запись файла
                
                // Проверяем целостность файла
                int integrityCheckCount = 0;
                const int maxIntegrityChecks = 50; // 5 секунд
                bool fileIsValid = false;
                
                while (integrityCheckCount < maxIntegrityChecks && !fileIsValid)
                {
                    if (File.Exists(newLocalPath))
                    {
                        try
                        {
                            var fileInfo = new FileInfo(newLocalPath);
                            if (fileInfo.Length > 0)
                            {
                                // Файл существует и не пустой
                                fileIsValid = true;
                                Debug.Log($"{LogPrefix} [Автовосстановление] Файл проверен: {newLocalPath}, размер={fileInfo.Length} байт");
                            }
                            else
                            {
                                Debug.LogWarning($"{LogPrefix} [Автовосстановление] Файл пуст, ожидаем... ({integrityCheckCount}/{maxIntegrityChecks})");
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"{LogPrefix} [Автовосстановление] Ошибка проверки файла: {e.Message}");
                        }
                    }
                    
                    if (!fileIsValid)
                    {
                        yield return new WaitForSeconds(0.1f);
                        integrityCheckCount++;
                    }
                }
                
                if (!fileIsValid)
                {
                    Debug.LogError($"{LogPrefix} [Автовосстановление] Файл не прошел проверку целостности: {newLocalPath}");
                    downloadSuccess = false;
                    downloadError = "Файл не прошел проверку целостности после загрузки";
                }
            }
            
            if (!downloadSuccess)
            {
                if (attempt < maxAutoRecoveryAttempts)
                {
                    yield return StartCoroutine(AutoRecoverVideo(operation, downloadError ?? "Неизвестная ошибка"));
                }
                else
                {
                    Debug.LogError($"{LogPrefix} [Автовосстановление] Не удалось восстановить видео после {maxAutoRecoveryAttempts} попыток");
                    activePlacements.Remove(operation.ArtifactId);
                }
                yield break;
            }
            
            operation.LocalPath = newLocalPath;
            operation.IsLoading = false;
            
            // Проверяем, не была ли операция отменена
            if (operation.IsCancelled || operation.TargetHost == null)
            {
                Debug.LogWarning($"{LogPrefix} [Автовосстановление] Операция отменена или хост уничтожен для {operation.ArtifactId}");
                activePlacements.Remove(operation.ArtifactId);
                yield break;
            }
            
            RequestVideoForHost(
                operation.ArtifactId,
                operation.TargetHost,
                newLocalPath,
                null,
                false,
                () => { },
                error => 
                {
                    Debug.LogError($"{LogPrefix} [Автовосстановление] Ошибка размещения восстановленного видео: {error}");
                    activePlacements.Remove(operation.ArtifactId);
                },
                operation.RemoteUrl,
                operation.MediaId,
                operation.MetadataJson);
        }

        /// <summary>
        /// Отменяет операцию размещения и правильно очищает ресурсы
        /// </summary>
        private void CancelPlacementOperation(string artifactId)
        {
            if (activePlacements.TryGetValue(artifactId, out var operation))
            {
                operation.IsCancelled = true;
                
                // Освободить файл из экземпляра
                if (operation.VideoInstance != null)
                {
                    var arVideoPlayer = operation.VideoInstance.GetComponent<ARVideoPlayer>();
                    if (arVideoPlayer != null)
                    {
                        StartCoroutine(arVideoPlayer.ForceReleaseFileCoroutine());
                    }
                    DestroyImmediate(operation.VideoInstance);
                }
                
                activePlacements.Remove(artifactId);
            }
        }
        
        public void RemoveVideoFromHost(string artifactId, TrackedVideoHost host)
        {
            if (string.IsNullOrEmpty(artifactId) || host == null)
            {
                return;
            }

            if (sceneVideos.TryGetValue(artifactId, out var instance))
            {
                if (instance.Host == host)
                {
                    host.ClearLoadedVideo();
                    
                    if (instance.VideoInstance != null)
                    {
                        Destroy(instance.VideoInstance);
                    }
                    
                    sceneVideos.Remove(artifactId);
                }
            }
        }

        /// <summary>
        /// Обновляет состояние видео при изменении состояния трекинга хоста
        /// </summary>
        public void UpdateVideoTrackingState(string artifactId, bool isTracking)
        {
            if (sceneVideos.TryGetValue(artifactId, out var instance))
            {
                if (instance.Host != null)
                {
                    instance.Host.SetTrackingActive(isTracking);
                }
            }
        }
    }
}

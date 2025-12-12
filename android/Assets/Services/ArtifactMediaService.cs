using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using ARArtifact.Storage;

namespace ARArtifact.Services
{
    /// <summary>
    /// Сервис для загрузки и кеширования медиафайлов артефактов (glb, превью и т.д.).
    /// Следит за параллельными запросами и не допускает повторных загрузок одного и того же URL.
    /// </summary>
    public class ArtifactMediaService : MonoBehaviour
    {
        private const string LogPrefix = "[ArtifactMediaService]";

        private class DownloadOperation
        {
            public string url;
            public string localPath;
            public string artifactId; // Для отслеживания прогресса
            public readonly List<Action<string>> onSuccess = new();
            public readonly List<Action<string>> onError = new();
            public Coroutine coroutine;
            public float Progress; // Прогресс загрузки (0-1)
        }

        private static ArtifactMediaService _instance;
        public static ArtifactMediaService Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("ArtifactMediaService");
                    _instance = go.AddComponent<ArtifactMediaService>();
                    DontDestroyOnLoad(go);
                }

                return _instance;
            }
        }

        private readonly Dictionary<string, DownloadOperation> activeDownloads = new(); // Ключ: URL
        private readonly Dictionary<string, DownloadOperation> activeDownloadsByArtifactId = new(); // Ключ: artifactId
        private readonly Queue<DownloadOperation> pendingDownloads = new(); // Очередь ожидания загрузок
        private ArtifactStorage storage;

        // Настройки управления памятью
        [Header("Download Settings")]
        [SerializeField] private int maxParallelDownloads = 3; // Максимум параллельных загрузок

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            storage = new ArtifactStorage();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>
        /// Скачивает GLB/3D медиа и сохраняет локально.
        /// </summary>
        public void DownloadModel(string artifactId, string mediaId, string remoteUrl, Action<string> onSuccess, Action<string> onError)
        {
            if (string.IsNullOrEmpty(mediaId))
            {
                onError?.Invoke("MediaId не задан");
                return;
            }

            string localPath = storage.GetMediaFilePath(artifactId, mediaId, remoteUrl);
            EnqueueDownload(remoteUrl, localPath, artifactId, onSuccess, onError);
        }

    /// <summary>
    /// Скачивает превью изображение артефакта.
    /// </summary>
    public void DownloadPreview(string artifactId, string remoteUrl, Action<string> onSuccess, Action<string> onError)
    {
        if (string.IsNullOrEmpty(remoteUrl))
        {
            onError?.Invoke("URL превью не задан");
            return;
        }

        string localPath = storage.GetPreviewFilePath(artifactId, remoteUrl);
        EnqueueDownload(remoteUrl, localPath, null, onSuccess, onError); // Превью не отслеживаем по artifactId
    }

    /// <summary>
    /// Скачивает видео и сохраняет локально.
    /// </summary>
    public void DownloadVideo(string artifactId, string mediaId, string remoteUrl, Action<string> onSuccess, Action<string> onError)
    {
        if (string.IsNullOrEmpty(mediaId))
        {
            onError?.Invoke("MediaId не задан");
            return;
        }

        string localPath = storage.GetMediaFilePath(artifactId, mediaId, remoteUrl);
        EnqueueDownload(remoteUrl, localPath, artifactId, onSuccess, onError); // Отслеживаем видео по artifactId для автовосстановления
    }

        /// <summary>
        /// Отменяет все активные загрузки (используется при очистке кеша).
        /// </summary>
        public void CancelAllDownloads()
        {
            foreach (var operation in activeDownloads.Values)
            {
                if (operation.coroutine != null)
                {
                    StopCoroutine(operation.coroutine);
                }

                if (File.Exists(operation.localPath))
                {
                    File.Delete(operation.localPath);
                }
            }

            activeDownloads.Clear();
            activeDownloadsByArtifactId.Clear();
            
            while (pendingDownloads.Count > 0)
            {
                var operation = pendingDownloads.Dequeue();
                NotifyError(operation, "Загрузка отменена");
            }
        }

        private void EnqueueDownload(string remoteUrl, string localPath, string artifactId, Action<string> onSuccess, Action<string> onError)
        {
            if (string.IsNullOrEmpty(remoteUrl))
            {
                onError?.Invoke("URL пуст");
                return;
            }

            try
            {
                string directory = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogPrefix} Не удалось подготовить папку для загрузки {localPath}: {e.Message}");
                onError?.Invoke($"Ошибка файловой системы: {e.Message}");
                return;
            }

            if (File.Exists(localPath))
            {
                onSuccess?.Invoke(localPath);
                return;
            }

            if (activeDownloads.TryGetValue(remoteUrl, out var existingOperation))
            {
                existingOperation.onSuccess.Add(onSuccess);
                existingOperation.onError.Add(onError);
                return;
            }

            var operation = new DownloadOperation
            {
                url = remoteUrl,
                localPath = localPath,
                artifactId = artifactId,
                Progress = 0f
            };
            operation.onSuccess.Add(onSuccess);
            operation.onError.Add(onError);
            
            if (activeDownloads.Count >= maxParallelDownloads)
            {
                pendingDownloads.Enqueue(operation);
                return;
            }

            StartDownload(operation);
        }

        private IEnumerator DownloadCoroutine(DownloadOperation operation)
        {

            bool completed = false;
            string error = null;

            using (UnityWebRequest request = UnityWebRequest.Get(operation.url))
            {
                request.downloadHandler = new DownloadHandlerFile(operation.localPath);
                var sendRequest = request.SendWebRequest();

                // Ожидаем завершения без таймаута, обновляя прогресс
                while (!sendRequest.isDone)
                {
                    // Обновляем прогресс загрузки
                    if (request.downloadedBytes > 0)
                    {
                        long? contentLength = null;
                        string contentLengthHeader = request.GetResponseHeader("Content-Length");
                        if (!string.IsNullOrEmpty(contentLengthHeader) && long.TryParse(contentLengthHeader, out long length))
                        {
                            contentLength = length;
                        }
                        
                        if (contentLength.HasValue && contentLength.Value > 0)
                        {
                            operation.Progress = Mathf.Clamp01((float)request.downloadedBytes / contentLength.Value);
                        }
                        else
                        {
                            // Если размер неизвестен, используем приблизительный прогресс на основе времени
                            // (но это не очень точно, лучше использовать размер файла на диске)
                            if (File.Exists(operation.localPath))
                            {
                                try
                                {
                                    var fileInfo = new FileInfo(operation.localPath);
                                    // Предполагаем, что файл будет примерно такого же размера, как уже скачано
                                    // Это приблизительная оценка
                                    operation.Progress = Mathf.Clamp01(0.1f + (fileInfo.Length / 1000000f) * 0.8f); // 0.1-0.9
                                }
                                catch
                                {
                                    // Игнорируем ошибки доступа к файлу
                                }
                            }
                        }
                    }
                    yield return null;
                }

                if (!completed)
                {
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        error = $"HTTP {request.responseCode}: {request.error}";
                        Debug.LogError($"{LogPrefix} Ошибка загрузки {operation.url}: {error}");
                        if (File.Exists(operation.localPath))
                        {
                            File.Delete(operation.localPath);
                        }
                        NotifyError(operation, error);
                    }
                    else
                    {
                        // Убеждаемся, что файл полностью записан на диск
                        yield return StartCoroutine(WaitForFileComplete(operation.localPath));
                        
                        // Проверяем, что файл существует и имеет корректный размер
                        if (File.Exists(operation.localPath))
                        {
                            var fileInfo = new FileInfo(operation.localPath);
                            if (fileInfo.Length == 0)
                            {
                                error = "Файл пуст после загрузки";
                                Debug.LogError($"{LogPrefix} {error}: {operation.localPath}");
                                File.Delete(operation.localPath);
                                NotifyError(operation, error);
                            }
                            else
                            {
                                // Проверяем Content-Length из заголовков, если доступен
                                long expectedSize = request.GetResponseHeader("Content-Length") != null 
                                    ? long.Parse(request.GetResponseHeader("Content-Length")) 
                                    : 0;
                                
                                if (expectedSize > 0 && fileInfo.Length != expectedSize)
                                {
                                    error = $"Размер файла не соответствует ожидаемому: ожидалось {expectedSize} байт, получено {fileInfo.Length} байт";
                                    Debug.LogError($"{LogPrefix} {error}: {operation.localPath}");
                                    File.Delete(operation.localPath);
                                    NotifyError(operation, error);
                                }
                                else
                                {
                                    // Устанавливаем прогресс на 100% перед уведомлением об успехе
                                    operation.Progress = 1.0f;
                                    NotifySuccess(operation);
                                }
                            }
                        }
                        else
                        {
                            error = "Файл не найден после загрузки";
                            Debug.LogError($"{LogPrefix} {error}: {operation.localPath}");
                            NotifyError(operation, error);
                        }
                    }
                }
            }

            activeDownloads.Remove(operation.url);
            if (!string.IsNullOrEmpty(operation.artifactId))
            {
                activeDownloadsByArtifactId.Remove(operation.artifactId);
            }
            
            operation.onSuccess.Clear();
            operation.onError.Clear();
            
            ProcessQueue();
        }
        
        private void StartDownload(DownloadOperation operation)
        {
            activeDownloads[operation.url] = operation;
            if (!string.IsNullOrEmpty(operation.artifactId))
            {
                activeDownloadsByArtifactId[operation.artifactId] = operation;
            }
            operation.coroutine = StartCoroutine(DownloadCoroutine(operation));
        }
        
        private void ProcessQueue()
        {
            while (activeDownloads.Count < maxParallelDownloads && pendingDownloads.Count > 0)
            {
                var nextOperation = pendingDownloads.Dequeue();
                
                if (activeDownloads.ContainsKey(nextOperation.url))
                {
                    var existingOperation = activeDownloads[nextOperation.url];
                    existingOperation.onSuccess.AddRange(nextOperation.onSuccess);
                    existingOperation.onError.AddRange(nextOperation.onError);
                }
                else
                {
                    StartDownload(nextOperation);
                }
            }
        }

        private void NotifySuccess(DownloadOperation operation)
        {
            foreach (var callback in operation.onSuccess)
            {
                try
                {
                    callback?.Invoke(operation.localPath);
                }
                catch (Exception e)
                {
                    Debug.LogError($"{LogPrefix} Ошибка в обработчике успеха: {e.Message}");
                }
            }
        }

        private void NotifyError(DownloadOperation operation, string error)
        {
            foreach (var callback in operation.onError)
            {
                try
                {
                    callback?.Invoke(error);
                }
                catch (Exception e)
                {
                    Debug.LogError($"{LogPrefix} Ошибка в обработчике ошибки: {e.Message}");
                }
            }
        }
        
        private IEnumerator WaitForFileComplete(string filePath, float maxWaitTime = 5f, float checkInterval = 0.1f)
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
                    long currentSize = fileInfo.Length;
                    
                    if (currentSize == lastSize)
                    {
                        stableCount++;
                        if (stableCount >= requiredStableChecks)
                        {
                            yield break;
                        }
                    }
                    else
                    {
                        stableCount = 0;
                        lastSize = currentSize;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{LogPrefix} Ошибка проверки размера файла: {e.Message}");
                }
                
                yield return new WaitForSeconds(checkInterval);
            }
            
            Debug.LogWarning($"{LogPrefix} Таймаут ожидания завершения записи файла: {filePath}");
        }
        
        public (int active, int pending) GetDownloadStats()
        {
            return (activeDownloads.Count, pendingDownloads.Count);
        }
        
        /// <summary>
        /// Проверяет, загружается ли файл для указанного артефакта
        /// </summary>
        public bool IsDownloading(string artifactId)
        {
            return !string.IsNullOrEmpty(artifactId) && activeDownloadsByArtifactId.ContainsKey(artifactId);
        }
        
        /// <summary>
        /// Получает прогресс загрузки файла для указанного артефакта (0-1)
        /// </summary>
        public float GetDownloadProgress(string artifactId)
        {
            if (string.IsNullOrEmpty(artifactId))
            {
                return 0f;
            }
            
            if (activeDownloadsByArtifactId.TryGetValue(artifactId, out var operation))
            {
                return operation.Progress;
            }
            
            return 0f;
        }
    }
}


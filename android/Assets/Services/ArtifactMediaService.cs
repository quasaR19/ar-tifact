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
            public readonly List<Action<string>> onSuccess = new();
            public readonly List<Action<string>> onError = new();
            public Coroutine coroutine;
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
                    Debug.Log($"{LogPrefix} Создан Singleton экземпляр");
                }

                return _instance;
            }
        }

        private readonly Dictionary<string, DownloadOperation> activeDownloads = new();
        private ArtifactStorage storage;

        // Настройки управления памятью
        [Header("Download Settings")]
        [SerializeField] private float downloadTimeoutSeconds = 60f; // Таймаут загрузки
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
            EnqueueDownload(remoteUrl, localPath, onSuccess, onError);
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
        EnqueueDownload(remoteUrl, localPath, onSuccess, onError);
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
        EnqueueDownload(remoteUrl, localPath, onSuccess, onError);
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
            Debug.Log($"{LogPrefix} Все активные загрузки отменены");
        }

        private void EnqueueDownload(string remoteUrl, string localPath, Action<string> onSuccess, Action<string> onError)
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
                Debug.Log($"{LogPrefix} Файл уже сохранен, используем кеш: {localPath}");
                onSuccess?.Invoke(localPath);
                return;
            }

            if (activeDownloads.TryGetValue(remoteUrl, out var existingOperation))
            {
                existingOperation.onSuccess.Add(onSuccess);
                existingOperation.onError.Add(onError);
                Debug.Log($"{LogPrefix} Повторный запрос на {remoteUrl} добавлен в очередь ожидания");
                return;
            }

            // Проверяем ограничение параллельных загрузок
            if (activeDownloads.Count >= maxParallelDownloads)
            {
                Debug.LogWarning($"{LogPrefix} Достигнут лимит параллельных загрузок ({maxParallelDownloads}), ожидание...");
                // В реальном приложении можно добавить очередь ожидания
                // Для простоты просто отказываем в загрузке
                onError?.Invoke("Достигнут лимит параллельных загрузок, попробуйте позже");
                return;
            }

            var operation = new DownloadOperation
            {
                url = remoteUrl,
                localPath = localPath
            };
            operation.onSuccess.Add(onSuccess);
            operation.onError.Add(onError);
            activeDownloads[remoteUrl] = operation;
            operation.coroutine = StartCoroutine(DownloadCoroutine(operation));
        }

        private IEnumerator DownloadCoroutine(DownloadOperation operation)
        {
            Debug.Log($"{LogPrefix} Начата загрузка {operation.url} -> {operation.localPath}");

            float startTime = Time.time;
            bool completed = false;
            string error = null;

            using (UnityWebRequest request = UnityWebRequest.Get(operation.url))
            {
                request.downloadHandler = new DownloadHandlerFile(operation.localPath);
                var sendRequest = request.SendWebRequest();

                // Ожидаем завершения с таймаутом
                while (!sendRequest.isDone)
                {
                    if (Time.time - startTime > downloadTimeoutSeconds)
                    {
                        request.Abort();
                        error = $"Таймаут загрузки ({downloadTimeoutSeconds}с)";
                        Debug.LogError($"{LogPrefix} {error}: {operation.url}");
                        completed = true;
                        break;
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
                                    Debug.Log($"{LogPrefix} Файл загружен: {operation.localPath}, размер: {fileInfo.Length} байт");
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
                else
                {
                    // Обработка таймаута
                    if (File.Exists(operation.localPath))
                    {
                        File.Delete(operation.localPath);
                    }
                    NotifyError(operation, error);
                }
            }

            activeDownloads.Remove(operation.url);
            
            // Очищаем колбэки для предотвращения утечек памяти
            operation.onSuccess.Clear();
            operation.onError.Clear();
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
        
        /// <summary>
        /// Ожидает завершения записи файла на диск
        /// </summary>
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
                            // Размер файла стабилен, запись завершена
                            Debug.Log($"{LogPrefix} Файл стабилизирован: {filePath}, размер: {currentSize} байт");
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
    }
}


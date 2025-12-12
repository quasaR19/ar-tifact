using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using ARArtifact.Config;
using ARArtifact.Storage;

namespace ARArtifact.Services
{
    /// <summary>
    /// Центральный сервис для работы с артефактами, историей и запросами в Supabase.
    /// </summary>
    public class ArtifactService : MonoBehaviour
    {
        private const string LogPrefix = "[ArtifactService]";
        private const string MediaType3DModel = "3d_model";
        private const string MediaTypeVideo = "video";
        private const string MediaTypeYouTube = "youtube";

        private static ArtifactService _instance;
        public static ArtifactService Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("ArtifactService");
                    _instance = go.AddComponent<ArtifactService>();
                    DontDestroyOnLoad(go);
                    Debug.Log($"{LogPrefix} Создан Singleton экземпляр");
                }

                return _instance;
            }
        }

        public event Action OnHistoryLoading;
        public event Action OnHistoryLoadingCompleted;
        public event Action<IReadOnlyList<ArtifactHistoryItem>> OnHistoryChanged;

        private SupabaseConfig config;
        private ArtifactStorage storage;
        private ArtifactStorage.ArtifactStorageData storageData;
        private readonly List<ArtifactHistoryItem> historyCache = new();
        private IReadOnlyList<ArtifactHistoryItem> historyReadonly;
        private ArtifactMediaService mediaService;
        private readonly Dictionary<string, ArtifactRequestOperation> activeTargetRequests = new();

        // Оптимизация сохранения истории - батчинг
        private Coroutine saveHistoryCoroutine;
        private bool historyDirty = false;
        private const float HistorySaveDelay = 2f; // Сохраняем историю через 2 секунды после последнего изменения

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            historyReadonly = historyCache.AsReadOnly();

            storage = new ArtifactStorage();
            mediaService = ArtifactMediaService.Instance;
            LoadConfig();
            LoadFromDisk();
        }

        private void OnDestroy()
        {
            // Принудительно сохраняем историю при уничтожении
            ForceSaveHistory();
            
            if (_instance == this)
            {
                _instance = null;
            }
        }
        
        private void OnApplicationPause(bool pauseStatus)
        {
            // Сохраняем историю при паузе приложения
            if (pauseStatus)
            {
                ForceSaveHistory();
            }
        }
        
        private void OnApplicationFocus(bool hasFocus)
        {
            // Сохраняем историю при потере фокуса
            if (!hasFocus)
            {
                ForceSaveHistory();
            }
        }

        /// <summary>
        /// Возвращает историю (read-only).
        /// </summary>
        public IReadOnlyList<ArtifactHistoryItem> GetHistoryItems()
        {
            return historyReadonly ?? historyCache.AsReadOnly();
        }

        /// <summary>
        /// Возвращает артефакт для конкретного targetId (с кешированием и загрузкой недостающих медиа).
        /// </summary>
        public void RequestArtifactForTarget(string targetId, Action<ArtifactAvailabilityResult> onSuccess, Action<string> onError)
        {
            if (string.IsNullOrEmpty(targetId))
            {
                onError?.Invoke("TargetId пуст");
                return;
            }

            if (TryGetCachedArtifact(targetId, out var cachedResult))
            {
                // Не обновляем историю при каждом запросе из кеша - только при первом распознавании
                onSuccess?.Invoke(cachedResult);
                return;
            }

            if (activeTargetRequests.TryGetValue(targetId, out var existingOperation))
            {
                existingOperation.successCallbacks.Add(onSuccess);
                existingOperation.errorCallbacks.Add(onError);
                // Логирование удалено для оптимизации производительности
                return;
            }

            var operation = new ArtifactRequestOperation
            {
                targetId = targetId
            };
            operation.successCallbacks.Add(onSuccess);
            operation.errorCallbacks.Add(onError);
            activeTargetRequests[targetId] = operation;

            StartCoroutine(ResolveArtifactForTargetCoroutine(operation));
        }

        private bool TryGetCachedArtifact(string targetId, out ArtifactAvailabilityResult result)
        {
            result = null;
            var record = FindRecordByTarget(targetId);
            if (record == null)
            {
                return false;
            }

            var primaryMedia = GetPrimaryMedia(record);
            if (primaryMedia == null)
            {
                return false;
            }
            
            bool isVideo = IsVideoMediaType(primaryMedia.mediaType);
            
            // Для видео проверяем наличие локального файла (если не YouTube) или URL
            if (isVideo)
            {
                if (string.Equals(primaryMedia.mediaType, MediaTypeYouTube, StringComparison.OrdinalIgnoreCase))
                {
                    // YouTube - достаточно наличия URL
                    if (string.IsNullOrEmpty(primaryMedia.remoteUrl))
                    {
                        return false;
                    }
                }
                else
                {
                    // Blob видео - нужен локальный файл
                    if (string.IsNullOrEmpty(primaryMedia.localPath) || !File.Exists(primaryMedia.localPath))
                    {
                        return false;
                    }
                }
            }
            else
            {
                // 3D модель - нужен локальный файл
                if (string.IsNullOrEmpty(primaryMedia.localPath) || !File.Exists(primaryMedia.localPath))
                {
                    return false;
                }
            }

            if (!string.IsNullOrEmpty(record.previewLocalPath) && !File.Exists(record.previewLocalPath))
            {
                record.previewLocalPath = null;
            }

            result = BuildAvailabilityResult(record, primaryMedia, isFromCache: true);
            return true;
        }

        /// <summary>
        /// Обновляет путь к локальному превью для записи артефакта.
        /// </summary>
        public void UpdateArtifactPreviewPath(string artifactId, string targetId, string previewLocalPath)
        {
            if (storageData?.artifacts == null)
            {
                return;
            }

            var record = storageData.artifacts.FirstOrDefault(a =>
                (string.IsNullOrEmpty(artifactId) || string.Equals(a.artifactId, artifactId, StringComparison.Ordinal)) &&
                (string.IsNullOrEmpty(targetId) || string.Equals(a.targetId, targetId, StringComparison.Ordinal)));

            if (record != null)
            {
                record.previewLocalPath = previewLocalPath;
                SaveToDisk();
                RebuildHistoryCache(true);
                Debug.Log($"{LogPrefix} Обновлен путь превью для артефакта {artifactId ?? targetId}: {previewLocalPath}");
            }
            else
            {
                Debug.LogWarning($"{LogPrefix} Запись артефакта не найдена для обновления превью: artifactId={artifactId}, targetId={targetId}");
            }
        }

        /// <summary>
        /// Добавляет/обновляет запись об артефакте в локальном кеше.
        /// </summary>
        public void UpsertArtifactRecord(ArtifactStorage.ArtifactRecord record, bool fireChangedEvent = true)
        {
            if (record == null)
            {
                Debug.LogWarning($"{LogPrefix} Попытка сохранить пустую запись артефакта");
                return;
            }

            if (storageData == null)
            {
                storageData = new ArtifactStorage.ArtifactStorageData();
            }

            var existing = storageData.artifacts.FirstOrDefault(a =>
                string.Equals(a.artifactId, record.artifactId, StringComparison.Ordinal) &&
                string.Equals(a.targetId, record.targetId, StringComparison.Ordinal));
            if (existing == null)
            {
                storageData.artifacts.Add(record);
                Debug.Log($"{LogPrefix} Добавлена запись артефакта {record.artifactId}");
            }
            else
            {
                int index = storageData.artifacts.IndexOf(existing);
                storageData.artifacts[index] = record;
                Debug.Log($"{LogPrefix} Обновлена запись артефакта {record.artifactId}");
            }

            SaveToDisk();
            if (fireChangedEvent)
            {
                RebuildHistoryCache(true);
            }
        }

        /// <summary>
        /// Добавляет или обновляет запись в истории сканирования.
        /// Если запись с таким targetId уже существует, обновляет её дату скана и статус.
        /// </summary>
        public void AppendHistoryEntry(string artifactId, string targetId, ArtifactHistoryStatus status, string statusDetails)
        {
            if (storageData == null)
            {
                storageData = new ArtifactStorage.ArtifactStorageData();
            }

            if (string.IsNullOrEmpty(targetId))
            {
                Debug.LogWarning($"{LogPrefix} Попытка добавить запись истории с пустым targetId");
                return;
            }

            // Ищем существующую запись по targetId
            var existingEntry = storageData.history.FirstOrDefault(h => 
                string.Equals(h.targetId, targetId, StringComparison.Ordinal));

            if (existingEntry != null)
            {
                // Обновляем существующую запись
                existingEntry.scannedAtTicks = DateTime.UtcNow.Ticks;
                existingEntry.status = status.ToString();
                existingEntry.statusDetails = statusDetails;
                
                // Обновляем artifactId, если он был null или если новый не null
                if (!string.IsNullOrEmpty(artifactId))
                {
                    existingEntry.artifactId = artifactId;
                }

                // Логирование удалено для оптимизации производительности
            }
            else
            {
                // Создаем новую запись
                var entry = new ArtifactStorage.ArtifactHistoryEntry
                {
                    artifactId = artifactId,
                    targetId = targetId,
                    scannedAtTicks = DateTime.UtcNow.Ticks,
                    status = status.ToString(),
                    statusDetails = statusDetails
                };

                storageData.history.Add(entry);
                // Логирование удалено для оптимизации производительности
            }

            // ограничим историю разумным числом
            if (storageData.history.Count > 1000)
            {
                storageData.history = storageData.history
                    .OrderByDescending(h => h.scannedAtTicks)
                    .Take(1000)
                    .ToList();
            }

            // Отложенное сохранение для оптимизации производительности
            ScheduleHistorySave();
            RebuildHistoryCache(true);
        }

        /// <summary>
        /// Полностью очищает историю и кеш.
        /// </summary>
        public void ClearHistoryAndCache()
        {
            Debug.Log($"{LogPrefix} Очистка истории и кеша");
            storageData = new ArtifactStorage.ArtifactStorageData();
            ArtifactMediaService.Instance?.CancelAllDownloads();
            storage.ClearAllData();
            SaveToDisk();
            RebuildHistoryCache(true);
        }

        /// <summary>
        /// Удаляет запись истории и связанные данные для конкретного targetId.
        /// </summary>
        public void DeleteHistoryItem(string targetId)
        {
            if (string.IsNullOrEmpty(targetId))
            {
                Debug.LogWarning($"{LogPrefix} Попытка удалить запись с пустым targetId");
                return;
            }

            if (storageData == null)
            {
                Debug.LogWarning($"{LogPrefix} Попытка удалить запись при отсутствии данных");
                return;
            }

            Debug.Log($"{LogPrefix} Удаление записи истории для targetId={targetId}");

            // Удаляем запись из истории
            var historyEntriesToRemove = storageData.history
                .Where(h => string.Equals(h.targetId, targetId, StringComparison.Ordinal))
                .ToList();
            
            foreach (var entry in historyEntriesToRemove)
            {
                storageData.history.Remove(entry);
            }

            // Находим и удаляем связанные записи артефактов
            var artifactRecordsToRemove = storageData.artifacts
                .Where(a => string.Equals(a.targetId, targetId, StringComparison.Ordinal))
                .ToList();

            foreach (var record in artifactRecordsToRemove)
            {
                // Удаляем превью файл
                if (!string.IsNullOrEmpty(record.previewLocalPath))
                {
                    storage.DeleteFileIfExists(record.previewLocalPath);
                }

                // Удаляем медиа файлы
                if (record.media != null)
                {
                    foreach (var media in record.media)
                    {
                        if (!string.IsNullOrEmpty(media.localPath))
                        {
                            storage.DeleteFileIfExists(media.localPath);
                        }
                    }
                }

                storageData.artifacts.Remove(record);
            }

            // Отменяем активные загрузки для этого targetId
            if (activeTargetRequests.ContainsKey(targetId))
            {
                activeTargetRequests.Remove(targetId);
                Debug.Log($"{LogPrefix} Отменена активная загрузка для targetId={targetId}");
            }

            // Сохраняем изменения
            SaveToDisk();
            RebuildHistoryCache(true);

            Debug.Log($"{LogPrefix} Запись истории удалена для targetId={targetId}");
        }

        /// <summary>
        /// Выполняет REST запрос к Supabase и возвращает артефакты, привязанные к targetId.
        /// </summary>
        public void FetchArtifactBundlesForTarget(string targetId, Action<List<ArtifactRemoteEntry>> onSuccess, Action<string> onError, bool notifyHistoryListeners = false)
        {
            if (string.IsNullOrEmpty(targetId))
            {
                onError?.Invoke("TargetId пуст");
                return;
            }

            if (config == null || !config.IsValid())
            {
                onError?.Invoke("SupabaseConfig не настроен");
                return;
            }

            StartCoroutine(FetchArtifactBundlesCoroutine(targetId, onSuccess, onError, notifyHistoryListeners));
        }

        private IEnumerator FetchArtifactBundlesCoroutine(string targetId, Action<List<ArtifactRemoteEntry>> onSuccess, Action<string> onError, bool notifyHistoryListeners)
        {
            if (notifyHistoryListeners)
            {
                OnHistoryLoading?.Invoke();
            }

            string escapedTargetId = Uri.EscapeDataString(targetId);
            // Запрашиваем таргет с привязанным артефактом и его медиа
            string selectClause = Uri.EscapeDataString("id,artifact_id,artifacts(*,artifact_media(media(*)))");
            
            string url = $"{config.supabaseUrl}/rest/v1/targets?select={selectClause}&id=eq.{escapedTargetId}";

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("apikey", config.supabaseAnonKey);
                request.SetRequestHeader("Authorization", $"Bearer {config.supabaseAnonKey}");
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Prefer", "return=representation");

                Debug.Log($"{LogPrefix} Запрос артефакта по target_id={targetId}");
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = $"HTTP {request.responseCode}: {request.error}";
                    Debug.LogError($"{LogPrefix} Ошибка загрузки артефакта: {error}");
                    onError?.Invoke(error);
                    if (notifyHistoryListeners)
                    {
                        OnHistoryLoadingCompleted?.Invoke();
                    }
                    yield break;
                }

                try
                {
                    string jsonResponse = request.downloadHandler.text;
                    Debug.Log($"{LogPrefix} Получен JSON ответ от API (первые 500 символов): {jsonResponse.Substring(0, Math.Min(500, jsonResponse.Length))}");
                    string wrappedJson = "{\"items\":" + jsonResponse + "}";
                    var wrapper = JsonUtility.FromJson<TargetArtifactWrapper>(wrappedJson);
                    List<ArtifactRemoteEntry> entries = ConvertTargetToRemoteEntries(wrapper?.items);
                    Debug.Log($"{LogPrefix} Получено артефактов: {entries.Count}");
                    onSuccess?.Invoke(entries);
                }
                catch (Exception e)
                {
                    Debug.LogError($"{LogPrefix} Ошибка парсинга ответа: {e.Message}");
                    onError?.Invoke($"Ошибка парсинга: {e.Message}");
                }
            }

            if (notifyHistoryListeners)
            {
                OnHistoryLoadingCompleted?.Invoke();
            }
        }

        private IEnumerator ResolveArtifactForTargetCoroutine(ArtifactRequestOperation operation)
        {
            if (operation == null)
            {
                yield break;
            }

            AppendHistoryEntry(null, operation.targetId, ArtifactHistoryStatus.Loading, "Запрошены данные артефакта");

            bool fetchCompleted = false;
            List<ArtifactRemoteEntry> remoteEntries = null;
            string fetchError = null;

            FetchArtifactBundlesForTarget(
                operation.targetId,
                entries =>
                {
                    remoteEntries = entries;
                    fetchCompleted = true;
                },
                error =>
                {
                    fetchError = error;
                    fetchCompleted = true;
                });

            while (!fetchCompleted)
            {
                yield return null;
            }

            if (!string.IsNullOrEmpty(fetchError))
            {
                FailOperation(operation, fetchError);
                yield break;
            }

            if (remoteEntries == null || remoteEntries.Count == 0)
            {
                FailOperation(operation, "Артефакт для маркера не найден");
                yield break;
            }

            var selectedEntry = SelectPreferredEntry(remoteEntries);
            if (selectedEntry == null)
            {
                FailOperation(operation, "Для маркера нет медиа (видео или 3D модели)");
                yield break;
            }

            var record = ConvertToRecord(selectedEntry);
            var primaryMedia = GetPrimaryMedia(record);
            if (primaryMedia == null)
            {
                FailOperation(operation, "Не удалось определить медиа для артефакта");
                yield break;
            }

            yield return EnsurePreviewDownloaded(record);

            string downloadError = null;
            bool isVideoMedia = IsVideoMediaType(primaryMedia.mediaType);
            
            if (isVideoMedia)
            {
                yield return EnsureVideoDownloaded(record, primaryMedia, err => downloadError = err);
            }
            else
            {
                yield return EnsureModelDownloaded(record, primaryMedia, err => downloadError = err);
            }

            if (!string.IsNullOrEmpty(downloadError))
            {
                FailOperation(operation, $"Ошибка загрузки медиа: {downloadError}");
                yield break;
            }

            UpsertArtifactRecord(record);
            bool isVideo = IsVideoMediaType(primaryMedia.mediaType);
            AppendHistoryEntry(record.artifactId, operation.targetId, ArtifactHistoryStatus.Ready, 
                isVideo ? "Видео готово" : "Модель готова");

            var availability = BuildAvailabilityResult(record, primaryMedia);
            CompleteOperation(operation, availability);
        }

        private List<ArtifactRemoteEntry> ConvertTargetToRemoteEntries(List<TargetDto> dtos)
        {
            var result = new List<ArtifactRemoteEntry>();
            if (dtos == null)
            {
                return result;
            }

            foreach (var dto in dtos)
            {
                if (dto == null || dto.artifacts == null)
                {
                    continue;
                }

                var remoteArtifact = new ArtifactRemoteEntry
                {
                    ArtifactTargetId = dto.id, // В новой схеме используем ID таргета как ID связи
                    ArtifactId = dto.artifact_id,
                    TargetId = dto.id, // ID таргета это и есть ID записи в таблице targets
                    DisplayPriority = 0, // В новой схеме приоритета нет, считаем 0
                    Artifact = new ArtifactRemoteArtifact
                    {
                        ArtifactId = dto.artifacts.id,
                        Name = dto.artifacts.name,
                        Description = dto.artifacts.description,
                        PreviewImageUrl = dto.artifacts.preview_image_url,
                        IsActive = dto.artifacts.is_active,
                        Media = new List<ArtifactRemoteMedia>()
                    }
                };

                if (dto.artifacts.artifact_media != null)
                {
                    foreach (var mediaLink in dto.artifacts.artifact_media)
                    {
                        if (mediaLink == null || mediaLink.media == null)
                        {
                            continue;
                        }

                        // Получаем метаданные из media.metadata (jsonb поле)
                        // Unity JsonUtility десериализует jsonb объект в MetadataDto
                        string metadataJson = null;
                        if (mediaLink.media.metadata != null)
                        {
                            // Сериализуем обратно в строку для хранения
                            metadataJson = JsonUtility.ToJson(mediaLink.media.metadata);
                            
                            // Логируем метаданные в зависимости от типа медиа
                            if (mediaLink.media.media_type == MediaTypeVideo)
                            {
                                Debug.Log($"{LogPrefix} Загружены метаданные для VIDEO media {mediaLink.media.id}: '{metadataJson}' (width={mediaLink.media.metadata?.width}, height={mediaLink.media.metadata?.height}, duration={mediaLink.media.metadata?.duration})");
                            }
                            else if (mediaLink.media.media_type == MediaType3DModel)
                            {
                                Debug.Log($"{LogPrefix} Загружены метаданные для 3D MODEL media {mediaLink.media.id}: '{metadataJson}' (center_model={mediaLink.media.metadata?.center_model})");
                            }
                            else
                            {
                                Debug.Log($"{LogPrefix} Загружены метаданные для media {mediaLink.media.id} (type={mediaLink.media.media_type}): '{metadataJson}'");
                            }
                        }
                        else
                        {
                            Debug.Log($"{LogPrefix} Метаданные отсутствуют для media {mediaLink.media.id} (type={mediaLink.media.media_type})");
                        }

                        remoteArtifact.Artifact.Media.Add(new ArtifactRemoteMedia
                        {
                            MediaId = mediaLink.media.id,
                            MediaType = mediaLink.media.media_type,
                            Url = mediaLink.media.url,
                            MetadataJson = metadataJson
                        });
                    }
                }

                result.Add(remoteArtifact);
            }

            return result;
        }

        private ArtifactRemoteEntry SelectPreferredEntry(List<ArtifactRemoteEntry> entries)
        {
            // Сначала ищем записи с видео (приоритет видео над 3D моделями)
            var videoEntry = entries.FirstOrDefault(entry =>
                entry.Artifact != null &&
                entry.Artifact.Media != null &&
                entry.Artifact.Media.Any(media =>
                    IsVideoMediaType(media.MediaType)));
            
            if (videoEntry != null)
            {
                return videoEntry;
            }
            
            // Если видео нет, ищем 3D модель
            return entries.FirstOrDefault(entry =>
                entry.Artifact != null &&
                entry.Artifact.Media != null &&
                entry.Artifact.Media.Any(media =>
                    string.Equals(media.MediaType, MediaType3DModel, StringComparison.OrdinalIgnoreCase)));
        }
        
        private bool IsVideoMediaType(string mediaType)
        {
            if (string.IsNullOrEmpty(mediaType))
            {
                return false;
            }
            
            string lowerType = mediaType.ToLowerInvariant();
            return lowerType == MediaTypeVideo ||
                   lowerType == MediaTypeYouTube ||
                   lowerType == "mp4" ||
                   lowerType == "webm";
        }

        private ArtifactStorage.ArtifactRecord ConvertToRecord(ArtifactRemoteEntry entry)
        {
            var record = new ArtifactStorage.ArtifactRecord
            {
                artifactId = entry.ArtifactId,
                targetId = entry.TargetId,
                name = entry.Artifact?.Name,
                description = entry.Artifact?.Description,
                previewImageUrl = entry.Artifact?.PreviewImageUrl,
                previewLocalPath = null,
                isActive = entry.Artifact?.IsActive ?? true,
                lastUpdatedTicks = DateTime.UtcNow.Ticks
            };

            if (entry.Artifact?.Media != null)
            {
                foreach (var media in entry.Artifact.Media)
                {
                    Debug.Log($"{LogPrefix} [ConvertToRecord] Медиа {media.MediaId}: metadataJson='{media.MetadataJson}'");
                    record.media.Add(new ArtifactStorage.MediaCacheRecord
                    {
                        mediaId = media.MediaId,
                        mediaType = media.MediaType,
                        remoteUrl = media.Url,
                        localPath = null,
                        cachedAtTicks = 0,
                        metadataJson = media.MetadataJson
                    });
                }
            }

            var existingRecord = FindRecordByTarget(entry.TargetId);
            if (existingRecord != null)
            {
                record.previewLocalPath = existingRecord.previewLocalPath;
                foreach (var media in record.media)
                {
                    var persistedMedia = existingRecord.media.FirstOrDefault(m => m.mediaId == media.mediaId);
                    if (persistedMedia != null)
                    {
                        media.localPath = persistedMedia.localPath;
                        media.cachedAtTicks = persistedMedia.cachedAtTicks;
                        // Сохраняем метаданные из существующей записи, если в новой записи их нет
                        // Но если в новой записи есть метаданные, они имеют приоритет
                        if (string.IsNullOrEmpty(media.metadataJson) && !string.IsNullOrEmpty(persistedMedia.metadataJson))
                        {
                            media.metadataJson = persistedMedia.metadataJson;
                        }
                    }
                }
            }

            return record;
        }

        private ArtifactStorage.MediaCacheRecord GetPrimaryModel(ArtifactStorage.ArtifactRecord record)
        {
            // Устаревший метод, используем GetPrimaryMedia
            return GetPrimaryMedia(record);
        }
        
        private ArtifactStorage.MediaCacheRecord GetPrimaryMedia(ArtifactStorage.ArtifactRecord record)
        {
            if (record?.media == null)
            {
                return null;
            }
            
            // Сначала ищем видео (приоритет видео над 3D моделями)
            var videoMedia = record.media.FirstOrDefault(media => IsVideoMediaType(media.mediaType));
            if (videoMedia != null)
            {
                return videoMedia;
            }
            
            // Если видео нет, ищем 3D модель
            return record.media.FirstOrDefault(media =>
                string.Equals(media.mediaType, MediaType3DModel, StringComparison.OrdinalIgnoreCase));
        }

        private IEnumerator EnsurePreviewDownloaded(ArtifactStorage.ArtifactRecord record)
        {
            if (mediaService == null)
            {
                yield break;
            }

            if (string.IsNullOrEmpty(record.previewImageUrl))
            {
                yield break;
            }

            if (!string.IsNullOrEmpty(record.previewLocalPath) && File.Exists(record.previewLocalPath))
            {
                yield break;
            }

            bool completed = false;
            string localPath = null;
            string error = null;

            mediaService.DownloadPreview(record.artifactId ?? record.targetId, record.previewImageUrl,
                path =>
                {
                    localPath = path;
                    completed = true;
                },
                err =>
                {
                    error = err;
                    completed = true;
                });

            while (!completed)
            {
                yield return null;
            }

            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogWarning($"{LogPrefix} Не удалось скачать превью: {error}");
            }
            else
            {
                record.previewLocalPath = localPath;
            }
        }

        private IEnumerator EnsureModelDownloaded(ArtifactStorage.ArtifactRecord record, ArtifactStorage.MediaCacheRecord media, Action<string> onError)
        {
            if (mediaService == null)
            {
                onError?.Invoke("Сервис загрузки медиа недоступен");
                yield break;
            }

            if (!string.IsNullOrEmpty(media.localPath) && File.Exists(media.localPath))
            {
                yield break;
            }

            bool completed = false;
            string localPath = null;
            string error = null;

            mediaService.DownloadModel(record.artifactId ?? record.targetId, media.mediaId, media.remoteUrl,
                path =>
                {
                    localPath = path;
                    completed = true;
                },
                err =>
                {
                    error = err;
                    completed = true;
                });

            while (!completed)
            {
                yield return null;
            }

            if (!string.IsNullOrEmpty(error))
            {
                onError?.Invoke(error);
                yield break;
            }

            media.localPath = localPath;
            media.cachedAtTicks = DateTime.UtcNow.Ticks;
        }
        
        private IEnumerator EnsureVideoDownloaded(ArtifactStorage.ArtifactRecord record, ArtifactStorage.MediaCacheRecord media, Action<string> onError)
        {
            if (mediaService == null)
            {
                onError?.Invoke("Сервис загрузки медиа недоступен");
                yield break;
            }

            // Для YouTube видео не нужно скачивать локально
            if (string.Equals(media.mediaType, MediaTypeYouTube, StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            if (!string.IsNullOrEmpty(media.localPath) && File.Exists(media.localPath))
            {
                yield break;
            }

            bool completed = false;
            string localPath = null;
            string error = null;

            mediaService.DownloadVideo(record.artifactId ?? record.targetId, media.mediaId, media.remoteUrl,
                path =>
                {
                    localPath = path;
                    completed = true;
                },
                err =>
                {
                    error = err;
                    completed = true;
                });

            while (!completed)
            {
                yield return null;
            }

            if (!string.IsNullOrEmpty(error))
            {
                onError?.Invoke(error);
                yield break;
            }

            media.localPath = localPath;
            media.cachedAtTicks = DateTime.UtcNow.Ticks;
        }

        private ArtifactAvailabilityResult BuildAvailabilityResult(ArtifactStorage.ArtifactRecord record, ArtifactStorage.MediaCacheRecord media, bool isFromCache = false)
        {
            bool isVideo = IsVideoMediaType(media.mediaType);
            
            return new ArtifactAvailabilityResult
            {
                ArtifactId = record.artifactId,
                TargetId = record.targetId,
                DisplayName = record.name,
                Description = record.description,
                PreviewLocalPath = !string.IsNullOrEmpty(record.previewLocalPath) && File.Exists(record.previewLocalPath)
                    ? record.previewLocalPath
                    : null,
                LocalModelPath = isVideo ? null : media.localPath,
                LocalVideoPath = isVideo ? media.localPath : null,
                VideoUrl = isVideo ? media.remoteUrl : null,
                IsVideo = isVideo,
                Record = record,
                IsFromCache = isFromCache
            };
        }

        private void CompleteOperation(ArtifactRequestOperation operation, ArtifactAvailabilityResult result)
        {
            if (operation == null)
            {
                return;
            }

            string targetId = operation.targetId;
            activeTargetRequests.Remove(targetId);
            Debug.Log($"{LogPrefix} Операция для targetId={targetId} завершена, удалена из activeTargetRequests");

            foreach (var callback in operation.successCallbacks)
            {
                try
                {
                    callback?.Invoke(result);
                }
                catch (Exception e)
                {
                    Debug.LogError($"{LogPrefix} Ошибка в обработчике успешной загрузки: {e.Message}");
                }
            }
        }

        private void FailOperation(ArtifactRequestOperation operation, string error)
        {
            if (operation == null)
            {
                return;
            }

            string targetId = operation.targetId;
            activeTargetRequests.Remove(targetId);
            Debug.Log($"{LogPrefix} Операция для targetId={targetId} завершена с ошибкой, удалена из activeTargetRequests");
            AppendHistoryEntry(null, targetId, ArtifactHistoryStatus.Error, error);

            foreach (var callback in operation.errorCallbacks)
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

        private ArtifactStorage.ArtifactRecord FindRecordByTarget(string targetId)
        {
            if (storageData?.artifacts == null)
            {
                return null;
            }

            return storageData.artifacts.LastOrDefault(a =>
                string.Equals(a.targetId, targetId, StringComparison.Ordinal));
        }

        private ArtifactStorage.ArtifactRecord FindRecordByArtifactId(string artifactId)
        {
            if (storageData?.artifacts == null || string.IsNullOrEmpty(artifactId))
            {
                return null;
            }

            return storageData.artifacts.LastOrDefault(a =>
                string.Equals(a.artifactId, artifactId, StringComparison.Ordinal));
        }

        private ArtifactStorage.ArtifactRecord FindRecordForHistoryEntry(ArtifactStorage.ArtifactHistoryEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            var recordByTarget = FindRecordByTarget(entry.targetId);
            if (recordByTarget != null)
            {
                return recordByTarget;
            }

            return FindRecordByArtifactId(entry.artifactId);
        }

        private void LoadConfig()
        {
            config = Resources.Load<SupabaseConfig>("SupabaseConfig");
            if (config == null)
            {
                Debug.LogError($"{LogPrefix} SupabaseConfig не найден в Resources");
            }
            else if (!config.IsValid())
            {
                Debug.LogWarning($"{LogPrefix} SupabaseConfig заполнен не полностью");
            }
            else
            {
                Debug.Log($"{LogPrefix} SupabaseConfig загружен");
            }
        }

        private void LoadFromDisk()
        {
            storageData = storage.LoadData();
            RebuildHistoryCache(false);
            OnHistoryChanged?.Invoke(historyReadonly);
        }

        private void SaveToDisk()
        {
            storage.SaveData(storageData ?? new ArtifactStorage.ArtifactStorageData());
        }
        
        /// <summary>
        /// Планирует отложенное сохранение истории для оптимизации производительности
        /// </summary>
        private void ScheduleHistorySave()
        {
            historyDirty = true;
            
            // Отменяем предыдущую корутину, если она есть
            if (saveHistoryCoroutine != null)
            {
                StopCoroutine(saveHistoryCoroutine);
            }
            
            // Запускаем новую корутину с задержкой
            saveHistoryCoroutine = StartCoroutine(SaveHistoryDelayed());
        }
        
        /// <summary>
        /// Корутина для отложенного сохранения истории
        /// </summary>
        private IEnumerator SaveHistoryDelayed()
        {
            yield return new WaitForSeconds(HistorySaveDelay);
            
            if (historyDirty)
            {
                SaveToDisk();
                historyDirty = false;
            }
            
            saveHistoryCoroutine = null;
        }
        
        /// <summary>
        /// Принудительно сохраняет историю (вызывается при закрытии приложения)
        /// </summary>
        public void ForceSaveHistory()
        {
            if (saveHistoryCoroutine != null)
            {
                StopCoroutine(saveHistoryCoroutine);
                saveHistoryCoroutine = null;
            }
            
            if (historyDirty)
            {
                SaveToDisk();
                historyDirty = false;
            }
        }

        private void RebuildHistoryCache(bool notifyListeners)
        {
            historyCache.Clear();
            if (storageData == null)
            {
                storageData = new ArtifactStorage.ArtifactStorageData();
            }

            // Группируем записи истории по targetId и берем самую свежую для каждого targetId
            // Это защищает от дубликатов, которые могли быть созданы до исправления AppendHistoryEntry
            var groupedHistory = storageData.history
                .Where(h => !string.IsNullOrEmpty(h.targetId))
                .GroupBy(h => h.targetId)
                .Select(g => g.OrderByDescending(h => h.scannedAtTicks).First())
                .OrderByDescending(h => h.scannedAtTicks);

            foreach (var entry in groupedHistory)
            {
                var artifactRecord = FindRecordForHistoryEntry(entry);
                var status = ParseStatus(entry.status);
                var previewPath = artifactRecord?.previewLocalPath;
                if (!string.IsNullOrEmpty(previewPath) && !File.Exists(previewPath))
                {
                    previewPath = null;
                }

                historyCache.Add(new ArtifactHistoryItem
                {
                    ArtifactId = entry.artifactId,
                    TargetId = entry.targetId,
                    DisplayName = artifactRecord?.name ?? "Неизвестный артефакт",
                    PreviewLocalPath = previewPath,
                    PreviewImageUrl = artifactRecord?.previewImageUrl,
                    LastScannedAt = entry.scannedAtTicks > 0
                        ? new DateTime(entry.scannedAtTicks, DateTimeKind.Utc)
                        : DateTime.UtcNow,
                    Status = status,
                    StatusDescription = string.IsNullOrEmpty(entry.statusDetails)
                        ? GetDefaultStatusDescription(status)
                        : entry.statusDetails
                });
            }

            if (notifyListeners)
            {
                OnHistoryChanged?.Invoke(historyReadonly);
            }
        }

        private ArtifactHistoryStatus ParseStatus(string status)
        {
            if (Enum.TryParse(status, out ArtifactHistoryStatus parsed))
            {
                return parsed;
            }

            return ArtifactHistoryStatus.Unknown;
        }

        private string GetDefaultStatusDescription(ArtifactHistoryStatus status)
        {
            return status switch
            {
                ArtifactHistoryStatus.Ready => "Готово к отображению",
                ArtifactHistoryStatus.Warning => "Требуется внимание",
                ArtifactHistoryStatus.Error => "Ошибка загрузки",
                ArtifactHistoryStatus.Loading => "Загрузка...",
                _ => "Нет данных"
            };
        }

        private class ArtifactRequestOperation
        {
            public string targetId;
            public readonly List<Action<ArtifactAvailabilityResult>> successCallbacks = new();
            public readonly List<Action<string>> errorCallbacks = new();
        }

        #region DTOs

        [Serializable]
        private class TargetArtifactWrapper
        {
            public List<TargetDto> items;
        }

        [Serializable]
        private class TargetDto
        {
            public string id;
            public string artifact_id;
            public ArtifactDto artifacts; // JSON поле называется 'artifacts' из-за join
        }

        [Serializable]
        private class ArtifactDto
        {
            public string id;
            public string name;
            public string description;
            public string preview_image_url;
            public bool is_active;
            public ArtifactMediaDto[] artifact_media;
        }

        [Serializable]
        private class ArtifactMediaDto
        {
            public string id;
            public string artifact_id;
            public string media_id;
            public MediaDto media;
        }

        [Serializable]
        private class MediaDto
        {
            public string id;
            public string media_type;
            public string url;
            public MetadataDto metadata;
        }

        [Serializable]
        private class MetadataDto
        {
            // Поля для 3D моделей
            public bool center_model;
            public long size;
            public string filename;
            
            // Поля для видео
            public int width;
            public int height;
            public float duration;
        }

        #endregion

        #region Public Models

        public enum ArtifactHistoryStatus
        {
            Unknown,
            Loading,
            Ready,
            Warning,
            Error
        }

        [Serializable]
        public class ArtifactHistoryItem
        {
            public string ArtifactId;
            public string TargetId;
            public string DisplayName;
            public string PreviewLocalPath;
            public string PreviewImageUrl;
            public DateTime LastScannedAt;
            public ArtifactHistoryStatus Status;
            public string StatusDescription;
        }

        public class ArtifactAvailabilityResult
        {
            public string ArtifactId;
            public string TargetId;
            public string DisplayName;
            public string Description;
            public string PreviewLocalPath;
            public string LocalModelPath;
            public string LocalVideoPath;
            public string VideoUrl;
            public bool IsVideo;
            public ArtifactStorage.ArtifactRecord Record;
            public bool IsFromCache; // Флаг, указывающий, загружена ли модель из кэша
        }

        public class ArtifactRemoteEntry
        {
            public string ArtifactTargetId;
            public string ArtifactId;
            public string TargetId;
            public int DisplayPriority;
            public ArtifactRemoteArtifact Artifact;
        }

        public class ArtifactRemoteArtifact
        {
            public string ArtifactId;
            public string Name;
            public string Description;
            public string PreviewImageUrl;
            public bool IsActive;
            public List<ArtifactRemoteMedia> Media;
        }

        public class ArtifactRemoteMedia
        {
            public string MediaId;
            public string MediaType;
            public string Url;
            public string MetadataJson;
        }

        #endregion
    }
}


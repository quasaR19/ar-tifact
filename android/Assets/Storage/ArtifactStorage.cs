using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ARArtifact.Storage
{
    /// <summary>
    /// Локальное хранилище артефактов, связанных медиа и истории сканирования.
    /// Сохраняет данные в JSON и управляет кешем файлов (glb, превью и т.д.).
    /// </summary>
    public class ArtifactStorage
    {
        private const string STORAGE_FILE_NAME = "artifact_history.json";
        private const string MEDIA_FOLDER_NAME = "artifact_media";
        private const string PREVIEW_FOLDER_NAME = "artifact_previews";

        private string StoragePath => Path.Combine(Application.persistentDataPath, STORAGE_FILE_NAME);
        public string MediaFolderPath => Path.Combine(Application.persistentDataPath, MEDIA_FOLDER_NAME);
        public string PreviewFolderPath => Path.Combine(Application.persistentDataPath, PREVIEW_FOLDER_NAME);

        /// <summary>
        /// Загружает данные истории и кеша из локального файла.
        /// </summary>
        public ArtifactStorageData LoadData()
        {
            try
            {
                if (!File.Exists(StoragePath))
                {
                    Debug.Log("[ArtifactStorage] Файл истории отсутствует, возвращаем пустые данные");
                    return new ArtifactStorageData();
                }

                string json = File.ReadAllText(StoragePath, Encoding.UTF8);
                var data = JsonUtility.FromJson<ArtifactStorageData>(json);
                if (data == null)
                {
                    Debug.LogWarning("[ArtifactStorage] Не удалось десериализовать данные, создаем новый контейнер");
                    return new ArtifactStorageData();
                }

                Debug.Log($"[ArtifactStorage] Данные загружены. Артефактов: {data.artifacts.Count}, записей истории: {data.history.Count}");
                return data;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ArtifactStorage] Ошибка чтения файла истории: {e.Message}");
                return new ArtifactStorageData();
            }
        }

        /// <summary>
        /// Сохраняет данные истории в файл.
        /// </summary>
        public void SaveData(ArtifactStorageData data)
        {
            if (data == null)
            {
                Debug.LogWarning("[ArtifactStorage] Попытка сохранить пустые данные, операция отменена");
                return;
            }

            try
            {
                EnsureFolders();
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(StoragePath, json, Encoding.UTF8);
                Debug.Log($"[ArtifactStorage] Данные сохранены: {StoragePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ArtifactStorage] Ошибка сохранения данных: {e.Message}");
            }
        }

        /// <summary>
        /// Очищает историю и удаляет все кешированные файлы.
        /// </summary>
        public void ClearAllData()
        {
            try
            {
                if (File.Exists(StoragePath))
                {
                    File.Delete(StoragePath);
                    Debug.Log("[ArtifactStorage] Файл истории удален");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ArtifactStorage] Ошибка удаления файла истории: {e.Message}");
            }

            DeleteDirectoryIfExists(MediaFolderPath);
            DeleteDirectoryIfExists(PreviewFolderPath);
        }

        /// <summary>
        /// Возвращает путь для сохранения медиа-файла (например, glb).
        /// </summary>
        public string GetMediaFilePath(string artifactId, string mediaId, string remoteUrl)
        {
            EnsureFolders();
            string extension = ResolveExtension(remoteUrl, ".bin");
            string fileName = $"{Sanitize(artifactId)}_{Sanitize(mediaId)}{extension}";
            string path = Path.Combine(MediaFolderPath, fileName);
            Debug.Log($"[ArtifactStorage] Путь для медиа: {path}");
            return path;
        }

        /// <summary>
        /// Возвращает путь для сохранения превью-изображения.
        /// </summary>
        public string GetPreviewFilePath(string artifactId, string remoteUrl)
        {
            EnsureFolders();
            string extension = ResolveExtension(remoteUrl, ".jpg");
            string fileName = $"{Sanitize(artifactId)}{extension}";
            string path = Path.Combine(PreviewFolderPath, fileName);
            Debug.Log($"[ArtifactStorage] Путь для превью: {path}");
            return path;
        }

        /// <summary>
        /// Удаляет локальный файл, если он существует.
        /// </summary>
        public void DeleteFileIfExists(string localPath)
        {
            if (string.IsNullOrEmpty(localPath)) return;
            try
            {
                if (File.Exists(localPath))
                {
                    File.Delete(localPath);
                    Debug.Log($"[ArtifactStorage] Удален файл: {localPath}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ArtifactStorage] Ошибка удаления файла {localPath}: {e.Message}");
            }
        }

        private void EnsureFolders()
        {
            if (!Directory.Exists(MediaFolderPath))
            {
                Directory.CreateDirectory(MediaFolderPath);
                Debug.Log($"[ArtifactStorage] Создан каталог медиа: {MediaFolderPath}");
            }

            if (!Directory.Exists(PreviewFolderPath))
            {
                Directory.CreateDirectory(PreviewFolderPath);
                Debug.Log($"[ArtifactStorage] Создан каталог превью: {PreviewFolderPath}");
            }
        }

        private void DeleteDirectoryIfExists(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                    Debug.Log($"[ArtifactStorage] Каталог удален: {path}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ArtifactStorage] Ошибка удаления каталога {path}: {e.Message}");
            }
        }

        private string ResolveExtension(string url, string fallback)
        {
            if (string.IsNullOrEmpty(url))
            {
                return fallback;
            }

            try
            {
                Uri uri = new Uri(url);
                string extension = Path.GetExtension(uri.AbsolutePath);
                if (string.IsNullOrEmpty(extension) || extension.Length > 8)
                {
                    return fallback;
                }

                return extension;
            }
            catch
            {
                return fallback;
            }
        }

        private string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "unknown";
            }

            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                builder.Append(char.IsLetterOrDigit(c) ? c : '_');
            }
            return builder.ToString();
        }

        [Serializable]
        public class ArtifactStorageData
        {
            public List<ArtifactRecord> artifacts = new();
            public List<ArtifactHistoryEntry> history = new();
        }

        [Serializable]
        public class ArtifactRecord
        {
            public string artifactId;
            public string targetId;
            public string name;
            public string description;
            public string previewImageUrl;
            public string previewLocalPath;
            public bool isActive;
            public long lastUpdatedTicks;
            public List<MediaCacheRecord> media = new();
        }

        [Serializable]
        public class MediaCacheRecord
        {
            public string mediaId;
            public string mediaType;
            public string remoteUrl;
            public string localPath;
            public long cachedAtTicks;
            public string metadataJson;
        }

        [Serializable]
        public class ArtifactHistoryEntry
        {
            public string artifactId;
            public string targetId;
            public long scannedAtTicks;
            public string status;
            public string statusDetails;
        }
    }
}


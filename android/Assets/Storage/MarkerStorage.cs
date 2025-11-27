using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ARArtifact.Storage
{
    /// <summary>
    /// Хранилище для маркеров (локальное хранение в JSON файле)
    /// </summary>
    public class MarkerStorage
    {
        private const string STORAGE_FILE_NAME = "markers.json";
        private const string IMAGES_FOLDER_NAME = "marker_images";
        private string StoragePath => Path.Combine(Application.persistentDataPath, STORAGE_FILE_NAME);
        private string ImagesFolderPath => Path.Combine(Application.persistentDataPath, IMAGES_FOLDER_NAME);
        
        /// <summary>
        /// Инициализирует папку для хранения изображений
        /// </summary>
        public void InitializeImagesFolder()
        {
            if (!Directory.Exists(ImagesFolderPath))
            {
                Directory.CreateDirectory(ImagesFolderPath);
                Debug.Log($"[MarkerStorage] Создана папка для изображений: {ImagesFolderPath}");
            }
        }
        
        /// <summary>
        /// Получает путь для сохранения изображения маркера
        /// </summary>
        public string GetImagePath(string markerId, string imageUrl)
        {
            InitializeImagesFolder();
            
            // Определяем расширение файла из URL
            string extension = ".jpg"; // По умолчанию
            try
            {
                Uri uri = new Uri(imageUrl);
                string path = uri.AbsolutePath;
                int lastDot = path.LastIndexOf('.');
                if (lastDot > 0)
                {
                    extension = path.Substring(lastDot);
                    // Ограничиваем длину расширения
                    if (extension.Length > 10) extension = ".jpg";
                }
            }
            catch
            {
                // Если не удалось определить расширение, используем по умолчанию
            }
            
            string fileName = $"{markerId}{extension}";
            return Path.Combine(ImagesFolderPath, fileName);
        }
        
        /// <summary>
        /// Проверяет, существует ли локальное изображение маркера
        /// </summary>
        public bool HasLocalImage(string localImagePath)
        {
            if (string.IsNullOrEmpty(localImagePath))
                return false;
            
            return File.Exists(localImagePath);
        }
        
        /// <summary>
        /// Удаляет локальное изображение маркера
        /// </summary>
        public void DeleteLocalImage(string localImagePath)
        {
            if (string.IsNullOrEmpty(localImagePath) || !File.Exists(localImagePath))
                return;
            
            try
            {
                File.Delete(localImagePath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[MarkerStorage] Ошибка удаления изображения: {e.Message}");
            }
        }
        
        /// <summary>
        /// Очищает все локальные изображения маркеров
        /// </summary>
        public void ClearAllImages()
        {
            try
            {
                if (Directory.Exists(ImagesFolderPath))
                {
                    Directory.Delete(ImagesFolderPath, true);
                    Debug.Log("[MarkerStorage] Все локальные изображения удалены");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[MarkerStorage] Ошибка очистки изображений: {e.Message}");
            }
        }
        
        [Serializable]
        private class StorageData
        {
            public List<MarkerData> markers = new List<MarkerData>();
            public string lastUpdateTime;
        }
        
        /// <summary>
        /// Данные маркера
        /// </summary>
        [Serializable]
        public class MarkerData
        {
            public string id;
            public string url; // Оригинальная ссылка на изображение
            public string localImagePath; // Путь к локально сохраненному изображению
            public string createdAt;
            public int sizeCm;
            public string artifactId;
            public string artifactName;
        }
        
        /// <summary>
        /// Проверяет, есть ли сохраненные маркеры
        /// </summary>
        public bool HasMarkers()
        {
            return File.Exists(StoragePath);
        }
        
        /// <summary>
        /// Сохраняет маркеры в локальное хранилище
        /// </summary>
        public void SaveMarkers(List<MarkerData> markers)
        {
            try
            {
                StorageData data = new StorageData
                {
                    markers = markers ?? new List<MarkerData>(),
                    lastUpdateTime = DateTime.UtcNow.ToString("O") // ISO 8601 format
                };
                
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(StoragePath, json);
                
                Debug.Log($"[MarkerStorage] Сохранено маркеров: {markers?.Count ?? 0}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[MarkerStorage] Ошибка сохранения: {e.Message}");
            }
        }
        
        /// <summary>
        /// Загружает маркеры из локального хранилища
        /// </summary>
        public List<MarkerData> GetMarkers()
        {
            if (!HasMarkers())
            {
                return new List<MarkerData>();
            }
            
            try
            {
                string json = File.ReadAllText(StoragePath);
                StorageData data = JsonUtility.FromJson<StorageData>(json);
                
                return data?.markers ?? new List<MarkerData>();
            }
            catch (Exception e)
            {
                Debug.LogError($"[MarkerStorage] Ошибка загрузки: {e.Message}");
                return new List<MarkerData>();
            }
        }
        
        /// <summary>
        /// Получает дату последнего обновления
        /// </summary>
        public DateTime GetLastUpdateTime()
        {
            if (!HasMarkers())
            {
                return DateTime.MinValue;
            }
            
            try
            {
                string json = File.ReadAllText(StoragePath);
                StorageData data = JsonUtility.FromJson<StorageData>(json);
                
                if (data != null && !string.IsNullOrEmpty(data.lastUpdateTime))
                {
                    if (DateTime.TryParse(data.lastUpdateTime, out DateTime result))
                    {
                        return result;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[MarkerStorage] Ошибка чтения даты обновления: {e.Message}");
            }
            
            return DateTime.MinValue;
        }
    }
}


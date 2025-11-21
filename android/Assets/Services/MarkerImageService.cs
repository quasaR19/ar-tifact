using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace ARArtifact.Services
{
    /// <summary>
    /// Сервис для загрузки и сохранения изображений маркеров
    /// </summary>
    public class MarkerImageService : MonoBehaviour
    {
        private static MarkerImageService _instance;
        public static MarkerImageService Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("MarkerImageService");
                    _instance = go.AddComponent<MarkerImageService>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }
        
        private Storage.MarkerStorage storage;
        
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            storage = new Storage.MarkerStorage();
        }
        
        private void OnDestroy()
        {
            // Очищаем ссылку на instance при уничтожении
            if (_instance == this)
            {
                _instance = null;
            }
            
            // Останавливаем все корутины
            StopAllCoroutines();
        }
        
        private void OnApplicationQuit()
        {
            // Очищаем при выходе из приложения
            if (_instance == this)
            {
                _instance = null;
            }
        }
        
        /// <summary>
        /// Загружает изображение маркера по URL и сохраняет локально
        /// </summary>
        public void DownloadMarkerImage(string markerId, string imageUrl, Action<string> onSuccess, Action<string> onError)
        {
            StartCoroutine(DownloadImageCoroutine(markerId, imageUrl, onSuccess, onError));
        }
        
        /// <summary>
        /// Загружает несколько изображений маркеров параллельно
        /// </summary>
        public void DownloadMarkerImages(List<Storage.MarkerStorage.MarkerData> markers, Action<int, int> onProgress, Action onComplete)
        {
            StartCoroutine(DownloadImagesCoroutine(markers, onProgress, onComplete));
        }
        
        private IEnumerator DownloadImageCoroutine(string markerId, string imageUrl, Action<string> onSuccess, Action<string> onError)
        {
            if (string.IsNullOrEmpty(imageUrl))
            {
                onError?.Invoke("URL изображения пуст");
                yield break;
            }
            
            // Получаем путь для сохранения
            string localPath = storage.GetImagePath(markerId, imageUrl);
            
            // Если изображение уже загружено, возвращаем путь
            if (storage.HasLocalImage(localPath))
            {
                onSuccess?.Invoke(localPath);
                yield break;
            }
            
            // Загружаем изображение
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(imageUrl))
            {
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        Texture2D texture = DownloadHandlerTexture.GetContent(request);
                        
                        // Определяем формат для сохранения
                        byte[] imageData = null;
                        string extension = Path.GetExtension(localPath).ToLower();
                        
                        if (extension == ".png")
                        {
                            imageData = texture.EncodeToPNG();
                        }
                        else
                        {
                            // По умолчанию сохраняем как JPG
                            imageData = texture.EncodeToJPG();
                            // Обновляем путь, если нужно
                            if (!localPath.EndsWith(".jpg"))
                            {
                                localPath = storage.GetImagePath(markerId, imageUrl);
                            }
                        }
                        
                        // Сохраняем файл
                        File.WriteAllBytes(localPath, imageData);
                        
                        Debug.Log($"[MarkerImageService] Изображение сохранено: {localPath}");
                        onSuccess?.Invoke(localPath);
                    }
                    catch (Exception e)
                    {
                        onError?.Invoke($"Ошибка сохранения изображения: {e.Message}");
                    }
                }
                else
                {
                    onError?.Invoke($"Ошибка загрузки изображения: {request.error} (HTTP {request.responseCode})");
                }
            }
        }
        
        private IEnumerator DownloadImagesCoroutine(List<Storage.MarkerStorage.MarkerData> markers, Action<int, int> onProgress, Action onComplete)
        {
            if (markers == null || markers.Count == 0)
            {
                onComplete?.Invoke();
                yield break;
            }
            
            int total = markers.Count;
            int downloaded = 0;
            int failed = 0;
            
            foreach (var marker in markers)
            {
                if (string.IsNullOrEmpty(marker.url))
                {
                    failed++;
                    onProgress?.Invoke(downloaded + failed, total);
                    continue;
                }
                
                // Пропускаем, если изображение уже загружено
                if (!string.IsNullOrEmpty(marker.localImagePath) && storage.HasLocalImage(marker.localImagePath))
                {
                    downloaded++;
                    onProgress?.Invoke(downloaded + failed, total);
                    continue;
                }
                
                bool completed = false;
                
                DownloadMarkerImage(
                    marker.id,
                    marker.url,
                    onSuccess: (localPath) =>
                    {
                        marker.localImagePath = localPath;
                        downloaded++;
                        completed = true;
                    },
                    onError: (error) =>
                    {
                        Debug.LogWarning($"[MarkerImageService] Не удалось загрузить изображение для маркера {marker.id}: {error}");
                        failed++;
                        completed = true;
                    }
                );
                
                // Ждем завершения загрузки
                while (!completed)
                {
                    yield return null;
                }
                
                onProgress?.Invoke(downloaded + failed, total);
                
                // Небольшая задержка между загрузками, чтобы не перегружать сеть
                yield return new WaitForSeconds(0.1f);
            }
            
            Debug.Log($"[MarkerImageService] Загрузка завершена: {downloaded} успешно, {failed} ошибок из {total}");
            onComplete?.Invoke();
        }
        
        /// <summary>
        /// Загружает изображение из локального файла в Texture2D с правильным форматом для ARCore
        /// </summary>
        public Texture2D LoadLocalImage(string localImagePath)
        {
            if (string.IsNullOrEmpty(localImagePath) || !File.Exists(localImagePath))
            {
                return null;
            }
            
            try
            {
                byte[] imageData = File.ReadAllBytes(localImagePath);
                
                // Создаем временную текстуру для загрузки изображения
                Texture2D tempTexture = new Texture2D(2, 2);
                bool loaded = tempTexture.LoadImage(imageData);
                
                if (!loaded)
                {
                    Debug.LogError($"[MarkerImageService] Не удалось загрузить изображение из данных: {localImagePath}");
                    Destroy(tempTexture);
                    return null;
                }
                
                // Определяем правильный формат для ARCore (RGB24 или RGBA32)
                // ARCore требует RGB24 или RGBA32 формат
                TextureFormat targetFormat = TextureFormat.RGB24;
                
                // Проверяем, есть ли альфа-канал в исходном изображении
                // Для этого проверяем формат или анализируем пиксели
                if (HasAlphaChannel(tempTexture))
                {
                    targetFormat = TextureFormat.RGBA32;
                }
                
                // Создаем финальную текстуру с правильным форматом, readable=true, без mipmap
                // ARCore может требовать текстуру без mipmap
                Texture2D finalTexture = new Texture2D(tempTexture.width, tempTexture.height, targetFormat, false);
                
                // Копируем пиксели через RenderTexture для гарантии правильного формата
                // Это также гарантирует, что текстура будет readable
                RenderTexture renderTexture = RenderTexture.GetTemporary(
                    tempTexture.width,
                    tempTexture.height,
                    0,
                    RenderTextureFormat.Default,
                    RenderTextureReadWrite.Linear);
                
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = renderTexture;
                
                // Копируем исходную текстуру в RenderTexture
                Graphics.Blit(tempTexture, renderTexture);
                
                // Читаем пиксели из RenderTexture в финальную текстуру
                finalTexture.ReadPixels(new Rect(0, 0, tempTexture.width, tempTexture.height), 0, 0);
                finalTexture.Apply();
                
                // Восстанавливаем активный RenderTexture
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(renderTexture);
                
                // Уничтожаем временную текстуру
                Destroy(tempTexture);
                
                Debug.Log($"[MarkerImageService] Изображение загружено: {localImagePath}, размер: {finalTexture.width}x{finalTexture.height}, формат: {finalTexture.format}, readable: {finalTexture.isReadable}");
                
                return finalTexture;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MarkerImageService] Ошибка загрузки локального изображения: {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Проверяет, имеет ли текстура альфа-канал
        /// </summary>
        private bool HasAlphaChannel(Texture2D texture)
        {
            if (texture == null) return false;
            
            // Проверяем формат текстуры
            return texture.format == TextureFormat.RGBA32 ||
                   texture.format == TextureFormat.ARGB32 ||
                   texture.format == TextureFormat.RGBA4444 ||
                   texture.format == TextureFormat.Alpha8;
        }
        
        /// <summary>
        /// Масштабирует текстуру до минимального размера, если она меньше требуемого
        /// </summary>
        /// <param name="texture">Исходная текстура</param>
        /// <param name="minWidth">Минимальная ширина (по умолчанию 300 для ARCore)</param>
        /// <param name="minHeight">Минимальная высота (по умолчанию 300 для ARCore)</param>
        /// <returns>Масштабированная текстура или исходная, если размер достаточен</returns>
        public Texture2D ScaleTextureIfNeeded(Texture2D texture, int minWidth = 300, int minHeight = 300)
        {
            if (texture == null)
            {
                return null;
            }
            
            int width = texture.width;
            int height = texture.height;
            
            // Если размер достаточен, возвращаем исходную текстуру
            if (width >= minWidth && height >= minHeight)
            {
                return texture;
            }
            
            // Вычисляем новый размер с сохранением пропорций
            float scaleX = (float)minWidth / width;
            float scaleY = (float)minHeight / height;
            float scale = Mathf.Max(scaleX, scaleY);
            
            int newWidth = Mathf.RoundToInt(width * scale);
            int newHeight = Mathf.RoundToInt(height * scale);
            
            Debug.Log($"[MarkerImageService] Масштабирование изображения: {width}x{height} -> {newWidth}x{newHeight} (минимальный размер: {minWidth}x{minHeight})");
            
            // Создаем RenderTexture для масштабирования
            RenderTexture renderTexture = RenderTexture.GetTemporary(newWidth, newHeight, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTexture;
            
            // Копируем исходную текстуру в RenderTexture с масштабированием
            Graphics.Blit(texture, renderTexture);
            
            // Создаем новую текстуру с правильным форматом
            TextureFormat targetFormat = texture.format == TextureFormat.RGBA32 ? TextureFormat.RGBA32 : TextureFormat.RGB24;
            Texture2D scaledTexture = new Texture2D(newWidth, newHeight, targetFormat, false);
            scaledTexture.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
            scaledTexture.Apply();
            
            // Восстанавливаем активный RenderTexture
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTexture);
            
            Debug.Log($"[MarkerImageService] Масштабирование завершено: {scaledTexture.width}x{scaledTexture.height}, формат: {scaledTexture.format}, readable: {scaledTexture.isReadable}");
            
            return scaledTexture;
        }
    }
}


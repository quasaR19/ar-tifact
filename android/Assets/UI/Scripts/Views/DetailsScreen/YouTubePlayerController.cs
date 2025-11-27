using System;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Networking;
using System.Collections;

namespace ARArtifact.UI
{
    /// <summary>
    /// Контроллер для отображения YouTube видео в UI Toolkit.
    /// Показывает превью и открывает видео в браузере при клике.
    /// </summary>
    public class YouTubePlayerController : MonoBehaviour
    {
        private const string LogPrefix = "[YouTubePlayerController]";
        
        // YouTube URL patterns
        private static readonly Regex[] YouTubePatterns = new[]
        {
            new Regex(@"(?:youtube\.com\/watch\?v=|youtu\.be\/)([a-zA-Z0-9_-]{11})", RegexOptions.IgnoreCase),
            new Regex(@"youtube\.com\/embed\/([a-zA-Z0-9_-]{11})", RegexOptions.IgnoreCase),
            new Regex(@"youtube\.com\/v\/([a-zA-Z0-9_-]{11})", RegexOptions.IgnoreCase)
        };
        
        // UI Elements
        private VisualElement playerContainer;
        private VisualElement thumbnailContainer;
        private Button playButton;
        private Label titleLabel;
        private Label statusLabel;
        
        // State
        private string videoId;
        private string videoUrl;
        private Texture2D thumbnailTexture;
        private bool hasOpenedVideo; // Гарантия одного открытия
        
        /// <summary>
        /// Инициализирует UI элементы.
        /// </summary>
        public void InitializeUI(VisualElement container)
        {
            playerContainer = container;
            
            // Находим элементы
            thumbnailContainer = playerContainer.Q<VisualElement>("thumbnail");
            playButton = playerContainer.Q<Button>("play-button");
            titleLabel = playerContainer.Q<Label>("youtube-title");
            statusLabel = playerContainer.Q<Label>("status-label");
            
            // Настраиваем кнопку воспроизведения
            if (playButton != null)
            {
                playButton.clicked += OnPlayClicked;
            }
            
            // Клик по thumbnail тоже открывает видео
            if (thumbnailContainer != null)
            {
                thumbnailContainer.RegisterCallback<ClickEvent>(evt =>
                {
                    OnPlayClicked();
                    evt.StopPropagation();
                });
            }
        }
        
        /// <summary>
        /// Загружает YouTube видео по URL.
        /// </summary>
        public void LoadVideo(string url, Action onSuccess = null, Action<string> onError = null)
        {
            if (string.IsNullOrEmpty(url))
            {
                onError?.Invoke("URL не указан");
                return;
            }
            
            videoUrl = url;
            hasOpenedVideo = false; // Сбрасываем флаг при новой загрузке
            
            // Извлекаем video ID
            videoId = ParseVideoId(url);
            
            if (string.IsNullOrEmpty(videoId))
            {
                string error = "Не удалось распознать YouTube URL";
                UpdateStatus(error);
                onError?.Invoke(error);
                Debug.LogWarning($"{LogPrefix} {error}: {url}");
                return;
            }
            
            Debug.Log($"{LogPrefix} YouTube Video ID: {videoId}");
            
            // Загружаем thumbnail
            StartCoroutine(LoadThumbnailCoroutine(videoId, onSuccess, onError));
        }
        
        /// <summary>
        /// Извлекает video ID из YouTube URL.
        /// </summary>
        private string ParseVideoId(string url)
        {
            foreach (var pattern in YouTubePatterns)
            {
                var match = pattern.Match(url);
                if (match.Success && match.Groups.Count > 1)
                {
                    return match.Groups[1].Value;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Корутина для загрузки thumbnail из YouTube.
        /// </summary>
        private IEnumerator LoadThumbnailCoroutine(string videoId, Action onSuccess, Action<string> onError)
        {
            UpdateStatus("Загрузка превью...");
            
            // YouTube thumbnail URLs (в порядке приоритета)
            string[] thumbnailUrls = new[]
            {
                $"https://img.youtube.com/vi/{videoId}/maxresdefault.jpg", // Высокое качество
                $"https://img.youtube.com/vi/{videoId}/hqdefault.jpg",     // Среднее качество
                $"https://img.youtube.com/vi/{videoId}/mqdefault.jpg",     // Низкое качество
                $"https://img.youtube.com/vi/{videoId}/default.jpg"        // Самое низкое качество
            };
            
            bool success = false;
            
            // Пробуем загрузить thumbnail по порядку
            foreach (var thumbnailUrl in thumbnailUrls)
            {
                using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(thumbnailUrl))
                {
                    yield return request.SendWebRequest();
                    
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        thumbnailTexture = DownloadHandlerTexture.GetContent(request);
                        
                        if (thumbnailContainer != null)
                        {
                            thumbnailContainer.style.backgroundImage = Background.FromTexture2D(thumbnailTexture);
                        }
                        
                        success = true;
                        Debug.Log($"{LogPrefix} Thumbnail загружен: {thumbnailUrl}");
                        break;
                    }
                }
            }
            
            if (success)
            {
                UpdateStatus("");
                
                // Устанавливаем заголовок (можно расширить для получения реального названия через YouTube API)
                if (titleLabel != null)
                {
                    titleLabel.text = "YouTube Video";
                }
                
                onSuccess?.Invoke();
            }
            else
            {
                string error = "Не удалось загрузить превью";
                UpdateStatus(error);
                onError?.Invoke(error);
                Debug.LogWarning($"{LogPrefix} {error} для video ID: {videoId}");
            }
        }
        
        /// <summary>
        /// Обработчик клика по кнопке воспроизведения.
        /// </summary>
        private void OnPlayClicked()
        {
            if (string.IsNullOrEmpty(videoUrl))
            {
                Debug.LogWarning($"{LogPrefix} URL видео не установлен");
                return;
            }
            
            // Гарантия одного открытия
            if (hasOpenedVideo)
            {
                Debug.Log($"{LogPrefix} Видео уже было открыто, пропускаем повторное открытие");
                return;
            }
            
            hasOpenedVideo = true;
            
            // Открываем видео в браузере
            Application.OpenURL(videoUrl);
            Debug.Log($"{LogPrefix} Открытие YouTube видео в браузере: {videoUrl}");
        }
        
        /// <summary>
        /// Обновляет текст статуса.
        /// </summary>
        private void UpdateStatus(string message)
        {
            if (statusLabel != null)
            {
                statusLabel.text = message;
                statusLabel.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }
        
        /// <summary>
        /// Получает URL YouTube видео.
        /// </summary>
        public string GetVideoUrl()
        {
            return videoUrl;
        }
        
        /// <summary>
        /// Получает ID YouTube видео.
        /// </summary>
        public string GetVideoId()
        {
            return videoId;
        }
        
        /// <summary>
        /// Очистка ресурсов.
        /// </summary>
        public void Cleanup()
        {
            if (thumbnailTexture != null)
            {
                Destroy(thumbnailTexture);
                thumbnailTexture = null;
            }
            
            videoId = null;
            videoUrl = null;
        }
        
        private void OnDestroy()
        {
            Cleanup();
        }
    }
}


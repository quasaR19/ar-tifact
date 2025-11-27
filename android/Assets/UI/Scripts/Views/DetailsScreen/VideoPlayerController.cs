using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace ARArtifact.UI
{
    /// <summary>
    /// Контроллер для воспроизведения видео в UI Toolkit.
    /// Поддерживает загрузку локальных видеофайлов и элементы управления.
    /// </summary>
    public class VideoPlayerController : MonoBehaviour
    {
        private const string LogPrefix = "[VideoPlayerController]";
        
        [Header("Render Settings")]
        [SerializeField] private int renderTextureWidth = 1920;
        [SerializeField] private int renderTextureHeight = 1080;
        
        // Components
        private VideoPlayer videoPlayer;
        private RenderTexture renderTexture;
        private GameObject playerObject;
        
        // UI Elements
        private VisualElement playerContainer;
        private VisualElement videoSurface;
        private VisualElement controlsContainer;
        private Button playPauseButton;
        private Slider progressSlider;
        private Label timeLabel;
        private Label statusLabel;
        
        // State
        private bool isLoading;
        private bool isPrepared;
        private bool isPlaying;
        private bool isScrubbing;
        private Coroutine updateCoroutine;
        
        private void Awake()
        {
            SetupVideoPlayer();
        }
        
        /// <summary>
        /// Настраивает VideoPlayer компонент.
        /// </summary>
        private void SetupVideoPlayer()
        {
            playerObject = new GameObject("VideoPlayer");
            playerObject.transform.SetParent(transform);
            
            videoPlayer = playerObject.AddComponent<VideoPlayer>();
            videoPlayer.playOnAwake = false;
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.skipOnDrop = true;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
            videoPlayer.SetDirectAudioVolume(0, 1.0f);
            
            // Создаем RenderTexture
            renderTexture = new RenderTexture(renderTextureWidth, renderTextureHeight, 0);
            videoPlayer.targetTexture = renderTexture;
            
            // Подписываемся на события
            videoPlayer.prepareCompleted += OnVideoPrepared;
            videoPlayer.loopPointReached += OnVideoFinished;
            videoPlayer.errorReceived += OnVideoError;
        }
        
        /// <summary>
        /// Инициализирует UI элементы.
        /// </summary>
        public void InitializeUI(VisualElement container)
        {
            playerContainer = container;
            
            // Находим video surface
            videoSurface = playerContainer.Q<VisualElement>("video-surface");
            if (videoSurface != null)
            {
                videoSurface.style.backgroundImage = Background.FromRenderTexture(renderTexture);
                
                // Клик по surface для паузы/воспроизведения
                videoSurface.RegisterCallback<ClickEvent>(evt =>
                {
                    TogglePlayPause();
                    evt.StopPropagation();
                });
            }
            
            // Находим элементы управления
            controlsContainer = playerContainer.Q<VisualElement>("video-controls");
            
            playPauseButton = playerContainer.Q<Button>("play-pause-button");
            if (playPauseButton != null)
            {
                playPauseButton.clicked += TogglePlayPause;
                UpdatePlayPauseButton();
            }
            
            progressSlider = playerContainer.Q<Slider>("progress-slider");
            if (progressSlider != null)
            {
                progressSlider.lowValue = 0;
                progressSlider.highValue = 1;
                progressSlider.value = 0;
                
                progressSlider.RegisterValueChangedCallback(evt =>
                {
                    if (isScrubbing)
                    {
                        OnProgressChanged(evt.newValue);
                    }
                });
                
                // Обработка scrubbing
                progressSlider.RegisterCallback<PointerDownEvent>(evt =>
                {
                    isScrubbing = true;
                });
                
                progressSlider.RegisterCallback<PointerUpEvent>(evt =>
                {
                    isScrubbing = false;
                });
            }
            
            timeLabel = playerContainer.Q<Label>("time-label");
            statusLabel = playerContainer.Q<Label>("status-label");
        }
        
        /// <summary>
        /// Загружает видео из локального файла.
        /// </summary>
        public void LoadVideo(string localPath, Action onSuccess = null, Action<string> onError = null)
        {
            if (isLoading)
            {
                Debug.LogWarning($"{LogPrefix} Видео уже загружается, пропускаем запрос");
                return;
            }
            
            if (string.IsNullOrEmpty(localPath))
            {
                onError?.Invoke("Путь к видео не указан");
                return;
            }
            
            // Проверяем существование файла
            if (!System.IO.File.Exists(localPath))
            {
                string error = $"Файл не найден: {localPath}";
                Debug.LogError($"{LogPrefix} {error}");
                onError?.Invoke(error);
                return;
            }
            
            isLoading = true;
            isPrepared = false;
            UpdateStatus("Загрузка видео...");
            
            // Для Android используем прямой путь, для других платформ - file://
            string videoUrl;
            #if UNITY_ANDROID && !UNITY_EDITOR
            videoUrl = localPath; // Android не требует file:// префикс
            #else
            videoUrl = "file://" + localPath;
            #endif
            
            Debug.Log($"{LogPrefix} Загрузка видео: path={localPath}, url={videoUrl}, exists={System.IO.File.Exists(localPath)}");
            
            videoPlayer.source = VideoSource.Url;
            videoPlayer.url = videoUrl;
            
            // Сохраняем callbacks для вызова после подготовки
            StartCoroutine(PrepareVideoCoroutine(onSuccess, onError));
        }
        
        /// <summary>
        /// Корутина для подготовки видео.
        /// </summary>
        private IEnumerator PrepareVideoCoroutine(Action onSuccess, Action<string> onError)
        {
            Debug.Log($"{LogPrefix} Начало подготовки видео: url={videoPlayer.url}, source={videoPlayer.source}");
            
            videoPlayer.Prepare();
            
            // Ждем подготовки или ошибки
            float timeout = 30f; // 30 секунд таймаут
            float elapsed = 0f;
            
            while (!isPrepared && elapsed < timeout)
            {
                if (!isLoading) // Прервано
                {
                    Debug.LogWarning($"{LogPrefix} Подготовка видео прервана");
                    yield break;
                }
                
                elapsed += Time.deltaTime;
                
                // Логируем прогресс каждые 5 секунд
                if (Mathf.FloorToInt(elapsed) % 5 == 0 && Mathf.FloorToInt(elapsed) > 0)
                {
                    Debug.Log($"{LogPrefix} Ожидание подготовки видео: {elapsed:F1}s / {timeout}s");
                }
                
                yield return null;
            }
            
            isLoading = false;
            
            if (isPrepared)
            {
                UpdateStatus("");
                onSuccess?.Invoke();
                Debug.Log($"{LogPrefix} Видео подготовлено: url={videoPlayer.url}, duration={videoPlayer.length}s, size={videoPlayer.width}x{videoPlayer.height}");
            }
            else
            {
                string error = $"Таймаут загрузки видео (>{timeout}s). URL: {videoPlayer.url}";
                UpdateStatus($"Ошибка: Таймаут загрузки");
                onError?.Invoke(error);
                Debug.LogError($"{LogPrefix} {error}");
            }
        }
        
        /// <summary>
        /// Обработчик события подготовки видео.
        /// </summary>
        private void OnVideoPrepared(VideoPlayer source)
        {
            isPrepared = true;
            
            // Обновляем UI
            if (progressSlider != null)
            {
                progressSlider.highValue = (float)source.length;
            }
            
            UpdateTimeLabel();
            
            Debug.Log($"{LogPrefix} Видео подготовлено: длительность={source.length}s, размер={source.width}x{source.height}");
        }
        
        /// <summary>
        /// Обработчик завершения видео.
        /// </summary>
        private void OnVideoFinished(VideoPlayer source)
        {
            isPlaying = false;
            UpdatePlayPauseButton();
            Debug.Log($"{LogPrefix} Видео завершено");
        }
        
        /// <summary>
        /// Обработчик ошибок видео.
        /// </summary>
        private void OnVideoError(VideoPlayer source, string message)
        {
            isLoading = false;
            isPrepared = false;
            string shortError = message.Length > 50 ? message.Substring(0, 50) + "..." : message;
            UpdateStatus($"Ошибка: {shortError}");
            Debug.LogError($"{LogPrefix} Ошибка воспроизведения: {message}, url={source.url}, source={source.source}, isPrepared={source.isPrepared}");
        }
        
        /// <summary>
        /// Переключает воспроизведение/паузу.
        /// </summary>
        private void TogglePlayPause()
        {
            if (!isPrepared) return;
            
            if (isPlaying)
            {
                Pause();
            }
            else
            {
                Play();
            }
        }
        
        /// <summary>
        /// Начинает воспроизведение видео.
        /// </summary>
        public void Play()
        {
            if (!isPrepared) return;
            
            videoPlayer.Play();
            isPlaying = true;
            UpdatePlayPauseButton();
            
            // Запускаем корутину для обновления прогресса
            if (updateCoroutine != null)
            {
                StopCoroutine(updateCoroutine);
            }
            updateCoroutine = StartCoroutine(UpdateProgressCoroutine());
        }
        
        /// <summary>
        /// Приостанавливает воспроизведение видео.
        /// </summary>
        public void Pause()
        {
            if (!isPrepared) return;
            
            videoPlayer.Pause();
            isPlaying = false;
            UpdatePlayPauseButton();
            
            // Останавливаем корутину обновления
            if (updateCoroutine != null)
            {
                StopCoroutine(updateCoroutine);
                updateCoroutine = null;
            }
        }
        
        /// <summary>
        /// Останавливает воспроизведение видео.
        /// </summary>
        public void Stop()
        {
            videoPlayer.Stop();
            isPlaying = false;
            UpdatePlayPauseButton();
            
            if (updateCoroutine != null)
            {
                StopCoroutine(updateCoroutine);
                updateCoroutine = null;
            }
        }
        
        /// <summary>
        /// Обработчик изменения прогресса (scrubbing).
        /// </summary>
        private void OnProgressChanged(float value)
        {
            if (!isPrepared) return;
            
            videoPlayer.time = value;
            UpdateTimeLabel();
        }
        
        /// <summary>
        /// Корутина для обновления прогресса воспроизведения.
        /// </summary>
        private IEnumerator UpdateProgressCoroutine()
        {
            while (isPlaying)
            {
                if (!isScrubbing && progressSlider != null)
                {
                    progressSlider.SetValueWithoutNotify((float)videoPlayer.time);
                }
                
                UpdateTimeLabel();
                
                yield return new WaitForSeconds(0.1f); // Обновляем 10 раз в секунду
            }
        }
        
        /// <summary>
        /// Обновляет кнопку Play/Pause.
        /// </summary>
        private void UpdatePlayPauseButton()
        {
            if (playPauseButton == null) return;
            
            // Меняем текст кнопки
            playPauseButton.text = isPlaying ? "⏸" : "▶";
        }
        
        /// <summary>
        /// Обновляет label времени.
        /// </summary>
        private void UpdateTimeLabel()
        {
            if (timeLabel == null || !isPrepared) return;
            
            double currentTime = videoPlayer.time;
            double totalTime = videoPlayer.length;
            
            string currentStr = FormatTime(currentTime);
            string totalStr = FormatTime(totalTime);
            
            timeLabel.text = $"{currentStr} / {totalStr}";
        }
        
        /// <summary>
        /// Форматирует время в строку MM:SS.
        /// </summary>
        private string FormatTime(double seconds)
        {
            int minutes = (int)(seconds / 60);
            int secs = (int)(seconds % 60);
            return $"{minutes}:{secs:D2}";
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
                // Включаем перенос текста для длинных сообщений
                statusLabel.style.whiteSpace = WhiteSpace.Normal;
            }
        }
        
        /// <summary>
        /// Очистка ресурсов.
        /// </summary>
        public void Cleanup()
        {
            Stop();
            
            if (videoPlayer != null)
            {
                videoPlayer.prepareCompleted -= OnVideoPrepared;
                videoPlayer.loopPointReached -= OnVideoFinished;
                videoPlayer.errorReceived -= OnVideoError;
                
                videoPlayer.Stop();
                videoPlayer.clip = null;
                videoPlayer.url = null;
                
                Destroy(playerObject);
                playerObject = null;
                videoPlayer = null;
            }
            
            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
                renderTexture = null;
            }
            
            if (updateCoroutine != null)
            {
                StopCoroutine(updateCoroutine);
                updateCoroutine = null;
            }
        }
        
        private void OnDestroy()
        {
            Cleanup();
        }
    }
}


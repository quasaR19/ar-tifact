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
        private bool isUserScrubbing; // Флаг для блокировки программных обновлений слайдера во время перемотки
        private bool wasPlayingBeforeScrub; // Сохраняем состояние воспроизведения перед перемоткой
        private Coroutine updateCoroutine; // Единая корутина обновления UI
        
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
                
                // Обработка начала перетаскивания - используем несколько типов событий для надежности
                progressSlider.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (!isPrepared) return;
                    StartScrubbing();
                    evt.StopPropagation();
                });
                
                progressSlider.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (!isPrepared) return;
                    StartScrubbing();
                    evt.StopPropagation();
                });
                
                // Обработка завершения перетаскивания
                progressSlider.RegisterCallback<MouseUpEvent>(evt =>
                {
                    if (isUserScrubbing)
                    {
                        EndScrubbing();
                        evt.StopPropagation();
                    }
                });
                
                progressSlider.RegisterCallback<PointerUpEvent>(evt =>
                {
                    if (isUserScrubbing)
                    {
                        EndScrubbing();
                        evt.StopPropagation();
                    }
                });
                
                // Обработка отмены перетаскивания
                progressSlider.RegisterCallback<PointerCancelEvent>(evt =>
                {
                    if (isUserScrubbing)
                    {
                        CancelScrubbing();
                    }
                });
                
                // Обработка изменения значения слайдера
                // Если значение изменилось значительно и мы еще не в режиме scrubbing,
                // это может быть начало перетаскивания (если PointerDownEvent не сработал)
                progressSlider.RegisterValueChangedCallback(evt =>
                {
                    if (!isPrepared) return;
                    
                    float newValue = evt.newValue;
                    float currentVideoTime = (float)videoPlayer.time;
                    float difference = Mathf.Abs(newValue - currentVideoTime);
                    
                    // Если мы уже в режиме scrubbing, обновляем время
                    if (isUserScrubbing)
                    {
                        OnProgressChanged(newValue);
                        return;
                    }
                    
                    // Если значение сильно отличается от текущего времени видео (больше 0.5 секунды),
                    // это похоже на начало перетаскивания - начинаем scrubbing
                    if (difference > 0.5f && videoPlayer.isPlaying)
                    {
                        Debug.Log($"{LogPrefix} Обнаружено начало перетаскивания через ValueChanged: разница={difference:F2}s");
                        StartScrubbing();
                        OnProgressChanged(newValue);
                    }
                    // Иначе это программное изменение из корутины - игнорируем
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
            
            // Проверяем размер файла
            try
            {
                var fileInfo = new System.IO.FileInfo(localPath);
                long fileSizeMB = fileInfo.Length / (1024 * 1024);
                Debug.Log($"{LogPrefix} Размер видеофайла: {fileSizeMB} MB ({fileInfo.Length} байт)");
                
                // Предупреждение для очень больших файлов
                if (fileSizeMB > 50)
                {
                    Debug.LogWarning($"{LogPrefix} Большой видеофайл ({fileSizeMB} MB), загрузка может занять больше времени");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Не удалось получить информацию о файле: {e.Message}");
            }
            
            isLoading = true;
            isPrepared = false;
            UpdateStatus("Загрузка видео...");
            
            // Для Android используем прямой путь, для других платформ - file://
            string videoUrl;
            #if UNITY_ANDROID && !UNITY_EDITOR
            videoUrl = localPath; // Android не требует file:// префикс
            #else
            // Нормализуем путь: заменяем все обратные слеши на прямые
            string normalizedPath = System.IO.Path.GetFullPath(localPath).Replace('\\', '/');
            // Для абсолютных путей на Windows используем формат file:///C:/path (три слеша)
            videoUrl = "file:///" + normalizedPath;
            Debug.Log($"{LogPrefix} Нормализованный URL: {videoUrl} (исходный путь: {localPath})");
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
            
            // Увеличиваем таймаут для больших файлов (до 60 секунд)
            float timeout = 60f;
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
            // Проверяем валидность видео: длительность должна быть больше 0 и не NaN
            bool isValidVideo = source.length > 0 && !double.IsNaN(source.length) && !double.IsInfinity(source.length);
            
            if (!isValidVideo)
            {
                Debug.LogError($"{LogPrefix} Видео подготовлено, но имеет невалидную длительность: {source.length}s. Файл может быть битым.");
                isLoading = false;
                isPrepared = false;
                
                string errorMsg = "Видеофайл не имеет метаданных длины (файл может быть поврежден или неполон)";
                UpdateStatus($"Ошибка: {errorMsg}");
                OnVideoError(source, errorMsg);
                return;
            }
            
            isPrepared = true;
            
            // Обновляем UI
            if (progressSlider != null)
            {
                progressSlider.highValue = (float)source.length;
            }
            
            UpdateTimeLabel();
            
            // Запускаем корутину обновления UI
            if (updateCoroutine != null)
            {
                StopCoroutine(updateCoroutine);
            }
            updateCoroutine = StartCoroutine(UpdateProgressCoroutine());
            
            Debug.Log($"{LogPrefix} Видео подготовлено: длительность={source.length}s, размер={source.width}x{source.height}");
        }
        
        /// <summary>
        /// Обработчик завершения видео.
        /// </summary>
        private void OnVideoFinished(VideoPlayer source)
        {
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
            
            // Улучшаем сообщение об ошибке для более понятного вывода
            string improvedMessage = message;
            if (message.Contains("0xc00d36e6") || message.Contains("Cannot read file"))
            {
                improvedMessage = $"Не удалось прочитать видеофайл. Возможно, файл поврежден, недоступен или имеет неподдерживаемый формат. URL: {source.url}. Оригинальная ошибка: {message}";
                
                // Проверяем размер файла для диагностики
                try
                {
                    if (source.url.StartsWith("file://"))
                    {
                        string filePath = source.url.Replace("file:///", "").Replace("file://", "");
                        if (System.IO.File.Exists(filePath))
                        {
                            var fileInfo = new System.IO.FileInfo(filePath);
                            long fileSizeMB = fileInfo.Length / (1024 * 1024);
                            improvedMessage += $" Размер файла: {fileSizeMB} MB.";
                            
                            // Предупреждение для очень больших файлов
                            if (fileSizeMB > 50)
                            {
                                improvedMessage += " Файл очень большой, возможно требуется больше времени для подготовки.";
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{LogPrefix} Не удалось получить информацию о файле: {e.Message}");
                }
            }
            else if (message.Contains("WindowsVideoMedia error"))
            {
                improvedMessage = $"Ошибка WindowsVideoMedia при чтении видео. Возможно, файл поврежден или имеет неподдерживаемый формат. URL: {source.url}. Оригинальная ошибка: {message}";
            }
            
            string shortError = improvedMessage.Length > 100 ? improvedMessage.Substring(0, 100) + "..." : improvedMessage;
            UpdateStatus($"Ошибка: {shortError}");
            Debug.LogError($"{LogPrefix} Ошибка воспроизведения: {improvedMessage}, url={source.url}, source={source.source}, isPrepared={source.isPrepared}");
        }
        
        /// <summary>
        /// Переключает воспроизведение/паузу.
        /// </summary>
        private void TogglePlayPause()
        {
            if (!isPrepared) return;
            
            if (videoPlayer.isPlaying)
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
            // UI обновится автоматически через корутину обновления
        }
        
        /// <summary>
        /// Приостанавливает воспроизведение видео.
        /// </summary>
        public void Pause()
        {
            if (!isPrepared) return;
            
            videoPlayer.Pause();
            // UI обновится автоматически через корутину обновления
        }
        
        /// <summary>
        /// Останавливает воспроизведение видео.
        /// </summary>
        public void Stop()
        {
            videoPlayer.Stop();
            // UI обновится автоматически через корутину обновления
        }
        
        /// <summary>
        /// Начинает перемотку (scrubbing).
        /// </summary>
        private void StartScrubbing()
        {
            if (!isPrepared || isUserScrubbing) return;
            
            // Сохраняем состояние воспроизведения перед началом перемотки
            wasPlayingBeforeScrub = videoPlayer.isPlaying;
            
            // Приостанавливаем воспроизведение во время перемотки
            // Делаем это явно, даже если видео уже на паузе
            if (videoPlayer.isPlaying)
            {
                videoPlayer.Pause();
                // Убеждаемся, что видео действительно остановилось
                // Небольшая задержка может потребоваться для некоторых платформ
                if (videoPlayer.isPlaying)
                {
                    Debug.LogWarning($"{LogPrefix} Видео не остановилось после Pause(), пытаемся еще раз");
                    videoPlayer.Pause();
                }
            }
            
            isUserScrubbing = true;
            
            Debug.Log($"{LogPrefix} Начало перемотки, было воспроизведение: {wasPlayingBeforeScrub}, сейчас: {videoPlayer.isPlaying}");
        }
        
        /// <summary>
        /// Завершает перемотку (scrubbing).
        /// </summary>
        private void EndScrubbing()
        {
            if (!isUserScrubbing) return;
            
            // Устанавливаем финальное время видео
            if (progressSlider != null && isPrepared)
            {
                OnProgressChanged(progressSlider.value);
            }
            
            isUserScrubbing = false;
            
            // Принудительно обновляем слайдер текущим временем видео
            // Это гарантирует, что слайдер синхронизирован с VideoPlayer
            if (progressSlider != null && isPrepared)
            {
                float currentTime = (float)videoPlayer.time;
                progressSlider.SetValueWithoutNotify(currentTime);
            }
            
            // Возобновляем воспроизведение, если оно было до перемотки
            if (wasPlayingBeforeScrub)
            {
                // Используем корутину для небольшой задержки перед возобновлением
                // Это дает VideoPlayer время обработать изменение времени
                StartCoroutine(ResumeAfterScrubCoroutine());
            }
            else
            {
                // Если видео не воспроизводилось, все равно обновляем UI
                UpdateTimeLabel();
            }
            
            // Убеждаемся, что корутина обновления запущена
            if (updateCoroutine == null && isPrepared)
            {
                updateCoroutine = StartCoroutine(UpdateProgressCoroutine());
            }
            
            Debug.Log($"{LogPrefix} Завершение перемотки, установлено время: {progressSlider?.value ?? 0f}, возобновлено воспроизведение: {wasPlayingBeforeScrub}, корутина запущена: {updateCoroutine != null}");
        }
        
        /// <summary>
        /// Корутина для возобновления воспроизведения после перемотки.
        /// Небольшая задержка дает VideoPlayer время обработать изменение времени.
        /// </summary>
        private IEnumerator ResumeAfterScrubCoroutine()
        {
            // Сохраняем состояние локально
            bool shouldResume = wasPlayingBeforeScrub;
            
            // Небольшая задержка для обработки изменения времени
            yield return new WaitForEndOfFrame();
            yield return null;
            yield return new WaitForSeconds(0.05f); // Дополнительная небольшая задержка
            
            // Возобновляем воспроизведение
            if (isPrepared && shouldResume && !isUserScrubbing)
            {
                videoPlayer.Play();
                
                // Проверяем, что видео действительно возобновилось
                yield return new WaitForSeconds(0.1f);
                if (!videoPlayer.isPlaying && shouldResume)
                {
                    Debug.LogWarning($"{LogPrefix} Видео не возобновилось, пытаемся еще раз");
                    videoPlayer.Play();
                }
                
                Debug.Log($"{LogPrefix} Воспроизведение возобновлено после перемотки, isPlaying={videoPlayer.isPlaying}");
            }
        }
        
        /// <summary>
        /// Отменяет перемотку (scrubbing).
        /// </summary>
        private void CancelScrubbing()
        {
            if (!isUserScrubbing) return;
            
            // Возвращаем слайдер к текущему времени видео
            if (isPrepared && progressSlider != null)
            {
                float currentTime = (float)videoPlayer.time;
                progressSlider.SetValueWithoutNotify(currentTime);
            }
            
            isUserScrubbing = false;
            
            // Возобновляем воспроизведение, если оно было до перемотки
            if (wasPlayingBeforeScrub)
            {
                StartCoroutine(ResumeAfterScrubCoroutine());
            }
            
            // Убеждаемся, что корутина обновления запущена
            if (updateCoroutine == null && isPrepared)
            {
                updateCoroutine = StartCoroutine(UpdateProgressCoroutine());
            }
            
            Debug.Log($"{LogPrefix} Отмена перемотки, возобновлено воспроизведение: {wasPlayingBeforeScrub}");
        }
        
        /// <summary>
        /// Обработчик изменения прогресса (scrubbing).
        /// </summary>
        private void OnProgressChanged(float value)
        {
            if (!isPrepared) return;
            
            // Ограничиваем значение в пределах длительности видео
            double clampedValue = Mathf.Clamp(value, 0f, (float)videoPlayer.length);
            
            // Убеждаемся, что видео на паузе во время перемотки
            if (isUserScrubbing && videoPlayer.isPlaying)
            {
                Debug.LogWarning($"{LogPrefix} Видео все еще воспроизводится во время перемотки, приостанавливаем");
                videoPlayer.Pause();
            }
            
            // Устанавливаем время видео
            // Во время scrubbing видео должно быть на паузе, поэтому просто устанавливаем время
            videoPlayer.time = clampedValue;
            
            // Обновляем label времени
            UpdateTimeLabel();
            
            Debug.Log($"{LogPrefix} Перемотка: установлено время {clampedValue:F2}s из {videoPlayer.length:F2}s, isPlaying={videoPlayer.isPlaying}");
        }
        
        /// <summary>
        /// Корутина для обновления прогресса воспроизведения.
        /// Работает постоянно, пока видео подготовлено.
        /// </summary>
        private IEnumerator UpdateProgressCoroutine()
        {
            while (isPrepared)
            {
                // Обновляем слайдер только если пользователь не перетаскивает его
                if (!isUserScrubbing && progressSlider != null)
                {
                    float currentTime = (float)videoPlayer.time;
                    // Всегда обновляем слайдер, чтобы он показывал текущее время
                    // Это важно даже когда видео на паузе, чтобы показать правильную позицию
                    progressSlider.SetValueWithoutNotify(currentTime);
                }
                
                // Обновляем UI элементы
                UpdateTimeLabel();
                UpdatePlayPauseButton();
                
                yield return new WaitForSeconds(0.1f); // Обновляем 10 раз в секунду
            }
            
            // Корутина завершена, очищаем ссылку
            updateCoroutine = null;
            Debug.Log($"{LogPrefix} Корутина обновления завершена");
        }
        
        /// <summary>
        /// Обновляет кнопку Play/Pause на основе фактического состояния VideoPlayer.
        /// </summary>
        private void UpdatePlayPauseButton()
        {
            if (playPauseButton == null || !isPrepared) return;
            
            // Меняем текст кнопки на основе фактического состояния воспроизведения
            playPauseButton.text = videoPlayer.isPlaying ? "||" : "▶";
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


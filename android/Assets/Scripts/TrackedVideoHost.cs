using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Хост для отображения видео в AR сцене. Управляет позицией, поворотом к камере и обработкой трекинга.
/// </summary>
public class TrackedVideoHost : MonoBehaviour
{
    private const string LogPrefix = "[TrackedVideoHost]";
    
    [Header("Placement")]
    [SerializeField] private Transform videoParent;
    [SerializeField] private float distance = 0.05f;
    [SerializeField] private float videoScale = 1f;
    
    [Header("Size Constraints")]
    [Tooltip("Максимальный размер видео относительно размера таргета (например, 1.0 = видео может быть равным размеру таргета)")]
    [SerializeField] private float maxSizeMultiplier = 1.0f;
    
    [Header("Rotation")]
    [SerializeField] private bool lookAtCamera = true;
    
    [Header("Persistence")]
    [SerializeField] private float fadeOutDelaySeconds = 1f;
    [SerializeField] private float fadeOutDurationSeconds = 0f;
    
    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private Color gizmoColor = Color.blue;
    [SerializeField] private float gizmoSize = 0.1f;
    
    private GameObject loadedVideo;
    private ARVideoPlayer videoPlayer;
    private GameObject placeholderVideo; // Плейсхолдер видео (первый дочерний объект)
    private float targetSize = 0.1f;
    private bool isTrackingActive = false; // Инициализируем как false, чтобы правильно отслеживать переход в true
    private bool isPinned = false;
    private Coroutine fadeCoroutine;
    private Camera mainCamera;
    
    public string CurrentArtifactId { get; private set; }
    public bool HasLoadedVideo => loadedVideo != null;
    public bool IsPinned => isPinned;
    
    private void Awake()
    {
        if (videoParent == null)
        {
            videoParent = transform;
        }
        
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            mainCamera = FindFirstObjectByType<Camera>();
        }
    }
    
    private void Start()
    {
        if (transform.childCount > 0)
        {
            placeholderVideo = transform.GetChild(0).gameObject;
        }
    }
    
    private void Update()
    {
        if (loadedVideo != null && lookAtCamera && mainCamera != null && isTrackingActive)
        {
            // Поворачиваем видео к камере, но не следуем за ней (поворот только при обновлении трекинга)
            // Поворот будет вызываться явно при появлении и восстановлении трекинга
        }
    }
    
    public Transform GetAttachmentRoot()
    {
        return videoParent != null ? videoParent : transform;
    }
    
    public bool HasLoadedArtifact(string artifactId)
    {
        return HasLoadedVideo && string.Equals(CurrentArtifactId, artifactId);
    }
    
    /// <summary>
    /// Устанавливает размер таргета для ограничения размера видео.
    /// </summary>
    public void SetTargetSize(float size)
    {
        targetSize = Mathf.Max(0.01f, size);
    }
    
    /// <summary>
    /// Прикрепляет видео к хосту.
    /// </summary>
    /// <param name="videoInstance">Экземпляр GameObject с ARVideoPlayer</param>
    /// <param name="artifactId">ID артефакта</param>
    /// <param name="videoUrl">URL или путь к видео</param>
    /// <param name="isYouTube">Является ли видео YouTube</param>
    /// <param name="videoMetadata">Метаданные видео из БД (опционально)</param>
    /// <param name="onError">Колбэк при ошибке</param>
    public void AttachVideo(GameObject videoInstance, string artifactId, string videoUrl, bool isYouTube = false, VideoMetadata videoMetadata = null, Action<string> onError = null)
    {
        if (videoInstance == null)
        {
            Debug.LogWarning($"{LogPrefix} AttachVideo: videoInstance == null");
            return;
        }
        
        // Если видео уже загружено с таким же artifactId, не делаем ничего
        if (HasLoadedArtifact(artifactId))
        {
            DestroyObject(videoInstance);
            return;
        }
        
        if (loadedVideo != null)
        {
            DestroyObject(loadedVideo);
            loadedVideo = null;
            videoPlayer = null;
        }
        
        loadedVideo = videoInstance;
        loadedVideo.transform.SetParent(GetAttachmentRoot(), false);
        
        if (!loadedVideo.activeSelf)
        {
            loadedVideo.SetActive(true);
        }
        
        videoPlayer = loadedVideo.GetComponent<ARVideoPlayer>();
        
        if (videoPlayer == null)
        {
            Debug.LogError($"{LogPrefix} ARVideoPlayer компонент не найден на {videoInstance.name}");
            return;
        }
        
        // Ensure MeshRenderer is enabled
        var meshRenderer = loadedVideo.GetComponent<MeshRenderer>();
        if (meshRenderer != null && !meshRenderer.enabled)
        {
            Debug.LogWarning($"{LogPrefix} [AttachVideo] MeshRenderer disabled on {loadedVideo.name}, enabling");
            meshRenderer.enabled = true;
        }
        
        CurrentArtifactId = artifactId;
        
        // Настраиваем размер и позицию
        AlignVideo();
        
        // Загружаем и воспроизводим видео
        if (isYouTube)
        {
            // YouTube видео не может быть воспроизведено через VideoPlayer напрямую
            // Создаем простой UI элемент с кнопкой для открытия в браузере
            Debug.LogWarning($"{LogPrefix} YouTube видео не поддерживается через VideoPlayer. Создаем альтернативный UI.");
            CreateYouTubePlaceholder(videoUrl);
            // Удаляем плейсхолдер после создания YouTube placeholder
            RemovePlaceholder();
        }
        else
        {
            // КРИТИЧНО: Активируем объекты перед загрузкой видео (как в TrackedModelHost)
            // Активируем хост, если он неактивен
            if (!gameObject.activeSelf)
            {
                Debug.Log($"{LogPrefix} [AttachVideo] Host object is inactive, activating before video load");
                gameObject.SetActive(true);
            }
            
            // Активируем видео объект, если он неактивен
            if (loadedVideo != null && !loadedVideo.activeSelf)
            {
                Debug.Log($"{LogPrefix} [AttachVideo] Video object {loadedVideo.name} is inactive, activating");
                loadedVideo.SetActive(true);
            }
            
            videoPlayer.LoadVideoFromFile(videoUrl, videoMetadata,
                onSuccess: () =>
                {
                    Debug.Log($"{LogPrefix} Video loaded successfully, removing placeholder");
                    RemovePlaceholder();
                    // Обновляем выравнивание после загрузки видео (размеры теперь известны)
                    AlignVideo();
                    LookAtCamera();
                    videoPlayer.Play();
                },
                onError: error =>
                {
                    Debug.LogError($"{LogPrefix} Video loading error: {error}");
                    // Remove placeholder even on error to avoid overlap
                    RemovePlaceholder();
                    onError?.Invoke(error);
                });
        }
        
        SetupClickHandler();
    }
    
    private void SetupClickHandler()
    {
        if (videoPlayer != null && loadedVideo != null)
        {
            var collider = loadedVideo.GetComponent<Collider>();
            if (collider == null)
            {
                Debug.LogWarning($"{LogPrefix} Collider не найден на видео объекте");
            }
        }
    }
    
    public void LookAtCamera()
    {
        if (loadedVideo == null)
        {
            Debug.LogWarning($"{LogPrefix} [LookAtCamera] loadedVideo == null");
            return;
        }
        
        if (mainCamera == null)
        {
            Debug.LogWarning($"{LogPrefix} [LookAtCamera] mainCamera == null, trying to find camera");
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                mainCamera = FindFirstObjectByType<Camera>();
            }
            
            if (mainCamera == null)
            {
                Debug.LogError($"{LogPrefix} [LookAtCamera] Failed to find camera");
                return;
            }
        }
        
        Vector3 directionToCamera = (mainCamera.transform.position - loadedVideo.transform.position).normalized;
        loadedVideo.transform.rotation = Quaternion.LookRotation(directionToCamera);
    }
    
    private void AlignVideo()
    {
        if (loadedVideo == null)
        {
            Debug.LogWarning($"{LogPrefix} [AlignVideo] loadedVideo is null");
            return;
        }
        
        // Устанавливаем масштаб
        float scale = CalculateConstrainedScale();
        loadedVideo.transform.localScale = new Vector3(scale, scale, scale);
        
        // Получаем реальное соотношение сторон видео из ARVideoPlayer
        // Если видео еще не загружено, используем значение по умолчанию 16:9
        float baseVideoHeight = 0.5625f; // 16:9 aspect ratio по умолчанию
        bool usingActualDimensions = false;
        
        if (videoPlayer != null && videoPlayer.ActualVideoHeight > 0 && videoPlayer.ActualVideoWidth > 0)
        {
            // Используем реальные размеры из видеофайла
            baseVideoHeight = videoPlayer.VideoHeight; // Уже содержит правильное соотношение сторон
            usingActualDimensions = true;
            
            Debug.Log($"{LogPrefix} [AlignVideo] Using actual video dimensions: actual={videoPlayer.ActualVideoWidth}x{videoPlayer.ActualVideoHeight}, mesh={videoPlayer.VideoWidth}x{videoPlayer.VideoHeight}, aspect={videoPlayer.AspectRatio:F4}, scale={scale}");
        }
        else
        {
            Debug.Log($"{LogPrefix} [AlignVideo] Video not prepared yet (videoPlayer={videoPlayer != null}, width={videoPlayer?.ActualVideoWidth ?? 0}, height={videoPlayer?.ActualVideoHeight ?? 0}), using default 16:9 aspect ratio");
        }
        
        float videoHeightScaled = baseVideoHeight * videoScale * scale;
        float halfVideoHeight = videoHeightScaled * 0.5f;
        
        Debug.Log($"{LogPrefix} [AlignVideo] Calculated: baseVideoHeight={baseVideoHeight:F4}, videoScale={videoScale}, scale={scale}, videoHeightScaled={videoHeightScaled:F4}, halfHeight={halfVideoHeight:F4}");
        
        // Устанавливаем позицию: приподнимаем на distance + половину высоты видео
        Vector3 targetWorldPosition = transform.position;
        Vector3 desiredWorldPosition = new Vector3(
            targetWorldPosition.x,
            targetWorldPosition.y + distance + halfVideoHeight,
            targetWorldPosition.z
        );
        
        Vector3 localPosition = transform.InverseTransformPoint(desiredWorldPosition);
        loadedVideo.transform.localPosition = localPosition;
        
        Debug.Log($"{LogPrefix} [AlignVideo] Position set: local={localPosition}, world={desiredWorldPosition}, usingActual={usingActualDimensions}");
    }
    
    private float CalculateConstrainedScale()
    {
        // Получаем реальное соотношение сторон видео из ARVideoPlayer
        // Если видео еще не загружено, используем значение по умолчанию 16:9
        float baseVideoHeight = 0.5625f; // 16:9 aspect ratio по умолчанию
        if (videoPlayer != null && videoPlayer.ActualVideoHeight > 0 && videoPlayer.ActualVideoWidth > 0)
        {
            baseVideoHeight = videoPlayer.VideoHeight; // Уже содержит правильное соотношение сторон
            Debug.Log($"{LogPrefix} [CalculateConstrainedScale] Using actual video height: {baseVideoHeight:F4} (from {videoPlayer.ActualVideoWidth}x{videoPlayer.ActualVideoHeight})");
        }
        else
        {
            Debug.Log($"{LogPrefix} [CalculateConstrainedScale] Using default video height: {baseVideoHeight:F4} (16:9)");
        }
        
        float scaledVideoHeight = baseVideoHeight * videoScale;
        float maxAllowedSize = targetSize * maxSizeMultiplier;
        
        if (scaledVideoHeight > maxAllowedSize)
        {
            float constrainedScale = videoScale * (maxAllowedSize / scaledVideoHeight);
            Debug.Log($"{LogPrefix} [CalculateConstrainedScale] Constraining scale: {videoScale} -> {constrainedScale} (scaledHeight={scaledVideoHeight:F4} > max={maxAllowedSize:F4})");
            return constrainedScale;
        }
        
        Debug.Log($"{LogPrefix} [CalculateConstrainedScale] No constraint needed: scale={videoScale} (scaledHeight={scaledVideoHeight:F4} <= max={maxAllowedSize:F4})");
        return videoScale;
    }
    
    public void ResetToPlaceholder()
    {
        CancelFadeRoutine();
        ClearLoadedVideo();
        CurrentArtifactId = null;
        isPinned = false;
        isTrackingActive = true;
        
        // Show model placeholder again if TrackedModelHost exists
        var modelHost = GetComponent<TrackedModelHost>();
        if (modelHost != null)
        {
            modelHost.ShowPlaceholder();
        }
        
        // If placeholder is alive (e.g., video wasn't loaded, but reset was called), activate it
        if (placeholderVideo != null)
        {
            placeholderVideo.SetActive(true);
        }
    }
    
    /// <summary>
    /// Removes video placeholder after successful loading (like for 3D models).
    /// Also hides model placeholder if TrackedModelHost exists on the same GameObject.
    /// </summary>
    private void RemovePlaceholder()
    {
        if (placeholderVideo != null)
        {
            Debug.Log($"{LogPrefix} Removing video placeholder: {placeholderVideo.name}");
            DestroyObject(placeholderVideo);
            placeholderVideo = null;
        }
        
        // Also hide model placeholder if TrackedModelHost exists on the same GameObject
        var modelHost = GetComponent<TrackedModelHost>();
        if (modelHost != null)
        {
            Debug.Log($"{LogPrefix} Hiding model placeholder on TrackedModelHost");
            modelHost.HidePlaceholder();
        }
    }
    
    public void SetTrackingActive(bool isActive)
    {
        bool oldState = isTrackingActive;
        bool isStateChanged = oldState != isActive;
        isTrackingActive = isActive;
        
        if (isStateChanged)
        {
        }
        
        if (isPinned)
        {
            EnsureVideoVisible();
            return;
        }
        
        // При восстановлении трекинга (переход из false в true) - поворачиваем к камере и продолжаем воспроизведение
        // Выполняем это ТОЛЬКО при изменении состояния с false на true
        if (isStateChanged && oldState == false && isActive == true)
        {
            if (videoPlayer != null && loadedVideo != null)
            {
                // Обновляем ссылку на камеру на случай, если она изменилась
                if (mainCamera == null)
                {
                    mainCamera = Camera.main;
                    if (mainCamera == null)
                    {
                        mainCamera = FindFirstObjectByType<Camera>();
                    }
                }
                
                LookAtCamera();
                videoPlayer.Play();
            }
            else
            {
                Debug.LogWarning($"{LogPrefix} Failed to resume video: videoPlayer={videoPlayer != null}, loadedVideo={loadedVideo != null}");
            }
        }
        
        if (isTrackingActive)
        {
            EnsureVideoVisible();
            CancelFadeRoutine();
        }
        else
        {
            // При потере трекинга - ставим на паузу (время автоматически сохраняется в Pause)
            if (videoPlayer != null && videoPlayer.IsPlaying)
            {
                videoPlayer.Pause();
            }
            
            StartFadeOutRoutine();
        }
    }
    
    public bool TogglePinned()
    {
        return SetPinned(!isPinned);
    }
    
    public bool SetPinned(bool shouldPin)
    {
        if (isPinned == shouldPin)
        {
            return isPinned;
        }
        
        isPinned = shouldPin;
        
        if (isPinned)
        {
            EnsureVideoVisible();
            CancelFadeRoutine();
        }
        else
        {
            if (!isTrackingActive)
            {
                StartFadeOutRoutine();
            }
        }
        
        return isPinned;
    }
    
    public void ClearLoadedVideo()
    {
        if (loadedVideo != null)
        {
            DestroyObject(loadedVideo);
            loadedVideo = null;
            videoPlayer = null;
        }
    }
    
    private new void DestroyObject(UnityEngine.Object obj)
    {
        if (obj == null)
        {
            return;
        }
        
        if (obj is GameObject go)
        {
            var videoPlayer = go.GetComponent<ARVideoPlayer>();
            if (videoPlayer != null)
            {
            }
        }
        
        if (Application.isPlaying)
        {
            DestroyImmediate(obj);
        }
        else
        {
            DestroyImmediate(obj);
        }
    }
    
    private void EnsureVideoVisible()
    {
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
        
        if (HasLoadedVideo && loadedVideo != null)
        {
            loadedVideo.SetActive(true);
        }
    }
    
    private void StartFadeOutRoutine()
    {
        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
        }
        
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
        
        fadeCoroutine = StartCoroutine(FadeOutCoroutine());
    }
    
    private IEnumerator FadeOutCoroutine()
    {
        if (fadeOutDelaySeconds > 0f)
        {
            yield return new WaitForSeconds(fadeOutDelaySeconds);
        }
        
        if (isTrackingActive || isPinned)
        {
            fadeCoroutine = null;
            yield break;
        }
        
        if (fadeOutDurationSeconds > 0f)
        {
            yield return new WaitForSeconds(fadeOutDurationSeconds);
        }
        
        gameObject.SetActive(false);
        fadeCoroutine = null;
    }
    
    private void CancelFadeRoutine()
    {
        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }
    }
    
    private void OnDestroy()
    {
        CancelFadeRoutine();
        ClearLoadedVideo();
    }
    
    private void CreateYouTubePlaceholder(string youtubeUrl)
    {
        // Для YouTube создаем простой placeholder с возможностью открыть в браузере
        // В будущем можно заменить на WebView компонент
        if (loadedVideo == null)
        {
            return;
        }
        
        // Создаем простой текст или иконку, указывающую на YouTube
        // При клике открываем в браузере
        Debug.Log($"{LogPrefix} YouTube видео будет открыто в браузере при клике: {youtubeUrl}");
        
        // Поворачиваем к камере
        LookAtCamera();
        
        // Сохраняем URL для открытия при клике
        // Можно добавить компонент для обработки кликов
    }
    
    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;
        Gizmos.color = gizmoColor;
        
        Vector3 targetPos = transform.position + transform.up * distance;
        Gizmos.DrawWireSphere(targetPos, gizmoSize);
    }
}


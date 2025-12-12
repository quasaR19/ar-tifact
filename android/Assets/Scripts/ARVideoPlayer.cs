using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Video;


/// <summary>
/// Компонент для воспроизведения видео в AR с поддержкой паузы по клику и сохранения времени при потере трекинга.
/// </summary>
public class ARVideoPlayer : MonoBehaviour
{
    private const string LogPrefix = "[ARVideoPlayer]";
    
    [Header("Video Settings")]
    [SerializeField] private float videoWidth = 1.0f;
    [SerializeField] private float videoHeight = 0.5625f; // 16:9 aspect ratio (будет обновлено из файла или метаданных)
    [SerializeField] private bool loopVideo = true;
    
    // Метаданные видео из БД (приоритет над VideoPlayer)
    private VideoMetadata videoMetadata = null;
    
    // Реальные размеры видео из файла или метаданных
    private uint actualVideoWidth = 0;
    private int actualVideoHeight = 0;
    
    private VideoPlayer videoPlayer;
    private RenderTexture renderTexture;
    private Material videoMaterial;
    private MeshRenderer meshRenderer;
    private BoxCollider boxCollider;
    
    private double savedPlaybackTime = 0.0;
    private bool isPaused = false;
    private bool isInitialized = false;
    private bool isDestroying = false;
    private Action<string> currentOnError; // Сохраняем ссылку на onError для вызова из OnVideoPrepared
    
    public bool IsPlaying => videoPlayer != null && videoPlayer.isPlaying;
    public bool IsPaused => isPaused;
    public double CurrentTime => videoPlayer != null ? videoPlayer.time : 0.0;
    public double Duration => videoPlayer != null ? videoPlayer.length : 0.0;
    
    // Публичные свойства для получения размеров видео
    public float VideoWidth => videoWidth;
    public float VideoHeight => videoHeight;
    public uint ActualVideoWidth => actualVideoWidth;
    public int ActualVideoHeight => actualVideoHeight;
    public float AspectRatio => videoHeight > 0 ? videoWidth / videoHeight : 16f / 9f;
    
    private void Awake()
    {
        SetupVideoPlayer();
        SetupRenderer();
        SetupCollider();
    }
    
    private void SetupVideoPlayer()
    {
        videoPlayer = gameObject.AddComponent<VideoPlayer>();
        videoPlayer.playOnAwake = false;
        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.skipOnDrop = true;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
        videoPlayer.SetDirectAudioVolume(0, 1.0f);
        videoPlayer.loopPointReached += OnVideoFinished;
        videoPlayer.errorReceived += OnVideoError;
        videoPlayer.prepareCompleted += OnVideoPrepared;
    }
    
    private void SetupRenderer()
    {
        // Try to find URP-compatible shader first, fallback to Built-in shader
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Texture");
        }
        if (shader == null)
        {
            // Last resort: try to find any unlit shader
            shader = Shader.Find("Unlit/Color");
        }
        
        if (shader == null)
        {
            Debug.LogError($"{LogPrefix} Failed to find suitable shader for video material!");
            return;
        }
        
        videoMaterial = new Material(shader);
        Debug.Log($"{LogPrefix} Created video material with shader: {shader.name}");
        
        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
        }
        
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            meshFilter = gameObject.AddComponent<MeshFilter>();
        }
        
        if (meshFilter.mesh == null || meshFilter.mesh.vertexCount == 0)
        {
            meshFilter.mesh = CreateQuadMesh();
        }
        
        meshRenderer.material = videoMaterial;
        Debug.Log($"{LogPrefix} MeshRenderer material set: {meshRenderer.material != null}, shader={meshRenderer.material?.shader?.name ?? "null"}, enabled={meshRenderer.enabled}");
    }
    
    private void EnsureRenderTexture()
    {
        // Если размеры видео уже известны, используем их для RenderTexture
        int rtWidth = 1920;
        int rtHeight = 1080;
        
        if (actualVideoWidth > 0 && actualVideoHeight > 0)
        {
            // Используем реальные размеры видео, но ограничиваем максимальным разрешением для производительности
            int maxDimension = 1920;
            float aspectRatio = (float)actualVideoWidth / (float)actualVideoHeight;
            
            // Определяем ориентацию для правильного масштабирования
            if (actualVideoWidth > actualVideoHeight)
            {
                // Горизонтальное видео: ограничиваем по ширине
                rtWidth = Mathf.Min((int)actualVideoWidth, maxDimension);
                rtHeight = Mathf.RoundToInt(rtWidth / aspectRatio);
            }
            else if (actualVideoHeight > actualVideoWidth)
            {
                // Вертикальное видео: ограничиваем по высоте
                rtHeight = Mathf.Min(actualVideoHeight, maxDimension);
                rtWidth = Mathf.RoundToInt(rtHeight * aspectRatio);
            }
            else
            {
                // Квадратное видео
                rtWidth = Mathf.Min((int)actualVideoWidth, maxDimension);
                rtHeight = rtWidth;
            }
            
            // Округляем до четных чисел (требование некоторых GPU)
            rtWidth = (rtWidth / 2) * 2;
            rtHeight = (rtHeight / 2) * 2;
            
            Debug.Log($"{LogPrefix} [EnsureRenderTexture] Calculated RenderTexture size: {rtWidth}x{rtHeight} from video {actualVideoWidth}x{actualVideoHeight} (aspect={aspectRatio:F4})");
        }
        
        if (renderTexture != null)
        {
            // Проверяем, нужно ли пересоздать RenderTexture с новыми размерами
            if (renderTexture.width != rtWidth || renderTexture.height != rtHeight)
            {
                Debug.Log($"{LogPrefix} RenderTexture size mismatch ({renderTexture.width}x{renderTexture.height} vs {rtWidth}x{rtHeight}), recreating...");
                renderTexture.Release();
                DestroyImmediate(renderTexture);
                renderTexture = null;
            }
            else if (!renderTexture.IsCreated())
            {
                Debug.LogWarning($"{LogPrefix} RenderTexture was not created, recreating...");
                renderTexture.Release();
                renderTexture = null;
            }
            else
            {
                return; // RenderTexture уже создан с правильными размерами
            }
        }
        
        if (isDestroying)
        {
            return;
        }
        
        renderTexture = new RenderTexture(rtWidth, rtHeight, 0, RenderTextureFormat.ARGB32);
        renderTexture.name = $"ARVideoRenderTexture_{GetInstanceID()}";
        renderTexture.useMipMap = false;
        renderTexture.autoGenerateMips = false;
        
        // Create RenderTexture and verify it was created successfully
        renderTexture.Create();
        if (!renderTexture.IsCreated())
        {
            Debug.LogError($"{LogPrefix} Failed to create RenderTexture!");
            return;
        }
        
        Debug.Log($"{LogPrefix} RenderTexture created: {renderTexture.width}x{renderTexture.height}, format={renderTexture.format}, created={renderTexture.IsCreated()}");
        
        if (videoPlayer != null)
        {
            videoPlayer.targetTexture = renderTexture;
            Debug.Log($"{LogPrefix} VideoPlayer.targetTexture set to RenderTexture");
        }
        
        if (videoMaterial != null)
        {
            videoMaterial.mainTexture = renderTexture;
            Debug.Log($"{LogPrefix} VideoMaterial.mainTexture set to RenderTexture");
        }
        else
        {
            Debug.LogWarning($"{LogPrefix} VideoMaterial is null, cannot set RenderTexture!");
        }
        
        if (meshRenderer != null)
        {
            if (meshRenderer.material != videoMaterial)
            {
                meshRenderer.material = videoMaterial;
                Debug.Log($"{LogPrefix} MeshRenderer.material set to VideoMaterial");
            }
            
            if (!meshRenderer.enabled)
            {
                meshRenderer.enabled = true;
                Debug.Log($"{LogPrefix} MeshRenderer enabled");
            }
        }
        else
        {
            Debug.LogWarning($"{LogPrefix} MeshRenderer is null!");
        }
        
        if (gameObject != null && !gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
    }
    
    private void SetupCollider()
    {
        boxCollider = GetComponent<BoxCollider>();
        if (boxCollider == null)
        {
            boxCollider = gameObject.AddComponent<BoxCollider>();
        }
        
        // Устанавливаем размер коллайдера по размеру видео
        boxCollider.size = new Vector3(videoWidth, videoHeight, 0.01f);
        
        // Настраиваем XR Interaction для обработки кликов в AR
        SetupXRInteraction();
    }
    
    /// <summary>
    /// Настраивает XR Interaction для обработки кликов/тапов в AR.
    /// </summary>
    private void SetupXRInteraction()
    {
        var interactable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
        if (interactable == null)
        {
            interactable = gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
            Debug.Log($"{LogPrefix} [SetupXRInteraction] XRSimpleInteractable component added to {gameObject.name}");
        }
        
        // Subscribe to selection event (click/tap)
        interactable.selectEntered.AddListener(_ => OnVideoClicked());
        Debug.Log($"{LogPrefix} [SetupXRInteraction] Click handler configured via XR Interaction");
    }
    
    /// <summary>
    /// Устанавливает метаданные видео из БД (используется как фоллбэк, если VideoPlayer не может определить размеры).
    /// </summary>
    public void SetVideoMetadata(VideoMetadata metadata)
    {
        videoMetadata = metadata;
        // Не устанавливаем размеры сразу - они будут использованы как фоллбэк в UpdateVideoDimensionsFromSource
        if (metadata != null && metadata.IsValid())
        {
            Debug.Log($"{LogPrefix} [SetVideoMetadata] Metadata stored for fallback: width={metadata.width}, height={metadata.height}, duration={metadata.duration}s");
        }
        else
        {
            Debug.LogWarning($"{LogPrefix} [SetVideoMetadata] Invalid metadata provided (metadata={metadata != null}, valid={metadata?.IsValid() ?? false}). Will use 256x256 fallback if VideoPlayer fails.");
        }
    }
    
    /// <summary>
    /// Обновляет размеры видео на основе реальных размеров из VideoPlayer или метаданных из БД.
    /// Приоритет: VideoPlayer > JSON метаданные > фоллбэк 256x256.
    /// </summary>
    private void UpdateVideoDimensionsFromSource(VideoPlayer source)
    {
        // Приоритет 1: Используем размеры из VideoPlayer (из самого файла)
        if (source.width > 0 && source.height > 0)
        {
            actualVideoWidth = source.width;
            actualVideoHeight = (int)source.height;
            
            float aspectRatio = (float)source.width / (float)source.height;
            videoWidth = 1.0f;
            videoHeight = 1.0f / aspectRatio;
            
            Debug.Log($"{LogPrefix} [UpdateVideoDimensionsFromSource] Using VideoPlayer dimensions from file: width={source.width}, height={source.height}, aspectRatio={aspectRatio:F4}, mesh={videoWidth}x{videoHeight}");
        }
        // Приоритет 2: Используем метаданные из БД (JSON), если VideoPlayer не смог определить размеры
        else if (videoMetadata != null && videoMetadata.IsValid())
        {
            actualVideoWidth = (uint)videoMetadata.width;
            actualVideoHeight = videoMetadata.height;
            
            float aspectRatio = videoMetadata.GetAspectRatio();
            videoWidth = 1.0f;
            videoHeight = 1.0f / aspectRatio;
            
            Debug.Log($"{LogPrefix} [UpdateVideoDimensionsFromSource] VideoPlayer failed, using metadata dimensions from JSON: width={videoMetadata.width}, height={videoMetadata.height}, duration={videoMetadata.duration}s, aspectRatio={aspectRatio:F4}, mesh={videoWidth}x{videoHeight}");
        }
        // Приоритет 3: Фоллбэк на 256x256, если и VideoPlayer, и JSON невалидны
        else
        {
            Debug.LogWarning($"{LogPrefix} [UpdateVideoDimensionsFromSource] VideoPlayer failed (width={source.width}, height={source.height}) and no valid metadata available (metadata={videoMetadata != null}, valid={videoMetadata?.IsValid() ?? false}). Using 256x256 fallback.");
            actualVideoWidth = 256;
            actualVideoHeight = 256;
            videoWidth = 1.0f;
            videoHeight = 1.0f;
            
            Debug.Log($"{LogPrefix} [UpdateVideoDimensionsFromSource] Using 256x256 fallback dimensions: mesh={videoWidth}x{videoHeight}");
        }
        
        // Обновляем меш и коллайдер
        UpdateQuadMesh();
        UpdateCollider();
    }
    
    /// <summary>
    /// Обновляет меш с текущими размерами видео.
    /// </summary>
    private void UpdateQuadMesh()
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.mesh == null)
        {
            Debug.LogWarning($"{LogPrefix} [UpdateQuadMesh] MeshFilter or mesh is null, creating new mesh");
            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = gameObject.AddComponent<MeshFilter>();
            }
            meshFilter.mesh = CreateQuadMesh();
            Debug.Log($"{LogPrefix} [UpdateQuadMesh] Created new mesh with dimensions: width={videoWidth}, height={videoHeight}");
            return;
        }
        
        Mesh mesh = meshFilter.mesh;
        
        // Проверяем текущие размеры меша для сравнения
        Bounds currentBounds = mesh.bounds;
        float currentWidth = currentBounds.size.x;
        float currentHeight = currentBounds.size.y;
        
        float halfWidth = videoWidth * 0.5f;
        float halfHeight = videoHeight * 0.5f;
        
        Vector3[] vertices = new Vector3[]
        {
            new Vector3(-halfWidth, -halfHeight, 0),
            new Vector3(halfWidth, -halfHeight, 0),
            new Vector3(halfWidth, halfHeight, 0),
            new Vector3(-halfWidth, halfHeight, 0)
        };
        
        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        // Проверяем, что меш обновился правильно
        Bounds newBounds = mesh.bounds;
        float newWidth = newBounds.size.x;
        float newHeight = newBounds.size.y;
        
        Debug.Log($"{LogPrefix} [UpdateQuadMesh] Mesh updated: old={currentWidth:F4}x{currentHeight:F4}, new={newWidth:F4}x{newHeight:F4}, target={videoWidth:F4}x{videoHeight:F4}");
        
        // Проверка на ошибки
        if (Mathf.Abs(newWidth - videoWidth) > 0.001f || Mathf.Abs(newHeight - videoHeight) > 0.001f)
        {
            Debug.LogError($"{LogPrefix} [UpdateQuadMesh] WARNING: Mesh size mismatch! Expected {videoWidth}x{videoHeight}, got {newWidth}x{newHeight}");
        }
    }
    
    /// <summary>
    /// Обновляет коллайдер с текущими размерами видео.
    /// </summary>
    private void UpdateCollider()
    {
        if (boxCollider == null)
        {
            boxCollider = GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                boxCollider = gameObject.AddComponent<BoxCollider>();
            }
        }
        
        boxCollider.size = new Vector3(videoWidth, videoHeight, 0.01f);
        Debug.Log($"{LogPrefix} [UpdateCollider] Collider updated with size: {boxCollider.size}");
    }
    
    private Mesh CreateQuadMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "VideoQuad";
        
        float halfWidth = videoWidth * 0.5f;
        float halfHeight = videoHeight * 0.5f;
        
        Vector3[] vertices = new Vector3[]
        {
            new Vector3(-halfWidth, -halfHeight, 0),
            new Vector3(halfWidth, -halfHeight, 0),
            new Vector3(halfWidth, halfHeight, 0),
            new Vector3(-halfWidth, halfHeight, 0)
        };
        
        Vector2[] uv = new Vector2[]
        {
            new Vector2(1, 0),  // Переворачиваем по X для исправления зеркального отражения
            new Vector2(0, 0),
            new Vector2(0, 1),
            new Vector2(1, 1)
        };
        
        int[] triangles = new int[]
        {
            0, 1, 2,
            0, 2, 3
        };
        
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        
        return mesh;
    }
    
    /// <summary>
    /// Loads and plays video from local file.
    /// </summary>
    /// <param name="localPath">Путь к локальному файлу видео</param>
    /// <param name="metadata">Метаданные видео из БД (опционально, приоритет над VideoPlayer)</param>
    /// <param name="onSuccess">Колбэк при успешной загрузке</param>
    /// <param name="onError">Колбэк при ошибке</param>
    public void LoadVideoFromFile(string localPath, VideoMetadata metadata = null, Action onSuccess = null, Action<string> onError = null)
    {
        if (isDestroying)
        {
            onError?.Invoke("Object is being destroyed");
            return;
        }
        
        if (string.IsNullOrEmpty(localPath))
        {
            onError?.Invoke("Video file path is empty");
            return;
        }
        
        if (!System.IO.File.Exists(localPath))
        {
            onError?.Invoke($"File not found: {localPath}");
            return;
        }
        
        // Check file size and integrity before loading
        long fileSizeBytes = 0;
        try
        {
            var fileInfo = new System.IO.FileInfo(localPath);
            fileSizeBytes = fileInfo.Length;
            long fileSizeMB = fileSizeBytes / (1024 * 1024);
            
            if (fileSizeBytes == 0)
            {
                onError?.Invoke($"Video file is empty (0 bytes): {localPath}");
                return;
            }
            
            // Проверяем, что файл не слишком маленький (минимум 1KB для видео)
            if (fileSizeBytes < 1024)
            {
                onError?.Invoke($"Video file is too small ({fileSizeBytes} bytes), file may be corrupted or incomplete: {localPath}");
                return;
            }
            
            // Проверяем доступность файла для чтения
            try
            {
                using (var fileStream = System.IO.File.OpenRead(localPath))
                {
                    // Пытаемся прочитать первые байты для проверки доступности
                    byte[] buffer = new byte[Math.Min(1024, (int)fileSizeBytes)];
                    int bytesRead = fileStream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        onError?.Invoke($"Video file is not readable: {localPath}");
                        return;
                    }
                }
            }
            catch (Exception readEx)
            {
                onError?.Invoke($"Video file is locked or inaccessible: {readEx.Message}. Path: {localPath}");
                return;
            }
            
            if (fileSizeMB > 50)
            {
                Debug.LogWarning($"{LogPrefix} Large video file ({fileSizeMB} MB), loading may take longer");
            }
            
            Debug.Log($"{LogPrefix} Video file info: path={localPath}, size={fileSizeMB} MB ({fileSizeBytes} bytes), accessible=True");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{LogPrefix} Failed to get file info: {e.Message}");
            onError?.Invoke($"Failed to read file info: {e.Message}");
            return;
        }
        
        // Устанавливаем метаданные, если они переданы
        if (metadata != null)
        {
            SetVideoMetadata(metadata);
        }
        
        // Проверяем стабильность файла перед использованием (файл может еще загружаться)
        var mediaService = ARArtifact.Services.ArtifactMediaService.Instance;
        if (mediaService != null)
        {
            MonoBehaviour coroutineHost = null;
            
            // КРИТИЧНО: Выбираем активный хост для корутины (как в TrackedModelHost)
            // Приоритет: текущий объект (если активен) > VideoSceneManager > ArtifactMediaService
            if (gameObject.activeSelf && enabled)
            {
                coroutineHost = this;
            }
            else
            {
                // Пытаемся активировать объект
                if (!gameObject.activeSelf)
                {
                    gameObject.SetActive(true);
                    // Проверяем снова после активации
                    if (gameObject.activeSelf && enabled)
                    {
                        coroutineHost = this;
                    }
                }
                
                // Если объект все еще не может быть хостом, используем внешний хост
                if (coroutineHost == null)
                {
                    var videoSceneManager = ARArtifact.Services.VideoSceneManager.Instance;
                    if (videoSceneManager != null && videoSceneManager.gameObject.activeSelf && videoSceneManager.enabled)
                    {
                        coroutineHost = videoSceneManager;
                        Debug.Log($"{LogPrefix} [LoadVideoFromFile] Using VideoSceneManager as coroutine host (video object inactive)");
                    }
                    else if (mediaService != null && mediaService.gameObject.activeSelf && mediaService.enabled)
                    {
                        coroutineHost = mediaService;
                        Debug.Log($"{LogPrefix} [LoadVideoFromFile] Using ArtifactMediaService as coroutine host (video object inactive)");
                    }
                }
            }
            
            if (coroutineHost != null)
            {
                coroutineHost.StartCoroutine(WaitForFileStableAndLoad(localPath, metadata, onSuccess, onError));
                return;
            }
            else
            {
                Debug.LogWarning($"{LogPrefix} [LoadVideoFromFile] No active coroutine host available, will try direct load");
            }
        }
        
        // Если не удалось найти хост для корутины, активируем объект и продолжаем напрямую
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
        
        EnsureRenderTexture();
        
        // On Android, VideoPlayer requires file:/// prefix (three slashes) for local files
        string videoUrl;
        #if UNITY_ANDROID && !UNITY_EDITOR
        // Android VideoPlayer needs file:/// prefix (three slashes) for absolute paths
        if (!localPath.StartsWith("file://"))
        {
            // Normalize path: ensure it starts with / (absolute path)
            string normalizedPath = localPath.TrimStart('/');
            if (string.IsNullOrEmpty(normalizedPath))
            {
                onError?.Invoke($"Invalid video file path: {localPath}");
                return;
            }
            // Add leading slash for absolute path
            normalizedPath = "/" + normalizedPath;
            // Use file:/// (three slashes) for Android
            // Format: file:/// + /path/to/file = file:////path/to/file (WRONG!)
            // We need: file:///path/to/file (correct)
            // So we should NOT add leading slash if path already has it, or remove it before adding file:///
            videoUrl = "file://" + normalizedPath; // file:// + /path = file:///path (correct!)
        }
        else
        {
            // Already has file:// prefix, but may have too many slashes
            videoUrl = localPath;
            // Fix multiple slashes after file://: file://// -> file:///
            // Replace any sequence of 3+ slashes after file:// with single slash
            int filePrefixLength = "file://".Length;
            if (videoUrl.Length > filePrefixLength)
            {
                string prefix = videoUrl.Substring(0, filePrefixLength);
                string path = videoUrl.Substring(filePrefixLength);
                // Remove all leading slashes and add one
                path = path.TrimStart('/');
                if (!string.IsNullOrEmpty(path))
                {
                    path = "/" + path;
                }
                videoUrl = prefix + path;
            }
        }
        long fileSizeForLog = 0;
        try
        {
            var fileInfo = new System.IO.FileInfo(localPath);
            fileSizeForLog = fileInfo.Length;
        }
        catch { }
        Debug.Log($"{LogPrefix} Android video URL: {videoUrl} (original path: {localPath}, exists: {File.Exists(localPath)}, size: {fileSizeForLog} bytes)");
        #else
        string normalizedPath = System.IO.Path.GetFullPath(localPath).Replace('\\', '/');
        videoUrl = "file:///" + normalizedPath;
        #endif
        
        LoadVideoFromUrl(videoUrl, onSuccess, onError);
    }
    
    /// <summary>
    /// Ожидает стабильности файла перед загрузкой
    /// </summary>
    private System.Collections.IEnumerator WaitForFileStableAndLoad(string localPath, VideoMetadata metadata, Action onSuccess, Action<string> onError)
    {
        // Ждем стабильности файла (3 проверки подряд с одинаковым размером)
        float checkInterval = 0.2f;
        float maxWaitTime = 10f;
        float startTime = Time.time;
        long lastSize = 0;
        int stableCount = 0;
        const int requiredStableChecks = 3;
        
        while (Time.time - startTime < maxWaitTime)
        {
            try
            {
                if (!System.IO.File.Exists(localPath))
                {
                    onError?.Invoke($"File not found: {localPath}");
                    yield break;
                }
                
                var fileInfo = new System.IO.FileInfo(localPath);
                long currentSize = fileInfo.Length;
                
                if (currentSize == lastSize && currentSize > 0)
                {
                    stableCount++;
                    if (stableCount >= requiredStableChecks)
                    {
                        Debug.Log($"{LogPrefix} File is stable: {localPath}, size={currentSize} bytes");
                        break;
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
                Debug.LogWarning($"{LogPrefix} Error checking file stability: {e.Message}");
            }
            
            yield return new WaitForSeconds(checkInterval);
        }
        
        if (Time.time - startTime >= maxWaitTime)
        {
            Debug.LogWarning($"{LogPrefix} Timeout waiting for file stability: {localPath}");
        }
        
        // Теперь загружаем видео
        LoadVideoFromFileInternal(localPath, metadata, onSuccess, onError);
    }
    
    /// <summary>
    /// Внутренний метод загрузки видео из файла (без проверки стабильности)
    /// </summary>
    private void LoadVideoFromFileInternal(string localPath, VideoMetadata metadata = null, Action onSuccess = null, Action<string> onError = null)
    {
        // Устанавливаем метаданные, если они переданы
        if (metadata != null)
        {
            SetVideoMetadata(metadata);
        }
        
        // On Android, VideoPlayer requires file:/// prefix (three slashes) for local files
        string videoUrl;
        #if UNITY_ANDROID && !UNITY_EDITOR
        // Android VideoPlayer needs file:/// prefix (three slashes) for absolute paths
        if (!localPath.StartsWith("file://"))
        {
            // Normalize path: ensure it starts with / (absolute path)
            string normalizedPath = localPath.TrimStart('/');
            if (string.IsNullOrEmpty(normalizedPath))
            {
                onError?.Invoke($"Invalid video file path: {localPath}");
                return;
            }
            // Add leading slash for absolute path
            normalizedPath = "/" + normalizedPath;
            // Use file:/// (three slashes) for Android
            // Format: file:/// + /path/to/file = file:////path/to/file (WRONG!)
            // We need: file:///path/to/file (correct)
            // So we should NOT add leading slash if path already has it, or remove it before adding file:///
            videoUrl = "file://" + normalizedPath; // file:// + /path = file:///path (correct!)
        }
        else
        {
            // Already has file:// prefix, but may have too many slashes
            videoUrl = localPath;
            // Fix multiple slashes after file://: file://// -> file:///
            // Replace any sequence of 3+ slashes after file:// with single slash
            int filePrefixLength = "file://".Length;
            if (videoUrl.Length > filePrefixLength)
            {
                string prefix = videoUrl.Substring(0, filePrefixLength);
                string path = videoUrl.Substring(filePrefixLength);
                // Remove all leading slashes and add one
                path = path.TrimStart('/');
                if (!string.IsNullOrEmpty(path))
                {
                    path = "/" + path;
                }
                videoUrl = prefix + path;
            }
        }
        long fileSizeForLog = 0;
        try
        {
            var fileInfo = new System.IO.FileInfo(localPath);
            fileSizeForLog = fileInfo.Length;
        }
        catch { }
        Debug.Log($"{LogPrefix} Android video URL: {videoUrl} (original path: {localPath}, exists: {File.Exists(localPath)}, size: {fileSizeForLog} bytes)");
        #else
        string normalizedPath = System.IO.Path.GetFullPath(localPath).Replace('\\', '/');
        videoUrl = "file:///" + normalizedPath;
        #endif
        
        LoadVideoFromUrl(videoUrl, onSuccess, onError);
    }
    
    /// <summary>
    /// Перегрузка LoadVideoFromFile для обратной совместимости (без metadata).
    /// </summary>
    public void LoadVideoFromFile(string localPath, Action onSuccess, Action<string> onError)
    {
        LoadVideoFromFile(localPath, null, onSuccess, onError);
    }
    
    /// <summary>
    /// Loads and plays video from URL (for blob, NOT for YouTube).
    /// </summary>
    public void LoadVideoFromUrl(string url, Action onSuccess = null, Action<string> onError = null)
    {
        if (isDestroying)
        {
            onError?.Invoke("Object is being destroyed");
            return;
        }
        
        if (string.IsNullOrEmpty(url))
        {
            onError?.Invoke("Video URL is empty");
            return;
        }
        
        // Check if this is a YouTube URL
        if (url.Contains("youtube.com") || url.Contains("youtu.be"))
        {
            onError?.Invoke("YouTube videos are not supported through VideoPlayer. Use built-in browser or WebView.");
            return;
        }
        
        EnsureRenderTexture();
        
        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = url;
        videoPlayer.isLooping = loopVideo;
        
        currentOnError = onError;
        
        // УЛУЧШЕНО: Найти активный хост для корутины
        MonoBehaviour coroutineHost = null;
        
        // 1. Попытка использовать текущий объект
        if (gameObject.activeSelf && enabled)
        {
            coroutineHost = this;
        }
        // 2. Попытка использовать ArtifactMediaService
        else
        {
            var mediaService = ARArtifact.Services.ArtifactMediaService.Instance;
            if (mediaService != null && mediaService.gameObject.activeSelf && mediaService.enabled)
            {
                coroutineHost = mediaService;
            }
        }
        // 3. Попытка использовать VideoSceneManager
        if (coroutineHost == null)
        {
            var videoSceneManager = ARArtifact.Services.VideoSceneManager.Instance;
            if (videoSceneManager != null && videoSceneManager.gameObject.activeSelf && videoSceneManager.enabled)
            {
                coroutineHost = videoSceneManager;
            }
        }
        // 4. Последняя попытка: активировать текущий объект
        if (coroutineHost == null)
        {
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }
            if (!enabled)
            {
                enabled = true;
            }
            coroutineHost = this;
        }
        
        if (coroutineHost != null)
        {
            coroutineHost.StartCoroutine(PrepareVideoCoroutine(onSuccess, onError));
        }
        else
        {
            string error = "Не удалось найти активный хост для корутины загрузки видео";
            Debug.LogError($"{LogPrefix} {error}");
            onError?.Invoke(error);
        }
    }
    
    private IEnumerator PrepareVideoCoroutine(Action onSuccess, Action<string> onError)
    {
        videoPlayer.Prepare();
        
        // Increased timeout for large files (up to 60 seconds)
        float timeout = 60f;
        float elapsed = 0f;
        
        while (!videoPlayer.isPrepared && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        if (!videoPlayer.isPrepared)
        {
            string error = $"Video preparation timeout (>{timeout}s). URL: {videoPlayer.url}";
            Debug.LogError($"{LogPrefix} {error}");
            Debug.LogWarning($"{LogPrefix} Preparation timeout may indicate corrupted file. Check file: {videoPlayer.url}");
            
            // Call callback before cleanup
            if (currentOnError != null)
            {
                currentOnError.Invoke(error);
            }
            onError?.Invoke(error);
            currentOnError = null; // Clear reference after call
            yield break;
        }
        
        // Check video validity after preparation
        bool isValidVideo = videoPlayer.length > 0 && !double.IsNaN(videoPlayer.length) && !double.IsInfinity(videoPlayer.length);
        if (!isValidVideo)
        {
            string error = $"Video file has no length metadata (length={videoPlayer.length}). File may be corrupted or incomplete. URL: {videoPlayer.url}";
            Debug.LogError($"{LogPrefix} {error}");
            currentOnError = null; // Clear reference
            onError?.Invoke(error);
            yield break;
        }
        
        isInitialized = true;
        currentOnError = null;
        onSuccess?.Invoke();
    }
    
    private void OnVideoPrepared(VideoPlayer source)
    {
        bool isValidVideo = source.length > 0 && !double.IsNaN(source.length) && !double.IsInfinity(source.length);
        
        if (!isValidVideo)
        {
            Debug.LogError($"{LogPrefix} [OnVideoPrepared] Video prepared but has invalid duration: {source.length}s. File may be corrupted. URL: {source.url}");
            string errorMsg = $"Video file has no length metadata (length={source.length}). File may be corrupted or incomplete.";
            OnVideoError(source, errorMsg);
            if (currentOnError != null)
            {
                currentOnError.Invoke(errorMsg);
                currentOnError = null;
            }
            return;
        }
        
        currentOnError = null;
        
        // Получаем размеры видео из файла (VideoPlayer) или используем метаданные/фоллбэк
        UpdateVideoDimensionsFromSource(source);
        
        // Ensure RenderTexture is created and properly configured with correct dimensions
        EnsureRenderTexture();
        
        // Verify all components are properly set up
        if (renderTexture == null || !renderTexture.IsCreated())
        {
            Debug.LogError($"{LogPrefix} [OnVideoPrepared] RenderTexture is null or not created!");
            return;
        }
        
        if (videoMaterial == null)
        {
            Debug.LogError($"{LogPrefix} [OnVideoPrepared] VideoMaterial is null!");
            return;
        }
        
        if (meshRenderer == null)
        {
            Debug.LogError($"{LogPrefix} [OnVideoPrepared] MeshRenderer is null!");
            return;
        }
        
        // Ensure material is properly configured
        if (videoMaterial.mainTexture != renderTexture)
        {
            videoMaterial.mainTexture = renderTexture;
            Debug.Log($"{LogPrefix} [OnVideoPrepared] Set videoMaterial.mainTexture to RenderTexture");
        }
        
        if (meshRenderer.material != videoMaterial)
        {
            meshRenderer.material = videoMaterial;
            Debug.Log($"{LogPrefix} [OnVideoPrepared] Set meshRenderer.material to VideoMaterial");
        }
        
        if (videoPlayer != null && videoPlayer.targetTexture != renderTexture)
        {
            videoPlayer.targetTexture = renderTexture;
            Debug.Log($"{LogPrefix} [OnVideoPrepared] Set videoPlayer.targetTexture to RenderTexture");
        }
        
        if (gameObject != null && !gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
        
        if (meshRenderer != null && !meshRenderer.enabled)
        {
            meshRenderer.enabled = true;
        }
        
        Debug.Log($"{LogPrefix} [OnVideoPrepared] Video prepared successfully: length={source.length}s, dimensions={actualVideoWidth}x{actualVideoHeight}, aspectRatio={videoWidth}:{videoHeight}, renderTexture={renderTexture != null && renderTexture.IsCreated()}, material={videoMaterial != null}, renderer={meshRenderer != null && meshRenderer.enabled}");
    }
    
    private void OnVideoFinished(VideoPlayer source)
    {
        Debug.Log($"{LogPrefix} Video finished");
        if (!loopVideo)
        {
            isPaused = true;
        }
    }
    
    private void OnVideoError(VideoPlayer source, string message)
    {
        // Improve error message for better understanding
        string improvedMessage = message;
        
        // Check if error indicates corrupted file
        bool isCorruptedError = message.Contains("0xc00d36e6") || 
                               message.Contains("WindowsVideoMedia error") || 
                               message.Contains("Cannot read file") ||
                               message.Contains("Getting duration") ||
                               message.Contains("length=0") ||
                               message.Contains("length=NaN") ||
                               message.Contains("Resource not available");
        
        if (isCorruptedError)
        {
            Debug.LogError($"{LogPrefix} [OnVideoError] Corrupted video detected: {message}");
            Debug.LogError($"{LogPrefix} [OnVideoError] URL: {source.url}");
        }
        
        if (message.Contains("0xc00d36e6") || message.Contains("Cannot read file") || message.Contains("Resource not available"))
        {
            improvedMessage = $"Failed to read video file. File may be corrupted, inaccessible, or in unsupported format. URL: {source.url}. Original error: {message}";
            
            // Check file size and existence for diagnostics
            try
            {
                string filePath = source.url;
                // Remove file:/// prefix to get actual path
                if (filePath.StartsWith("file:///"))
                {
                    filePath = filePath.Substring(7); // Remove "file:///"
                }
                else if (filePath.StartsWith("file://"))
                {
                    filePath = filePath.Substring(7); // Remove "file://"
                }
                
                // Fix double slashes at the beginning
                filePath = filePath.TrimStart('/');
                if (!filePath.StartsWith("/"))
                {
                    filePath = "/" + filePath;
                }
                
                Debug.Log($"{LogPrefix} [OnVideoError] Checking file: {filePath}, exists: {File.Exists(filePath)}");
                
                if (File.Exists(filePath))
                {
                    var fileInfo = new FileInfo(filePath);
                    long fileSizeBytes = fileInfo.Length;
                    long fileSizeMB = fileSizeBytes / (1024 * 1024);
                    
                    improvedMessage += $" File size: {fileSizeMB} MB ({fileSizeBytes} bytes).";
                    
                    if (fileSizeBytes == 0)
                    {
                        improvedMessage += " File is empty (0 bytes) - download may have failed.";
                    }
                    else if (fileSizeMB > 50)
                    {
                        improvedMessage += " File is very large, may require more time to prepare.";
                    }
                }
                else
                {
                    improvedMessage += $" File does not exist at path: {filePath}";
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Failed to get file info: {e.Message}");
                improvedMessage += $" Could not check file info: {e.Message}";
            }
        }
        else if (message.Contains("WindowsVideoMedia error"))
        {
            improvedMessage = $"WindowsVideoMedia error reading video. File may be corrupted or in unsupported format. URL: {source.url}. Original error: {message}";
        }
        
        Debug.LogError($"{LogPrefix} Video playback error: {improvedMessage}");
        
        // Call onError callback if provided
        if (currentOnError != null)
        {
            currentOnError.Invoke(improvedMessage);
            currentOnError = null; // Clear reference after call
        }
    }
    
    /// <summary>
    /// Воспроизводит видео с сохраненного времени.
    /// </summary>
    public void Play()
    {
        if (videoPlayer == null || !isInitialized)
        {
            return;
        }
        
        if (savedPlaybackTime > 0.0)
        {
            videoPlayer.time = savedPlaybackTime;
            savedPlaybackTime = 0.0;
        }
        
        videoPlayer.Play();
        isPaused = false;
    }
    
    public void Pause()
    {
        if (videoPlayer == null || !videoPlayer.isPlaying)
        {
            return;
        }
        
        savedPlaybackTime = videoPlayer.time;
        videoPlayer.Pause();
        isPaused = true;
    }
    
    public void TogglePause()
    {
        if (isPaused || !videoPlayer.isPlaying)
        {
            Play();
        }
        else
        {
            Pause();
        }
    }
    
    public void StopAndReleaseFile()
    {
        if (videoPlayer == null)
        {
            return;
        }
        
        try
        {
            // 1. Остановить воспроизведение
            if (videoPlayer.isPlaying)
            {
                videoPlayer.Stop();
            }
            
            // 2. Отключить все события
            videoPlayer.prepareCompleted -= OnVideoPrepared;
            videoPlayer.loopPointReached -= OnVideoFinished;
            videoPlayer.errorReceived -= OnVideoError;
            
            // 3. Очистить URL и targetTexture
            videoPlayer.targetTexture = null;
            videoPlayer.url = null;
            videoPlayer.clip = null;
            
            // 4. Сбросить состояние
            isInitialized = false;
            isPaused = false;
            currentOnError = null;
            
            // 5. Освободить RenderTexture
            if (renderTexture != null && renderTexture.IsCreated())
            {
                renderTexture.Release();
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{LogPrefix} [StopAndReleaseFile] Error stopping VideoPlayer: {e.Message}");
        }
    }
    
    /// <summary>
    /// Принудительно освобождает файл с ожиданием полного освобождения
    /// </summary>
    public System.Collections.IEnumerator ForceReleaseFileCoroutine()
    {
        StopAndReleaseFile();
        
        // Дать время Windows освободить файл
        yield return new WaitForSeconds(0.5f);
        
        // Принудительная сборка мусора для освобождения файловых дескрипторов
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        
        yield return new WaitForSeconds(0.5f);
    }
    
    public void SavePlaybackTime()
    {
        if (videoPlayer != null && videoPlayer.isPlaying)
        {
            savedPlaybackTime = videoPlayer.time;
            videoPlayer.Pause();
            isPaused = true;
        }
    }
    
    public void RestorePlayback()
    {
        if (videoPlayer == null || !isInitialized)
        {
            return;
        }
        
        if (savedPlaybackTime > 0.0)
        {
            videoPlayer.time = savedPlaybackTime;
        }
        
        videoPlayer.Play();
        isPaused = false;
    }
    
    /// <summary>
    /// Обработка клика на видео (вызывается из TrackedVideoHost или через XR Interactable).
    /// </summary>
    public void OnVideoClicked()
    {
        TogglePause();
    }
    
    private void OnMouseDown()
    {
        OnVideoClicked();
    }
    
    private void OnDestroy()
    {
        isDestroying = true;
        
        if (videoPlayer != null)
        {
            try
            {
                videoPlayer.Stop();
                videoPlayer.targetTexture = null;
                videoPlayer.prepareCompleted -= OnVideoPrepared;
                videoPlayer.loopPointReached -= OnVideoFinished;
                videoPlayer.errorReceived -= OnVideoError;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"{LogPrefix} [OnDestroy] Error cleaning up VideoPlayer: {e.Message}");
            }
        }
        
        if (renderTexture != null)
        {
            try
            {
                if (renderTexture.IsCreated())
                {
                    renderTexture.Release();
                }
                DestroyImmediate(renderTexture);
                renderTexture = null;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"{LogPrefix} [OnDestroy] Error releasing RenderTexture: {e.Message}\n{e.StackTrace}");
            }
        }
        
        if (videoMaterial != null)
        {
            try
            {
                DestroyImmediate(videoMaterial);
                videoMaterial = null;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"{LogPrefix} [OnDestroy] Error destroying material: {e.Message}\n{e.StackTrace}");
            }
        }
    }
    
    private void OnDisable()
    {
        if (videoPlayer != null && videoPlayer.isPlaying)
        {
            SavePlaybackTime();
        }
    }
}



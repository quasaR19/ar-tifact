using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;
using ARArtifact.Services;
using ARArtifact.Storage;
using ARArtifact.UI.Common;

namespace ARArtifact.UI
{
    public class DetailsScreenController : BaseScreenController
    {
        [Header("UI References")]
        [SerializeField] private StyleSheet styleSheet;

        public StyleSheet StyleSheet
        {
            get => styleSheet;
            set => styleSheet = value;
        }

        private VisualElement previewImage;
        private Label descriptionLabel;
        private VisualElement mediaList;
        
        // Медиа-плееры
        private Dictionary<string, GameObject> mediaPlayerObjects = new Dictionary<string, GameObject>();
        private ArtifactStorage.ArtifactRecord currentRecord;
        private string activeGLBViewerId = null; // Храним активный GLB viewer
        private Texture2D currentPreviewTexture = null; // Храним текстуру превью для правильной очистки
        
        private void OnEnable()
        {
            if (_root != null) OnInitialize();
        }

        public override void Initialize(UIDocument uiDocument, string screenName = "Детали артефакта")
        {
            base.Initialize(uiDocument, screenName);
        }

        protected override void OnInitialize()
        {
            if (_uiDocument == null || _root == null) return;

            // Подключаем стили, если они назначены
            if (styleSheet != null)
            {
                if (!_root.styleSheets.Contains(styleSheet))
                {
                    _root.styleSheets.Add(styleSheet);
                }
            }
            else
            {
                // Пытаемся загрузить стили автоматически
                styleSheet = Resources.Load<StyleSheet>("UI/Views/DetailsScreen/DetailsScreen");
                if (styleSheet != null && !_root.styleSheets.Contains(styleSheet))
                {
                    _root.styleSheets.Add(styleSheet);
                }
            }

            // Header handled by Base
            
            // Настраиваем ScrollView для прокрутки
            var contentScroll = _root.Q<ScrollView>("content-scroll");
            if (contentScroll != null)
            {
                // Устанавливаем режим вертикальной прокрутки
                contentScroll.mode = ScrollViewMode.Vertical;
                // Включаем touch scrolling
                contentScroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
                // Включаем видимость вертикального скроллера
                contentScroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
                contentScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                // Альтернативный способ (для старых версий Unity)
                contentScroll.showVertical = true;
                contentScroll.showHorizontal = false;
            }
            
            previewImage = _root.Q<VisualElement>("preview-image");
            descriptionLabel = _root.Q<Label>("description-label");
            mediaList = _root.Q<VisualElement>("media-list");
            
            Hide();
        }

        public override void Show()
        {
            base.Show();
            if (_root != null)
            {
                // Убеждаемся, что DetailsScreen поверх других экранов (should be handled by NavigationManager order, but safe to keep)
                _root.BringToFront();
            }
        }
        
        public override void Hide()
        {
            base.Hide();
            CleanupMediaPlayers();
        }
        
        private void OnDisable()
        {
            CleanupMediaPlayers();
        }
        
        private void OnDestroy()
        {
            CleanupMediaPlayers();
        }
        
        /// <summary>
        /// Корутина для отложенного уничтожения текстуры после использования UI Toolkit
        /// </summary>
        private IEnumerator DestroyTextureAfterDelay(Texture2D texture, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (texture != null)
            {
                Destroy(texture);
            }
        }

        public void Display(ArtifactStorage.ArtifactRecord record)
        {
            if (_root == null) OnInitialize();
            if (record == null) return;

            // Очищаем предыдущих плееров
            CleanupMediaPlayers();
            currentRecord = record;

            SetTitle(record.name ?? "Без названия");
            if (descriptionLabel != null) descriptionLabel.text = record.description ?? "Нет описания";

            if (previewImage != null)
            {
                previewImage.style.backgroundImage = null;
                previewImage.Clear();
                
                // Освобождаем предыдущую текстуру превью
                if (currentPreviewTexture != null)
                {
                    Destroy(currentPreviewTexture);
                    currentPreviewTexture = null;
                }
                
                if (!string.IsNullOrEmpty(record.previewLocalPath) && File.Exists(record.previewLocalPath))
                {
                    try
                    {
                        byte[] data = File.ReadAllBytes(record.previewLocalPath);
                        var texture = new Texture2D(2, 2);
                        if (texture.LoadImage(data))
                        {
                            previewImage.style.backgroundImage = Background.FromTexture2D(texture);
                            // Используем Contain для ScaleToFit (показывает всё изображение без обрезки)
                            previewImage.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                            
                            // Сохраняем ссылку на текстуру для очистки при смене экрана
                            // НЕ уничтожаем текстуру сразу - UI Toolkit НЕ создает копию!
                            currentPreviewTexture = texture;
                        }
                        else
                        {
                            // Если загрузка не удалась, освобождаем текстуру сразу
                            Destroy(texture);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[DetailsScreen] Ошибка загрузки превью: {e.Message}");
                        previewImage.Add(new Label("Ошибка загрузки превью"));
                    }
                }
                else
                {
                     previewImage.Add(new Label("Нет превью"));
                }
            }

            if (mediaList != null)
            {
                mediaList.Clear();
                if (record.media != null)
                {
                    var sortedMedia = record.media.OrderBy(m => m.displayOrder).ToList();
                    
                    foreach (var media in sortedMedia)
                    {
                        mediaList.Add(CreateMediaItem(media));
                    }
                }
            }
        }

        private VisualElement CreateMediaItem(ArtifactStorage.MediaCacheRecord media)
        {
            var item = new VisualElement();
            item.AddToClassList("media-item");

            var header = new VisualElement();
            header.AddToClassList("media-header");

            var typeBadge = new Label(GetMediaTypeDisplayName(media.mediaType));
            typeBadge.AddToClassList("media-type-badge");
            
            var urlLabel = new Label(media.remoteUrl);
            urlLabel.AddToClassList("media-url");

            header.Add(typeBadge);
            header.Add(urlLabel);
            item.Add(header);

            // Создаем контейнер для медиа-плеера
            var playerContainer = new VisualElement();
            playerContainer.name = "player-container";
            playerContainer.AddToClassList("media-player-container");
            item.Add(playerContainer);

            // Создаем соответствующий плеер в зависимости от типа медиа
            string mediaType = media.mediaType?.ToLower() ?? "";
            
            if (mediaType == "3d_model" || mediaType == "glb" || mediaType == "gltf")
            {
                CreateGLBViewer(playerContainer, media);
            }
            else if (mediaType == "video" || mediaType == "mp4" || mediaType == "webm")
            {
                CreateVideoPlayer(playerContainer, media);
            }
            else if (mediaType == "youtube")
            {
                CreateYouTubePlayer(playerContainer, media);
                // Для YouTube не показываем метаданные
            }
            else
            {
                // Неизвестный тип медиа - показываем метаданные
                Debug.Log($"[DetailsScreen] Неизвестный тип медиа: {media.mediaType}");
            }

            // Показываем метаданные (если есть и это не YouTube)
            if (mediaType != "youtube" && !string.IsNullOrEmpty(media.metadataJson))
            {
                var metadataContainer = FormatMetadata(media.metadataJson);
                item.Add(metadataContainer);
            }

            return item;
        }
        
        private string GetMediaTypeDisplayName(string mediaType)
        {
            return mediaType?.ToLower() switch
            {
                "3d_model" or "glb" or "gltf" => "3D Модель",
                "video" or "mp4" or "webm" => "Видео",
                "youtube" => "YouTube",
                _ => mediaType ?? "Медиа"
            };
        }

        private VisualElement FormatMetadata(string json)
        {
            var container = new VisualElement();
            string trimmedJson = json.Trim();
            
            if (trimmedJson.StartsWith("{") && trimmedJson.IndexOf("{", 1) == -1) 
            {
                try 
                {
                    // Улучшенное регулярное выражение для парсинга JSON
                    // Обрабатывает строки в кавычках, числа, булевы значения (true/false), null
                    string content = json.Trim().TrimStart('{').TrimEnd('}');
                    // Паттерн: "ключ": значение (где значение может быть строкой, числом, true, false, null)
                    var matches = Regex.Matches(content, "\"([^\"]+)\"\\s*:\\s*(\"[^\"]*\"|true|false|null|\\d+\\.?\\d*)");
                    bool hasMatches = false;
                    
                    foreach (Match match in matches)
                    {
                        hasMatches = true;
                        string key = match.Groups[1].Value;
                        string value = match.Groups[2].Value.Trim('"');
                        
                        // Преобразуем булевы значения в читаемый формат
                        if (value == "true") value = "Да";
                        if (value == "false") value = "Нет";
                        if (value == "null") value = "null";

                        // Переводим ключи на русский для лучшей читаемости
                        string displayKey = TranslateKey(key);
                        var label = new Label($"{displayKey}: {value}");
                        label.AddToClassList("media-metadata");
                        container.Add(label);
                    }

                    if (!hasMatches)
                    {
                        var codeBlock = new Label(json);
                        codeBlock.AddToClassList("json-block");
                        container.Add(codeBlock);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DetailsScreen] Ошибка парсинга метаданных: {e.Message}, JSON: {json}");
                    var codeBlock = new Label(json);
                    codeBlock.AddToClassList("json-block");
                    container.Add(codeBlock);
                }
            }
            else
            {
                var codeBlock = new Label(json);
                codeBlock.AddToClassList("json-block");
                container.Add(codeBlock);
            }

            return container;
        }

        private string TranslateKey(string key)
        {
            // Переводим ключи метаданных на русский для отображения
            return key switch
            {
                "center_model" => "Центрировать модель",
                "centerModel" => "Центрировать модель",
                _ => key
            };
        }
        
        /// <summary>
        /// Создает GLB viewer для 3D модели.
        /// </summary>
        private void CreateGLBViewer(VisualElement container, ArtifactStorage.MediaCacheRecord media)
        {
            var viewerElement = CreateGLBViewerUI();
            container.Add(viewerElement);
            
            // Скрываем viewer по умолчанию (показываем только один за раз)
            viewerElement.style.display = DisplayStyle.None;
            
            // Создаем GameObject с контроллером
            var viewerGO = new GameObject($"GLBViewer_{media.mediaId}");
            viewerGO.transform.SetParent(transform);
            var viewer = viewerGO.AddComponent<GLBViewerController>();
            viewer.InitializeUI(viewerElement);
            
            mediaPlayerObjects[media.mediaId] = viewerGO;
            
            // Добавляем кнопку для активации этого viewer
            var activateButton = new Button { text = "Показать 3D модель" };
            activateButton.name = "activate-viewer-button";
            activateButton.AddToClassList("activate-viewer-button");
            activateButton.clicked += () =>
            {
                // Скрываем все другие GLB viewers
                foreach (var kvp in mediaPlayerObjects)
                {
                    if (kvp.Key != media.mediaId && kvp.Value != null)
                    {
                        var otherViewer = kvp.Value.GetComponent<GLBViewerController>();
                        if (otherViewer != null)
                        {
                            // Находим элемент через parent
                            var mediaItem = container.parent;
                            if (mediaItem != null)
                            {
                                var otherContainer = mediaItem.Q<VisualElement>("player-container");
                                if (otherContainer != null)
                                {
                                    var otherElement = otherContainer.Q<VisualElement>("glb-viewer");
                                    if (otherElement != null)
                                    {
                                        otherElement.style.display = DisplayStyle.None;
                                    }
                                    // Показываем кнопку активации для других
                                    var otherButton = otherContainer.Q<Button>("activate-viewer-button");
                                    if (otherButton != null)
                                    {
                                        otherButton.style.display = DisplayStyle.Flex;
                                    }
                                }
                            }
                        }
                    }
                }
                
                // Показываем этот viewer и скрываем кнопку
                viewerElement.style.display = DisplayStyle.Flex;
                activateButton.style.display = DisplayStyle.None;
                activeGLBViewerId = media.mediaId;
            };
            
            // Вставляем кнопку перед viewer
            container.Insert(container.IndexOf(viewerElement), activateButton);
            
            // Если это первая модель, показываем её автоматически
            if (activeGLBViewerId == null)
            {
                activeGLBViewerId = media.mediaId;
                viewerElement.style.display = DisplayStyle.Flex;
                activateButton.style.display = DisplayStyle.None;
            }
            
            // Загружаем модель
            if (!string.IsNullOrEmpty(media.localPath) && File.Exists(media.localPath))
            {
                viewer.LoadModel(media.localPath,
                    onSuccess: () => Debug.Log($"[DetailsScreen] GLB модель загружена: {media.mediaId}"),
                    onError: error => Debug.LogError($"[DetailsScreen] Ошибка загрузки GLB: {error}"));
            }
            else
            {
                // Скачиваем модель
                ShowLoadingState(viewerElement, "Загрузка 3D модели...");
                ArtifactMediaService.Instance.DownloadModel(
                    currentRecord.artifactId,
                    media.mediaId,
                    media.remoteUrl,
                    localPath =>
                    {
                        media.localPath = localPath;
                        viewer.LoadModel(localPath,
                            onSuccess: () => Debug.Log($"[DetailsScreen] GLB модель загружена: {media.mediaId}"),
                            onError: error => ShowErrorState(viewerElement, error));
                    },
                    error => ShowErrorState(viewerElement, error));
            }
        }
        
        /// <summary>
        /// Создает video player для видео.
        /// </summary>
        private void CreateVideoPlayer(VisualElement container, ArtifactStorage.MediaCacheRecord media)
        {
            var playerElement = CreateVideoPlayerUI();
            container.Add(playerElement);
            
            // Создаем GameObject с контроллером
            var playerGO = new GameObject($"VideoPlayer_{media.mediaId}");
            playerGO.transform.SetParent(transform);
            var player = playerGO.AddComponent<VideoPlayerController>();
            player.InitializeUI(playerElement);
            
            mediaPlayerObjects[media.mediaId] = playerGO;
            
            // Загружаем видео
            if (!string.IsNullOrEmpty(media.localPath) && File.Exists(media.localPath))
            {
                player.LoadVideo(media.localPath,
                    onSuccess: () => Debug.Log($"[DetailsScreen] Видео загружено: {media.mediaId}"),
                    onError: error => Debug.LogError($"[DetailsScreen] Ошибка загрузки видео: {error}"));
            }
            else
            {
                // Скачиваем видео
                ShowLoadingState(playerElement, "Загрузка видео...");
                ArtifactMediaService.Instance.DownloadVideo(
                    currentRecord.artifactId,
                    media.mediaId,
                    media.remoteUrl,
                    localPath =>
                    {
                        media.localPath = localPath;
                        player.LoadVideo(localPath,
                            onSuccess: () => Debug.Log($"[DetailsScreen] Видео загружено: {media.mediaId}"),
                            onError: error => ShowErrorState(playerElement, error));
                    },
                    error => ShowErrorState(playerElement, error));
            }
        }
        
        /// <summary>
        /// Создает YouTube player.
        /// </summary>
        private void CreateYouTubePlayer(VisualElement container, ArtifactStorage.MediaCacheRecord media)
        {
            var playerElement = CreateYouTubePlayerUI();
            container.Add(playerElement);
            
            // Создаем GameObject с контроллером
            var playerGO = new GameObject($"YouTubePlayer_{media.mediaId}");
            playerGO.transform.SetParent(transform);
            var player = playerGO.AddComponent<YouTubePlayerController>();
            player.InitializeUI(playerElement);
            
            mediaPlayerObjects[media.mediaId] = playerGO;
            
            // Загружаем YouTube видео
            player.LoadVideo(media.remoteUrl,
                onSuccess: () => Debug.Log($"[DetailsScreen] YouTube видео загружено: {media.mediaId}"),
                onError: error => Debug.LogError($"[DetailsScreen] Ошибка загрузки YouTube: {error}"));
        }
        
        /// <summary>
        /// Создает UI структуру для GLB viewer.
        /// </summary>
        private VisualElement CreateGLBViewerUI()
        {
            var viewer = new VisualElement();
            viewer.AddToClassList("glb-viewer");
            viewer.name = "glb-viewer";
            
            var renderTarget = new VisualElement();
            renderTarget.name = "render-target";
            renderTarget.AddToClassList("glb-render-target");
            viewer.Add(renderTarget);
            
            var controls = new VisualElement();
            controls.AddToClassList("glb-controls");
            
            var resetButton = new Button { text = "Сброс камеры" };
            resetButton.name = "reset-camera-button";
            resetButton.AddToClassList("control-button");
            controls.Add(resetButton);
            
            var centerButton = new Button { text = "Центрировать" };
            centerButton.name = "center-model-button";
            centerButton.AddToClassList("control-button");
            controls.Add(centerButton);
            
            viewer.Add(controls);
            
            var statusLabel = new Label();
            statusLabel.name = "status-label";
            statusLabel.AddToClassList("status-label");
            statusLabel.style.display = DisplayStyle.None;
            viewer.Add(statusLabel);
            
            return viewer;
        }
        
        /// <summary>
        /// Создает UI структуру для video player.
        /// </summary>
        private VisualElement CreateVideoPlayerUI()
        {
            var player = new VisualElement();
            player.AddToClassList("video-player");
            player.name = "video-player";
            
            var surface = new VisualElement();
            surface.name = "video-surface";
            surface.AddToClassList("video-surface");
            player.Add(surface);
            
            var controls = new VisualElement();
            controls.name = "video-controls";
            controls.AddToClassList("video-controls");
            
            var playPauseButton = new Button { text = "▶" };
            playPauseButton.name = "play-pause-button";
            playPauseButton.AddToClassList("control-button");
            controls.Add(playPauseButton);
            
            var progressSlider = new Slider();
            progressSlider.name = "progress-slider";
            progressSlider.AddToClassList("progress-slider");
            controls.Add(progressSlider);
            
            var timeLabel = new Label { text = "0:00 / 0:00" };
            timeLabel.name = "time-label";
            timeLabel.AddToClassList("time-label");
            controls.Add(timeLabel);
            
            player.Add(controls);
            
            var statusLabel = new Label();
            statusLabel.name = "status-label";
            statusLabel.AddToClassList("status-label");
            statusLabel.style.display = DisplayStyle.None;
            player.Add(statusLabel);
            
            return player;
        }
        
        /// <summary>
        /// Создает UI структуру для YouTube player.
        /// </summary>
        private VisualElement CreateYouTubePlayerUI()
        {
            var player = new VisualElement();
            player.AddToClassList("youtube-player");
            player.name = "youtube-player";
            
            var thumbnail = new VisualElement();
            thumbnail.name = "thumbnail";
            thumbnail.AddToClassList("youtube-thumbnail");
            player.Add(thumbnail);
            
            var playButton = new Button();
            playButton.name = "play-button";
            playButton.AddToClassList("youtube-play-button");
            var playIcon = new Label { text = "▶" };
            playIcon.AddToClassList("play-icon");
            // Выравнивание иконки по центру
            playIcon.style.alignSelf = Align.Center;
            playIcon.style.unityTextAlign = TextAnchor.MiddleCenter;
            playButton.Add(playIcon);
            player.Add(playButton);
            
            var title = new Label { text = "YouTube Video" };
            title.name = "youtube-title";
            title.AddToClassList("youtube-title");
            player.Add(title);
            
            var statusLabel = new Label();
            statusLabel.name = "status-label";
            statusLabel.AddToClassList("status-label");
            statusLabel.style.display = DisplayStyle.None;
            player.Add(statusLabel);
            
            return player;
        }
        
        /// <summary>
        /// Показывает состояние загрузки.
        /// </summary>
        private void ShowLoadingState(VisualElement container, string message)
        {
            var statusLabel = container.Q<Label>("status-label");
            if (statusLabel != null)
            {
                statusLabel.text = message;
                statusLabel.style.display = DisplayStyle.Flex;
            }
        }
        
        /// <summary>
        /// Показывает состояние ошибки.
        /// </summary>
        private void ShowErrorState(VisualElement container, string error)
        {
            var statusLabel = container.Q<Label>("status-label");
            if (statusLabel != null)
            {
                statusLabel.text = $"Ошибка: {error}";
                statusLabel.style.display = DisplayStyle.Flex;
                statusLabel.style.color = new StyleColor(new Color(1f, 0.3f, 0.3f));
            }
        }
        
        /// <summary>
        /// Очищает все медиа-плееры.
        /// </summary>
        private void CleanupMediaPlayers()
        {
            foreach (var kvp in mediaPlayerObjects)
            {
                if (kvp.Value != null)
                {
                    Destroy(kvp.Value);
                }
            }
            mediaPlayerObjects.Clear();
            activeGLBViewerId = null;
            
            // Очищаем текстуру превью
            if (currentPreviewTexture != null)
            {
                Destroy(currentPreviewTexture);
                currentPreviewTexture = null;
            }
        }
    }
}

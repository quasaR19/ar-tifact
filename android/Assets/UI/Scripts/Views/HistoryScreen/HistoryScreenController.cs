using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using ARArtifact.Services;
using ARArtifact.UI.Common;

namespace ARArtifact.UI
{
    /// <summary>
    /// Контроллер экрана истории сканированных артефактов.
    /// Отвечает за биндинг данных, состояние загрузки и обработку кнопок.
    /// </summary>
    public class HistoryScreenController : BaseScreenController
    {
        [Header("UI References")]
        [SerializeField] private StyleSheet styleSheet;

        public StyleSheet StyleSheet
        {
            get => styleSheet;
            set => styleSheet = value;
        }

        public event Action OnClearHistory;
        public event Action<string> OnItemClicked;
        public event Action<string> OnItemDeleted;

        private Button clearButton;
        private ScrollView historyScrollView;
        private VisualElement historyContainer;
        private VisualElement emptyState;
        private VisualElement loadingState;
        private bool initialized;

        private readonly Dictionary<string, Texture2D> previewCache = new();
        private readonly Dictionary<string, bool> downloadingPreviews = new(); // Отслеживание загружаемых превью
        private readonly Dictionary<string, VisualElement> previewElements = new(); // Связь targetId -> previewElement для обновления UI

        private void OnEnable()
        {
            if (!initialized && _root != null)
            {
                OnInitialize();
            }
        }

        private void OnDestroy()
        {
            ClearPreviewCache();
        }

        public override void Initialize(UIDocument uiDocument, string screenName = "История")
        {
            base.Initialize(uiDocument, screenName);
            initialized = true;
        }

        protected override void OnInitialize()
        {
            if (_uiDocument == null || _root == null)
            {
                return;
            }

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
                styleSheet = Resources.Load<StyleSheet>("UI/Views/HistoryScreen/HistoryScreen");
                if (styleSheet != null && !_root.styleSheets.Contains(styleSheet))
                {
                    _root.styleSheets.Add(styleSheet);
                }
            }

            // Header elements like title and close button are handled by BaseScreenController
            // _closeButton.clicked += ... is already hooked up to OnCloseClicked -> OnClose

            // Custom elements
            clearButton = _root.Q<Button>("clear-button");
            historyScrollView = _root.Q<ScrollView>("history-list");
            if (historyScrollView != null)
            {
                // Устанавливаем режим вертикальной прокрутки
                historyScrollView.mode = ScrollViewMode.Vertical;
                // Включаем touch scrolling
                historyScrollView.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
                // Включаем видимость вертикального скроллера
                historyScrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;
                historyScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            }
            historyContainer = _root.Q<VisualElement>("history-container");
            emptyState = _root.Q<VisualElement>("empty-state");
            loadingState = _root.Q<VisualElement>("loading-state");

            if (clearButton != null)
            {
                clearButton.clicked += () =>
                {
                    Debug.Log("[HistoryScreen] Нажата кнопка очистки истории");
                    OnClearHistory?.Invoke();
                };
            }
            else
            {
                Debug.LogWarning("[HistoryScreen] Кнопка очистки не найдена");
            }

            // Не скрываем экран при инициализации - он будет показан через Show()
            // Hide();
            ShowLoading(false);
            ShowEmptyState(true);
        }

        public void UpdateHistory(IReadOnlyList<ArtifactService.ArtifactHistoryItem> items)
        {
            if (!initialized || historyContainer == null)
            {
                 // Try to find container if not initialized yet (e.g. if called before Initialize)
                 if (_root != null) historyContainer = _root.Q<VisualElement>("history-container");
                 
                 if (historyContainer == null) {
                     Debug.LogWarning("[HistoryScreen] Попытка обновить историю до инициализации UI");
                     return;
                 }
            }

            historyContainer.Clear();
            // Очищаем только те previewElements, которые больше не в списке
            var currentTargetIds = new HashSet<string>();
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (!string.IsNullOrEmpty(item.TargetId))
                    {
                        currentTargetIds.Add(item.TargetId);
                    }
                }
            }
            
            // Удаляем ссылки на элементы, которых больше нет в истории
            var keysToRemove = new List<string>();
            foreach (var key in previewElements.Keys)
            {
                if (!currentTargetIds.Contains(key))
                {
                    keysToRemove.Add(key);
                }
            }
            foreach (var key in keysToRemove)
            {
                previewElements.Remove(key);
            }

            if (items == null || items.Count == 0)
            {
                Debug.Log("[HistoryScreen] История пуста, показываем empty-state");
                ShowEmptyState(true);
                return;
            }

            ShowEmptyState(false);

            foreach (var item in items)
            {
                historyContainer.Add(CreateHistoryItemElement(item));
            }

            Debug.Log($"[HistoryScreen] История обновлена, элементов: {items.Count}");
        }

        public void ShowLoading(bool isVisible)
        {
            if (loadingState == null) return;
            loadingState.EnableInClassList("visible", isVisible);
        }

        private void ShowEmptyState(bool isVisible)
        {
            if (emptyState == null) return;
            emptyState.EnableInClassList("visible", isVisible);
            if (historyScrollView != null)
            {
                historyScrollView.style.display = isVisible ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        private VisualElement CreateHistoryItemElement(ArtifactService.ArtifactHistoryItem item)
        {
            var itemElement = new VisualElement();
            itemElement.AddToClassList("history-item");

            var previewElement = new VisualElement();
            previewElement.AddToClassList("history-preview");

            // Сохраняем связь для последующего обновления UI
            if (!string.IsNullOrEmpty(item.TargetId))
            {
                previewElements[item.TargetId] = previewElement;
            }

            if (!string.IsNullOrEmpty(item.PreviewLocalPath))
            {
                var texture = TryGetPreviewTexture(item.PreviewLocalPath);
                if (texture != null)
                {
                    previewElement.style.backgroundImage = new StyleBackground(texture);
                }
                else
                {
                    previewElement.Add(new Label("Нет превью"));
                    // Если файл не найден, но есть URL - пытаемся загрузить
                    if (!string.IsNullOrEmpty(item.PreviewImageUrl))
                    {
                        TryDownloadPreview(item.TargetId, item.ArtifactId, item.PreviewImageUrl);
                    }
                }
            }
            else
            {
                previewElement.Add(new Label("Нет превью"));
                // Если нет локального файла, но есть URL - пытаемся загрузить
                if (!string.IsNullOrEmpty(item.PreviewImageUrl))
                {
                    TryDownloadPreview(item.TargetId, item.ArtifactId, item.PreviewImageUrl);
                }
            }

            var infoElement = new VisualElement();
            infoElement.AddToClassList("history-info");

            var nameLabel = new Label(string.IsNullOrEmpty(item.DisplayName) ? "Неизвестный артефакт" : item.DisplayName);
            nameLabel.AddToClassList("history-name");

            var metaText = $"Маркер: {(string.IsNullOrEmpty(item.TargetId) ? "—" : item.TargetId)} · Скан: {item.LastScannedAt.ToLocalTime():g}";
            var metaLabel = new Label(metaText);
            metaLabel.AddToClassList("history-meta");

            infoElement.Add(nameLabel);
            infoElement.Add(metaLabel);

            // Кнопка удаления
            var deleteButton = new Button();
            deleteButton.AddToClassList("history-item-delete");
            deleteButton.AddToClassList("icon-button");
            var deleteLabel = new Label("×");
            deleteLabel.AddToClassList("icon");
            deleteButton.Add(deleteLabel);
            
            // Используем RegisterCallback для предотвращения распространения события
            deleteButton.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation(); // Предотвращаем срабатывание клика на элементе
                Debug.Log($"[HistoryScreen] Нажата кнопка удаления для: {item.TargetId}");
                OnItemDeleted?.Invoke(item.TargetId);
            });

            itemElement.Add(previewElement);
            itemElement.Add(infoElement);
            itemElement.Add(deleteButton);

            itemElement.RegisterCallback<ClickEvent>(evt => 
            {
                Debug.Log($"[HistoryScreen] Нажат элемент истории: {item.TargetId}");
                OnItemClicked?.Invoke(item.TargetId);
            });

            return itemElement;
        }

        private Texture2D TryGetPreviewTexture(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            if (previewCache.TryGetValue(path, out var cachedTexture) && cachedTexture != null)
            {
                return cachedTexture;
            }

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[HistoryScreen] Локальный файл превью не найден: {path}");
                return null;
            }

            try
            {
                byte[] data = File.ReadAllBytes(path);
                var texture = new Texture2D(2, 2);
                if (!texture.LoadImage(data))
                {
                    Debug.LogWarning($"[HistoryScreen] Не удалось загрузить превью из файла: {path}");
                    Destroy(texture);
                    return null;
                }

                previewCache[path] = texture;
                return texture;
            }
            catch (Exception e)
            {
                Debug.LogError($"[HistoryScreen] Ошибка загрузки превью: {e.Message}");
                return null;
            }
        }

        private void ClearPreviewCache()
        {
            foreach (var texture in previewCache.Values)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }
            previewCache.Clear();
            downloadingPreviews.Clear();
            previewElements.Clear();
        }

        private void TryDownloadPreview(string targetId, string artifactId, string previewUrl)
        {
            if (string.IsNullOrEmpty(targetId) || string.IsNullOrEmpty(previewUrl))
            {
                return;
            }

            // Проверяем, не загружается ли уже это превью
            if (downloadingPreviews.ContainsKey(targetId))
            {
                return;
            }

            var mediaService = ArtifactMediaService.Instance;
            if (mediaService == null)
            {
                Debug.LogWarning($"[HistoryScreen] ArtifactMediaService недоступен для загрузки превью: {targetId}");
                return;
            }

            downloadingPreviews[targetId] = true;
            Debug.Log($"[HistoryScreen] Начинаем автозагрузку превью для {targetId} из {previewUrl}");

            mediaService.DownloadPreview(
                artifactId ?? targetId,
                previewUrl,
                localPath =>
                {
                    downloadingPreviews.Remove(targetId);
                    Debug.Log($"[HistoryScreen] Превью успешно загружено для {targetId}: {localPath}");
                    
                    // Обновляем UI элемента
                    UpdatePreviewElement(targetId, localPath);
                    
                    // Обновляем запись в ArtifactService, чтобы при следующем обновлении истории путь был доступен
                    if (ArtifactService.Instance != null)
                    {
                        StartCoroutine(UpdateArtifactRecordPreviewPath(artifactId, targetId, localPath));
                    }
                },
                error =>
                {
                    downloadingPreviews.Remove(targetId);
                    Debug.LogWarning($"[HistoryScreen] Не удалось загрузить превью для {targetId}: {error}");
                }
            );
        }

        private void UpdatePreviewElement(string targetId, string localPath)
        {
            if (!previewElements.TryGetValue(targetId, out var previewElement) || previewElement == null)
            {
                return;
            }

            var texture = TryGetPreviewTexture(localPath);
            if (texture != null)
            {
                // Удаляем label "Нет превью", если он есть
                previewElement.Clear();
                previewElement.style.backgroundImage = new StyleBackground(texture);
                Debug.Log($"[HistoryScreen] UI обновлен для {targetId}");
            }
        }

        private System.Collections.IEnumerator UpdateArtifactRecordPreviewPath(string artifactId, string targetId, string localPath)
        {
            // Ждем немного, чтобы убедиться, что файл записан на диск
            yield return new WaitForSeconds(0.1f);
            
            // Обновляем запись артефакта с новым путем к превью
            if (ArtifactService.Instance != null && File.Exists(localPath))
            {
                ArtifactService.Instance.UpdateArtifactPreviewPath(artifactId, targetId, localPath);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using ARArtifact.Services;

namespace ARArtifact.UI
{
    /// <summary>
    /// Контроллер экрана истории сканированных артефактов.
    /// Отвечает за биндинг данных, состояние загрузки и обработку кнопок.
    /// </summary>
    public class HistoryScreenController : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private StyleSheet styleSheet;

        public StyleSheet StyleSheet
        {
            get => styleSheet;
            set => styleSheet = value;
        }

        public event Action OnClose;
        public event Action OnClearHistory;

        private VisualElement root;
        private Button closeButton;
        private Button clearButton;
        private ScrollView historyScrollView;
        private VisualElement historyContainer;
        private VisualElement emptyState;
        private VisualElement loadingState;
        private bool initialized;

        private readonly Dictionary<string, Texture2D> previewCache = new();

        private void Awake()
        {
            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
                if (uiDocument == null)
                {
                    uiDocument = gameObject.AddComponent<UIDocument>();
                    Debug.LogWarning("[HistoryScreen] UIDocument не найден, создан новый компонент");
                }
            }
        }

        private void OnEnable()
        {
            if (!initialized)
            {
                InitializeUI();
            }
        }

        private void OnDestroy()
        {
            ClearPreviewCache();
        }

        /// <summary>
        /// Инициализирует визуальные элементы и обработчики событий.
        /// </summary>
        private void InitializeUI()
        {
            if (uiDocument == null)
            {
                Debug.LogError("[HistoryScreen] UIDocument не назначен, инициализация прервана");
                return;
            }

            root = uiDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogWarning("[HistoryScreen] rootVisualElement еще не доступен, повторная попытка в следующем кадре");
                // Отложим повторную инициализацию
                Invoke(nameof(InitializeUI), 0.1f);
                return;
            }

            if (styleSheet != null && !root.styleSheets.Contains(styleSheet))
            {
                root.styleSheets.Add(styleSheet);
            }

            closeButton = root.Q<Button>("close-button");
            clearButton = root.Q<Button>("clear-button");
            historyScrollView = root.Q<ScrollView>("history-list");
            historyContainer = root.Q<VisualElement>("history-container");
            emptyState = root.Q<VisualElement>("empty-state");
            loadingState = root.Q<VisualElement>("loading-state");

            if (closeButton != null)
            {
                closeButton.clicked += () =>
                {
                    Debug.Log("[HistoryScreen] Нажата кнопка закрытия");
                    OnClose?.Invoke();
                };
            }
            else
            {
                Debug.LogWarning("[HistoryScreen] Кнопка закрытия не найдена");
            }

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

            initialized = true;
            Hide();
            ShowLoading(false);
            ShowEmptyState(true);
        }

        /// <summary>
        /// Обновляет отображение истории.
        /// </summary>
        public void UpdateHistory(IReadOnlyList<ArtifactService.ArtifactHistoryItem> items)
        {
            if (!initialized || historyContainer == null)
            {
                Debug.LogWarning("[HistoryScreen] Попытка обновить историю до инициализации UI");
                return;
            }

            historyContainer.Clear();

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

        /// <summary>
        /// Показывает или скрывает индикатор загрузки.
        /// </summary>
        public void ShowLoading(bool isVisible)
        {
            if (loadingState == null) return;
            loadingState.EnableInClassList("visible", isVisible);
        }

        /// <summary>
        /// Показывает экран.
        /// </summary>
        public void Show()
        {
            if (root == null) return;
            root.style.display = DisplayStyle.Flex;
        }

        /// <summary>
        /// Скрывает экран.
        /// </summary>
        public void Hide()
        {
            if (root == null) return;
            root.style.display = DisplayStyle.None;
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
                }
            }
            else
            {
                previewElement.Add(new Label("Нет превью"));
            }

            var infoElement = new VisualElement();
            infoElement.AddToClassList("history-info");

            var nameLabel = new Label(string.IsNullOrEmpty(item.DisplayName) ? "Неизвестный артефакт" : item.DisplayName);
            nameLabel.AddToClassList("history-name");

            var metaText = $"Маркер: {(string.IsNullOrEmpty(item.TargetId) ? "—" : item.TargetId)} · Скан: {item.LastScannedAt.ToLocalTime():g}";
            var metaLabel = new Label(metaText);
            metaLabel.AddToClassList("history-meta");

            var statusLabel = new Label(item.StatusDescription ?? string.Empty);
            statusLabel.AddToClassList("history-status");
            switch (item.Status)
            {
                case ArtifactService.ArtifactHistoryStatus.Warning:
                    statusLabel.AddToClassList("warning");
                    break;
                case ArtifactService.ArtifactHistoryStatus.Error:
                    statusLabel.AddToClassList("error");
                    break;
            }

            infoElement.Add(nameLabel);
            infoElement.Add(metaLabel);
            infoElement.Add(statusLabel);

            itemElement.Add(previewElement);
            itemElement.Add(infoElement);

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
        }
    }
}


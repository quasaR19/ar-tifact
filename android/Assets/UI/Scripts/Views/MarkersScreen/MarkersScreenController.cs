using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace ARArtifact.UI
{
    /// <summary>
    /// Контроллер для экрана маркеров
    /// </summary>
    public class MarkersScreenController : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private StyleSheet styleSheet;
        
        public StyleSheet StyleSheet 
        { 
            get => styleSheet; 
            set => styleSheet = value; 
        }
        
        private VisualElement root;
        private VisualElement header;
        private Button closeButton;
        private Label lastUpdateTime;
        private Button refreshButton;
        private VisualElement loadingIndicator;
        private ScrollView markersList;
        private VisualElement markersContainer;
        private VisualElement emptyState;
        private readonly Dictionary<string, Texture2D> markerPreviewCache = new();
        
        public event Action OnClose;
        public event Action OnRefresh;
        
        private void Awake()
        {
            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
            }
            
            if (uiDocument == null)
            {
                uiDocument = gameObject.AddComponent<UIDocument>();
            }
        }
        
        private void OnEnable()
        {
            StartCoroutine(WaitForRootVisualElement());
        }

        private void OnDestroy()
        {
            ClearTextureCache();
        }
        
        private IEnumerator WaitForRootVisualElement()
        {
            while (uiDocument == null || uiDocument.rootVisualElement == null)
            {
                yield return null;
            }
            
            InitializeUI();
        }
        
        private void InitializeUI()
        {
            root = uiDocument.rootVisualElement;
            
            // Подключаем стили
            if (styleSheet != null && !root.styleSheets.Contains(styleSheet))
            {
                root.styleSheets.Add(styleSheet);
            }
            
            // Получаем элементы
            header = root.Q<VisualElement>("header");
            closeButton = root.Q<Button>("close-button");
            lastUpdateTime = root.Q<Label>("last-update-time");
            refreshButton = root.Q<Button>("refresh-button");
            loadingIndicator = root.Q<VisualElement>("loading-indicator");
            markersList = root.Q<ScrollView>("markers-list");
            markersContainer = root.Q<VisualElement>("markers-container");
            emptyState = root.Q<VisualElement>("empty-state");
            
            // Настраиваем обработчики
            if (closeButton != null)
            {
                closeButton.clicked += () => OnClose?.Invoke();
            }
            
            if (refreshButton != null)
            {
                refreshButton.clicked += () => OnRefresh?.Invoke();
            }
            
            // Скрываем по умолчанию (после инициализации root)
            if (root != null)
            {
                root.style.display = DisplayStyle.None;
            }
        }
        
        /// <summary>
        /// Обновляет список маркеров
        /// </summary>
        public void UpdateMarkers(List<Storage.MarkerStorage.MarkerData> markers)
        {
            if (markersContainer == null) return;
            
            markersContainer.Clear();
            
            if (markers == null || markers.Count == 0)
            {
                ShowEmptyState();
                return;
            }
            
            HideEmptyState();
            
            foreach (var marker in markers)
            {
                VisualElement markerItem = new VisualElement();
                markerItem.AddToClassList("marker-item");
                
                // Делаем элемент квадратным (aspect-ratio не поддерживается в UI Toolkit)
                // Используем RegisterCallback для установки height равным width после измерения
                markerItem.RegisterCallback<GeometryChangedEvent>(evt =>
                {
                    if (evt.newRect.width > 0)
                    {
                        markerItem.style.height = evt.newRect.width;
                    }
                });
                
                // Загружаем изображение маркера
                if (!string.IsNullOrEmpty(marker.localImagePath))
                {
                    Texture2D texture = TryGetMarkerTexture(marker.localImagePath);
                    if (texture != null)
                    {
                        markerItem.style.backgroundImage = new StyleBackground(texture);
                        markerItem.AddToClassList("marker-image");
                    }
                    else
                    {
                        markerItem.AddToClassList("marker-image-placeholder");
                        markerItem.Add(new Label("Нет изображения"));
                    }
                }
                else
                {
                    markerItem.AddToClassList("marker-image-placeholder");
                    markerItem.Add(new Label("Нет изображения"));
                }
                
                markersContainer.Add(markerItem);
            }
        }
        
        /// <summary>
        /// Обновляет дату последнего обновления
        /// </summary>
        public void UpdateLastUpdateTime(DateTime lastUpdate)
        {
            if (lastUpdateTime == null) return;
            
            if (lastUpdate == DateTime.MinValue)
            {
                lastUpdateTime.text = "Никогда";
            }
            else
            {
                // Конвертируем UTC в локальное время
                DateTime localTime = lastUpdate.ToLocalTime();
                lastUpdateTime.text = localTime.ToString("dd.MM.yyyy HH:mm:ss");
            }
        }
        
        /// <summary>
        /// Показывает индикатор загрузки
        /// </summary>
        public void ShowLoading(bool show)
        {
            if (loadingIndicator == null) return;
            
            if (show)
            {
                loadingIndicator.AddToClassList("visible");
            }
            else
            {
                loadingIndicator.RemoveFromClassList("visible");
            }
            
            if (refreshButton != null)
            {
                refreshButton.SetEnabled(!show);
            }
        }
        
        /// <summary>
        /// Показывает сообщение об отсутствии маркеров
        /// </summary>
        private void ShowEmptyState()
        {
            if (emptyState != null)
            {
                emptyState.AddToClassList("visible");
            }
            if (markersList != null)
            {
                markersList.style.display = DisplayStyle.None;
            }
        }
        
        /// <summary>
        /// Скрывает сообщение об отсутствии маркеров
        /// </summary>
        private void HideEmptyState()
        {
            if (emptyState != null)
            {
                emptyState.RemoveFromClassList("visible");
            }
            if (markersList != null)
            {
                markersList.style.display = DisplayStyle.Flex;
            }
        }
        
        /// <summary>
        /// Показывает экран
        /// </summary>
        public void Show()
        {
            if (root != null)
            {
                root.style.display = DisplayStyle.Flex;
            }
        }
        
        /// <summary>
        /// Скрывает экран
        /// </summary>
        public void Hide()
        {
            if (root != null)
            {
                root.style.display = DisplayStyle.None;
            }
        }

        private Texture2D TryGetMarkerTexture(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            if (markerPreviewCache.TryGetValue(path, out var cachedTexture) && cachedTexture != null)
            {
                return cachedTexture;
            }

            if (!System.IO.File.Exists(path))
            {
                Debug.LogWarning($"[MarkersScreen] Локальный файл маркера не найден: {path}");
                return null;
            }

            var service = Services.MarkerImageService.Instance;
            if (service == null)
            {
                Debug.LogWarning("[MarkersScreen] MarkerImageService недоступен");
                return null;
            }

            var texture = service.LoadLocalImage(path);
            if (texture != null)
            {
                markerPreviewCache[path] = texture;
            }
            else
            {
                Debug.LogWarning($"[MarkersScreen] Не удалось загрузить изображение маркера: {path}");
            }

            return texture;
        }

        private void ClearTextureCache()
        {
            foreach (var texture in markerPreviewCache.Values)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }

            markerPreviewCache.Clear();
        }
    }
}


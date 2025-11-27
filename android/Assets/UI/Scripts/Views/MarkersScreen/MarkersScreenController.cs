using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using ARArtifact.UI.Common;

namespace ARArtifact.UI
{
    /// <summary>
    /// Контроллер для экрана маркеров
    /// </summary>
    public class MarkersScreenController : BaseScreenController
    {
        [Header("UI References")]
        [SerializeField] private StyleSheet styleSheet;
        
        public StyleSheet StyleSheet 
        { 
            get => styleSheet; 
            set => styleSheet = value; 
        }
        
        private Label lastUpdateTime;
        private Button refreshButton;
        private VisualElement loadingIndicator;
        private ScrollView markersList;
        private VisualElement markersContainer;
        private VisualElement emptyState;
        private readonly Dictionary<string, Texture2D> markerPreviewCache = new();
        
        public event Action OnRefresh;
        
        private void OnEnable()
        {
            if (_root != null) OnInitialize();
        }

        private void OnDestroy()
        {
            ClearTextureCache();
        }
        
        public override void Initialize(UIDocument uiDocument, string screenName = "Маркеры")
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
                styleSheet = Resources.Load<StyleSheet>("UI/Views/MarkersScreen/MarkersScreen");
                if (styleSheet != null && !_root.styleSheets.Contains(styleSheet))
                {
                    _root.styleSheets.Add(styleSheet);
                }
            }
            
            // Получаем элементы
            // Header elements handled by BaseScreenController
            
            lastUpdateTime = _root.Q<Label>("last-update-time");
            refreshButton = _root.Q<Button>("refresh-button");
            loadingIndicator = _root.Q<VisualElement>("loading-indicator");
            markersList = _root.Q<ScrollView>("markers-list");
            if (markersList != null)
            {
                // Устанавливаем режим вертикальной прокрутки
                markersList.mode = ScrollViewMode.Vertical;
                // Включаем touch scrolling
                markersList.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
                // Включаем видимость вертикального скроллера
                markersList.verticalScrollerVisibility = ScrollerVisibility.Auto;
                markersList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                // Альтернативный способ (для старых версий Unity)
                markersList.showVertical = true;
                markersList.showHorizontal = false;
            }
            markersContainer = _root.Q<VisualElement>("markers-container");
            emptyState = _root.Q<VisualElement>("empty-state");
            
            // Настраиваем обработчики
            if (refreshButton != null)
            {
                refreshButton.clicked += () => OnRefresh?.Invoke();
            }
            
            // Не скрываем экран при инициализации - он будет показан через Show()
            // Hide();
        }
        
        /// <summary>
        /// Обновляет список маркеров
        /// </summary>
        public void UpdateMarkers(List<Storage.MarkerStorage.MarkerData> markers)
        {
            if (markersContainer == null) 
            {
                if (_root != null) markersContainer = _root.Q<VisualElement>("markers-container");
                if (markersContainer == null) return;
            }
            
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
                
                markerItem.RegisterCallback<GeometryChangedEvent>(evt =>
                {
                    if (evt.newRect.width > 0)
                    {
                        markerItem.style.height = evt.newRect.width;
                    }
                });

                // Проверяем, является ли маркер failed
                if (_failedMarkerIds.Contains(marker.id))
                {
                    markerItem.AddToClassList("failed");
                }
                
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
        
        public void UpdateLastUpdateTime(DateTime lastUpdate)
        {
            if (lastUpdateTime == null) return;
            
            if (lastUpdate == DateTime.MinValue)
            {
                lastUpdateTime.text = "Никогда";
            }
            else
            {
                DateTime localTime = lastUpdate.ToLocalTime();
                lastUpdateTime.text = localTime.ToString("dd.MM.yyyy HH:mm:ss");
            }
        }
        
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
        
        // Методы для установки данных из Manager
        public void SetMarkerTexture(string path, Texture2D texture)
        {
            if (string.IsNullOrEmpty(path) || texture == null) return;
            
            if (!markerPreviewCache.ContainsKey(path) || markerPreviewCache[path] == null)
            {
                markerPreviewCache[path] = texture;
            }
        }
        
        public void SetFailedMarkerIds(System.Collections.Generic.HashSet<string> failedIds)
        {
            // Сохраняем для использования при обновлении маркеров
            _failedMarkerIds = failedIds ?? new System.Collections.Generic.HashSet<string>();
        }
        
        private System.Collections.Generic.HashSet<string> _failedMarkerIds = new System.Collections.Generic.HashSet<string>();
        
        private Texture2D TryGetMarkerTexture(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            if (markerPreviewCache.TryGetValue(path, out var cachedTexture) && cachedTexture != null)
            {
                return cachedTexture;
            }

            return null; // Текстура должна быть загружена через Manager
        }

        private void ClearTextureCache()
        {
            foreach (var texture in markerPreviewCache.Values)
            {
                if (texture != null) Destroy(texture);
            }

            markerPreviewCache.Clear();
        }
    }
}

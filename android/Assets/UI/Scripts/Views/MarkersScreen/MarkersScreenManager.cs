using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using ARArtifact.UI.Common;

namespace ARArtifact.UI
{
    /// <summary>
    /// Менеджер для управления экраном маркеров
    /// Обрабатывает логику отображения и обновления маркеров
    /// </summary>
    public class MarkersScreenManager : MonoBehaviour
    {
        [Header("UI Configuration")]
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private VisualTreeAsset markersScreenUXML;
        [SerializeField] private StyleSheet markersScreenStyleSheet;

        private MarkersScreenController markersScreenController;
        
        private void Awake()
        {
            // НЕ выключаем gameObject - это ломает панель UIDocument!
            // Скрытие происходит через DisplayStyle.None в Hide()
            
            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
                if (uiDocument == null)
                {
                    uiDocument = gameObject.AddComponent<UIDocument>();
                }
            }
            
            if (markersScreenUXML == null) markersScreenUXML = Resources.Load<VisualTreeAsset>("UI/Views/MarkersScreen/MarkersScreen");
            if (markersScreenStyleSheet == null) markersScreenStyleSheet = Resources.Load<StyleSheet>("UI/Views/MarkersScreen/MarkersScreen");
            
            if (markersScreenUXML != null && uiDocument.visualTreeAsset == null)
            {
                uiDocument.visualTreeAsset = markersScreenUXML;
            }
            
            markersScreenController = uiDocument.GetComponent<MarkersScreenController>();
            if (markersScreenController == null)
            {
                markersScreenController = uiDocument.gameObject.AddComponent<MarkersScreenController>();
            }
            
            if (markersScreenController != null && markersScreenStyleSheet != null)
            {
                markersScreenController.StyleSheet = markersScreenStyleSheet;
            }
        }
        
        private void Start()
        {
            // Start вызывается только если gameObject активен
            // Но мы выключили его в Awake, поэтому Start не вызовется при первом запуске
            // Инициализацию делаем при первом Show()
            
            // Если gameObject активен (например, в редакторе), инициализируем сразу
            if (gameObject.activeSelf && !_isInitialized)
            {
                InitializeController();
            }
        }
        
        private void OnDestroy()
        {
            if (markersScreenController != null)
            {
                markersScreenController.OnClose -= OnCloseButtonClicked;
                markersScreenController.OnRefresh -= RefreshMarkers;
            }
            
            if (Services.MarkerService.Instance != null)
            {
                Services.MarkerService.Instance.OnMarkersUpdated -= OnMarkersUpdated;
                Services.MarkerService.Instance.OnUpdateStarted -= OnUpdateStarted;
                Services.MarkerService.Instance.OnUpdateCompleted -= OnUpdateCompleted;
            }
        }
        
        public void Show()
        {
            // НЕ трогаем gameObject.SetActive - это ломает панель UIDocument!
            // Все экраны остаются активными, скрытие/показ через DisplayStyle
            
            // Убеждаемся, что контроллер существует
            if (markersScreenController == null)
            {
                markersScreenController = uiDocument.GetComponent<MarkersScreenController>();
                if (markersScreenController == null)
                {
                    markersScreenController = uiDocument.gameObject.AddComponent<MarkersScreenController>();
                }
            }
            
            // Инициализируем контроллер при первом показе, если еще не инициализирован
            if (!_isInitialized)
            {
                InitializeController();
            }
            
            if (markersScreenController == null)
            {
                Debug.LogError("[MarkersScreen] markersScreenController is null after initialization!");
                return;
            }
            
            if (NavigationManager.Instance != null)
            {
                Debug.Log($"[MarkersScreen] Вызываем NavigationManager.NavigateTo для {markersScreenController.GetType().Name}");
                NavigationManager.Instance.NavigateTo(markersScreenController);
            }
            else
            {
                Debug.Log($"[MarkersScreen] NavigationManager.Instance is null, вызываем markersScreenController.Show() напрямую");
                markersScreenController.Show();
            }
            
            // Обновляем отображение после показа экрана
            RefreshDisplay();
        }
        
        private bool _isInitialized = false;
        
        private void InitializeController()
        {
            if (markersScreenController == null || _isInitialized) return;
            
            // НЕ трогаем gameObject.SetActive - это ломает панель UIDocument!
            
            // Убеждаемся, что uiDocument и root доступны
            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
            }
            
            if (uiDocument != null && uiDocument.rootVisualElement == null && markersScreenUXML != null)
            {
                uiDocument.visualTreeAsset = markersScreenUXML;
            }
            
            markersScreenController.Initialize(uiDocument, "Маркеры");
            markersScreenController.OnClose += OnCloseButtonClicked;
            markersScreenController.OnRefresh += RefreshMarkers;
            
            if (Services.MarkerService.Instance != null)
            {
                Services.MarkerService.Instance.OnMarkersUpdated += OnMarkersUpdated;
                Services.MarkerService.Instance.OnUpdateStarted += OnUpdateStarted;
                Services.MarkerService.Instance.OnUpdateCompleted += OnUpdateCompleted;
            }
            
            _isInitialized = true;
        }
        
        public void Hide()
        {
             if (NavigationManager.Instance != null)
            {
                NavigationManager.Instance.GoBack();
            }
            else
            {
                markersScreenController?.Hide();
            }
        }
        
        public MarkersScreenController GetController()
        {
            return markersScreenController;
        }
        
        private void OnCloseButtonClicked()
        {
            Hide();
        }
        
        private void RefreshDisplay()
        {
            if (Services.MarkerService.Instance == null || markersScreenController == null)
            {
                return;
            }
            
            var markers = Services.MarkerService.Instance.GetMarkers();
            var lastUpdate = Services.MarkerService.Instance.GetLastUpdateTime();
            
            // Загружаем текстуры для маркеров
            if (markers != null && Services.MarkerImageService.Instance != null)
            {
                foreach (var marker in markers)
                {
                    if (!string.IsNullOrEmpty(marker.localImagePath))
                    {
                        var texture = Services.MarkerImageService.Instance.LoadLocalImage(marker.localImagePath);
                        if (texture != null)
                        {
                            markersScreenController.SetMarkerTexture(marker.localImagePath, texture);
                        }
                    }
                }
            }
            
            // Обновляем список failed маркеров
            if (Services.DynamicReferenceLibrary.Instance != null)
            {
                markersScreenController.SetFailedMarkerIds(Services.DynamicReferenceLibrary.Instance.FailedMarkerIds);
            }
            
            markersScreenController.UpdateMarkers(markers);
            markersScreenController.UpdateLastUpdateTime(lastUpdate);
        }
        
        private void RefreshMarkers()
        {
            if (Services.MarkerService.Instance != null)
            {
                Services.MarkerService.Instance.LoadMarkersFromSupabase(true);
            }
        }
        
        private void OnMarkersUpdated(System.Collections.Generic.List<Storage.MarkerStorage.MarkerData> markers)
        {
            RefreshDisplay();
            
            // Обновляем список failed маркеров в контроллере
            if (markersScreenController != null && Services.DynamicReferenceLibrary.Instance != null)
            {
                markersScreenController.SetFailedMarkerIds(Services.DynamicReferenceLibrary.Instance.FailedMarkerIds);
                Services.DynamicReferenceLibrary.Instance.UpdateReferenceLibrary();
            }
        }
        
        private void OnUpdateStarted()
        {
            markersScreenController?.ShowLoading(true);
        }
        
        private void OnUpdateCompleted()
        {
            markersScreenController?.ShowLoading(false);
        }
        
        #if UNITY_EDITOR
        /// <summary>
        /// Перезагружает UI для горячей перезагрузки в Editor режиме
        /// </summary>
        public void ReloadUI()
        {
            if (uiDocument == null || markersScreenController == null) return;
            
            // Сохраняем текущее состояние
            bool wasVisible = markersScreenController.gameObject.activeSelf && 
                             (uiDocument.rootVisualElement?.style.display == DisplayStyle.Flex);
            
            // В Editor режиме используем AssetDatabase для перезагрузки
            markersScreenUXML = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/Resources/UI/Views/MarkersScreen/MarkersScreen.uxml");
            markersScreenStyleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/Resources/UI/Views/MarkersScreen/MarkersScreen.uss");
            
            // Fallback на Resources
            if (markersScreenUXML == null)
            {
                var oldUXML = markersScreenUXML;
                if (oldUXML != null) Resources.UnloadAsset(oldUXML);
                markersScreenUXML = Resources.Load<VisualTreeAsset>("UI/Views/MarkersScreen/MarkersScreen");
            }
            if (markersScreenStyleSheet == null)
            {
                var oldStyleSheet = markersScreenStyleSheet;
                if (oldStyleSheet != null) Resources.UnloadAsset(oldStyleSheet);
                markersScreenStyleSheet = Resources.Load<StyleSheet>("UI/Views/MarkersScreen/MarkersScreen");
            }
            
            // Пересоздаем дерево элементов
            uiDocument.visualTreeAsset = null;
            uiDocument.visualTreeAsset = markersScreenUXML;
            
            // Обновляем стили
            if (markersScreenStyleSheet != null)
            {
                markersScreenController.StyleSheet = markersScreenStyleSheet;
            }
            
            // Переинициализируем контроллер
            markersScreenController.Initialize(uiDocument, "Маркеры");
            markersScreenController.OnClose += OnCloseButtonClicked;
            markersScreenController.OnRefresh += RefreshMarkers;
            
            // Восстанавливаем видимость и данные на следующем кадре (после полной инициализации UI)
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (markersScreenController == null) return;
                
                // Сначала восстанавливаем данные (чтобы экран не был пустым при показе)
                RestoreStateFromService();
                
                // Затем показываем/скрываем экран
                if (wasVisible)
                {
                    markersScreenController.Show();
                    // Обновляем отображение еще раз после показа (на случай если данные изменились)
                    RefreshDisplay();
                }
                else
                {
                    markersScreenController.Hide();
                }
            };
            
            Debug.Log("[MarkersScreenManager] UI перезагружен");
        }
        #endif

        private void RestoreStateFromService()
        {
            if (markersScreenController == null) return;

            var markerService = Services.MarkerService.Instance;
            if (markerService == null) return;

            var markers = markerService.GetMarkers();
            if (markers != null)
            {
                // Создаем копию списка, чтобы избежать непредвиденных модификаций
                markersScreenController.UpdateMarkers(new System.Collections.Generic.List<Storage.MarkerStorage.MarkerData>(markers));
            }

            var lastUpdate = markerService.GetLastUpdateTime();
            markersScreenController.UpdateLastUpdateTime(lastUpdate);
        }
    }
}

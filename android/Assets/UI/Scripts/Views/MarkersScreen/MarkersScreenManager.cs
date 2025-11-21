using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

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
            // Получаем или создаем UIDocument
            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
                if (uiDocument == null)
                {
                    uiDocument = gameObject.AddComponent<UIDocument>();
                }
            }
            
            // Автоматически загружаем UXML если он не назначен
            if (markersScreenUXML == null)
            {
                markersScreenUXML = Resources.Load<VisualTreeAsset>("UI/Views/MarkersScreen/MarkersScreen");
                if (markersScreenUXML == null)
                {
                    // Пытаемся загрузить через AssetDatabase (только в редакторе)
                    #if UNITY_EDITOR
                    markersScreenUXML = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Views/MarkersScreen/MarkersScreen.uxml");
                    #endif
                }
            }
            
            // Автоматически загружаем StyleSheet если он не назначен
            if (markersScreenStyleSheet == null)
            {
                markersScreenStyleSheet = Resources.Load<StyleSheet>("UI/Views/MarkersScreen/MarkersScreen");
                if (markersScreenStyleSheet == null)
                {
                    // Пытаемся загрузить через AssetDatabase (только в редакторе)
                    #if UNITY_EDITOR
                    markersScreenStyleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/UI/Views/MarkersScreen/MarkersScreen.uss");
                    #endif
                }
            }
            
            // Загружаем UXML если он назначен
            if (markersScreenUXML != null && uiDocument.visualTreeAsset == null)
            {
                uiDocument.visualTreeAsset = markersScreenUXML;
            }
            
            // Добавляем контроллер
            markersScreenController = uiDocument.GetComponent<MarkersScreenController>();
            if (markersScreenController == null)
            {
                markersScreenController = uiDocument.gameObject.AddComponent<MarkersScreenController>();
            }
            
            // Передаем стили в контроллер
            if (markersScreenController != null && markersScreenStyleSheet != null)
            {
                markersScreenController.StyleSheet = markersScreenStyleSheet;
            }

        }
        
        private void OnEnable()
        {
            // Используем корутину для ожидания rootVisualElement
            StartCoroutine(WaitAndApplyStyles());
        }
        
        private System.Collections.IEnumerator WaitAndApplyStyles()
        {
            // Ждем, пока rootVisualElement станет доступен
            while (uiDocument == null || uiDocument.rootVisualElement == null)
            {
                yield return null;
            }
            
            // Подключаем стили после загрузки UXML
            if (markersScreenStyleSheet != null && uiDocument != null && uiDocument.rootVisualElement != null)
            {
                if (!uiDocument.rootVisualElement.styleSheets.Contains(markersScreenStyleSheet))
                {
                    uiDocument.rootVisualElement.styleSheets.Add(markersScreenStyleSheet);
                }
            }
        }
        
        private void Start()
        {
            // Подписываемся на события контроллера
            if (markersScreenController != null)
            {
                markersScreenController.OnClose += OnCloseButtonClicked;
                markersScreenController.OnRefresh += RefreshMarkers;
            }
            
            // Подписываемся на события MarkerService
            if (Services.MarkerService.Instance != null)
            {
                Services.MarkerService.Instance.OnMarkersUpdated += OnMarkersUpdated;
                Services.MarkerService.Instance.OnUpdateStarted += OnUpdateStarted;
                Services.MarkerService.Instance.OnUpdateCompleted += OnUpdateCompleted;
            }
            
            // Скрываем экран маркеров по умолчанию (он будет показан при выборе пункта меню)
            if (markersScreenController != null)
            {
                markersScreenController.Hide();
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
        
        /// <summary>
        /// Показывает экран маркеров
        /// </summary>
        public void Show()
        {
            if (markersScreenController != null)
            {
                markersScreenController.Show();
                RefreshDisplay();
            }
        }
        
        /// <summary>
        /// Скрывает экран маркеров
        /// </summary>
        public void Hide()
        {
            if (markersScreenController != null)
            {
                markersScreenController.Hide();
            }
        }
        
        /// <summary>
        /// Получает контроллер экрана маркеров
        /// </summary>
        public MarkersScreenController GetController()
        {
            return markersScreenController;
        }
        
        /// <summary>
        /// Обработчик закрытия экрана (также закрывает боковое меню)
        /// </summary>
        private void OnCloseButtonClicked()
        {
            Hide();
            
            // Закрываем боковое меню MainScreen
            var mainScreenManager = FindFirstObjectByType<MainScreenManager>();
            if (mainScreenManager != null && mainScreenManager.GetController() != null)
            {
                mainScreenManager.GetController().CloseMenu();
            }
        }
        
        /// <summary>
        /// Обновляет отображение маркеров
        /// </summary>
        private void RefreshDisplay()
        {
            if (Services.MarkerService.Instance == null || markersScreenController == null)
                return;
            
            var markers = Services.MarkerService.Instance.GetMarkers();
            var lastUpdate = Services.MarkerService.Instance.GetLastUpdateTime();
            
            markersScreenController.UpdateMarkers(markers);
            markersScreenController.UpdateLastUpdateTime(lastUpdate);
        }
        
        /// <summary>
        /// Обновляет маркеры из Supabase
        /// </summary>
        private void RefreshMarkers()
        {
            if (Services.MarkerService.Instance != null)
            {
                Services.MarkerService.Instance.LoadMarkersFromSupabase(true);
            }
        }
        
        /// <summary>
        /// Обработчик обновления маркеров
        /// </summary>
        private void OnMarkersUpdated(System.Collections.Generic.List<Storage.MarkerStorage.MarkerData> markers)
        {
            RefreshDisplay();
        }
        
        /// <summary>
        /// Обработчик начала обновления
        /// </summary>
        private void OnUpdateStarted()
        {
            if (markersScreenController != null)
            {
                markersScreenController.ShowLoading(true);
            }
        }
        
        /// <summary>
        /// Обработчик завершения обновления
        /// </summary>
        private void OnUpdateCompleted()
        {
            if (markersScreenController != null)
            {
                markersScreenController.ShowLoading(false);
            }
        }

    }
}


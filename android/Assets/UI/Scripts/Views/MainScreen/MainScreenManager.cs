using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace ARArtifact.UI
{
    /// <summary>
    /// Менеджер для управления главным экраном приложения
    /// Обрабатывает логику отображения хедера, меню и камеры
    /// </summary>
    public class MainScreenManager : MonoBehaviour
    {
        [Header("UI Configuration")]
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private VisualTreeAsset mainScreenUXML;
        [SerializeField] private StyleSheet mainScreenStyleSheet;

        
        [Header("Other Screens")]
        [SerializeField] private MarkersScreenManager markersScreenManager;
        [SerializeField] private HistoryScreenManager historyScreenManager;
        
        private MainScreenController mainScreenController;
        
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
            if (mainScreenUXML == null)
            {
                mainScreenUXML = Resources.Load<VisualTreeAsset>("UI/Views/MainScreen/MainScreen");
                if (mainScreenUXML == null)
                {
                    // Пытаемся загрузить через AssetDatabase (только в редакторе)
                    #if UNITY_EDITOR
                    mainScreenUXML = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Views/MainScreen/MainScreen.uxml");
                    #endif
                }
            }
            
            // Автоматически загружаем StyleSheet если он не назначен
            if (mainScreenStyleSheet == null)
            {
                mainScreenStyleSheet = Resources.Load<StyleSheet>("UI/Views/MainScreen/MainScreen");
                if (mainScreenStyleSheet == null)
                {
                    // Пытаемся загрузить через AssetDatabase (только в редакторе)
                    #if UNITY_EDITOR
                    mainScreenStyleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/UI/Views/MainScreen/MainScreen.uss");
                    #endif
                }
            }
            
            // Загружаем UXML если он назначен
            if (mainScreenUXML != null && uiDocument.visualTreeAsset == null)
            {
                uiDocument.visualTreeAsset = mainScreenUXML;
            }
            
            // Добавляем контроллер
            mainScreenController = uiDocument.GetComponent<MainScreenController>();
            if (mainScreenController == null)
            {
                mainScreenController = uiDocument.gameObject.AddComponent<MainScreenController>();
            }
            
            // Передаем стили в контроллер
            if (mainScreenController != null && mainScreenStyleSheet != null)
            {
                mainScreenController.StyleSheet = mainScreenStyleSheet;
            }
            
            // Настраиваем пункты меню
            SetupMenuItems();

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
            if (mainScreenStyleSheet != null && uiDocument != null && uiDocument.rootVisualElement != null)
            {
                if (!uiDocument.rootVisualElement.styleSheets.Contains(mainScreenStyleSheet))
                {
                    uiDocument.rootVisualElement.styleSheets.Add(mainScreenStyleSheet);
                }
            }
        }
        
        private void Start()
        {
            // Скрываем главный экран по умолчанию (он будет показан после загрузки)
            if (mainScreenController != null)
            {
                mainScreenController.Hide();
            }
            
            // Подключаем события распознавания таргетов
            ConnectTargetRecognitionEvents();
        }
        
        /// <summary>
        /// Подключает события распознавания таргетов к лог-окошку
        /// </summary>
        private void ConnectTargetRecognitionEvents()
        {
            if (mainScreenController == null)
            {
                return;
            }
            
            // Ищем TrackedArtifactManager в сцене
            TrackedArtifactManager trackedManager = FindFirstObjectByType<TrackedArtifactManager>();
            if (trackedManager != null)
            {
                // Подписываемся на события
                trackedManager.OnTargetRecognized += (targetId) =>
                {
                    if (mainScreenController != null)
                    {
                        mainScreenController.LogTargetRecognition($"Распознан таргет: {targetId}", targetId);
                    }
                };
                
                trackedManager.OnArtifactFound += (targetId, artifactName) =>
                {
                    if (mainScreenController != null)
                    {
                        mainScreenController.LogTargetRecognition($"Найдено: {artifactName}", targetId);
                    }
                };
                
                Debug.Log("[MainScreen] События распознавания таргетов подключены");
            }
            else
            {
                Debug.LogWarning("[MainScreen] TrackedArtifactManager не найден в сцене. Лог-окошко не будет обновляться.");
            }
        }
        
        /// <summary>
        /// Настраивает пункты меню
        /// </summary>
        private void SetupMenuItems()
        {
            if (mainScreenController == null) return;
            
            // Очищаем существующие пункты
            mainScreenController.ClearMenuItems();
            
            // Добавляем пункты меню
            mainScreenController.AddMenuItem("Маркеры", OnMarkersClicked);
            mainScreenController.AddMenuItem("История", OnHistoryClicked);
            mainScreenController.AddMenuItem("Настройки", OnSettingsClicked);
            mainScreenController.AddMenuItem("О приложении", OnAboutClicked);
            mainScreenController.AddMenuItem("Выход", OnExitClicked);
        }
        
        /// <summary>
        /// Обработчик клика на "Маркеры"
        /// </summary>
        private void OnMarkersClicked()
        {
            Debug.Log("[MainScreen] Маркеры");
            
            // Ищем MarkersScreenManager если он не назначен
            if (markersScreenManager == null)
            {
                markersScreenManager = FindFirstObjectByType<MarkersScreenManager>();
            }
            
            if (markersScreenManager != null)
            {
                markersScreenManager.Show();
            }
            else
            {
                Debug.LogWarning("[MainScreen] MarkersScreenManager не найден. Экран маркеров не будет отображен.");
            }
        }

        /// <summary>
        /// Обработчик клика на "История"
        /// </summary>
        private void OnHistoryClicked()
        {
            Debug.Log("[MainScreen] История");

            if (historyScreenManager == null)
            {
                historyScreenManager = FindFirstObjectByType<HistoryScreenManager>();
            }

            if (historyScreenManager != null)
            {
                historyScreenManager.Show();
            }
            else
            {
                Debug.LogWarning("[MainScreen] HistoryScreenManager не найден. Экран истории не будет отображен.");
            }
        }
        
        /// <summary>
        /// Обработчик клика на "Настройки"
        /// </summary>
        private void OnSettingsClicked()
        {
            Debug.Log("[MainScreen] Настройки");
            // TODO: Реализовать открытие экрана настроек
        }
        
        /// <summary>
        /// Обработчик клика на "О приложении"
        /// </summary>
        private void OnAboutClicked()
        {
            Debug.Log("[MainScreen] О приложении");
            // TODO: Реализовать открытие экрана "О приложении"
        }
        
        /// <summary>
        /// Обработчик клика на "Выход"
        /// </summary>
        private void OnExitClicked()
        {
            Debug.Log("[MainScreen] Выход");
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            #else
            Application.Quit();
            #endif
        }
        
        /// <summary>
        /// Показывает главный экран
        /// </summary>
        public void Show()
        {
            if (mainScreenController != null)
            {
                mainScreenController.Show();
            }
        }
        
        /// <summary>
        /// Скрывает главный экран
        /// </summary>
        public void Hide()
        {
            if (mainScreenController != null)
            {
                mainScreenController.Hide();
            }
        }
        
        /// <summary>
        /// Получает контроллер главного экрана
        /// </summary>
        public MainScreenController GetController()
        {
            return mainScreenController;
        }

    }
}


using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using ARArtifact.Services;
using ARArtifact.UI.Common;

namespace ARArtifact.UI
{
    /// <summary>
    /// Менеджер экрана истории. Загружает UXML/USS, управляет показом и подписывается на сервис.
    /// </summary>
    public class HistoryScreenManager : MonoBehaviour
    {
        [Header("UI Configuration")]
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private VisualTreeAsset historyScreenUXML;
        [SerializeField] private StyleSheet historyScreenStyleSheet;
        [SerializeField] private DetailsScreenManager detailsScreenManager;

        private HistoryScreenController historyScreenController;

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

            // Load Resources if needed (omitted for brevity, assuming assigned or handled in Controller via Base)
            // Actually, BaseScreenController doesn't load UXML, Manager usually does.
            if (historyScreenUXML == null) historyScreenUXML = Resources.Load<VisualTreeAsset>("UI/Views/HistoryScreen/HistoryScreen");
            if (historyScreenStyleSheet == null) historyScreenStyleSheet = Resources.Load<StyleSheet>("UI/Views/HistoryScreen/HistoryScreen");

            if (historyScreenUXML != null && uiDocument.visualTreeAsset == null)
            {
                uiDocument.visualTreeAsset = historyScreenUXML;
            }

            historyScreenController = uiDocument.GetComponent<HistoryScreenController>();
            if (historyScreenController == null)
            {
                historyScreenController = uiDocument.gameObject.AddComponent<HistoryScreenController>();
            }

            if (historyScreenStyleSheet != null)
            {
                historyScreenController.StyleSheet = historyScreenStyleSheet;
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
            if (historyScreenController != null)
            {
                historyScreenController.OnClose -= HandleCloseRequested;
                historyScreenController.OnClearHistory -= HandleClearHistory;
                historyScreenController.OnItemClicked -= HandleItemClicked;
            }

            if (ArtifactService.Instance != null)
            {
                ArtifactService.Instance.OnHistoryChanged -= HandleHistoryChanged;
                ArtifactService.Instance.OnHistoryLoading -= HandleHistoryLoading;
                ArtifactService.Instance.OnHistoryLoadingCompleted -= HandleHistoryLoadingCompleted;
            }
        }

        public void Show()
        {
            if (historyScreenController == null) return;
            
            // НЕ трогаем gameObject.SetActive - это ломает панель UIDocument!
            // Все экраны остаются активными, скрытие/показ через DisplayStyle
            
            // Инициализируем контроллер при первом показе, если еще не инициализирован
            if (!_isInitialized)
            {
                InitializeController();
            }
            
            // Use NavigationManager if available
            if (NavigationManager.Instance != null)
            {
                NavigationManager.Instance.NavigateTo(historyScreenController);
            }
            else
            {
                // Fallback
                historyScreenController.Show();
            }
            
            // Обновляем историю после показа экрана
            RefreshHistory();
        }
        
        private bool _isInitialized = false;
        
        private void InitializeController()
        {
            if (historyScreenController == null || _isInitialized) return;
            
            // НЕ трогаем gameObject.SetActive - это ломает панель UIDocument!
            
            // Убеждаемся, что uiDocument и root доступны
            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
            }
            
            if (uiDocument != null && uiDocument.rootVisualElement == null && historyScreenUXML != null)
            {
                uiDocument.visualTreeAsset = historyScreenUXML;
            }
            
            historyScreenController.Initialize(uiDocument, "История");
            historyScreenController.OnClose += HandleCloseRequested;
            historyScreenController.OnClearHistory += HandleClearHistory;
            historyScreenController.OnItemClicked += HandleItemClicked;
            
            if (ArtifactService.Instance != null)
            {
                ArtifactService.Instance.OnHistoryChanged += HandleHistoryChanged;
                ArtifactService.Instance.OnHistoryLoading += HandleHistoryLoading;
                ArtifactService.Instance.OnHistoryLoadingCompleted += HandleHistoryLoadingCompleted;
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
                historyScreenController?.Hide();
            }
        }

        private void RefreshHistory()
        {
            if (ArtifactService.Instance == null || historyScreenController == null)
            {
                return;
            }
            var historyItems = ArtifactService.Instance.GetHistoryItems();
            historyScreenController.UpdateHistory(historyItems);
        }

        private void HandleCloseRequested()
        {
            // NavigationManager handles hiding via OnClose event subscribed in NavigateTo
            // OnClose вызывает GoBack(), который скроет текущий экран и покажет предыдущий
            // Дополнительных действий не требуется
        }

        private void HandleClearHistory()
        {
            if (ArtifactService.Instance != null)
            {
                ArtifactService.Instance.ClearHistoryAndCache();
            }
        }

        private void HandleItemClicked(string targetId)
        {
            if (detailsScreenManager == null) detailsScreenManager = FindFirstObjectByType<DetailsScreenManager>();
            
            if (detailsScreenManager != null)
            {
                detailsScreenManager.Show(targetId);
            }
        }

        private void HandleHistoryChanged(System.Collections.Generic.IReadOnlyList<ArtifactService.ArtifactHistoryItem> historyItems)
        {
            historyScreenController?.UpdateHistory(historyItems);
        }

        private void HandleHistoryLoading()
        {
            historyScreenController?.ShowLoading(true);
        }

        private void HandleHistoryLoadingCompleted()
        {
            historyScreenController?.ShowLoading(false);
        }
        
        public HistoryScreenController GetController()
        {
            return historyScreenController;
        }
        
        #if UNITY_EDITOR
        /// <summary>
        /// Перезагружает UI для горячей перезагрузки в Editor режиме
        /// </summary>
        public void ReloadUI()
        {
            if (uiDocument == null || historyScreenController == null) return;
            
            // Сохраняем текущее состояние
            bool wasVisible = historyScreenController.gameObject.activeSelf && 
                             (uiDocument.rootVisualElement?.style.display == DisplayStyle.Flex);
            
            // В Editor режиме используем AssetDatabase для перезагрузки
            historyScreenUXML = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/Resources/UI/Views/HistoryScreen/HistoryScreen.uxml");
            historyScreenStyleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/Resources/UI/Views/HistoryScreen/HistoryScreen.uss");
            
            // Fallback на Resources
            if (historyScreenUXML == null)
            {
                var oldUXML = historyScreenUXML;
                if (oldUXML != null) Resources.UnloadAsset(oldUXML);
                historyScreenUXML = Resources.Load<VisualTreeAsset>("UI/Views/HistoryScreen/HistoryScreen");
            }
            if (historyScreenStyleSheet == null)
            {
                var oldStyleSheet = historyScreenStyleSheet;
                if (oldStyleSheet != null) Resources.UnloadAsset(oldStyleSheet);
                historyScreenStyleSheet = Resources.Load<StyleSheet>("UI/Views/HistoryScreen/HistoryScreen");
            }
            
            // Пересоздаем дерево элементов
            uiDocument.visualTreeAsset = null;
            uiDocument.visualTreeAsset = historyScreenUXML;
            
            // Обновляем стили
            if (historyScreenStyleSheet != null)
            {
                historyScreenController.StyleSheet = historyScreenStyleSheet;
            }
            
            // Переинициализируем контроллер
            historyScreenController.Initialize(uiDocument, "История");
            historyScreenController.OnClose += HandleCloseRequested;
            historyScreenController.OnClearHistory += HandleClearHistory;
            historyScreenController.OnItemClicked += HandleItemClicked;
            
            // Восстанавливаем видимость и данные
            if (wasVisible)
            {
                historyScreenController.Show();
                RefreshHistory();
            }
            else
            {
                historyScreenController.Hide();
            }
            
            Debug.Log("[HistoryScreenManager] UI перезагружен");
        }
        #endif
    }
}

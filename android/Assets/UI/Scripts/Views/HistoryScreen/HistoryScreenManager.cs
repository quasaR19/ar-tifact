using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using ARArtifact.Services;

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
        [SerializeField] private MainScreenManager mainScreenManager;


        private HistoryScreenController historyScreenController;
        private Coroutine applyStylesCoroutine;
        private bool mainScreenHiddenForHistory;

        private void Awake()
        {
            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
                if (uiDocument == null)
                {
                    uiDocument = gameObject.AddComponent<UIDocument>();
                    Debug.LogWarning("[HistoryScreenManager] UIDocument добавлен автоматически");
                }
            }

            if (historyScreenUXML == null)
            {
                historyScreenUXML = Resources.Load<VisualTreeAsset>("UI/Views/HistoryScreen/HistoryScreen");
#if UNITY_EDITOR
                if (historyScreenUXML == null)
                {
                    historyScreenUXML = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Views/HistoryScreen/HistoryScreen.uxml");
                }
#endif
            }

            if (historyScreenStyleSheet == null)
            {
                historyScreenStyleSheet = Resources.Load<StyleSheet>("UI/Views/HistoryScreen/HistoryScreen");
#if UNITY_EDITOR
                if (historyScreenStyleSheet == null)
                {
                    historyScreenStyleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/UI/Views/HistoryScreen/HistoryScreen.uss");
                }
#endif
            }

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

        private void OnEnable()
        {
            if (applyStylesCoroutine == null)
            {
                applyStylesCoroutine = StartCoroutine(ApplyStylesWhenReady());
            }
        }

        private void Start()
        {
            if (historyScreenController != null)
            {
                historyScreenController.OnClose += HandleCloseRequested;
                historyScreenController.OnClearHistory += HandleClearHistory;
                historyScreenController.Hide();
            }

            if (ArtifactService.Instance != null)
            {
                ArtifactService.Instance.OnHistoryChanged += HandleHistoryChanged;
                ArtifactService.Instance.OnHistoryLoading += HandleHistoryLoading;
                ArtifactService.Instance.OnHistoryLoadingCompleted += HandleHistoryLoadingCompleted;
            }
            else
            {
                Debug.LogWarning("[HistoryScreenManager] ArtifactService.Instance недоступен");
            }
        }

        private void OnDestroy()
        {
            if (applyStylesCoroutine != null)
            {
                StopCoroutine(applyStylesCoroutine);
                applyStylesCoroutine = null;
            }

            if (historyScreenController != null)
            {
                historyScreenController.OnClose -= HandleCloseRequested;
                historyScreenController.OnClearHistory -= HandleClearHistory;
            }

            if (ArtifactService.Instance != null)
            {
                ArtifactService.Instance.OnHistoryChanged -= HandleHistoryChanged;
                ArtifactService.Instance.OnHistoryLoading -= HandleHistoryLoading;
                ArtifactService.Instance.OnHistoryLoadingCompleted -= HandleHistoryLoadingCompleted;
            }

            RestoreMainScreenIfNeeded();
        }

        private IEnumerator ApplyStylesWhenReady()
        {
            while (uiDocument == null || uiDocument.rootVisualElement == null)
            {
                yield return null;
            }

            if (historyScreenStyleSheet != null && !uiDocument.rootVisualElement.styleSheets.Contains(historyScreenStyleSheet))
            {
                uiDocument.rootVisualElement.styleSheets.Add(historyScreenStyleSheet);
            }
        }

        /// <summary>
        /// Показывает экран истории.
        /// </summary>
        public void Show()
        {
            if (historyScreenController == null)
            {
                Debug.LogWarning("[HistoryScreenManager] Попытка показать экран без контроллера");
                return;
            }

            historyScreenController.Show();
            HideMainScreen();
            RefreshHistory();
        }

        /// <summary>
        /// Скрывает экран истории.
        /// </summary>
        public void Hide()
        {
            if (historyScreenController == null) return;
            historyScreenController.Hide();
            RestoreMainScreenIfNeeded();
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
            Hide();

            var manager = ResolveMainScreenManager();
            if (manager != null && manager.GetController() != null)
            {
                manager.GetController().CloseMenu();
            }
        }

        private void HandleClearHistory()
        {
            if (ArtifactService.Instance == null)
            {
                Debug.LogWarning("[HistoryScreenManager] ArtifactService недоступен, очистка невозможна");
                return;
            }

            ArtifactService.Instance.ClearHistoryAndCache();
        }

        private void HandleHistoryChanged(System.Collections.Generic.IReadOnlyList<ArtifactService.ArtifactHistoryItem> historyItems)
        {
            if (historyScreenController == null)
            {
                return;
            }

            historyScreenController.UpdateHistory(historyItems);
        }

        private void HandleHistoryLoading()
        {
            if (historyScreenController == null)
            {
                return;
            }

            historyScreenController.ShowLoading(true);
        }

        private void HandleHistoryLoadingCompleted()
        {
            if (historyScreenController == null)
            {
                return;
            }

            historyScreenController.ShowLoading(false);
        }

        private void HideMainScreen()
        {
            if (mainScreenHiddenForHistory)
            {
                return;
            }

            var manager = ResolveMainScreenManager();
            if (manager == null)
            {
                return;
            }

            manager.Hide();
            mainScreenHiddenForHistory = true;
        }

        private void RestoreMainScreenIfNeeded()
        {
            if (!mainScreenHiddenForHistory)
            {
                return;
            }

            var manager = ResolveMainScreenManager();
            if (manager != null)
            {
                manager.Show();
            }

            mainScreenHiddenForHistory = false;
        }

        private MainScreenManager ResolveMainScreenManager()
        {
            if (mainScreenManager != null)
            {
                return mainScreenManager;
            }

            mainScreenManager = FindFirstObjectByType<MainScreenManager>();
            if (mainScreenManager == null)
            {
                Debug.LogWarning("[HistoryScreenManager] MainScreenManager не найден в сцене");
            }

            return mainScreenManager;
        }

    }
}


using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using ARArtifact.Services;
using ARArtifact.Storage;
using ARArtifact.UI.Common;

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
        [SerializeField] private DetailsScreenManager detailsScreenManager;
        
        private MainScreenController mainScreenController;
        
        private void Awake()
        {
            // НЕ выключаем gameObject - это ломает панель UIDocument!
            // Скрытие происходит через DisplayStyle.None в Hide()
            
            // Ensure NavigationManager exists
            if (NavigationManager.Instance == null)
            {
                var navGO = new GameObject("NavigationManager");
                navGO.AddComponent<NavigationManager>();
            }

            // Получаем или создаем UIDocument
            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
                if (uiDocument == null)
                {
                    uiDocument = gameObject.AddComponent<UIDocument>();
                }
            }
            
            if (mainScreenUXML == null) mainScreenUXML = Resources.Load<VisualTreeAsset>("UI/Views/MainScreen/MainScreen");
            if (mainScreenStyleSheet == null) mainScreenStyleSheet = Resources.Load<StyleSheet>("UI/Views/MainScreen/MainScreen");
            
            if (mainScreenUXML != null && uiDocument.visualTreeAsset == null)
            {
                uiDocument.visualTreeAsset = mainScreenUXML;
            }
            
            mainScreenController = uiDocument.GetComponent<MainScreenController>();
            if (mainScreenController == null)
            {
                mainScreenController = uiDocument.gameObject.AddComponent<MainScreenController>();
            }
            
            if (mainScreenController != null && mainScreenStyleSheet != null)
            {
                mainScreenController.StyleSheet = mainScreenStyleSheet;
            }
        }
        
        private bool _isInitialized = false;
        
        private void Start()
        {
            // Теперь gameObject всегда активен (не используем SetActive для управления видимостью)
            // Инициализацию делаем при первом Show() или здесь, если еще не инициализирован
            if (!_isInitialized)
            {
                InitializeController();
            }
        }
        
        private void InitializeController()
        {
            if (mainScreenController == null || _isInitialized) return;
            
            mainScreenController.Initialize(uiDocument, "AR Artifact");
            
            // Set as Home Screen for Navigation
            if (NavigationManager.Instance != null)
            {
                NavigationManager.Instance.SetHomeScreen(mainScreenController);
            }

            // Setup menu items after initialization
            SetupMenuItems();

            // Subscribe to preview click events
            mainScreenController.OnPreviewClicked += HandlePreviewClicked;
            
            ConnectTargetRecognitionEvents();
            
            _isInitialized = true;
        }
        
        private void OnDestroy()
        {
            if (mainScreenController != null)
            {
                mainScreenController.OnPreviewClicked -= HandlePreviewClicked;
            }
            
            // Очищаем кеш текстур
            foreach (var texture in previewTextureCache.Values)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }
            previewTextureCache.Clear();
            processedTargetIds.Clear();
        }
        
        private void ConnectTargetRecognitionEvents()
        {
            if (mainScreenController == null) return;
            
            TrackedArtifactManager trackedManager = FindFirstObjectByType<TrackedArtifactManager>();
            if (trackedManager != null)
            {
                trackedManager.OnTargetRecognized += (targetId) =>
                {
                    if (mainScreenController != null)
                    {
                        mainScreenController.UpdateTargetState(targetId, true);
                        
                        // Откладываем тяжелые операции на следующий кадр для оптимизации
                        StartCoroutine(DelayedTargetOperations(targetId));
                    }
                };
                
                trackedManager.OnTargetLost += (targetId) =>
                {
                    if (mainScreenController != null)
                    {
                        mainScreenController.UpdateTargetState(targetId, false);
                    }
                    // Очищаем кеш при потере трекинга (опционально, можно оставить для переиспользования)
                    // processedTargetIds.Remove(targetId);
                };
                
                trackedManager.OnArtifactFound += (targetId, artifactName) =>
                {
                    if (mainScreenController != null)
                    {
                        // Обновляем лог с именем артефакта
                        mainScreenController.LogTargetRecognition($"Найдено: {artifactName}", targetId);
                    }
                };
                
                // Подписываемся на новое событие с artifactId
                trackedManager.OnArtifactFoundWithId += (targetId, artifactId, artifactName) =>
                {
                    if (mainScreenController != null && !string.IsNullOrEmpty(artifactId))
                    {
                        // Устанавливаем имя артефакта для отображения в прогресс-баре
                        mainScreenController.SetArtifactDisplayName(artifactId, artifactName);
                    }
                };
                
                trackedManager.OnTargetPinStateChanged += (targetId, isPinned) =>
                {
                    if (mainScreenController != null)
                    {
                        mainScreenController.UpdateTargetPinState(targetId, isPinned);
                    }
                };
                
                // Подключаем события контроллера для работы с менеджером
                mainScreenController.OnGetTargetPinState += (targetId) => trackedManager.IsTargetPinned(targetId);
                mainScreenController.OnToggleTargetPin += (targetId) =>
                {
                    bool newState = trackedManager.TogglePinForTarget(targetId);
                    
                    // Запрашиваем имя артефакта для сообщения
                    RequestArtifactNameForPinMessage(targetId, newState);
                    
                    return newState;
                };
            }
        }
        
        // Кеш для загруженных текстур превью, чтобы не загружать повторно
        private readonly Dictionary<string, Texture2D> previewTextureCache = new();
        // Кеш для обработанных targetId, чтобы не вызывать функции повторно
        private readonly HashSet<string> processedTargetIds = new();
        
        /// <summary>
        /// Откладывает тяжелые операции при захвате таргета на следующий кадр
        /// </summary>
        private IEnumerator DelayedTargetOperations(string targetId)
        {
            // Ждем кадр перед тяжелыми операциями
            yield return null;
            
            LoadPreviewTexture(targetId);
            
            // Ждем еще кадр перед запросом имени
            yield return null;
            
            RequestArtifactNameForLog(targetId);
        }
        
        private void LoadPreviewTexture(string targetId)
        {
            if (mainScreenController == null || MarkerService.Instance == null) return;
            
            // Проверяем кеш
            if (previewTextureCache.TryGetValue(targetId, out var cachedTexture))
            {
                if (cachedTexture != null)
                {
                    mainScreenController.SetPreviewTexture(targetId, cachedTexture);
                    return;
                }
                else
                {
                    // Текстура была уничтожена, удаляем из кеша
                    previewTextureCache.Remove(targetId);
                }
            }
            
            var markers = MarkerService.Instance.GetMarkers();
            var markerData = markers?.FirstOrDefault(m => m.id == targetId);
            if (markerData != null && !string.IsNullOrEmpty(markerData.localImagePath))
            {
                var texture = MarkerImageService.Instance?.LoadLocalImage(markerData.localImagePath);
                if (texture != null)
                {
                    // Кешируем текстуру
                    previewTextureCache[targetId] = texture;
                    mainScreenController.SetPreviewTexture(targetId, texture);
                }
            }
        }
        
        private void RequestArtifactNameForLog(string targetId)
        {
            if (mainScreenController == null || ArtifactService.Instance == null) return;
            
            // Проверяем, не обрабатывали ли мы уже этот targetId
            if (processedTargetIds.Contains(targetId))
            {
                return;
            }
            
            // Запрашиваем артефакт для получения имени
            ArtifactService.Instance.RequestArtifactForTarget(
                targetId,
                result =>
                {
                    if (mainScreenController != null && result.Record != null)
                    {
                        string artifactName = result.Record.name;
                        if (string.IsNullOrEmpty(artifactName))
                        {
                            artifactName = "Неизвестный артефакт";
                        }
                        mainScreenController.LogTargetRecognition($"Найдено: {artifactName}", targetId);
                        // Помечаем как обработанный
                        processedTargetIds.Add(targetId);
                    }
                    else
                    {
                        // Если артефакт не найден, показываем ID таргета
                        mainScreenController.LogTargetRecognition($"Распознан таргет: {targetId}", targetId);
                    }
                },
                error =>
                {
                    // При ошибке показываем ID таргета
                    if (mainScreenController != null)
                    {
                        mainScreenController.LogTargetRecognition($"Распознан таргет: {targetId}", targetId);
                    }
                });
        }
        
        private void RequestArtifactNameForPinMessage(string targetId, bool isPinned)
        {
            if (mainScreenController == null || ArtifactService.Instance == null)
            {
                // Если сервис недоступен, используем ID
                string message = isPinned
                    ? $"Закреплен артефакт: {targetId}"
                    : $"Возврат к трекингу: {targetId}";
                mainScreenController?.LogTargetRecognition(message, targetId);
                return;
            }
            
            // Запрашиваем артефакт для получения имени
            ArtifactService.Instance.RequestArtifactForTarget(
                targetId,
                result =>
                {
                    if (mainScreenController != null)
                    {
                        string artifactName = result?.Record?.name;
                        if (string.IsNullOrEmpty(artifactName))
                        {
                            artifactName = "Неизвестный артефакт";
                        }
                        
                        string message = isPinned
                            ? $"Закреплен артефакт: {artifactName}"
                            : $"Возврат к трекингу: {artifactName}";
                        mainScreenController.LogTargetRecognition(message, targetId);
                    }
                },
                error =>
                {
                    // При ошибке используем ID таргета
                    if (mainScreenController != null)
                    {
                        string message = isPinned
                            ? $"Закреплен артефакт: {targetId}"
                            : $"Возврат к трекингу: {targetId}";
                        mainScreenController.LogTargetRecognition(message, targetId);
                    }
                });
        }
        
        private void SetupMenuItems()
        {
            if (mainScreenController == null) return;
            
            mainScreenController.ClearMenuItems();
            
            mainScreenController.AddMenuItem("Маркеры", OnMarkersClicked);
            mainScreenController.AddMenuItem("История", OnHistoryClicked);
        }
        
        private void OnMarkersClicked()
        {
            if (markersScreenManager == null) markersScreenManager = FindFirstObjectByType<MarkersScreenManager>();
            
            if (markersScreenManager != null)
            {
                markersScreenManager.Show();
            }
        }

        private void OnHistoryClicked()
        {
            if (historyScreenManager == null) historyScreenManager = FindFirstObjectByType<HistoryScreenManager>();

            if (historyScreenManager != null)
            {
                historyScreenManager.Show();
            }
        }
        
        private void HandlePreviewClicked(string targetId)
        {
            if (detailsScreenManager == null) detailsScreenManager = FindFirstObjectByType<DetailsScreenManager>();
            
            if (detailsScreenManager != null)
            {
                detailsScreenManager.Show(targetId);
            }
        }
        
        public void Show()
        {
            // НЕ трогаем gameObject.SetActive - это ломает панель UIDocument!
            // Все экраны остаются активными, скрытие/показ через DisplayStyle
            
            // Инициализируем контроллер при первом показе, если еще не инициализирован
            if (!_isInitialized)
            {
                InitializeController();
            }
            
            // Use NavigationManager to go back to home
            if (NavigationManager.Instance != null)
            {
                // Если MainScreen уже в стеке, просто показываем его
                // Иначе добавляем через NavigateTo
                NavigationManager.Instance.NavigateTo(mainScreenController);
            }
            else
            {
                mainScreenController?.Show();
            }
        }
        
        public void Hide()
        {
            mainScreenController?.Hide();
        }
        
        public MainScreenController GetController()
        {
            return mainScreenController;
        }
        
        #if UNITY_EDITOR
        /// <summary>
        /// Перезагружает UI для горячей перезагрузки в Editor режиме
        /// </summary>
        public void ReloadUI()
        {
            if (uiDocument == null || mainScreenController == null) return;
            
            // Сохраняем текущее состояние
            bool wasVisible = mainScreenController.gameObject.activeSelf && 
                             (uiDocument.rootVisualElement?.style.display == DisplayStyle.Flex);
            
            // В Editor режиме используем AssetDatabase для перезагрузки (без кэширования)
            mainScreenUXML = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/Resources/UI/Views/MainScreen/MainScreen.uxml");
            mainScreenStyleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/Resources/UI/Views/MainScreen/MainScreen.uss");
            
            // Fallback на Resources если AssetDatabase не сработал
            if (mainScreenUXML == null)
            {
                var oldUXML = mainScreenUXML;
                if (oldUXML != null) Resources.UnloadAsset(oldUXML);
                mainScreenUXML = Resources.Load<VisualTreeAsset>("UI/Views/MainScreen/MainScreen");
            }
            if (mainScreenStyleSheet == null)
            {
                var oldStyleSheet = mainScreenStyleSheet;
                if (oldStyleSheet != null) Resources.UnloadAsset(oldStyleSheet);
                mainScreenStyleSheet = Resources.Load<StyleSheet>("UI/Views/MainScreen/MainScreen");
            }
            
            // Пересоздаем дерево элементов
            uiDocument.visualTreeAsset = null;
            uiDocument.visualTreeAsset = mainScreenUXML;
            
            // Обновляем стили в контроллере
            if (mainScreenStyleSheet != null)
            {
                mainScreenController.StyleSheet = mainScreenStyleSheet;
            }
            
            // Переинициализируем контроллер
            mainScreenController.Initialize(uiDocument, "AR Artifact");
            SetupMenuItems();
            mainScreenController.OnPreviewClicked += HandlePreviewClicked;
            
            // Обновляем домашний экран в навигации
            if (NavigationManager.Instance != null)
            {
                NavigationManager.Instance.SetHomeScreen(mainScreenController);
            }
            
            // Восстанавливаем видимость
            if (wasVisible)
            {
                mainScreenController.Show();
            }
            else
            {
                mainScreenController.Hide();
            }
            
            Debug.Log("[MainScreenManager] UI перезагружен");
        }
        #endif
    }
}

using System;
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
    /// Контроллер для главного экрана приложения
    /// Управляет отображением хедера, бургер-меню и камеры
    /// </summary>
    public class MainScreenController : BaseScreenController
    {
        [Header("UI References")]
        [SerializeField] private StyleSheet styleSheet;
        
        public StyleSheet StyleSheet 
        { 
            get => styleSheet; 
            set => styleSheet = value; 
        }
        
        // Removed fields that are now in BaseScreenController or local
        private Button burgerMenuButton;
        private VisualElement sideMenu;
        private Button menuCloseButton;
        private VisualElement menuItems;
        
        // Лог-окошко для таргетов
        private VisualElement targetLogContainer;
        private Label targetLogNew;
        private Label targetLogOld;

        // Список превью артефактов
        private VisualElement targetPreviewContainer;
        private ScrollView targetPreviewList;
        
        // Контроллер прогресса загрузок
        private VisualElement downloadProgressContainer;
        private DownloadProgressController downloadProgressController;
        
        private bool isMenuOpen = false;
        
        // Для предотвращения повторного вывода одного и того же сообщения
        private string lastLoggedTargetId = null;
        private float lastLogTime = 0f;
        private const float LOG_COOLDOWN = 2f; 
        
        public System.Action OnMenuToggle;
        public event System.Action<string> OnPreviewClicked;
        public event System.Func<string, bool> OnGetTargetPinState; // targetId -> isPinned
        public event System.Func<string, bool> OnToggleTargetPin; // targetId -> newPinState

        private const int PreviewLongPressMs = 600;
        
        private class ArtifactPreviewItem
        {
            public string TargetId;
            public Texture2D PreviewTexture;
            public bool IsActive;
            public float LastActiveTime;
            public VisualElement VisualElement;
            public bool IsPinned;
            public IVisualElementScheduledItem HoldScheduledItem;
            public bool HoldTriggered;
            public bool SuppressNextClick;
            public bool IsPointerDown; // Флаг для отслеживания состояния нажатия
        }

        private readonly Dictionary<string, ArtifactPreviewItem> artifactPreviews = new();
        
        // Словарь для связи artifactId с displayName для отображения в прогресс-баре
        private readonly Dictionary<string, string> artifactDisplayNames = new();
        
        // Методы для обновления состояния из Manager
        public void UpdateTargetState(string targetId, bool isActive)
        {
            OnTargetRecognized(targetId, isActive);
        }
        
        public void UpdateTargetPinState(string targetId, bool isPinned)
        {
            HandlePinStateChanged(targetId, isPinned);
        }
        
        // Initialize is called by MainScreenManager
        public override void Initialize(UIDocument uiDocument, string screenName = "AR Artifact")
        {
            base.Initialize(uiDocument, screenName);
        }
        
        public override void Show()
        {
            base.Show();
            Debug.Log("[MainScreen] Show() вызван");
            // Панель больше не теряется, так как GameObject не отключается
        }
        
        protected override void OnInitialize()
        {
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
                styleSheet = Resources.Load<StyleSheet>("UI/Views/MainScreen/MainScreen");
                if (styleSheet != null && !_root.styleSheets.Contains(styleSheet))
                {
                    _root.styleSheets.Add(styleSheet);
                }
            }
            
            // Получаем элементы по имени
            // Header is already handled in BaseScreenController, but we need the burger button
            if (_header != null)
            {
                burgerMenuButton = _header.Q<Button>("burger-menu-button");
            }
            
            sideMenu = _root.Q<VisualElement>("side-menu");
            // menu-close-button is inside sideMenu, likely
            if (sideMenu != null)
            {
                menuCloseButton = sideMenu.Q<Button>("menu-close-button");
            }
            menuItems = _root.Q<VisualElement>("menu-items");
            
            // Лог-окошко для таргетов
            targetLogContainer = _root.Q<VisualElement>("target-log-container");
            targetLogNew = _root.Q<Label>("target-log-new");
            targetLogOld = _root.Q<Label>("target-log-old");

            // Список превью
            targetPreviewList = _root.Q<ScrollView>("target-preview-list");
            if (targetPreviewList != null)
            {
                // Устанавливаем режим вертикальной прокрутки
                targetPreviewList.mode = ScrollViewMode.Vertical;
                // Включаем touch scrolling
                targetPreviewList.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
                // Включаем видимость вертикального скроллера
                targetPreviewList.verticalScrollerVisibility = ScrollerVisibility.Auto;
                targetPreviewList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                // Альтернативный способ (для старых версий Unity)
                targetPreviewList.showVertical = true;
                targetPreviewList.showHorizontal = false;
                
                // Отладка: проверяем размеры после первого кадра
                targetPreviewList.schedule.Execute(() => {
                    var viewport = targetPreviewList.Q<VisualElement>(null, "unity-scroll-view__content-viewport");
                    var content = targetPreviewList.contentContainer;
                    if (viewport != null && content != null)
                    {
                        Debug.Log($"[MainScreen] ScrollView размеры - viewport: {viewport.layout.height}, content: {content.layout.height}");
                    }
                }).ExecuteLater(100);
            }
            targetPreviewContainer = _root.Q<VisualElement>("target-preview-container");
            
            // Контроллер прогресса загрузок
            downloadProgressContainer = _root.Q<VisualElement>("download-progress-container");
            if (downloadProgressContainer != null)
            {
                // Удаляем тестовый контент, если есть
                var testDownload = downloadProgressContainer.Q<VisualElement>("test-download");
                if (testDownload != null)
                {
                    testDownload.RemoveFromHierarchy();
                }
                
                downloadProgressController = new DownloadProgressController(downloadProgressContainer, this);
            }
            
            // Инициализируем лог-окошко пустыми значениями
            if (targetLogNew != null) targetLogNew.text = "";
            if (targetLogOld != null) targetLogOld.text = "";
            
            // Настраиваем обработчики событий
            if (burgerMenuButton != null)
            {
                // Отписываемся от предыдущих обработчиков
                burgerMenuButton.UnregisterCallback<ClickEvent>(OnBurgerMenuButtonClicked);
                burgerMenuButton.clicked -= ToggleMenu;
                // Используем только RegisterCallback для избежания двойного срабатывания
                burgerMenuButton.RegisterCallback<ClickEvent>(OnBurgerMenuButtonClicked);
                Debug.Log($"[MainScreen] burgerMenuButton найден и обработчик установлен. gameObject.activeSelf={gameObject.activeSelf}, _root={(_root != null ? "found" : "null")}, burgerMenuButton.enabledSelf={burgerMenuButton.enabledSelf}");
            }
            else
            {
                Debug.LogError($"[MainScreen] burgerMenuButton не найден! Проверка элементов: _header={(_header != null ? "found" : "null")}, _root={(_root != null ? "found" : "null")}, gameObject.activeSelf={gameObject.activeSelf}");
                if (_header == null)
                {
                    Debug.LogError("[MainScreen] _header is null!");
                }
                else
                {
                    Debug.LogError($"[MainScreen] _header найден, но burger-menu-button не найден внутри. Доступные элементы в header:");
                    var allButtons = _header.Query<Button>().ToList();
                    Debug.LogError($"[MainScreen] Найдено кнопок в header: {allButtons.Count}");
                    foreach (var btn in allButtons)
                    {
                        Debug.LogError($"[MainScreen] Кнопка: name={btn.name}, classList={string.Join(", ", btn.GetClasses())}, enabledSelf={btn.enabledSelf}");
                    }
                }
            }
            
            if (menuCloseButton != null)
            {
                menuCloseButton.clicked += CloseMenu;
            }
            
            // Закрываем меню по клику на overlay
            if (sideMenu != null)
            {
                VisualElement overlay = _root.Q<VisualElement>("side-menu-overlay");
                if (overlay != null)
                {
                    overlay.RegisterCallback<ClickEvent>(evt => CloseMenu());
                }
            }
            
            // Закрываем меню по умолчанию
            CloseMenu();
            
            // Подписываемся на события загрузки моделей
            ConnectModelLoaderEvents();
        }
        
        private void OnDisable()
        {
            OnDisconnect();
        }
        
        private void OnDestroy()
        {
            OnDisconnect();
        }
        
        private void ConnectModelLoaderEvents()
        {
            var modelLoader = ModelLoaderService.Instance;
            if (modelLoader != null)
            {
                modelLoader.OnLoadStarted += OnModelLoadStarted;
                modelLoader.OnLoadCompleted += OnModelLoadCompleted;
                modelLoader.OnLoadFailed += OnModelLoadFailed;
            }
        }
        
        private void OnDisconnect()
        {
            var modelLoader = ModelLoaderService.Instance;
            if (modelLoader != null)
            {
                modelLoader.OnLoadStarted -= OnModelLoadStarted;
                modelLoader.OnLoadCompleted -= OnModelLoadCompleted;
                modelLoader.OnLoadFailed -= OnModelLoadFailed;
            }
            
            downloadProgressController?.ClearAll();
        }
        
        private void OnModelLoadStarted(string artifactId)
        {
            if (downloadProgressController != null)
            {
                // Получаем имя артефакта, если оно известно
                string displayName = artifactDisplayNames.TryGetValue(artifactId, out var name) ? name : null;
                downloadProgressController.StartTracking(artifactId, displayName);
            }
        }
        
        private void OnModelLoadCompleted(string artifactId)
        {
            if (downloadProgressController != null)
            {
                downloadProgressController.StopTracking(artifactId, removeImmediately: false);
            }
        }
        
        private void OnModelLoadFailed(string artifactId, string error)
        {
            if (downloadProgressController != null)
            {
                downloadProgressController.StopTracking(artifactId, removeImmediately: true);
            }
        }
        
        /// <summary>
        /// Устанавливает имя артефакта для отображения в прогресс-баре
        /// </summary>
        public void SetArtifactDisplayName(string artifactId, string displayName)
        {
            if (!string.IsNullOrEmpty(artifactId) && !string.IsNullOrEmpty(displayName))
            {
                artifactDisplayNames[artifactId] = displayName;
                
                // Проверяем статус модели перед началом отслеживания
                if (downloadProgressController != null)
                {
                    var modelLoader = ModelLoaderService.Instance;
                    if (modelLoader != null)
                    {
                        // Если модель уже загружена из кэша, не показываем прогресс
                        if (modelLoader.TryGetLoadedModel(artifactId, out _))
                        {
                            // Модель уже в кэше, не начинаем отслеживание
                            return;
                        }
                        
                        // Если модель загружается, начинаем отслеживание
                        if (modelLoader.IsLoading(artifactId))
                        {
                            downloadProgressController.StartTracking(artifactId, displayName);
                        }
                        // Если модель не загружена и не загружается, не начинаем отслеживание
                        // Оно начнется автоматически при вызове OnLoadStarted
                    }
                }
            }
        }
        
        private void OnTargetRecognized(string targetId, bool isActive)
        {
            // Оптимизация: не обновляем, если элемент уже активен
            if (artifactPreviews.TryGetValue(targetId, out var existingItem) && existingItem.IsActive && isActive)
            {
                // Только обновляем время активности, но не перестраиваем UI
                existingItem.LastActiveTime = Time.time;
                return;
            }
            
            UpdateArtifactPreview(targetId, isActive);
        }

        private void UpdateArtifactPreview(string targetId, bool isActive)
        {
            if (targetPreviewContainer == null) return;

            bool wasNewItem = false;
            if (!artifactPreviews.TryGetValue(targetId, out var item))
            {
                wasNewItem = true;
                // Создаем новый элемент
                item = new ArtifactPreviewItem
                {
                    TargetId = targetId,
                    IsActive = isActive,
                    LastActiveTime = Time.time
                };
                // Получаем состояние закрепления через событие
                item.IsPinned = OnGetTargetPinState?.Invoke(targetId) ?? false;

                // Создаем UI элемент
                var ve = new VisualElement();
                ve.AddToClassList("target-preview-item");
                
                if (item.PreviewTexture != null)
                {
                    ve.style.backgroundImage = new StyleBackground(item.PreviewTexture);
                    ve.AddToClassList("target-preview-image");
                }
                else
                {
                    ve.style.backgroundColor = Color.gray;
                }

                // Регистрируем ClickEvent - проверяем флаги в самом начале
                ve.RegisterCallback<ClickEvent>(evt => {
                    // Если было зажатие или SuppressNextClick установлен, не открываем Details
                    if (item.HoldTriggered || item.SuppressNextClick)
                    {
                        evt.StopImmediatePropagation();
                        // Сбрасываем флаги после предотвращения события
                        item.HoldTriggered = false;
                        item.SuppressNextClick = false;
                        item.IsPointerDown = false;
                        return;
                    }
                    
                    // Сбрасываем флаг нажатия при нормальном клике
                    item.IsPointerDown = false;
                    
                    OnPreviewClicked?.Invoke(targetId);
                });
                
                item.VisualElement = ve;
                RegisterPreviewInteractions(item);
                artifactPreviews[targetId] = item;
            }

            // Проверяем, изменилось ли состояние
            bool stateChanged = item.IsActive != isActive;
            item.IsActive = isActive;
            if (isActive)
            {
                item.LastActiveTime = Time.time;
            }
            
            // Обновляем визуальное состояние только если оно изменилось или это новый элемент
            if (stateChanged || wasNewItem)
            {
                UpdatePreviewVisualState(item);
                
                // Перестраиваем список только если состояние изменилось или это новый элемент
                RebuildPreviewList();
            }
        }

        private void RebuildPreviewList()
        {
            if (targetPreviewContainer == null) return;

            targetPreviewContainer.Clear();

            // Сортировка: активные сверху (по времени активности), затем неактивные (по времени активности)
            var sortedItems = artifactPreviews.Values
                .OrderByDescending(x => x.IsActive)
                .ThenByDescending(x => x.LastActiveTime)
                .ToList();

            foreach (var item in sortedItems)
            {
                targetPreviewContainer.Add(item.VisualElement);
            }
        }
        
        private void RegisterPreviewInteractions(ArtifactPreviewItem item)
        {
            if (item?.VisualElement == null)
            {
                return;
            }
            
            item.VisualElement.RegisterCallback<PointerDownEvent>(evt => HandlePreviewPointerDown(evt, item));
            item.VisualElement.RegisterCallback<PointerUpEvent>(evt => HandlePreviewPointerUp(evt, item));
            item.VisualElement.RegisterCallback<PointerLeaveEvent>(evt => CancelPreviewLongPress(item));
            item.VisualElement.RegisterCallback<PointerCancelEvent>(evt => CancelPreviewLongPress(item));
        }
        
        private void HandlePreviewPointerDown(PointerDownEvent evt, ArtifactPreviewItem item)
        {
            if (item == null)
            {
                return;
            }
            
            if (item.VisualElement == null)
            {
                return;
            }
            
            // Устанавливаем флаг нажатия
            item.IsPointerDown = true;
            
            // Сбрасываем все флаги при новом нажатии
            CancelPreviewLongPress(item);
            item.HoldTriggered = false;
            item.SuppressNextClick = false;
            
            // Запланируем длительное нажатие
            item.HoldScheduledItem = item.VisualElement.schedule.Execute(() =>
            {
                item.HoldTriggered = true;
                item.SuppressNextClick = true;
                HandlePreviewPinToggle(item);
            }).StartingIn(PreviewLongPressMs);
        }
        
        private void HandlePreviewPointerUp(PointerUpEvent evt, ArtifactPreviewItem item)
        {
            if (item == null)
            {
                return;
            }
            
            // Отменяем запланированное длительное нажатие, если оно еще не сработало
            CancelPreviewLongPress(item);
            
            // Если было длительное нажатие, предотвращаем срабатывание ClickEvent
            bool wasLongPress = item.HoldTriggered || item.SuppressNextClick;
            
            if (wasLongPress)
            {
                // Устанавливаем флаг до того, как может сработать ClickEvent
                item.SuppressNextClick = true;
                evt.StopImmediatePropagation();
            }
            
            // Сбрасываем флаг нажатия
            item.IsPointerDown = false;
        }
        
        private void CancelPreviewLongPress(ArtifactPreviewItem item)
        {
            if (item?.HoldScheduledItem != null)
            {
                item.HoldScheduledItem.Pause();
                item.HoldScheduledItem = null;
            }
            
            // Если длительное нажатие не сработало, сбрасываем флаги
            if (item != null && !item.HoldTriggered)
            {
                item.SuppressNextClick = false;
                // Не сбрасываем IsPointerDown здесь - он должен сбрасываться в PointerUp или ClickEvent
            }
        }
        
        private void HandlePreviewPinToggle(ArtifactPreviewItem item)
        {
            if (item == null)
            {
                return;
            }
            
            bool? newState = OnToggleTargetPin?.Invoke(item.TargetId);
            if (newState.HasValue)
            {
                item.IsPinned = newState.Value;
                UpdatePreviewVisualState(item);
            }
        }
        
        private void HandlePinStateChanged(string targetId, bool isPinned)
        {
            if (artifactPreviews.TryGetValue(targetId, out var item))
            {
                item.IsPinned = isPinned;
                UpdatePreviewVisualState(item);
            }
        }
        
        private void UpdatePreviewVisualState(ArtifactPreviewItem item)
        {
            if (item?.VisualElement == null)
            {
                return;
            }
            
            // Для закрепленных элементов всегда убираем inactive и добавляем pinned
            if (item.IsPinned)
            {
                item.VisualElement.RemoveFromClassList("inactive");
                item.VisualElement.AddToClassList("pinned");
            }
            else
            {
                // Для незакрепленных элементов применяем обычную логику
                item.VisualElement.RemoveFromClassList("pinned");
                if (item.IsActive)
                {
                    item.VisualElement.RemoveFromClassList("inactive");
                }
                else
                {
                    item.VisualElement.AddToClassList("inactive");
                }
            }
        }

        private void OnBurgerMenuButtonClicked(ClickEvent evt)
        {
            Debug.Log($"[MainScreen] ===== OnBurgerMenuButtonClicked ВЫЗВАН =====");
            var targetElement = evt.target as VisualElement;
            var currentTargetElement = evt.currentTarget as VisualElement;
            Debug.Log($"[MainScreen] ClickEvent: target={(targetElement != null ? targetElement.name : "null")}, currentTarget={(currentTargetElement != null ? currentTargetElement.name : "null")}, button={burgerMenuButton?.name}");
            evt.StopPropagation();
            ToggleMenu();
        }
        
        public void ToggleMenu()
        {
            Debug.Log($"[MainScreen] ===== ToggleMenu ВЫЗВАН =====");
            Debug.Log($"[MainScreen] ToggleMenu: isMenuOpen={isMenuOpen}, sideMenu={(sideMenu != null ? "found" : "null")}, gameObject.activeSelf={gameObject.activeSelf}, _root={(_root != null ? "found" : "null")}, burgerMenuButton={(burgerMenuButton != null ? "found" : "null")}");
            if (isMenuOpen)
            {
                Debug.Log("[MainScreen] ToggleMenu: закрываем меню");
                CloseMenu();
            }
            else
            {
                Debug.Log("[MainScreen] ToggleMenu: открываем меню");
                OpenMenu();
            }
        }
        
        public void OpenMenu()
        {
            Debug.Log($"[MainScreen] OpenMenu вызван, sideMenu={(sideMenu != null ? "found" : "null")}, gameObject.activeSelf={gameObject.activeSelf}, _root={(_root != null ? "found" : "null")}");
            if (sideMenu != null)
            {
                sideMenu.AddToClassList("visible");
                isMenuOpen = true;
                Debug.Log($"[MainScreen] Меню открыто, классы sideMenu: {string.Join(", ", sideMenu.GetClasses())}, display={sideMenu.style.display.value}");
                OnMenuToggle?.Invoke();
            }
            else
            {
                Debug.LogError($"[MainScreen] OpenMenu: sideMenu is null! _root={(_root != null ? "found" : "null")}, gameObject.activeSelf={gameObject.activeSelf}");
                // Пытаемся найти sideMenu заново
                if (_root != null)
                {
                    sideMenu = _root.Q<VisualElement>("side-menu");
                    Debug.Log($"[MainScreen] Попытка переинициализации sideMenu: {(sideMenu != null ? "found" : "null")}");
                    if (sideMenu != null)
                    {
                        sideMenu.AddToClassList("visible");
                        isMenuOpen = true;
                        OnMenuToggle?.Invoke();
                    }
                }
            }
        }
        
        public void CloseMenu()
        {
            if (sideMenu != null)
            {
                sideMenu.RemoveFromClassList("visible");
                isMenuOpen = false;
                OnMenuToggle?.Invoke();
            }
        }
        
        public bool IsMenuOpen => isMenuOpen;
        
        public void AddMenuItem(string label, System.Action onClick)
        {
            if (menuItems == null) return;
            
            VisualElement menuItem = new VisualElement();
            menuItem.AddToClassList("menu-item");
            
            Label labelElement = new Label(label);
            labelElement.AddToClassList("menu-item-label");
            menuItem.Add(labelElement);
            
            menuItem.RegisterCallback<ClickEvent>(evt => {
                onClick?.Invoke();
                CloseMenu();
            });
            
            menuItems.Add(menuItem);
        }
        
        public void ClearMenuItems()
        {
            if (menuItems != null)
            {
                menuItems.Clear();
            }
        }
        
        // Show/Hide are inherited from BaseScreenController, but we can override if needed
        // MainScreen is special, it might be the "Background" screen.
        
        // Метод для установки превью текстуры из Manager
        public void SetPreviewTexture(string targetId, Texture2D texture)
        {
            if (artifactPreviews.TryGetValue(targetId, out var item))
            {
                item.PreviewTexture = texture;
                if (item.VisualElement != null)
                {
                    if (texture != null)
                    {
                        item.VisualElement.style.backgroundImage = new StyleBackground(texture);
                        item.VisualElement.AddToClassList("target-preview-image");
                    }
                    else
                    {
                        item.VisualElement.style.backgroundColor = Color.gray;
                        item.VisualElement.RemoveFromClassList("target-preview-image");
                    }
                }
            }
        }
        
        public void LogTargetRecognition(string message, string targetId = null)
        {
            if (targetLogNew == null || targetLogOld == null)
            {
                return;
            }
            
            // Проверяем cooldown
            if (!string.IsNullOrEmpty(targetId))
            {
                float currentTime = Time.time;
                if (targetId == lastLoggedTargetId && (currentTime - lastLogTime) < LOG_COOLDOWN)
                {
                    return; 
                }
                lastLoggedTargetId = targetId;
                lastLogTime = currentTime;
            }
            
            if (!string.IsNullOrEmpty(targetLogNew.text))
            {
                targetLogOld.text = targetLogNew.text;
            }
            
            targetLogNew.text = message;
        }
        
        public void ClearTargetLog()
        {
            if (targetLogNew != null) targetLogNew.text = "";
            if (targetLogOld != null) targetLogOld.text = "";
            lastLoggedTargetId = null;
            lastLogTime = 0f;
        }
    }
}

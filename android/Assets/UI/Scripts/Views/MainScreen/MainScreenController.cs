using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace ARArtifact.UI
{
    /// <summary>
    /// Контроллер для главного экрана приложения
    /// Управляет отображением хедера, бургер-меню и камеры
    /// </summary>
    public class MainScreenController : MonoBehaviour
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
        private Button burgerMenuButton;
        private VisualElement sideMenu;
        private Button menuCloseButton;
        private VisualElement menuItems;
        
        // Лог-окошко для таргетов
        private VisualElement targetLogContainer;
        private Label targetLogNew;
        private Label targetLogOld;
        
        private bool isMenuOpen = false;
        
        // Для предотвращения повторного вывода одного и того же сообщения
        private string lastLoggedTargetId = null;
        private float lastLogTime = 0f;
        private const float LOG_COOLDOWN = 2f; // Минимальный интервал между логами одного таргета (секунды)
        
        public System.Action OnMenuToggle;
        
        private void Awake()
        {
            // Если UIDocument не назначен, пытаемся найти его автоматически
            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
            }
            
            // Если все еще null, создаем новый
            if (uiDocument == null)
            {
                uiDocument = gameObject.AddComponent<UIDocument>();
            }
        }
        
        private void OnEnable()
        {
            // Используем корутину для ожидания rootVisualElement
            StartCoroutine(WaitForRootVisualElement());
        }
        
        private System.Collections.IEnumerator WaitForRootVisualElement()
        {
            // Ждем, пока rootVisualElement станет доступен
            while (uiDocument == null || uiDocument.rootVisualElement == null)
            {
                yield return null;
            }
            
            InitializeUI();
        }
        
        /// <summary>
        /// Инициализация UI элементов
        /// </summary>
        private void InitializeUI()
        {
            root = uiDocument.rootVisualElement;
            
            // Подключаем стили, если они назначены
            if (styleSheet != null)
            {
                if (!root.styleSheets.Contains(styleSheet))
                {
                    root.styleSheets.Add(styleSheet);
                }
            }
            else
            {
                // Пытаемся загрузить стили автоматически
                styleSheet = Resources.Load<StyleSheet>("UI/Views/MainScreen/MainScreen");
                if (styleSheet != null && !root.styleSheets.Contains(styleSheet))
                {
                    root.styleSheets.Add(styleSheet);
                }
            }
            
            // Получаем элементы по имени
            header = root.Q<VisualElement>("header");
            burgerMenuButton = root.Q<Button>("burger-menu-button");
            sideMenu = root.Q<VisualElement>("side-menu");
            menuCloseButton = root.Q<Button>("menu-close-button");
            menuItems = root.Q<VisualElement>("menu-items");
            
            // Лог-окошко для таргетов
            targetLogContainer = root.Q<VisualElement>("target-log-container");
            targetLogNew = root.Q<Label>("target-log-new");
            targetLogOld = root.Q<Label>("target-log-old");
            
            // Инициализируем лог-окошко пустыми значениями
            if (targetLogNew != null) targetLogNew.text = "";
            if (targetLogOld != null) targetLogOld.text = "";
            
            // cameraContainer больше не нужен - камера рендерится через ARCameraBackground, UI просто оверлей
            
            // Настраиваем обработчики событий
            if (burgerMenuButton != null)
            {
                burgerMenuButton.clicked += ToggleMenu;
            }
            
            if (menuCloseButton != null)
            {
                menuCloseButton.clicked += CloseMenu;
            }
            
            // Закрываем меню по клику на overlay
            if (sideMenu != null)
            {
                VisualElement overlay = root.Q<VisualElement>("side-menu-overlay");
                if (overlay != null)
                {
                    overlay.RegisterCallback<ClickEvent>(evt => CloseMenu());
                }
            }
            
            // Закрываем меню по умолчанию
            CloseMenu();
            
            // UI работает как оверлей поверх AR камеры
            // Камера рендерится через ARCameraBackground компонент на Main Camera
            // UI Toolkit автоматически рендерится поверх всего через PanelSettings
        }
        
        /// <summary>
        /// Переключает состояние бокового меню
        /// </summary>
        public void ToggleMenu()
        {
            if (isMenuOpen)
            {
                CloseMenu();
            }
            else
            {
                OpenMenu();
            }
        }
        
        /// <summary>
        /// Открывает боковое меню
        /// </summary>
        public void OpenMenu()
        {
            if (sideMenu != null)
            {
                sideMenu.AddToClassList("visible");
                isMenuOpen = true;
                OnMenuToggle?.Invoke();
            }
        }
        
        /// <summary>
        /// Закрывает боковое меню
        /// </summary>
        public void CloseMenu()
        {
            if (sideMenu != null)
            {
                sideMenu.RemoveFromClassList("visible");
                isMenuOpen = false;
                OnMenuToggle?.Invoke();
            }
        }
        
        /// <summary>
        /// Проверяет, открыто ли меню
        /// </summary>
        public bool IsMenuOpen => isMenuOpen;
        
        /// <summary>
        /// Добавляет пункт в меню
        /// </summary>
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
        
        /// <summary>
        /// Очищает все пункты меню
        /// </summary>
        public void ClearMenuItems()
        {
            if (menuItems != null)
            {
                menuItems.Clear();
            }
        }
        
        /// <summary>
        /// Показывает весь главный экран
        /// </summary>
        public void Show()
        {
            if (root != null)
            {
                root.style.display = DisplayStyle.Flex;
            }
        }
        
        /// <summary>
        /// Скрывает главный экран
        /// </summary>
        public void Hide()
        {
            if (root != null)
            {
                root.style.display = DisplayStyle.None;
            }
        }
        
        /// <summary>
        /// Добавляет сообщение о распознанном таргете в лог-окошко
        /// </summary>
        /// <param name="message">Текст сообщения (например, "Найдено: Название артефакта")</param>
        /// <param name="targetId">ID таргета для предотвращения повторных выводов</param>
        public void LogTargetRecognition(string message, string targetId = null)
        {
            if (targetLogNew == null || targetLogOld == null)
            {
                return;
            }
            
            // Проверяем cooldown для предотвращения спама
            if (!string.IsNullOrEmpty(targetId))
            {
                float currentTime = Time.time;
                if (targetId == lastLoggedTargetId && (currentTime - lastLogTime) < LOG_COOLDOWN)
                {
                    return; // Пропускаем повторное сообщение
                }
                lastLoggedTargetId = targetId;
                lastLogTime = currentTime;
            }
            
            // Перемещаем текущее новое сообщение в старое
            if (!string.IsNullOrEmpty(targetLogNew.text))
            {
                targetLogOld.text = targetLogNew.text;
            }
            
            // Устанавливаем новое сообщение
            targetLogNew.text = message;
        }
        
        /// <summary>
        /// Очищает лог-окошко
        /// </summary>
        public void ClearTargetLog()
        {
            if (targetLogNew != null) targetLogNew.text = "";
            if (targetLogOld != null) targetLogOld.text = "";
            lastLoggedTargetId = null;
            lastLogTime = 0f;
        }
    }
}


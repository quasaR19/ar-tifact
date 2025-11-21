using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace ARArtifact.UI
{
    /// <summary>
    /// Контроллер для страницы запуска приложения
    /// Управляет отображением throbber и статусных сообщений
    /// </summary>
    public class LaunchScreenController : MonoBehaviour
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
        private VisualElement throbber;
        private Label statusLabel;
        private float rotationAngle = 0f;
        
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
                styleSheet = Resources.Load<StyleSheet>("UI/Views/LaunchScreen/LaunchScreen");
                if (styleSheet != null && !root.styleSheets.Contains(styleSheet))
                {
                    root.styleSheets.Add(styleSheet);
                }
            }
            
            // Получаем элементы по имени
            throbber = root.Q<VisualElement>("throbber");
            statusLabel = root.Q<Label>("status-label");
            
            // Устанавливаем начальный статус
            SetStatus("Инициализация...");
            
            // Запускаем анимацию вращения throbber
            if (throbber != null)
            {
                StartThrobberAnimation();
            }
        }
        
        private void Update()
        {
            // Обновляем анимацию вращения throbber (1 оборот в секунду)
            if (throbber != null && throbber.style.display == DisplayStyle.Flex)
            {
                rotationAngle += 360f * Time.deltaTime;
                if (rotationAngle >= 360f)
                {
                    rotationAngle -= 360f;
                }
                // Используем правильный API для UI Toolkit
                throbber.style.rotate = new Rotate(new Angle(rotationAngle));
            }
        }
        
        /// <summary>
        /// Запускает анимацию вращения throbber
        /// </summary>
        private void StartThrobberAnimation()
        {
            rotationAngle = 0f;
        }
        
        /// <summary>
        /// Устанавливает текст статуса
        /// </summary>
        /// <param name="status">Текст статуса</param>
        public void SetStatus(string status)
        {
            if (statusLabel != null)
            {
                statusLabel.text = status;
            }
            else
            {
                Debug.LogWarning("Status label not found. Status: " + status);
            }
        }
        
        /// <summary>
        /// Показывает throbber
        /// </summary>
        public void ShowThrobber()
        {
            if (throbber != null)
            {
                throbber.style.display = DisplayStyle.Flex;
            }
        }
        
        /// <summary>
        /// Скрывает throbber
        /// </summary>
        public void HideThrobber()
        {
            if (throbber != null)
            {
                throbber.style.display = DisplayStyle.None;
            }
        }
        
        /// <summary>
        /// Показывает весь экран запуска
        /// </summary>
        public void Show()
        {
            if (root != null)
            {
                root.style.display = DisplayStyle.Flex;
            }
        }
        
        /// <summary>
        /// Скрывает экран запуска
        /// </summary>
        public void Hide()
        {
            if (root != null)
            {
                root.style.display = DisplayStyle.None;
            }
        }
    }
}


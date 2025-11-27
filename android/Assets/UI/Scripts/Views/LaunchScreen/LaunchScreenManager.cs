using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;

namespace ARArtifact.UI
{
    /// <summary>
    /// Менеджер для управления экраном запуска
    /// Обрабатывает логику инициализации приложения
    /// </summary>
    public class LaunchScreenManager : MonoBehaviour
    {
        [Header("UI Configuration")]
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private VisualTreeAsset launchScreenUXML;
        [SerializeField] private StyleSheet launchScreenStyleSheet;
        
        [Header("Initialization Settings")]
        [SerializeField] private float minDisplayTime = 2f; // Минимальное время отображения экрана запуска
        [SerializeField] private bool autoHideAfterInit = true;

        [Header("AR Components")]
        // Компоненты AR теперь управляются через ARManager
        // Оставляем поля сериализованными для совместимости со сценой, но не используем логику инициализации здесь
        [SerializeField] private ARSession arSession;
        [SerializeField] private ARTrackedImageManager trackedImageManager;
        
        private LaunchScreenController launchScreenController;
        private bool isInitialized = false;
        
        [Header("Main Screen")]
        [SerializeField] private MainScreenManager mainScreenManager;
        
        private void Awake()
        {
            // ARManager сам позаботится об инициализации и отключении компонентов в своем Awake
            if (Services.ARManager.Instance == null)
            {
                GameObject arManagerGO = new GameObject("ARManager");
                arManagerGO.AddComponent<Services.ARManager>();
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
            
            // Автоматически загружаем UXML если он не назначен
            // Сначала пытаемся загрузить из Resources (основной источник)
            if (launchScreenUXML == null)
            {
                launchScreenUXML = Resources.Load<VisualTreeAsset>("UI/Views/LaunchScreen/LaunchScreen");
                
                // Fallback для редактора (только если не найдено в Resources)
                if (launchScreenUXML == null)
                {
                    #if UNITY_EDITOR
                    // Пробуем загрузить из Resources через AssetDatabase
                    launchScreenUXML = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Resources/UI/Views/LaunchScreen/LaunchScreen.uxml");
                    
                    // Последний fallback - из Assets/UI/Views (обратная совместимость)
                    if (launchScreenUXML == null)
                    {
                        launchScreenUXML = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Views/LaunchScreen/LaunchScreen.uxml");
                    }
                    #endif
                }
            }
            
            // Автоматически загружаем StyleSheet если он не назначен
            // Сначала пытаемся загрузить из Resources (основной источник)
            if (launchScreenStyleSheet == null)
            {
                launchScreenStyleSheet = Resources.Load<StyleSheet>("UI/Views/LaunchScreen/LaunchScreen");
                
                // Fallback для редактора (только если не найдено в Resources)
                if (launchScreenStyleSheet == null)
                {
                    #if UNITY_EDITOR
                    // Пробуем загрузить из Resources через AssetDatabase
                    launchScreenStyleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Resources/UI/Views/LaunchScreen/LaunchScreen.uss");
                    
                    // Последний fallback - из Assets/UI/Views (обратная совместимость)
                    if (launchScreenStyleSheet == null)
                    {
                        launchScreenStyleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/UI/Views/LaunchScreen/LaunchScreen.uss");
                    }
                    #endif
                }
            }
            
            // Загружаем UXML если он назначен
            if (launchScreenUXML != null && uiDocument.visualTreeAsset == null)
            {
                uiDocument.visualTreeAsset = launchScreenUXML;
            }
            
            // Добавляем контроллер
            launchScreenController = uiDocument.GetComponent<LaunchScreenController>();
            if (launchScreenController == null)
            {
                launchScreenController = uiDocument.gameObject.AddComponent<LaunchScreenController>();
            }
            
            // Передаем стили в контроллер
            if (launchScreenController != null && launchScreenStyleSheet != null)
            {
                launchScreenController.StyleSheet = launchScreenStyleSheet;
            }
            
        }
        
        private void OnEnable()
        {
            // Используем корутину для ожидания rootVisualElement
            StartCoroutine(WaitAndApplyStyles());
        }
        
        private IEnumerator WaitAndApplyStyles()
        {
            // Ждем, пока rootVisualElement станет доступен
            while (uiDocument == null || uiDocument.rootVisualElement == null)
            {
                yield return null;
            }
            
            // Подключаем стили после загрузки UXML
            if (launchScreenStyleSheet != null && uiDocument != null && uiDocument.rootVisualElement != null)
            {
                if (!uiDocument.rootVisualElement.styleSheets.Contains(launchScreenStyleSheet))
                {
                    uiDocument.rootVisualElement.styleSheets.Add(launchScreenStyleSheet);
                }
            }
        }

        private void Start()
        {
            StartCoroutine(InitializeApplication());
        }
        
        /// <summary>
        /// Корутина для инициализации приложения
        /// </summary>
        private IEnumerator InitializeApplication()
        {
            float startTime = Time.time;
            
            // Показываем экран запуска
            if (launchScreenController != null)
            {
                launchScreenController.Show();
                launchScreenController.ShowThrobber();
            }
            
            // Обновляем статус
            UpdateStatus("Загрузка конфигурации...");
            yield return new WaitForSeconds(0.5f);
            
            // Инициализация AR системы
            UpdateStatus("Инициализация AR...");
            yield return InitializeAR();
            
            // Загрузка данных
            UpdateStatus("Загрузка данных...");
            yield return LoadData();
            
            // Финальная инициализация
            UpdateStatus("Завершение инициализации...");
            yield return new WaitForSeconds(0.5f);
            
            // Ждем минимальное время отображения
            float elapsedTime = Time.time - startTime;
            if (elapsedTime < minDisplayTime)
            {
                yield return new WaitForSeconds(minDisplayTime - elapsedTime);
            }
            
            // Завершаем инициализацию
            isInitialized = true;
            
            if (autoHideAfterInit)
            {
                UpdateStatus("Готово!");
                yield return new WaitForSeconds(0.5f);
                
                // Показываем главный экран
                ShowMainScreen();
                
                // Скрываем LaunchScreen после перехода на MainScreen
                yield return new WaitForSeconds(0.1f); // Небольшая задержка для завершения показа MainScreen
                HideLaunchScreen();
                // НЕ отключаем gameObject - это ломает панель UIDocument!
            }
        }

        /// <summary>
        /// Инициализация AR системы
        /// </summary>
        private IEnumerator InitializeAR()
        {
            Debug.Log("[LaunchScreen] InitializeAR: начало инициализации AR через ARManager");
            
            if (Services.ARManager.Instance == null)
            {
                Debug.LogError("[LaunchScreen] ARManager не найден! Создаем...");
                new GameObject("ARManager").AddComponent<Services.ARManager>();
                yield return null;
            }

            // Запрашиваем разрешение камеры (лучше делать это тут, перед инициализацией ARManager)
            #if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
                float permissionTimeout = 5f;
                float permissionElapsed = 0f;
                while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera) && permissionElapsed < permissionTimeout)
                {
                    permissionElapsed += Time.deltaTime;
                    yield return null;
                }
            }
            #endif

            bool initComplete = false;
            bool initSuccess = false;

            Debug.Log("[LaunchScreen] Вызов ARManager.InitializeAR");
            Services.ARManager.Instance.InitializeAR((success) => {
                Debug.Log($"[LaunchScreen] Callback вызван! success={success}");
                initComplete = true;
                initSuccess = success;
            });

            // Ждем завершения инициализации с таймаутом
            float initTimeout = 30f;
            float initElapsed = 0f;
            
            Debug.Log("[LaunchScreen] Ожидание завершения инициализации AR...");
            while (!initComplete && initElapsed < initTimeout)
            {
                initElapsed += Time.deltaTime;
                yield return null;
            }

            if (!initComplete)
            {
                Debug.LogError($"[LaunchScreen] Таймаут ожидания инициализации AR ({initElapsed}s)");
                UpdateStatus("AR недоступен (таймаут). Запуск в ограниченном режиме...");
                yield return new WaitForSeconds(1.0f);
            }
            else if (!initSuccess)
            {
                Debug.LogError("[LaunchScreen] ARManager не смог инициализировать AR");
                UpdateStatus("AR недоступен. Запуск в ограниченном режиме...");
                yield return new WaitForSeconds(1.0f);
            }
            else
            {
                Debug.Log("[LaunchScreen] AR успешно инициализирован");
            }
        }
        
        /// <summary>
        /// Загрузка данных из базы данных
        /// </summary>
        private IEnumerator LoadData()
        {
            // Инициализируем MarkerService если маркеров нет
            if (Services.MarkerService.Instance != null)
            {
                var storage = new Storage.MarkerStorage();
                if (!storage.HasMarkers())
                {
                    UpdateStatus("Загрузка маркеров...");
                    // MarkerService автоматически загрузит маркеры при инициализации
                    // Ждем немного, чтобы дать время на загрузку
                    yield return new WaitForSeconds(1.0f);
                }
                else
                {
                    // Маркеры уже есть, обновление происходит в фоне
                    UpdateStatus("Проверка обновлений...");
                    yield return new WaitForSeconds(0.3f);
                }
            }
            else
            {
                yield return new WaitForSeconds(0.5f);
            }
        }
        
        /// <summary>
        /// Обновляет статус на экране запуска
        /// </summary>
        private void UpdateStatus(string status)
        {
            if (launchScreenController != null)
            {
                launchScreenController.SetStatus(status);
            }
            Debug.Log($"[LaunchScreen] {status}");
        }
        
        /// <summary>
        /// Скрывает экран запуска
        /// </summary>
        public void HideLaunchScreen()
        {
            if (launchScreenController != null)
            {
                launchScreenController.HideThrobber();
                launchScreenController.Hide();
            }
        }
        
        /// <summary>
        /// Показывает экран запуска
        /// </summary>
        public void ShowLaunchScreen()
        {
            if (launchScreenController != null)
            {
                launchScreenController.Show();
                launchScreenController.ShowThrobber();
            }
        }
        
        /// <summary>
        /// Проверяет, завершена ли инициализация
        /// </summary>
        public bool IsInitialized => isInitialized;
        
        /// <summary>
        /// Показывает главный экран
        /// </summary>
        private void ShowMainScreen()
        {
            // Ищем MainScreenManager если он не назначен
            if (mainScreenManager == null)
            {
                mainScreenManager = FindFirstObjectByType<MainScreenManager>();
            }
            
            if (mainScreenManager != null)
            {
                mainScreenManager.Show();
            }
            else
            {
                Debug.LogWarning("[LaunchScreen] MainScreenManager не найден. Главный экран не будет отображен.");
            }
        }
        
        #if UNITY_EDITOR
        /// <summary>
        /// Перезагружает UI для горячей перезагрузки в Editor режиме
        /// </summary>
        public void ReloadUI()
        {
            if (uiDocument == null || launchScreenController == null) return;
            
            // Сохраняем текущее состояние
            bool wasVisible = launchScreenController.gameObject.activeSelf && 
                             (uiDocument.rootVisualElement?.style.display == DisplayStyle.Flex);
            
            // В Editor режиме используем AssetDatabase для перезагрузки
            launchScreenUXML = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/Resources/UI/Views/LaunchScreen/LaunchScreen.uxml");
            launchScreenStyleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/Resources/UI/Views/LaunchScreen/LaunchScreen.uss");
            
            // Fallback на Resources
            if (launchScreenUXML == null)
            {
                var oldUXML = launchScreenUXML;
                if (oldUXML != null) Resources.UnloadAsset(oldUXML);
                launchScreenUXML = Resources.Load<VisualTreeAsset>("UI/Views/LaunchScreen/LaunchScreen");
            }
            if (launchScreenStyleSheet == null)
            {
                var oldStyleSheet = launchScreenStyleSheet;
                if (oldStyleSheet != null) Resources.UnloadAsset(oldStyleSheet);
                launchScreenStyleSheet = Resources.Load<StyleSheet>("UI/Views/LaunchScreen/LaunchScreen");
            }
            
            // Пересоздаем дерево элементов
            uiDocument.visualTreeAsset = null;
            uiDocument.visualTreeAsset = launchScreenUXML;
            
            // Обновляем стили
            if (launchScreenStyleSheet != null)
            {
                launchScreenController.StyleSheet = launchScreenStyleSheet;
            }
            
            // Переинициализируем контроллер (через корутину, так как он использует WaitAndApplyStyles)
            StartCoroutine(WaitAndReloadController(wasVisible));
            
            Debug.Log("[LaunchScreenManager] UI перезагружен");
        }
        
        private IEnumerator WaitAndReloadController(bool wasVisible)
        {
            // Ждем, пока rootVisualElement станет доступен
            while (uiDocument == null || uiDocument.rootVisualElement == null)
            {
                yield return null;
            }
            
            // Применяем стили
            if (launchScreenStyleSheet != null && uiDocument != null && uiDocument.rootVisualElement != null)
            {
                if (!uiDocument.rootVisualElement.styleSheets.Contains(launchScreenStyleSheet))
                {
                    uiDocument.rootVisualElement.styleSheets.Add(launchScreenStyleSheet);
                }
            }
            
            // Восстанавливаем видимость
            if (wasVisible)
            {
                launchScreenController.Show();
            }
            else
            {
                launchScreenController.Hide();
            }
        }
        #endif
    }
}

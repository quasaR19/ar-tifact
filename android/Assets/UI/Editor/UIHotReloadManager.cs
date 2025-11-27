using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

namespace ARArtifact.UI.Editor
{
    /// <summary>
    /// Менеджер горячей перезагрузки UI в Editor режиме
    /// Отслеживает изменения UXML/USS файлов и автоматически перезагружает UI
    /// </summary>
    [InitializeOnLoad]
    public static class UIHotReloadManager
    {
        private static Dictionary<string, System.DateTime> _fileTimestamps = new Dictionary<string, System.DateTime>();
        private static bool _isReloading = false;
        
        static UIHotReloadManager()
        {
            // Инициализация при загрузке редактора
            EditorApplication.update += OnUpdate;
            
            // Отслеживание изменений ассетов
            AssetDatabase.importPackageCompleted += OnAssetsImported;
            AssetDatabase.importPackageCancelled += OnAssetsImported;
            
            // Автоматическая перезагрузка UI после горячей перезагрузки скриптов
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
            
            Debug.Log("[UIHotReload] Система горячей перезагрузки UI инициализирована");
        }
        
        private static void OnUpdate()
        {
            // Работает только в Play Mode
            if (!EditorApplication.isPlaying) return;
            if (_isReloading) return;
            
            // Проверяем изменения файлов каждые 0.5 секунды
            if (Time.time % 0.5f < 0.1f)
            {
                CheckForFileChanges();
            }
        }
        
        private static void CheckForFileChanges()
        {
            string resourcesPath = "Assets/Resources/UI";
            
            if (!Directory.Exists(resourcesPath)) return;
            
            // Проверяем все UXML и USS файлы
            string[] uxmlFiles = Directory.GetFiles(resourcesPath, "*.uxml", SearchOption.AllDirectories);
            string[] ussFiles = Directory.GetFiles(resourcesPath, "*.uss", SearchOption.AllDirectories);
            
            bool needsReload = false;
            
            foreach (string file in uxmlFiles)
            {
                if (CheckFileChanged(file))
                {
                    needsReload = true;
                    break;
                }
            }
            
            if (!needsReload)
            {
                foreach (string file in ussFiles)
                {
                    if (CheckFileChanged(file))
                    {
                        needsReload = true;
                        break;
                    }
                }
            }
            
            if (needsReload)
            {
                ReloadAllUI();
            }
        }
        
        private static bool CheckFileChanged(string filePath)
        {
            if (!File.Exists(filePath)) return false;
            
            System.DateTime currentTime = File.GetLastWriteTime(filePath);
            
            if (_fileTimestamps.TryGetValue(filePath, out System.DateTime lastTime))
            {
                if (currentTime > lastTime)
                {
                    _fileTimestamps[filePath] = currentTime;
                    return true;
                }
            }
            else
            {
                _fileTimestamps[filePath] = currentTime;
            }
            
            return false;
        }
        
        private static void OnAssetsImported(string packageName)
        {
            // При импорте пакета сбрасываем кэш
            _fileTimestamps.Clear();
        }
        
        /// <summary>
        /// Вызывается после горячей перезагрузки скриптов
        /// Автоматически перезагружает UI, если приложение в Play Mode
        /// </summary>
        private static void OnAfterAssemblyReload()
        {
            // Перезагружаем UI только если мы в Play Mode
            if (EditorApplication.isPlaying)
            {
                // Используем delayCall для того, чтобы дать время всем скриптам инициализироваться
                EditorApplication.delayCall += () =>
                {
                    if (EditorApplication.isPlaying && !_isReloading)
                    {
                        Debug.Log("[UIHotReload] Автоматическая перезагрузка UI после горячей перезагрузки скриптов");
                        ReloadAllUI();
                    }
                };
            }
        }
        
        [MenuItem("AR Artifact/Reload All UI")]
        public static void ReloadAllUI()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Ошибка", 
                    "Горячая перезагрузка работает только в Play Mode!", "OK");
                return;
            }
            
            if (_isReloading)
            {
                Debug.LogWarning("[UIHotReload] Перезагрузка уже выполняется");
                return;
            }
            
            _isReloading = true;
            
            try
            {
                Debug.Log("[UIHotReload] Начало перезагрузки UI...");
                
                // Обновляем AssetDatabase перед перезагрузкой
                AssetDatabase.Refresh();
                
                // Сохраняем состояние навигации (исключая LaunchScreen)
                var navManager = ARArtifact.UI.Common.NavigationManager.Instance;
                ARArtifact.UI.Common.BaseScreenController activeScreen = null;
                string activeScreenType = null;
                MonoBehaviour activeManager = null;
                
                if (navManager != null)
                {
                    // Получаем текущий активный экран через рефлексию
                    var stackField = typeof(ARArtifact.UI.Common.NavigationManager)
                        .GetField("_navigationStack", 
                            System.Reflection.BindingFlags.NonPublic | 
                            System.Reflection.BindingFlags.Instance);
                    
                    if (stackField != null)
                    {
                        var stack = stackField.GetValue(navManager) as System.Collections.Generic.Stack<ARArtifact.UI.Common.BaseScreenController>;
                        if (stack != null && stack.Count > 0)
                        {
                            activeScreen = stack.Peek();
                            activeScreenType = activeScreen.GetType().Name;
                            
                            // Пропускаем LaunchScreen - он не должен восстанавливаться
                            if (activeScreenType == "LaunchScreenController")
                            {
                                activeScreen = null;
                                activeScreenType = null;
                            }
                            else
                            {
                                // Находим Manager для этого контроллера
                                var allManagers = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                                foreach (var manager in allManagers)
                                {
                                    string typeName = manager.GetType().Name;
                                    if (typeName.EndsWith("ScreenManager") && typeName != "LaunchScreenManager")
                                    {
                                        var getControllerMethod = manager.GetType().GetMethod("GetController",
                                            System.Reflection.BindingFlags.Public | 
                                            System.Reflection.BindingFlags.Instance);
                                        
                                        if (getControllerMethod != null)
                                        {
                                            var controller = getControllerMethod.Invoke(manager, null) as ARArtifact.UI.Common.BaseScreenController;
                                            if (controller == activeScreen)
                                            {
                                                activeManager = manager;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                
                // Перезагружаем Theme.uss
                ReloadTheme();
                
                // Находим все ScreenManager компоненты
                var managers = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                List<System.Action> reloadActions = new List<System.Action>();
                
                foreach (var manager in managers)
                {
                    string typeName = manager.GetType().Name;
                    
                    if (typeName.EndsWith("ScreenManager"))
                    {
                        var reloadMethod = manager.GetType().GetMethod("ReloadUI", 
                            System.Reflection.BindingFlags.Public | 
                            System.Reflection.BindingFlags.NonPublic | 
                            System.Reflection.BindingFlags.Instance);
                        
                        if (reloadMethod != null)
                        {
                            reloadActions.Add(() => reloadMethod.Invoke(manager, null));
                        }
                    }
                }
                
                // Выполняем перезагрузку
                foreach (var action in reloadActions)
                {
                    try
                    {
                        action.Invoke();
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[UIHotReload] Ошибка при перезагрузке: {e.Message}");
                    }
                }
                
                // Сначала скрываем все экраны явно (включая LaunchScreen)
                HideAllScreens();
                
                // Гарантированно скрываем LaunchScreen
                ForceHideLaunchScreen();
                
                // Обновляем домашний экран и сбрасываем стек навигации
                // MainScreen всегда должен быть главным экраном
                var mainScreenManagerInstance = Object.FindFirstObjectByType<ARArtifact.UI.MainScreenManager>();
                var homeController = mainScreenManagerInstance != null ? mainScreenManagerInstance.GetController() : null;
                
                if (navManager != null)
                {
                    navManager.ResetStack();
                    if (homeController != null)
                    {
                        navManager.SetHomeScreen(homeController);
                        // Показываем MainScreen как главный экран
                        homeController.Show();
                        Debug.Log("[UIHotReload] MainScreen установлен как главный экран");
                    }
                    else
                    {
                        Debug.LogWarning("[UIHotReload] MainScreenManager не найден!");
                    }
                }
                
                // Восстанавливаем активный экран (только если это не LaunchScreen)
                if (activeManager != null && navManager != null && activeScreenType != "LaunchScreenController")
                {
                    // Небольшая задержка для завершения инициализации всех экранов
                    EditorApplication.delayCall += () =>
                    {
                        // Получаем контроллер из сохраненного Manager
                        var getControllerMethod = activeManager.GetType().GetMethod("GetController",
                            System.Reflection.BindingFlags.Public | 
                            System.Reflection.BindingFlags.Instance);
                        
                        if (getControllerMethod != null)
                        {
                            var screenToShow = getControllerMethod.Invoke(activeManager, null) as ARArtifact.UI.Common.BaseScreenController;
                            
                            if (screenToShow != null && navManager != null)
                            {
                                // Проверяем, что это не LaunchScreen
                                string screenTypeName = screenToShow.GetType().Name;
                                if (screenTypeName == "LaunchScreenController")
                                {
                                    Debug.LogWarning("[UIHotReload] Попытка восстановить LaunchScreen - игнорируется");
                                    ForceHideLaunchScreen();
                                    return;
                                }
                                
                                // Затем показываем нужный экран через NavigationManager
                                if (homeController != null && screenToShow == homeController)
                                {
                                    // Домашний экран уже показан SetHomeScreen
                                    Debug.Log("[UIHotReload] Восстановлен домашний экран (MainScreen)");
                                }
                                else
                                {
                                    navManager.NavigateTo(screenToShow);
                                    Debug.Log($"[UIHotReload] Восстановлен активный экран: {activeScreenType}");
                                }
                                // Гарантированно скрываем LaunchScreen
                                ForceHideLaunchScreen();
                            }
                        }
                    };
                }
                else
                {
                    // Если активный экран не найден или это LaunchScreen, 
                    // убеждаемся что LaunchScreen скрыт и показываем MainScreen
                    ForceHideLaunchScreen();
                    if (homeController != null && navManager != null)
                    {
                        EditorApplication.delayCall += () =>
                        {
                            if (homeController != null)
                            {
                                homeController.Show();
                                Debug.Log("[UIHotReload] Показан MainScreen как главный экран");
                            }
                            ForceHideLaunchScreen();
                        };
                    }
                }
                
                // Обновляем кэш файлов
                RefreshFileTimestamps();
                
                Debug.Log("[UIHotReload] Перезагрузка UI завершена");
            }
            finally
            {
                _isReloading = false;
            }
        }
        
        private static void HideAllScreens()
        {
            // Скрываем все ScreenManager экраны
            var managers = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var manager in managers)
            {
                string typeName = manager.GetType().Name;
                if (typeName.EndsWith("ScreenManager"))
                {
                    var hideMethod = manager.GetType().GetMethod("Hide",
                        System.Reflection.BindingFlags.Public | 
                        System.Reflection.BindingFlags.Instance);
                    if (hideMethod != null)
                    {
                        hideMethod.Invoke(manager, null);
                    }
                }
            }
            
            // Принудительно скрываем LaunchScreen и отключаем его GameObject
            var launchManager = Object.FindFirstObjectByType<ARArtifact.UI.LaunchScreenManager>();
            if (launchManager != null)
            {
                launchManager.HideLaunchScreen();
                launchManager.gameObject.SetActive(false);
                Debug.Log("[UIHotReload] LaunchScreen скрыт и GameObject отключен в HideAllScreens");
            }
        }
        
        private static void HideLaunchScreenIfPossible()
        {
            var launchManager = Object.FindFirstObjectByType<ARArtifact.UI.LaunchScreenManager>();
            if (launchManager != null && launchManager.IsInitialized)
            {
                launchManager.HideLaunchScreen();
            }
        }
        
        /// <summary>
        /// Принудительно скрывает LaunchScreen независимо от состояния
        /// </summary>
        private static void ForceHideLaunchScreen()
        {
            var launchManager = Object.FindFirstObjectByType<ARArtifact.UI.LaunchScreenManager>();
            if (launchManager != null)
            {
                launchManager.HideLaunchScreen();
                // Отключаем GameObject для гарантированного скрытия
                launchManager.gameObject.SetActive(false);
                Debug.Log("[UIHotReload] LaunchScreen принудительно скрыт и GameObject отключен");
            }
        }
        
        private static void ReloadTheme()
        {
            // Обновляем AssetDatabase перед перезагрузкой
            AssetDatabase.Refresh();
            
            // Перезагружаем Theme.uss для всех UIDocument
            var uiDocuments = Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
            
            foreach (var doc in uiDocuments)
            {
                if (doc.rootVisualElement != null)
                {
                    // Перезагружаем Theme.uss через AssetDatabase (без кэширования)
                    var themeSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                        "Assets/Resources/UI/Styles/Theme.uss");
                    
                    if (themeSheet == null)
                    {
                        // Fallback на Resources
                        var oldTheme = Resources.Load<StyleSheet>("UI/Styles/Theme");
                        if (oldTheme != null) Resources.UnloadAsset(oldTheme);
                        themeSheet = Resources.Load<StyleSheet>("UI/Styles/Theme");
                    }
                    
                    if (themeSheet != null)
                    {
                        // Обновляем стили в rootVisualElement
                        // VisualElementStyleSheetSet не поддерживает foreach, поэтому используем Contains и Remove
                        var sheets = doc.rootVisualElement.styleSheets;
                        
                        // Пытаемся удалить старый Theme (если он был загружен через Resources)
                        var oldThemeFromResources = Resources.Load<StyleSheet>("UI/Styles/Theme");
                        if (oldThemeFromResources != null && oldThemeFromResources != themeSheet && sheets.Contains(oldThemeFromResources))
                        {
                            sheets.Remove(oldThemeFromResources);
                        }
                        
                        // Пытаемся удалить старый Theme (если он был загружен через AssetDatabase)
                        // Используем известный путь для поиска
                        var oldThemeAsset = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Resources/UI/Styles/Theme.uss");
                        if (oldThemeAsset != null && oldThemeAsset != themeSheet && sheets.Contains(oldThemeAsset))
                        {
                            sheets.Remove(oldThemeAsset);
                        }
                        
                        // Добавляем новый Theme.uss (если еще не добавлен)
                        // Theme обычно подключается через UXML, но добавляем вручную для гарантии
                        if (!sheets.Contains(themeSheet))
                        {
                            sheets.Add(themeSheet);
                        }
                    }
                }
            }
        }
        
        private static void RefreshFileTimestamps()
        {
            _fileTimestamps.Clear();
            string resourcesPath = "Assets/Resources/UI";
            
            if (!Directory.Exists(resourcesPath)) return;
            
            string[] files = Directory.GetFiles(resourcesPath, "*.*", SearchOption.AllDirectories);
            
            foreach (string file in files)
            {
                if (file.EndsWith(".uxml") || file.EndsWith(".uss"))
                {
                    if (File.Exists(file))
                    {
                        _fileTimestamps[file] = File.GetLastWriteTime(file);
                    }
                }
            }
        }
    }
}


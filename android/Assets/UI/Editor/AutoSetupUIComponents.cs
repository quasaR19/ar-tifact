using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace ARArtifact.UI.Editor
{
    /// <summary>
    /// Автоматически добавляет необходимые компоненты на UI GameObject'ы в сцене
    /// </summary>
    [InitializeOnLoad]
    public static class AutoSetupUIComponents
    {
        static AutoSetupUIComponents()
        {
            EditorSceneManager.sceneOpened += OnSceneOpened;
        }
        
        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            // Проверяем и добавляем компоненты
            CheckAndAddComponents();
        }
        
        [MenuItem("AR Artifact/UI/Setup UI Components")]
        private static void SetupUIComponents()
        {
            CheckAndAddComponents();
        }
        
        private static void CheckAndAddComponents()
        {
            bool modified = false;
            
            // Проверяем LaunchScreen
            GameObject launchScreen = GameObject.Find("LaunchScreen");
            if (launchScreen != null)
            {
                var manager = launchScreen.GetComponent<ARArtifact.UI.LaunchScreenManager>();
                if (manager == null)
                {
                    manager = launchScreen.AddComponent<ARArtifact.UI.LaunchScreenManager>();
                    Debug.Log("[AutoSetup] Добавлен компонент LaunchScreenManager на LaunchScreen");
                    modified = true;
                }
            }
            else
            {
                Debug.LogWarning("[AutoSetup] GameObject 'LaunchScreen' не найден в сцене");
            }
            
            // Проверяем MainScreen
            GameObject mainScreen = GameObject.Find("MainScreen");
            if (mainScreen != null)
            {
                var manager = mainScreen.GetComponent<ARArtifact.UI.MainScreenManager>();
                if (manager == null)
                {
                    manager = mainScreen.AddComponent<ARArtifact.UI.MainScreenManager>();
                    Debug.Log("[AutoSetup] Добавлен компонент MainScreenManager на MainScreen");
                    modified = true;
                }
            }
            else
            {
                Debug.LogWarning("[AutoSetup] GameObject 'MainScreen' не найден в сцене");
            }
            
            // Проверяем MarkersScreen
            GameObject markersScreen = GameObject.Find("MarkersScreen");
            if (markersScreen != null)
            {
                var manager = markersScreen.GetComponent<ARArtifact.UI.MarkersScreenManager>();
                if (manager == null)
                {
                    manager = markersScreen.AddComponent<ARArtifact.UI.MarkersScreenManager>();
                    Debug.Log("[AutoSetup] Добавлен компонент MarkersScreenManager на MarkersScreen");
                    modified = true;
                }
            }
            else
            {
                Debug.LogWarning("[AutoSetup] GameObject 'MarkersScreen' не найден в сцене (создайте его вручную, если нужен)");
            }
            
            if (modified)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                Debug.Log("[AutoSetup] Компоненты успешно добавлены. Сохраните сцену.");
            }
        }
    }
}


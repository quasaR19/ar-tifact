using System.IO;
using UnityEngine;
using UnityEditor;

namespace ARArtifact.UI.Editor
{
    /// <summary>
    /// Автоматическая синхронизация UI файлов при изменении файлов в Resources
    /// </summary>
    public class UIAutoSyncProcessor : AssetPostprocessor
    {
        private const string RESOURCES_BASE_PATH = "Assets/Resources/UI/Views";
        private const string ASSETS_BASE_PATH = "Assets/UI/Views";
        private const string RESOURCES_THEME_PATH = "Assets/Resources/UI/Styles/Theme.uss";
        private const string ASSETS_THEME_PATH = "Assets/UI/Styles/Theme.uss";
        
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            bool needsSync = false;
            
            // Проверяем измененные файлы в Resources/UI/Views
            foreach (string assetPath in importedAssets)
            {
                if (assetPath.StartsWith(RESOURCES_BASE_PATH) && 
                    (assetPath.EndsWith(".uss") || assetPath.EndsWith(".uxml")))
                {
                    needsSync = true;
                    break;
                }
                
                // Проверяем Theme.uss
                if (assetPath == RESOURCES_THEME_PATH)
                {
                    SyncThemeFile();
                }
            }
            
            // Синхронизируем измененные файлы
            if (needsSync)
            {
                SyncChangedFiles(importedAssets);
            }
        }
        
        private static void SyncChangedFiles(string[] importedAssets)
        {
            foreach (string assetPath in importedAssets)
            {
                if (!assetPath.StartsWith(RESOURCES_BASE_PATH)) continue;
                if (!assetPath.EndsWith(".uss") && !assetPath.EndsWith(".uxml")) continue;
                
                // Извлекаем имя экрана из пути
                string relativePath = assetPath.Substring(RESOURCES_BASE_PATH.Length + 1);
                string[] pathParts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                
                if (pathParts.Length < 2) continue;
                
                string screenName = pathParts[0];
                string fileName = pathParts[1];
                string targetPath = Path.Combine(ASSETS_BASE_PATH, screenName, fileName);
                
                // Создаем папку если её нет
                string targetDir = Path.GetDirectoryName(targetPath);
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }
                
                // Копируем файл
                try
                {
                    File.Copy(assetPath, targetPath, true);
                    Debug.Log($"[UIAutoSync] Автоматически синхронизирован: {fileName}");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[UIAutoSync] Ошибка при синхронизации {fileName}: {e.Message}");
                }
            }
            
            AssetDatabase.Refresh();
        }
        
        private static void SyncThemeFile()
        {
            if (!File.Exists(RESOURCES_THEME_PATH)) return;
            
            try
            {
                string targetDir = Path.GetDirectoryName(ASSETS_THEME_PATH);
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }
                
                File.Copy(RESOURCES_THEME_PATH, ASSETS_THEME_PATH, true);
                Debug.Log("[UIAutoSync] Theme.uss автоматически синхронизирован");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UIAutoSync] Ошибка при синхронизации Theme.uss: {e.Message}");
            }
        }
    }
}


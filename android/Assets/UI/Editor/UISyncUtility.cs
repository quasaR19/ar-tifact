using System.IO;
using UnityEngine;
using UnityEditor;

namespace ARArtifact.UI.Editor
{
    /// <summary>
    /// Утилита для синхронизации USS и UXML файлов между Resources и Assets/UI/Views
    /// </summary>
    public static class UISyncUtility
    {
        private const string RESOURCES_BASE_PATH = "Assets/Resources/UI/Views";
        private const string ASSETS_BASE_PATH = "Assets/UI/Views";
        
        [MenuItem("AR Artifact/Sync All UI Files")]
        public static void SyncAllUIFiles()
        {
            if (!Directory.Exists(RESOURCES_BASE_PATH))
            {
                EditorUtility.DisplayDialog("Ошибка", 
                    $"Папка {RESOURCES_BASE_PATH} не найдена!", "OK");
                return;
            }
            
            int syncedCount = 0;
            int errorCount = 0;
            
            // Получаем все папки экранов из Resources
            string[] screenFolders = Directory.GetDirectories(RESOURCES_BASE_PATH);
            
            foreach (string screenFolder in screenFolders)
            {
                string screenName = Path.GetFileName(screenFolder);
                string targetFolder = Path.Combine(ASSETS_BASE_PATH, screenName);
                
                // Создаем папку в Assets/UI/Views если её нет
                if (!Directory.Exists(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                    AssetDatabase.Refresh();
                }
                
                // Синхронизируем USS файлы
                string ussSource = Path.Combine(screenFolder, $"{screenName}.uss");
                string ussTarget = Path.Combine(targetFolder, $"{screenName}.uss");
                
                if (File.Exists(ussSource))
                {
                    try
                    {
                        File.Copy(ussSource, ussTarget, true);
                        syncedCount++;
                        Debug.Log($"[UISync] Синхронизирован: {screenName}.uss");
                    }
                    catch (System.Exception e)
                    {
                        errorCount++;
                        Debug.LogError($"[UISync] Ошибка при синхронизации {screenName}.uss: {e.Message}");
                    }
                }
                
                // Синхронизируем UXML файлы
                string uxmlSource = Path.Combine(screenFolder, $"{screenName}.uxml");
                string uxmlTarget = Path.Combine(targetFolder, $"{screenName}.uxml");
                
                if (File.Exists(uxmlSource))
                {
                    try
                    {
                        File.Copy(uxmlSource, uxmlTarget, true);
                        syncedCount++;
                        Debug.Log($"[UISync] Синхронизирован: {screenName}.uxml");
                    }
                    catch (System.Exception e)
                    {
                        errorCount++;
                        Debug.LogError($"[UISync] Ошибка при синхронизации {screenName}.uxml: {e.Message}");
                    }
                }
            }
            
            AssetDatabase.Refresh();
            
            string message = $"Синхронизация завершена!\n\nСинхронизировано файлов: {syncedCount}";
            if (errorCount > 0)
            {
                message += $"\nОшибок: {errorCount}";
            }
            
            EditorUtility.DisplayDialog("Синхронизация UI файлов", message, "OK");
            Debug.Log($"[UISync] Итого: {syncedCount} файлов синхронизировано, {errorCount} ошибок");
        }
        
        [MenuItem("AR Artifact/Sync Theme.uss")]
        public static void SyncTheme()
        {
            string themeSource = "Assets/Resources/UI/Styles/Theme.uss";
            string themeTarget = "Assets/UI/Styles/Theme.uss";
            
            if (!File.Exists(themeSource))
            {
                EditorUtility.DisplayDialog("Ошибка", 
                    $"Файл {themeSource} не найден!", "OK");
                return;
            }
            
            try
            {
                // Создаем папку если её нет
                string targetDir = Path.GetDirectoryName(themeTarget);
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }
                
                File.Copy(themeSource, themeTarget, true);
                AssetDatabase.Refresh();
                
                EditorUtility.DisplayDialog("Успех", 
                    "Theme.uss успешно синхронизирован!", "OK");
                Debug.Log("[UISync] Theme.uss синхронизирован");
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("Ошибка", 
                    $"Ошибка при синхронизации Theme.uss: {e.Message}", "OK");
                Debug.LogError($"[UISync] Ошибка: {e.Message}");
            }
        }
        
        [MenuItem("AR Artifact/Validate UI Files Structure")]
        public static void ValidateUIFilesStructure()
        {
            System.Text.StringBuilder report = new System.Text.StringBuilder();
            report.AppendLine("=== Отчет о структуре UI файлов ===\n");
            
            int missingCount = 0;
            int extraCount = 0;
            
            // Проверяем файлы в Resources
            if (Directory.Exists(RESOURCES_BASE_PATH))
            {
                string[] screenFolders = Directory.GetDirectories(RESOURCES_BASE_PATH);
                report.AppendLine($"Найдено экранов в Resources: {screenFolders.Length}\n");
                
                foreach (string screenFolder in screenFolders)
                {
                    string screenName = Path.GetFileName(screenFolder);
                    string ussPath = Path.Combine(screenFolder, $"{screenName}.uss");
                    string uxmlPath = Path.Combine(screenFolder, $"{screenName}.uxml");
                    
                    report.AppendLine($"\n[{screenName}]");
                    report.AppendLine($"  Resources/USS: {(File.Exists(ussPath) ? "✓" : "✗")}");
                    report.AppendLine($"  Resources/UXML: {(File.Exists(uxmlPath) ? "✓" : "✗")}");
                    
                    // Проверяем наличие в Assets/UI/Views
                    string targetFolder = Path.Combine(ASSETS_BASE_PATH, screenName);
                    string targetUss = Path.Combine(targetFolder, $"{screenName}.uss");
                    string targetUxml = Path.Combine(targetFolder, $"{screenName}.uxml");
                    
                    bool hasUss = File.Exists(targetUss);
                    bool hasUxml = File.Exists(targetUxml);
                    
                    report.AppendLine($"  Assets/UI/USS: {(hasUss ? "✓" : "✗")}");
                    report.AppendLine($"  Assets/UI/UXML: {(hasUxml ? "✓" : "✗")}");
                    
                    if (!hasUss || !hasUxml)
                    {
                        missingCount++;
                    }
                }
            }
            
            // Проверяем лишние файлы в Assets/UI/Views
            if (Directory.Exists(ASSETS_BASE_PATH))
            {
                string[] assetsFolders = Directory.GetDirectories(ASSETS_BASE_PATH);
                foreach (string assetsFolder in assetsFolders)
                {
                    string screenName = Path.GetFileName(assetsFolder);
                    string resourcesFolder = Path.Combine(RESOURCES_BASE_PATH, screenName);
                    
                    if (!Directory.Exists(resourcesFolder))
                    {
                        extraCount++;
                        report.AppendLine($"\n[!] Лишняя папка в Assets/UI/Views: {screenName}");
                    }
                }
            }
            
            report.AppendLine($"\n=== Итого ===");
            report.AppendLine($"Отсутствующих файлов: {missingCount}");
            report.AppendLine($"Лишних папок: {extraCount}");
            
            Debug.Log(report.ToString());
            EditorUtility.DisplayDialog("Валидация структуры UI", 
                report.ToString(), "OK");
        }
    }
}


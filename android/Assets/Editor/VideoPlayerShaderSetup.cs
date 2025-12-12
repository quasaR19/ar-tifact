using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

namespace ARArtifact.Editor
{
    /// <summary>
    /// Скрипт для настройки компиляции шейдеров VideoPlayer при сборке на Android.
    /// Обеспечивает, что необходимые шейдеры включены в сборку.
    /// </summary>
    public static class VideoPlayerShaderSetup
    {
        private const string UnlitTextureShaderName = "Unlit/Texture";
        private const string URPUnlitShaderName = "Universal Render Pipeline/Unlit";
        
        [MenuItem("AR Artifact/Setup VideoPlayer Shaders for Android")]
        public static void SetupVideoPlayerShaders()
        {
            bool hasChanges = false;
            
            // Try to find URP shader first, then fallback to Built-in
            Shader unlitTextureShader = Shader.Find(URPUnlitShaderName);
            if (unlitTextureShader == null)
            {
                unlitTextureShader = Shader.Find(UnlitTextureShaderName);
            }
            
            if (unlitTextureShader == null)
            {
                EditorUtility.DisplayDialog("Error", 
                    $"Shaders '{URPUnlitShaderName}' and '{UnlitTextureShaderName}' not found. Make sure they are available in the project.", 
                    "OK");
                return;
            }
            
            string shaderNameToAdd = unlitTextureShader.name;
            
            // Загружаем GraphicsSettings через UnityEditor API
            // Используем прямой доступ к ProjectSettings
            var graphicsSettingsPath = "ProjectSettings/GraphicsSettings.asset";
            var graphicsSettingsAssets = AssetDatabase.LoadAllAssetsAtPath(graphicsSettingsPath);
            
            if (graphicsSettingsAssets == null || graphicsSettingsAssets.Length == 0)
            {
                EditorUtility.DisplayDialog("Ошибка", 
                    "Не удалось загрузить GraphicsSettings.asset. Убедитесь, что проект открыт в Unity Editor.", 
                    "OK");
                return;
            }
            
            // Получаем GraphicsSettings объект
            var graphicsSettingsObj = new SerializedObject(graphicsSettingsAssets[0]);
            
            if (graphicsSettingsObj != null)
            {
                
                // Получаем текущий список всегда включаемых шейдеров
                var alwaysIncludedShadersProp = graphicsSettingsObj.FindProperty("m_AlwaysIncludedShaders");
                
                if (alwaysIncludedShadersProp != null)
                {
                    // Check if shader is already in the list
                    bool shaderAlreadyIncluded = false;
                    for (int i = 0; i < alwaysIncludedShadersProp.arraySize; i++)
                    {
                        var shaderProp = alwaysIncludedShadersProp.GetArrayElementAtIndex(i);
                        var shader = shaderProp.objectReferenceValue as Shader;
                        if (shader != null && (shader.name == shaderNameToAdd || shader.name == UnlitTextureShaderName))
                        {
                            shaderAlreadyIncluded = true;
                            break;
                        }
                    }
                    
                    if (!shaderAlreadyIncluded)
                    {
                        // Add shader to the list
                        alwaysIncludedShadersProp.arraySize++;
                        var newShaderProp = alwaysIncludedShadersProp.GetArrayElementAtIndex(
                            alwaysIncludedShadersProp.arraySize - 1);
                        newShaderProp.objectReferenceValue = unlitTextureShader;
                        hasChanges = true;
                        Debug.Log($"[VideoPlayerShaderSetup] Shader '{shaderNameToAdd}' added to Always Included Shaders list.");
                    }
                    else
                    {
                        Debug.Log($"[VideoPlayerShaderSetup] Shader '{shaderNameToAdd}' already included in Always Included Shaders list.");
                    }
                }
                
                // Configure VideoShadersIncludeMode
                // Values: 0 = Never Include, 1 = Always Include, 2 = Include If Used
                // Set to "Always Include" for guarantee
                var videoShadersIncludeModeProp = graphicsSettingsObj.FindProperty("m_VideoShadersIncludeMode");
                
                if (videoShadersIncludeModeProp != null)
                {
                    int currentMode = videoShadersIncludeModeProp.intValue;
                    if (currentMode != 1) // 1 = Always Include
                    {
                        videoShadersIncludeModeProp.intValue = 1;
                        hasChanges = true;
                        Debug.Log($"[VideoPlayerShaderSetup] VideoShadersIncludeMode changed from {currentMode} to 1 (Always Include).");
                    }
                    else
                    {
                        Debug.Log($"[VideoPlayerShaderSetup] VideoShadersIncludeMode already set to 'Always Include' (1).");
                    }
                }
                
                if (hasChanges)
                {
                    graphicsSettingsObj.ApplyModifiedProperties();
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    
                    EditorUtility.DisplayDialog("Setup Complete", 
                        "VideoPlayer shader compilation settings successfully applied:\n\n" +
                        $"✓ Shader '{shaderNameToAdd}' added to Always Included Shaders\n" +
                        "✓ VideoShadersIncludeMode set to 'Always Include'\n\n" +
                        "These settings will ensure necessary shaders are compiled when building for Android.\n\n" +
                        "IMPORTANT: You need to rebuild the project for changes to take effect!", 
                        "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Settings Already Applied", 
                        "All necessary settings for VideoPlayer shader compilation are already applied.\n\n" +
                        "If you still see 'Video shaders not found' error, try:\n" +
                        "1. Rebuild the project\n" +
                        "2. Check that VideoShadersIncludeMode is set to 'Always Include'\n" +
                        "3. Verify shaders are in Always Included Shaders list", 
                        "OK");
                }
            }
            else
            {
                EditorUtility.DisplayDialog("Ошибка", 
                    "Не удалось загрузить GraphicsSettings.asset. Попробуйте настроить вручную через Edit > Project Settings > Graphics.", 
                    "OK");
            }
        }
        
        [MenuItem("AR Artifact/Check VideoPlayer Shader Settings")]
        public static void CheckVideoPlayerShaderSettings()
        {
            var graphicsSettingsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            
            if (graphicsSettingsAssets == null || graphicsSettingsAssets.Length == 0)
            {
                EditorUtility.DisplayDialog("Ошибка", 
                    "Не удалось загрузить GraphicsSettings.asset. Убедитесь, что проект открыт в Unity Editor.", 
                    "OK");
                return;
            }
            
            var graphicsSettingsObj = new SerializedObject(graphicsSettingsAssets[0]);
            var videoShadersIncludeModeProp = graphicsSettingsObj.FindProperty("m_VideoShadersIncludeMode");
            
            int mode = videoShadersIncludeModeProp != null ? videoShadersIncludeModeProp.intValue : -1;
            string modeName = mode switch
            {
                0 => "Never Include",
                1 => "Always Include",
                2 => "Include If Used",
                _ => "Unknown"
            };
            
            Shader unlitTextureShader = Shader.Find(UnlitTextureShaderName);
            bool shaderFound = unlitTextureShader != null;
            
            var alwaysIncludedShadersProp = graphicsSettingsObj.FindProperty("m_AlwaysIncludedShaders");
            bool shaderIncluded = false;
            if (shaderFound && alwaysIncludedShadersProp != null)
            {
                for (int i = 0; i < alwaysIncludedShadersProp.arraySize; i++)
                {
                    var shaderProp = alwaysIncludedShadersProp.GetArrayElementAtIndex(i);
                    var shader = shaderProp.objectReferenceValue as Shader;
                    if (shader != null && shader.name == UnlitTextureShaderName)
                    {
                        shaderIncluded = true;
                        break;
                    }
                }
            }
            
            string message = $"Текущие настройки шейдеров VideoPlayer:\n\n" +
                           $"VideoShadersIncludeMode: {mode} ({modeName})\n" +
                           $"Шейдер '{UnlitTextureShaderName}': {(shaderFound ? "Найден" : "Не найден")}\n" +
                           $"В Always Included Shaders: {(shaderIncluded ? "Да ✓" : "Нет ✗")}\n\n";
            
            if (mode == 1 && shaderIncluded)
            {
                message += "Все настройки корректны!";
            }
            else
            {
                message += "Рекомендуется запустить 'Setup VideoPlayer Shaders for Android' для настройки.";
            }
            
            EditorUtility.DisplayDialog("Проверка настроек", message, "OK");
        }
    }
}


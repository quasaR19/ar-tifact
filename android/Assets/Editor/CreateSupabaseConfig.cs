using UnityEngine;
using UnityEditor;

namespace ARArtifact.Editor
{
    /// <summary>
    /// Editor скрипт для создания конфига Supabase
    /// </summary>
    public static class CreateSupabaseConfig
    {
        [MenuItem("AR Artifact/Create Supabase Config")]
        public static void CreateConfig()
        {
            // Проверяем, существует ли уже конфиг
            var existingConfig = Resources.Load<Config.SupabaseConfig>("SupabaseConfig");
            if (existingConfig != null)
            {
                EditorUtility.DisplayDialog("Конфиг уже существует", 
                    "Конфиг SupabaseConfig уже существует в папке Resources.", "OK");
                Selection.activeObject = existingConfig;
                return;
            }
            
            // Создаем папку Resources, если её нет
            string resourcesPath = "Assets/Resources";
            if (!AssetDatabase.IsValidFolder(resourcesPath))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }
            
            // Создаем конфиг
            Config.SupabaseConfig config = ScriptableObject.CreateInstance<Config.SupabaseConfig>();
            string assetPath = "Assets/Resources/SupabaseConfig.asset";
            
            AssetDatabase.CreateAsset(config, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            // Выделяем созданный конфиг
            Selection.activeObject = config;
            EditorUtility.FocusProjectWindow();
            
            Debug.Log($"[CreateSupabaseConfig] Конфиг создан: {assetPath}");
            EditorUtility.DisplayDialog("Конфиг создан", 
                $"Конфиг SupabaseConfig создан в {assetPath}\n\nНе забудьте заполнить:\n- Supabase URL\n- Supabase Anon Key", "OK");
        }
    }
}


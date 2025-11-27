using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

namespace ARArtifact.UI.Editor
{
    /// <summary>
    /// Editor скрипт для автоматической настройки MainScreenManager
    /// </summary>
    [CustomEditor(typeof(MainScreenManager))]
    [CanEditMultipleObjects]
    public class MainScreenManagerEditor : UnityEditor.Editor
    {
        private void OnEnable()
        {
            MainScreenManager manager = (MainScreenManager)target;
            
            // Автоматически настраиваем ссылки если они не назначены
            SerializedProperty uiDocumentProp = serializedObject.FindProperty("uiDocument");
            SerializedProperty uxmlProp = serializedObject.FindProperty("mainScreenUXML");
            SerializedProperty ussProp = serializedObject.FindProperty("mainScreenStyleSheet");
            
            // Настраиваем UIDocument
            UIDocument uiDocument = null;
            if (uiDocumentProp.objectReferenceValue == null)
            {
                uiDocument = manager.GetComponent<UIDocument>();
                if (uiDocument == null)
                {
                    uiDocument = manager.gameObject.AddComponent<UIDocument>();
                }
                uiDocumentProp.objectReferenceValue = uiDocument;
            }
            else
            {
                uiDocument = uiDocumentProp.objectReferenceValue as UIDocument;
            }
            
            // Настраиваем UXML - сначала пытаемся загрузить из Resources (оптимизация)
            if (uxmlProp.objectReferenceValue == null)
            {
                // Пробуем загрузить из Resources
                string resourcesPath = "Assets/Resources/UI/Views/MainScreen/MainScreen.uxml";
                VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(resourcesPath);
                
                // Если не найдено в Resources, пробуем из Assets/UI/Views (обратная совместимость)
                if (uxml == null)
            {
                string uxmlPath = "Assets/UI/Views/MainScreen/MainScreen.uxml";
                    uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
                }
                
                if (uxml != null)
                {
                    uxmlProp.objectReferenceValue = uxml;
                }
            }
            
            // Настраиваем USS - сначала пытаемся загрузить из Resources (оптимизация)
            if (ussProp.objectReferenceValue == null)
            {
                // Пробуем загрузить из Resources
                string resourcesPath = "Assets/Resources/UI/Views/MainScreen/MainScreen.uss";
                StyleSheet uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(resourcesPath);
                
                // Если не найдено в Resources, пробуем из Assets/UI/Views (обратная совместимость)
                if (uss == null)
            {
                string ussPath = "Assets/UI/Views/MainScreen/MainScreen.uss";
                    uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);
                }
                
                if (uss != null)
                {
                    ussProp.objectReferenceValue = uss;
                }
            }
            
            // Настраиваем UIDocument
            if (uiDocument != null)
            {
                SerializedObject uiDocSerialized = new SerializedObject(uiDocument);
                SerializedProperty panelSettingsProp = uiDocSerialized.FindProperty("m_PanelSettings");
                
                // Создаем PanelSettings если его нет
                if (panelSettingsProp.objectReferenceValue == null)
                {
                    // Ищем существующий PanelSettings в проекте
                    string[] guids = AssetDatabase.FindAssets("t:PanelSettings");
                    PanelSettings panelSettings = null;
                    
                    if (guids.Length > 0)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                        panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
                    }
                    
                    // Если не нашли, создаем новый
                    if (panelSettings == null)
                    {
                        string panelSettingsPath = "Assets/UI/PanelSettings.asset";
                        panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                        AssetDatabase.CreateAsset(panelSettings, panelSettingsPath);
                        AssetDatabase.SaveAssets();
                        Debug.Log($"[MainScreen] Создан PanelSettings: {panelSettingsPath}");
                    }
                    
                    // Назначаем PanelSettings
                    panelSettingsProp.objectReferenceValue = panelSettings;
                    uiDocSerialized.ApplyModifiedProperties();
                }
                
                // Настраиваем visualTreeAsset
                if (uxmlProp.objectReferenceValue != null)
                {
                    SerializedProperty visualTreeAssetProp = uiDocSerialized.FindProperty("m_SourceAsset");
                    if (visualTreeAssetProp != null && visualTreeAssetProp.objectReferenceValue == null)
                    {
                        visualTreeAssetProp.objectReferenceValue = uxmlProp.objectReferenceValue as VisualTreeAsset;
                        uiDocSerialized.ApplyModifiedProperties();
                    }
                }
            }
            
            serializedObject.ApplyModifiedProperties();
        }
        
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Ссылки на UXML и USS файлы настраиваются автоматически при добавлении компонента.", MessageType.Info);
        }
    }
}

using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

namespace ARArtifact.UI.Editor
{
    /// <summary>
    /// Editor скрипт для автоматической настройки LaunchScreenManager
    /// </summary>
    [CustomEditor(typeof(LaunchScreenManager))]
    [CanEditMultipleObjects]
    public class LaunchScreenManagerEditor : UnityEditor.Editor
    {
        private const string UXML_GUID = "27fd69f3bcc02b047a9202e4f759e8fa";
        private const string USS_GUID = "7bb4e930a2bd61642bd5074144264289";
        
        private void OnEnable()
        {
            LaunchScreenManager manager = (LaunchScreenManager)target;
            
            // Автоматически настраиваем ссылки если они не назначены
            SerializedProperty uiDocumentProp = serializedObject.FindProperty("uiDocument");
            SerializedProperty uxmlProp = serializedObject.FindProperty("launchScreenUXML");
            SerializedProperty ussProp = serializedObject.FindProperty("launchScreenStyleSheet");
            
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
            
            // Настраиваем UXML
            if (uxmlProp.objectReferenceValue == null)
            {
                string uxmlPath = "Assets/UI/Views/LaunchScreen/LaunchScreen.uxml";
                VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
                if (uxml != null)
                {
                    uxmlProp.objectReferenceValue = uxml;
                }
            }
            
            // Настраиваем USS
            if (ussProp.objectReferenceValue == null)
            {
                string ussPath = "Assets/UI/Views/LaunchScreen/LaunchScreen.uss";
                StyleSheet uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);
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
                        Debug.Log($"[LaunchScreen] Создан PanelSettings: {panelSettingsPath}");
                    }
                    
                    // Назначаем PanelSettings
                    panelSettingsProp.objectReferenceValue = panelSettings;
                    uiDocSerialized.ApplyModifiedProperties();
                }
                
                // Настраиваем visualTreeAsset
                if (uxmlProp.objectReferenceValue != null)
                {
                    SerializedProperty visualTreeAssetProp = uiDocSerialized.FindProperty("m_SourceAsset");
                    if (visualTreeAssetProp.objectReferenceValue == null)
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

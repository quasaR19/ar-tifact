using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using ARArtifact.Services;
using ARArtifact.Storage;
using ARArtifact.UI.Common;

namespace ARArtifact.UI
{
    public class DetailsScreenManager : MonoBehaviour
    {
        [Header("UI Configuration")]
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private VisualTreeAsset screenUXML;
        [SerializeField] private StyleSheet screenStyleSheet;

        private DetailsScreenController controller;

        private void Awake()
        {
            // НЕ выключаем gameObject - это ломает панель UIDocument!
            // Скрытие происходит через DisplayStyle.None в Hide()
            
            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
                if (uiDocument == null)
                {
                    uiDocument = gameObject.AddComponent<UIDocument>();
                }
            }

            if (screenUXML == null) screenUXML = Resources.Load<VisualTreeAsset>("UI/Views/DetailsScreen/DetailsScreen");
            if (screenStyleSheet == null) screenStyleSheet = Resources.Load<StyleSheet>("UI/Views/DetailsScreen/DetailsScreen");

            if (screenUXML != null && uiDocument.visualTreeAsset == null)
            {
                uiDocument.visualTreeAsset = screenUXML;
            }

            controller = uiDocument.GetComponent<DetailsScreenController>();
            if (controller == null)
            {
                controller = uiDocument.gameObject.AddComponent<DetailsScreenController>();
            }

            if (screenStyleSheet != null)
            {
                controller.StyleSheet = screenStyleSheet;
            }
        }

        private void Start()
        {
            // Start вызывается только если gameObject активен
            // Но мы выключили его в Awake, поэтому Start не вызовется при первом запуске
            // Инициализацию делаем при первом Show()
            
            // Если gameObject активен (например, в редакторе), инициализируем сразу
            if (gameObject.activeSelf && !_isInitialized)
            {
                InitializeController();
            }
        }

        private void OnDestroy()
        {
            if (controller != null)
            {
                controller.OnClose -= HandleCloseRequested;
            }
        }

        public void Show(string targetId)
        {
            if (string.IsNullOrEmpty(targetId)) return;
            
            ShowViaNavigation();
            
            // Fetch data
            ArtifactService.Instance.RequestArtifactForTarget(targetId, 
                result =>
                {
                    controller?.Display(result.Record);
                },
                error =>
                {
                    Debug.LogError($"[DetailsScreen] Ошибка загрузки данных для {targetId}: {error}");
                });
        }
        
        public void Show(ArtifactStorage.ArtifactRecord record)
        {
            if (controller != null && record != null)
            {
                ShowViaNavigation();
                controller.Display(record);
            }
        }

        private void ShowViaNavigation()
        {
            if (controller == null) return;
            
            // НЕ трогаем gameObject.SetActive - это ломает панель UIDocument!
            // Все экраны остаются активными, скрытие/показ через DisplayStyle
            
            // Инициализируем контроллер при первом показе, если еще не инициализирован
            if (!_isInitialized)
            {
                InitializeController();
            }
            
            if (NavigationManager.Instance != null)
            {
                NavigationManager.Instance.NavigateTo(controller);
            }
            else
            {
                controller.Show();
            }
        }
        
        private bool _isInitialized = false;
        
        private void InitializeController()
        {
            if (controller == null || _isInitialized) return;
            
            controller.Initialize(uiDocument, "Детали артефакта");
            controller.OnClose += HandleCloseRequested;
            
            _isInitialized = true;
        }

        public void Hide()
        {
             if (NavigationManager.Instance != null)
            {
                NavigationManager.Instance.GoBack();
            }
            else
            {
                controller?.Hide();
            }
        }

        private void HandleCloseRequested()
        {
            Hide();
        }
        
        public DetailsScreenController GetController()
        {
            return controller;
        }
        
        #if UNITY_EDITOR
        /// <summary>
        /// Перезагружает UI для горячей перезагрузки в Editor режиме
        /// </summary>
        public void ReloadUI()
        {
            if (uiDocument == null || controller == null) return;
            
            // Сохраняем текущее состояние
            bool wasVisible = controller.gameObject.activeSelf && 
                             (uiDocument.rootVisualElement?.style.display == DisplayStyle.Flex);
            
            // В Editor режиме используем AssetDatabase для перезагрузки
            screenUXML = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/Resources/UI/Views/DetailsScreen/DetailsScreen.uxml");
            screenStyleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/Resources/UI/Views/DetailsScreen/DetailsScreen.uss");
            
            // Fallback на Resources
            if (screenUXML == null)
            {
                var oldUXML = screenUXML;
                if (oldUXML != null) Resources.UnloadAsset(oldUXML);
                screenUXML = Resources.Load<VisualTreeAsset>("UI/Views/DetailsScreen/DetailsScreen");
            }
            if (screenStyleSheet == null)
            {
                var oldStyleSheet = screenStyleSheet;
                if (oldStyleSheet != null) Resources.UnloadAsset(oldStyleSheet);
                screenStyleSheet = Resources.Load<StyleSheet>("UI/Views/DetailsScreen/DetailsScreen");
            }
            
            // Пересоздаем дерево элементов
            uiDocument.visualTreeAsset = null;
            uiDocument.visualTreeAsset = screenUXML;
            
            // Обновляем стили
            if (screenStyleSheet != null)
            {
                controller.StyleSheet = screenStyleSheet;
            }
            
            // Переинициализируем контроллер
            controller.Initialize(uiDocument, "Детали артефакта");
            controller.OnClose += HandleCloseRequested;
            
            // Восстанавливаем видимость
            if (wasVisible)
            {
                controller.Show();
            }
            else
            {
                controller.Hide();
            }
            
            Debug.Log("[DetailsScreenManager] UI перезагружен");
        }
        #endif
    }
}

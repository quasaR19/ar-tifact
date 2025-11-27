using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using UnityGLTF;

namespace ARArtifact.UI
{
    /// <summary>
    /// Контроллер для просмотра GLB/GLTF моделей в UI Toolkit.
    /// Поддерживает загрузку моделей, управление камерой и рендеринг в RenderTexture.
    /// </summary>
    public class GLBViewerController : MonoBehaviour
    {
        private const string LogPrefix = "[GLBViewerController]";
        
        // Позиция GLB viewer далеко от центра AR сцены, чтобы избежать конфликтов
        private static readonly Vector3 ViewerWorldOffset = new Vector3(1000, 0, 0);
        
        [Header("Render Settings")]
        [SerializeField] private int renderTextureWidth = 1024;
        [SerializeField] private int renderTextureHeight = 1024;
        
        // Components
        private Camera renderCamera;
        private RenderTexture renderTexture;
        private GameObject modelContainer;
        private GameObject loadedModel;
        private GLTFComponent gltfComponent;
        private OrbitCameraController orbitController;
        private Light directionalLight;
        private Transform pivotTransform; // Независимый pivot для орбитальной камеры
        
        // UI Elements
        private VisualElement viewerContainer;
        private VisualElement renderTarget;
        private Button resetCameraButton;
        private Button centerModelButton;
        private Label statusLabel;
        
        // State
        private bool isLoading;
        private string currentModelPath;
        private GameObject loaderObject; // Храним ссылку для очистки
        
        private void Awake()
        {
            // Перемещаем GLBViewerController далеко от центра AR сцены
            transform.position = ViewerWorldOffset;
            
            SetupPivot();
            SetupRenderCamera();
            SetupLighting();
            SetupModelContainer();
        }
        
        /// <summary>
        /// Создает независимый pivot для орбитальной камеры (НЕ дочерний объект камеры).
        /// </summary>
        private void SetupPivot()
        {
            var pivotGO = new GameObject("GLBOrbitPivot");
            pivotGO.transform.SetParent(transform);
            pivotGO.transform.localPosition = Vector3.zero;
            pivotTransform = pivotGO.transform;
        }
        
        /// <summary>
        /// Настраивает камеру для рендеринга модели.
        /// </summary>
        private void SetupRenderCamera()
        {
            var cameraGO = new GameObject("GLBRenderCamera");
            cameraGO.transform.SetParent(transform);
            cameraGO.transform.localPosition = new Vector3(0, 1, -3);
            
            renderCamera = cameraGO.AddComponent<Camera>();
            renderCamera.clearFlags = CameraClearFlags.SolidColor;
            renderCamera.backgroundColor = new Color(0.08f, 0.08f, 0.12f); // Темный фон
            renderCamera.cullingMask = 1 << LayerMask.NameToLayer("Default"); // Рендерим только Default layer
            renderCamera.enabled = true;
            
            // Создаем RenderTexture
            renderTexture = new RenderTexture(renderTextureWidth, renderTextureHeight, 24);
            renderTexture.antiAliasing = 4; // Сглаживание
            renderCamera.targetTexture = renderTexture;
            
            // Добавляем OrbitCameraController с внешним pivot
            orbitController = cameraGO.AddComponent<OrbitCameraController>();
            orbitController.SetExternalPivot(pivotTransform);
        }
        
        /// <summary>
        /// Настраивает освещение для модели.
        /// </summary>
        private void SetupLighting()
        {
            var lightGO = new GameObject("GLBLight");
            lightGO.transform.SetParent(transform);
            lightGO.transform.localPosition = new Vector3(0, 3, -2);
            lightGO.transform.localRotation = Quaternion.Euler(50, -30, 0);
            
            directionalLight = lightGO.AddComponent<Light>();
            directionalLight.type = LightType.Directional;
            directionalLight.color = Color.white;
            directionalLight.intensity = 1.0f;
            directionalLight.shadows = LightShadows.Soft;
        }
        
        /// <summary>
        /// Настраивает контейнер для модели.
        /// </summary>
        private void SetupModelContainer()
        {
            modelContainer = new GameObject("GLBModelContainer");
            modelContainer.transform.SetParent(transform);
            modelContainer.transform.localPosition = Vector3.zero;
            modelContainer.transform.localRotation = Quaternion.identity;
            modelContainer.transform.localScale = Vector3.one;
        }
        
        /// <summary>
        /// Инициализирует UI элементы.
        /// </summary>
        public void InitializeUI(VisualElement container)
        {
            viewerContainer = container;
            
            // Находим render target
            renderTarget = viewerContainer.Q<VisualElement>("render-target");
            if (renderTarget != null)
            {
                renderTarget.style.backgroundImage = Background.FromRenderTexture(renderTexture);
                
                // Подключаем обработчики событий для управления камерой
                if (orbitController != null)
                {
                    orbitController.AttachToUIElement(renderTarget);
                }
            }
            
            // Находим кнопки управления
            resetCameraButton = viewerContainer.Q<Button>("reset-camera-button");
            if (resetCameraButton != null)
            {
                resetCameraButton.clicked += OnResetCamera;
            }
            
            centerModelButton = viewerContainer.Q<Button>("center-model-button");
            if (centerModelButton != null)
            {
                centerModelButton.clicked += OnCenterModel;
            }
            
            // Находим label для статуса
            statusLabel = viewerContainer.Q<Label>("status-label");
        }
        
        /// <summary>
        /// Загружает GLB модель из локального файла.
        /// </summary>
        public void LoadModel(string localPath, Action onSuccess = null, Action<string> onError = null)
        {
            if (isLoading)
            {
                Debug.LogWarning($"{LogPrefix} Модель уже загружается, пропускаем запрос");
                return;
            }
            
            if (string.IsNullOrEmpty(localPath))
            {
                onError?.Invoke("Путь к модели не указан");
                return;
            }
            
            currentModelPath = localPath;
            StartCoroutine(LoadModelCoroutine(localPath, onSuccess, onError));
        }
        
        /// <summary>
        /// Корутина для загрузки GLB модели.
        /// </summary>
        private IEnumerator LoadModelCoroutine(string localPath, Action onSuccess, Action<string> onError)
        {
            isLoading = true;
            UpdateStatus("Загрузка модели...");
            
            // Очищаем предыдущую модель
            ClearModel();
            
            // Создаем объект для загрузчика
            loaderObject = new GameObject("GLTF_Loader");
            loaderObject.transform.SetParent(modelContainer.transform);
            loaderObject.transform.localPosition = Vector3.zero;
            loaderObject.transform.localRotation = Quaternion.identity;
            loaderObject.transform.localScale = Vector3.one;
            
            gltfComponent = loaderObject.AddComponent<GLTFComponent>();
            gltfComponent.GLTFUri = localPath;
            gltfComponent.LoadFromStreamingAssets = false;
            gltfComponent.Multithreaded = true;
            gltfComponent.loadOnStart = false;
            gltfComponent.HideSceneObjDuringLoad = true;
            
            Task loadTask;
            try
            {
                loadTask = gltfComponent.Load();
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogPrefix} Ошибка при запуске загрузки: {e.Message}");
                UpdateStatus($"Ошибка: {e.Message}");
                Destroy(loaderObject);
                isLoading = false;
                onError?.Invoke(e.Message);
                yield break;
            }
            
            // Ждем завершения загрузки
            while (!loadTask.IsCompleted)
            {
                yield return null;
            }
            
            if (loadTask.IsFaulted)
            {
                string error = loadTask.Exception?.GetBaseException().Message ?? "Неизвестная ошибка";
                Debug.LogError($"{LogPrefix} Ошибка загрузки модели: {error}");
                UpdateStatus($"Ошибка: {error}");
                Destroy(loaderObject);
                isLoading = false;
                onError?.Invoke(error);
                yield break;
            }
            
            var loadedScene = gltfComponent.LastLoadedScene;
            if (loadedScene == null)
            {
                Debug.LogError($"{LogPrefix} Загруженная сцена пуста");
                UpdateStatus("Ошибка: модель не содержит объектов");
                Destroy(loaderObject);
                isLoading = false;
                onError?.Invoke("Модель не содержит объектов");
                yield break;
            }
            
            // КРИТИЧНО: Отсоединяем loadedScene от loaderObject ПЕРЕД его уничтожением
            // Иначе модель будет уничтожена вместе с loaderObject!
            loadedScene.transform.SetParent(modelContainer.transform, false);
            loadedScene.transform.localPosition = Vector3.zero;
            loadedScene.transform.localRotation = Quaternion.identity;
            loadedScene.transform.localScale = Vector3.one;
            
            // Активируем модель (UnityGLTF может скрывать её во время загрузки)
            loadedScene.SetActive(true);
            
            // Сохраняем ссылку на модель
            loadedModel = loadedScene;
            
            Debug.Log($"{LogPrefix} Модель перемещена в modelContainer: {loadedScene.name}, position={loadedScene.transform.position}");
            
            // Уничтожаем gltfComponent
            gltfComponent = null;
            
            // Уничтожаем loaderObject после перемещения модели
            if (loaderObject != null)
            {
                Destroy(loaderObject);
                loaderObject = null;
            }
            
            // Ждем кадр, чтобы Unity обновил bounds
            yield return null;
            
            // Центрируем модель и настраиваем камеру
            CenterModel();
            
            // Скрываем статус после успешной загрузки
            UpdateStatus("");
            isLoading = false;
            onSuccess?.Invoke();
            
            Debug.Log($"{LogPrefix} Модель успешно загружена: {localPath}");
        }
        
        /// <summary>
        /// Центрирует модель и настраивает оптимальное расстояние камеры.
        /// </summary>
        private void CenterModel()
        {
            if (loadedModel == null || orbitController == null) return;
            
            // Вычисляем bounding box модели
            Bounds bounds = CalculateBounds(loadedModel);
            
            // Устанавливаем pivot point в центр модели
            Vector3 center = bounds.center;
            orbitController.SetPivotPoint(center);
            
            // Устанавливаем оптимальное расстояние камеры
            float size = bounds.size.magnitude;
            orbitController.SetOptimalDistance(size);
            
            Debug.Log($"{LogPrefix} Модель центрирована: center={center}, size={size}");
        }
        
        /// <summary>
        /// Вычисляет общие границы (bounds) всех объектов в модели.
        /// </summary>
        private Bounds CalculateBounds(GameObject model)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return new Bounds(model.transform.position, Vector3.one);
            }
            
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            
            return bounds;
        }
        
        /// <summary>
        /// Очищает текущую модель.
        /// </summary>
        private void ClearModel()
        {
            if (loadedModel != null)
            {
                Destroy(loadedModel);
                loadedModel = null;
            }
            
            if (gltfComponent != null)
            {
                gltfComponent = null;
            }
            
            if (loaderObject != null)
            {
                Destroy(loaderObject);
                loaderObject = null;
            }
        }
        
        /// <summary>
        /// Обработчик кнопки "Сброс камеры".
        /// </summary>
        private void OnResetCamera()
        {
            if (orbitController != null)
            {
                orbitController.ResetCamera();
            }
        }
        
        /// <summary>
        /// Обработчик кнопки "Центрировать модель".
        /// </summary>
        private void OnCenterModel()
        {
            CenterModel();
        }
        
        /// <summary>
        /// Обновляет текст статуса.
        /// </summary>
        private void UpdateStatus(string message)
        {
            if (statusLabel != null)
            {
                statusLabel.text = message;
                statusLabel.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }
        
        /// <summary>
        /// Очистка ресурсов.
        /// </summary>
        public void Cleanup()
        {
            ClearModel();
            
            if (orbitController != null && renderTarget != null)
            {
                orbitController.DetachFromUIElement(renderTarget);
            }
            
            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
                renderTexture = null;
            }
            
            if (renderCamera != null)
            {
                Destroy(renderCamera.gameObject);
                renderCamera = null;
            }
            
            if (directionalLight != null)
            {
                Destroy(directionalLight.gameObject);
                directionalLight = null;
            }
            
            if (modelContainer != null)
            {
                Destroy(modelContainer);
                modelContainer = null;
            }
            
            if (pivotTransform != null)
            {
                Destroy(pivotTransform.gameObject);
                pivotTransform = null;
            }
        }
        
        private void OnDestroy()
        {
            Cleanup();
        }
    }
}


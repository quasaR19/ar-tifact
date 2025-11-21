using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Компонент для отображения желтой рамки на распознанных AR таргетах
/// </summary>
public class MarkerFrameIndicator : MonoBehaviour
{
    [Header("Настройки рамки")]
    [Tooltip("Толщина линий рамки")]
    public float frameThickness = 0.005f;
    
    [Tooltip("Отступ рамки от края таргета")]
    public float frameOffset = 0.01f;
    
    [Tooltip("Цвет рамки")]
    public Color frameColor = Color.yellow;
    
    [Tooltip("Высота рамки над таргетом")]
    public float frameHeight = 0.001f;

    private ARTrackedImageManager trackedImageManager;
    private Camera arCamera;
    private Dictionary<string, GameObject> frameObjects = new Dictionary<string, GameObject>();

    void Start()
    {
        Debug.Log("[MarkerFrameIndicator] Инициализация...");
        
        // Находим ARTrackedImageManager
        trackedImageManager = FindFirstObjectByType<ARTrackedImageManager>();
        if (trackedImageManager == null)
        {
            Debug.LogError("[MarkerFrameIndicator] ARTrackedImageManager не найден в сцене!");
            return;
        }
        
        Debug.Log($"[MarkerFrameIndicator] ARTrackedImageManager найден: {trackedImageManager.name}");
        
        // Проверяем, установлена ли библиотека референсов
        if (trackedImageManager.referenceLibrary == null)
        {
            Debug.LogWarning("[MarkerFrameIndicator] Библиотека референсов не установлена в ARTrackedImageManager!");
            Debug.LogWarning("[MarkerFrameIndicator] Убедитесь, что DynamicReferenceLibrary.CreateReferenceLibrary() был вызван");
        }
        else
        {
            Debug.Log($"[MarkerFrameIndicator] Библиотека референсов установлена: {trackedImageManager.referenceLibrary.count} изображений");
        }
        
        // Проверяем, включен ли менеджер
        if (!trackedImageManager.enabled)
        {
            Debug.LogWarning("[MarkerFrameIndicator] ARTrackedImageManager отключен!");
        }
        
        // Находим камеру AR
        arCamera = Camera.main;
        if (arCamera == null)
        {
            // Пробуем найти через XROrigin
            var xrOrigin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOrigin != null && xrOrigin.Camera != null)
            {
                arCamera = xrOrigin.Camera;
            }
        }
        
        if (arCamera == null)
        {
            Debug.LogWarning("[MarkerFrameIndicator] AR камера не найдена, рамка не будет следовать за камерой");
        }
        else
        {
            Debug.Log($"[MarkerFrameIndicator] AR камера найдена: {arCamera.name}");
        }
        
        // Подписываемся на события распознавания таргетов
        // trackablesChanged - это UnityEvent, используем AddListener
        trackedImageManager.trackablesChanged.AddListener(OnTrackedImagesChanged);
        
        Debug.Log("[MarkerFrameIndicator] Инициализация завершена, подписка на события установлена");
    }

    void OnDestroy()
    {
        // Отписываемся от событий
        if (trackedImageManager != null)
        {
            trackedImageManager.trackablesChanged.RemoveListener(OnTrackedImagesChanged);
        }
        
        // Очищаем все рамки
        ClearAllFrames();
    }

    void Update()
    {
        // Обновляем ориентацию всех рамок, чтобы они смотрели на камеру
        if (arCamera != null)
        {
            foreach (var frameObj in frameObjects.Values)
            {
                if (frameObj != null)
                {
                    UpdateFrameOrientation(frameObj);
                }
            }
        }
        
        // Периодически проверяем состояние библиотеки (только в режиме игры)
        if (Application.isPlaying && trackedImageManager != null && Time.frameCount % 300 == 0)
        {
            CheckLibraryStatus();
        }
    }
    
    /// <summary>
    /// Проверяет состояние библиотеки референсов
    /// </summary>
    private void CheckLibraryStatus()
    {
        if (trackedImageManager == null)
            return;
        
        if (trackedImageManager.referenceLibrary == null)
        {
            Debug.LogWarning("[MarkerFrameIndicator] Библиотека референсов не установлена!");
        }
        else
        {
            Debug.Log($"[MarkerFrameIndicator] Статус библиотеки: {trackedImageManager.referenceLibrary.count} изображений, enabled={trackedImageManager.enabled}");
        }
    }

    /// <summary>
    /// Обработчик изменения распознанных таргетов
    /// </summary>
    private void OnTrackedImagesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
    {
        Debug.Log($"[MarkerFrameIndicator] OnTrackedImagesChanged вызван: added={args.added.Count}, updated={args.updated.Count}, removed={args.removed.Count}");
        
        // Обрабатываем добавленные таргеты
        foreach (var trackedImage in args.added)
        {
            if (trackedImage == null)
            {
                Debug.LogWarning("[MarkerFrameIndicator] Получен null в args.added");
                continue;
            }
            
            string imageName = trackedImage.referenceImage.name;
            Debug.Log($"[MarkerFrameIndicator] Таргет добавлен: {imageName}, trackingState={trackedImage.trackingState}");
            CreateFrame(trackedImage);
        }
        
        // Обрабатываем обновленные таргеты
        foreach (var trackedImage in args.updated)
        {
            if (trackedImage == null)
            {
                continue;
            }
            UpdateFrame(trackedImage);
        }
        
        // Обрабатываем удаленные таргеты
        // args.removed - это KeyValuePair<TrackableId, ARTrackedImage>
        foreach (var kvp in args.removed)
        {
            ARTrackedImage trackedImage = kvp.Value;
            if (trackedImage != null)
            {
                string imageName = trackedImage.referenceImage.name;
                Debug.Log($"[MarkerFrameIndicator] Таргет удален: {imageName}");
                RemoveFrame(trackedImage);
            }
        }
    }

    /// <summary>
    /// Создает рамку для распознанного таргета
    /// </summary>
    private void CreateFrame(ARTrackedImage trackedImage)
    {
        if (trackedImage == null)
        {
            Debug.LogError("[MarkerFrameIndicator] CreateFrame: trackedImage равен null!");
            return;
        }
        
        // XRReferenceImage - это структура, не может быть null
        // Проверяем, что имя не пустое
        string imageName = trackedImage.referenceImage.name;
        if (string.IsNullOrEmpty(imageName))
        {
            Debug.LogError("[MarkerFrameIndicator] CreateFrame: имя referenceImage пустое!");
            return;
        }
        
        // Проверяем, не создана ли уже рамка для этого таргета
        if (frameObjects.ContainsKey(imageName))
        {
            Debug.LogWarning($"[MarkerFrameIndicator] Рамка для таргета {imageName} уже существует");
            return;
        }
        
        // Получаем размер таргета
        Vector2 imageSize = trackedImage.size;
        if (imageSize.x == 0 || imageSize.y == 0)
        {
            // Если размер не определен, используем размер из referenceImage
            imageSize = trackedImage.referenceImage.size;
        }
        
        Debug.Log($"[MarkerFrameIndicator] Создание рамки для таргета {imageName}, размер: {imageSize}");
        
        // Создаем GameObject для рамки
        GameObject frameObject = new GameObject($"Frame_{imageName}");
        frameObject.transform.SetParent(trackedImage.transform, false);
        
        // Создаем линии рамки
        CreateFrameLines(frameObject, imageSize);
        
        // Сохраняем ссылку
        frameObjects[imageName] = frameObject;
    }

    /// <summary>
    /// Создает линии рамки
    /// </summary>
    private void CreateFrameLines(GameObject parent, Vector2 size)
    {
        float halfWidth = size.x * 0.5f + frameOffset;
        float halfHeight = size.y * 0.5f + frameOffset;
        float thickness = frameThickness;
        
        // Создаем 4 линии для рамки
        // Верхняя линия
        CreateLine(parent, "Top", 
            new Vector3(-halfWidth, halfHeight, frameHeight),
            new Vector3(halfWidth, halfHeight, frameHeight),
            thickness);
        
        // Нижняя линия
        CreateLine(parent, "Bottom",
            new Vector3(-halfWidth, -halfHeight, frameHeight),
            new Vector3(halfWidth, -halfHeight, frameHeight),
            thickness);
        
        // Левая линия
        CreateLine(parent, "Left",
            new Vector3(-halfWidth, -halfHeight, frameHeight),
            new Vector3(-halfWidth, halfHeight, frameHeight),
            thickness);
        
        // Правая линия
        CreateLine(parent, "Right",
            new Vector3(halfWidth, -halfHeight, frameHeight),
            new Vector3(halfWidth, halfHeight, frameHeight),
            thickness);
    }

    /// <summary>
    /// Создает линию для рамки
    /// </summary>
    private void CreateLine(GameObject parent, string name, Vector3 start, Vector3 end, float thickness)
    {
        GameObject lineObject = new GameObject(name);
        lineObject.transform.SetParent(parent.transform, false);
        
        // Создаем цилиндр для линии
        GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder.name = "Cylinder";
        cylinder.transform.SetParent(lineObject.transform, false);
        
        // Вычисляем длину и направление линии
        Vector3 direction = end - start;
        float length = direction.magnitude;
        Vector3 center = (start + end) * 0.5f;
        
        // Позиционируем цилиндр
        lineObject.transform.localPosition = center;
        lineObject.transform.localRotation = Quaternion.LookRotation(direction);
        
        // Масштабируем цилиндр (по умолчанию цилиндр имеет высоту 2, радиус 0.5)
        cylinder.transform.localScale = new Vector3(thickness, length * 0.5f, thickness);
        cylinder.transform.localRotation = Quaternion.Euler(90, 0, 0); // Поворачиваем цилиндр горизонтально
        
        // Применяем цвет
        Renderer renderer = cylinder.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            if (material.shader == null)
            {
                material.shader = Shader.Find("Unlit/Color");
            }
            material.color = frameColor;
            renderer.material = material;
        }
        
        // Удаляем коллайдер (он не нужен)
        Collider collider = cylinder.GetComponent<Collider>();
        if (collider != null)
        {
            DestroyImmediate(collider);
        }
    }

    /// <summary>
    /// Обновляет позицию рамки при обновлении таргета
    /// </summary>
    private void UpdateFrame(ARTrackedImage trackedImage)
    {
        string imageName = trackedImage.referenceImage.name;
        
        if (!frameObjects.ContainsKey(imageName))
        {
            // Если рамка не существует, создаем её
            CreateFrame(trackedImage);
            return;
        }
        
        GameObject frameObject = frameObjects[imageName];
        if (frameObject == null)
        {
            // Если объект был уничтожен, удаляем из словаря и создаем заново
            frameObjects.Remove(imageName);
            CreateFrame(trackedImage);
            return;
        }
        
        // Обновляем ориентацию рамки
        UpdateFrameOrientation(frameObject);
    }

    /// <summary>
    /// Обновляет ориентацию рамки, чтобы она всегда смотрела на камеру (billboard эффект)
    /// </summary>
    private void UpdateFrameOrientation(GameObject frameObject)
    {
        if (arCamera == null || frameObject == null)
            return;
        
        // Получаем родительский transform (ARTrackedImage)
        Transform targetTransform = frameObject.transform.parent;
        if (targetTransform == null)
            return;
        
        // Вычисляем направление от рамки к камере в мировых координатах
        Vector3 frameWorldPos = frameObject.transform.position;
        Vector3 cameraWorldPos = arCamera.transform.position;
        Vector3 directionToCameraWorld = cameraWorldPos - frameWorldPos;
        
        if (directionToCameraWorld.magnitude > 0.01f)
        {
            // Преобразуем направление в локальные координаты родителя
            Vector3 directionToCameraLocal = targetTransform.InverseTransformDirection(directionToCameraWorld);
            
            // Вычисляем локальный поворот, чтобы рамка смотрела на камеру
            // Отрицательное направление, потому что LookRotation смотрит в направлении forward
            frameObject.transform.localRotation = Quaternion.LookRotation(-directionToCameraLocal);
        }
    }

    /// <summary>
    /// Удаляет рамку для таргета
    /// </summary>
    private void RemoveFrame(ARTrackedImage trackedImage)
    {
        string imageName = trackedImage.referenceImage.name;
        
        if (frameObjects.ContainsKey(imageName))
        {
            GameObject frameObject = frameObjects[imageName];
            if (frameObject != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(frameObject);
                }
                else
                {
                    DestroyImmediate(frameObject);
                }
            }
            frameObjects.Remove(imageName);
            Debug.Log($"[MarkerFrameIndicator] Рамка для {imageName} удалена");
        }
    }

    /// <summary>
    /// Очищает все рамки
    /// </summary>
    private void ClearAllFrames()
    {
        bool isEditor = !Application.isPlaying;
        
        foreach (var frameObject in frameObjects.Values)
        {
            if (frameObject != null)
            {
                if (isEditor)
                {
                    DestroyImmediate(frameObject);
                }
                else
                {
                    Destroy(frameObject);
                }
            }
        }
        
        frameObjects.Clear();
    }
}


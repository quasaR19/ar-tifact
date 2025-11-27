using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class TrackedModelHost : MonoBehaviour
{
    [Header("Placement")]
    [SerializeField] private Transform modelParent;
    [SerializeField] private float distance = 0.05f;
    [SerializeField] private float modelScale = 1f;

    [Header("Size Constraints")]
    [Tooltip("Максимальный размер модели относительно размера таргета (например, 2.0 = модель может быть в 2 раза больше таргета)")]
    [SerializeField] private float maxSizeMultiplier = 2.0f;

    [Header("Rotation")]
    [SerializeField] private bool spinModel = true;
    [SerializeField] private float placeholderSpinSpeed = 30f;
    [SerializeField] private float modelSpinSpeed = 10f;
    
    [Header("Persistence")]
    [SerializeField] private float fadeOutDelaySeconds = 3f;
    [SerializeField] private float fadeOutDurationSeconds = 0f; // Моментальное скрытие после задержки

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private Color gizmoColor = Color.red;
    [SerializeField] private float gizmoSize = 0.1f;

    private GameObject placeholderModel;
    private GameObject loadedModel;
    private GameObject modelWrapper; // Wrapper для центрирования модели по геометрическому центру
    private Transform modelInsideWrapper; // Ссылка на модель внутри wrapper'а (для обновления позиции)
    private float targetSize = 0.1f; // Размер таргета по умолчанию (в метрах)
    
    // Базовые ротации для моделей (выравнивание относительно мира)
    private Quaternion basePlaceholderRotation = Quaternion.identity;
    private Quaternion baseLoadedModelRotation = Quaternion.identity;
    
    // Текущие углы поворота для накопления
    private float placeholderRotationAngle = 0f;
    private float loadedModelRotationAngle = 0f;
    
    // Кеш
    private Vector3 cachedModelScale = Vector3.one;
    private Vector3 placeholderInitialScale = Vector3.one;
    private bool isScaleCacheValid = false;
    private bool isTrackingActive = true;
    private bool isPinned = false;
    private Coroutine fadeCoroutine;
    private Coroutine attachModelCoroutine;
    private bool rendererCacheDirty = true;
    private readonly List<RendererMaterialState> rendererMaterialStates = new();
    
    // Кеш для bounds модели (оптимизация производительности)
    private Bounds? cachedModelBounds = null;
    private bool boundsCacheValid = false;
    
    private class RendererMaterialState
    {
        public Renderer Renderer;
        public Material[] Materials;
        public Color[] BaseColors;
        public int[] PropertyTypes;
    }
    
    private const int PROP_NONE = 0;
    private const int PROP_COLOR = 1;
    private const int PROP_BASE_COLOR = 2;
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
    
    // Смещение для выравнивания низа модели по distance
    private float currentModelBottomOffset = 0f;
    private float currentPlaceholderBottomOffset = 0f;

    public string CurrentArtifactId { get; private set; }
    public bool HasLoadedModel => loadedModel != null;
    public bool IsPinned => isPinned;

    private void Awake()
    {
        if (modelParent == null)
        {
            modelParent = transform;
        }
    }

    private void Start()
    {
        if (transform.childCount > 0)
        {
            placeholderModel = transform.GetChild(0).gameObject;
            placeholderInitialScale = placeholderModel.transform.localScale;
        }

        if (placeholderModel != null)
        {
            AlignModel(placeholderModel.transform, true);
        }
    }

    private void Update()
    {
        // Проверяем, что хост все еще привязан к родителю (ARTrackedImage)
        // Если родитель null, модель не будет следовать за таргетом
        if (transform.parent == null && loadedModel != null)
        {
            Debug.LogError($"[TrackedModelHost] КРИТИЧЕСКАЯ ОШИБКА: Хост {name} потерял родителя (ARTrackedImage)! Модель не будет следовать за таргетом.");
        }
        
        if (loadedModel != null)
        {
            // Проверяем, что модель все еще привязана к хосту
            if (!loadedModel.transform.IsChildOf(transform))
            {
                Debug.LogError($"[TrackedModelHost] КРИТИЧЕСКАЯ ОШИБКА: Модель {loadedModel.name} не является дочерним объектом хоста {name}!");
            }
            
            UpdateModelTransform(loadedModel.transform, ref baseLoadedModelRotation, ref loadedModelRotationAngle, modelSpinSpeed, spinModel, false);
        }
        else if (placeholderModel != null)
        {
            UpdateModelTransform(placeholderModel.transform, ref basePlaceholderRotation, ref placeholderRotationAngle, placeholderSpinSpeed, spinModel, true);
        }
    }

    public Transform GetAttachmentRoot()
    {
        return modelParent != null ? modelParent : transform;
    }

    public bool HasLoadedArtifact(string artifactId)
    {
        return HasLoadedModel && string.Equals(CurrentArtifactId, artifactId);
    }

    /// <summary>
    /// Устанавливает размер таргета для ограничения размера модели.
    /// </summary>
    public void SetTargetSize(float size)
    {
        targetSize = Mathf.Max(0.01f, size);
        // Инвалидируем кеш масштаба, так как изменение размера таргета требует пересчета
        isScaleCacheValid = false;
    }

    public void AttachLoadedModel(GameObject modelInstance, string artifactId, string metadataJson = null)
    {
        if (modelInstance == null)
        {
            Debug.LogWarning($"[TrackedModelHost] AttachLoadedModel: modelInstance == null");
            return;
        }

        // Если модель уже загружена с таким же artifactId, не делаем ничего
        if (HasLoadedArtifact(artifactId))
        {
            DestroyObject(modelInstance);
            return;
        }

        // Отменяем предыдущую корутину прикрепления, если она есть
        if (attachModelCoroutine != null)
        {
            StopCoroutine(attachModelCoroutine);
            attachModelCoroutine = null;
        }

        // Очищаем старую модель и wrapper (если были)
        if (loadedModel != null)
        {
            DestroyObject(loadedModel);
            loadedModel = null;
        }
        if (modelWrapper != null)
        {
            DestroyObject(modelWrapper);
            modelWrapper = null;
        }
        
        // Инвалидируем кеш bounds
        boundsCacheValid = false;
        cachedModelBounds = null;

        // Проверяем, нужно ли центрировать модель по геометрическому центру
        bool shouldCenterModel = false;
        if (!string.IsNullOrEmpty(metadataJson))
        {
            try
            {
                var metadata = JsonUtility.FromJson<ModelMetadata>(metadataJson);
                shouldCenterModel = metadata.center_model;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TrackedModelHost] Ошибка парсинга metadata: {e.Message}");
            }
        }

        // КРИТИЧНО: Убеждаемся, что modelInstance отсоединен от любого родителя перед обработкой
        if (modelInstance.transform.parent != null)
        {
            modelInstance.transform.SetParent(null, true);
        }
        
        // Запускаем асинхронное прикрепление модели для оптимизации производительности
        attachModelCoroutine = StartCoroutine(AttachModelAsync(modelInstance, artifactId, metadataJson, shouldCenterModel));
    }
    
    /// <summary>
    /// Асинхронно прикрепляет модель к хосту, разбивая тяжелые операции на несколько кадров
    /// </summary>
    private IEnumerator AttachModelAsync(GameObject modelInstance, string artifactId, string metadataJson, bool shouldCenterModel)
    {
        // Шаг 1: Сначала скрываем модель, чтобы не было видно процесса загрузки
        modelInstance.SetActive(false);
        
        // Шаг 2: Создаем wrapper если нужно (легкая операция)
        if (shouldCenterModel)
        {
            modelWrapper = new GameObject($"ModelWrapper_{artifactId}");
            modelWrapper.transform.localPosition = Vector3.zero;
            modelWrapper.transform.localRotation = Quaternion.identity;
            modelWrapper.transform.localScale = Vector3.one;

            modelInstance.transform.SetParent(modelWrapper.transform, false);
            modelInsideWrapper = modelInstance.transform;
        }
        else
        {
            loadedModel = modelInstance;
        }
        
        // Ждем один кадр перед тяжелыми операциями
        yield return null;
        
        // Шаг 3: Вычисляем bounds (тяжелая операция) - разбиваем на части
        Bounds modelBounds = default;
        Vector3 geometricCenter = Vector3.zero;
        
        if (shouldCenterModel)
        {
            Vector3 originalScale = modelInstance.transform.localScale;
            modelInstance.transform.localScale = Vector3.one;
            
            // Вычисляем bounds с задержкой между частями
            yield return StartCoroutine(CalculateBoundsAsync(modelInstance.transform, (bounds) =>
            {
                modelBounds = bounds;
                geometricCenter = bounds.center;
            }));
            
            modelInstance.transform.localScale = originalScale;
            modelInstance.transform.localPosition = -geometricCenter;
            
            modelWrapper.transform.SetParent(GetAttachmentRoot(), false);
            loadedModel = modelWrapper;
        }
        else
        {
            loadedModel.transform.SetParent(GetAttachmentRoot(), false);
        }
        
        // Ждем кадр перед следующей операцией
        yield return null;
        
        // Шаг 4: Очистка siblings (может быть тяжелой)
        CleanupSiblings(loadedModel.transform);
        placeholderModel = null;
        
        // Ждем кадр
        yield return null;
        
        // Шаг 5: Выравнивание модели (вызывает GetModelBounds)
        isScaleCacheValid = false;
        boundsCacheValid = false;
        AlignModel(loadedModel.transform, false);
        
        // Ждем кадр перед активацией
        yield return null;
        
        // Шаг 6: Кеширование материалов откладываем до первого использования
        rendererCacheDirty = true;
        CurrentArtifactId = artifactId;
        
        // Шаг 7: Проверка иерархии и активация
        if (!loadedModel.transform.IsChildOf(GetAttachmentRoot()))
        {
            Debug.LogError($"[TrackedModelHost] КРИТИЧЕСКАЯ ОШИБКА: Модель НЕ прикреплена к хосту после AttachLoadedModel! Принудительно прикрепляем...");
            loadedModel.transform.SetParent(GetAttachmentRoot(), false);
        }
        
        // Проверяем полную иерархию для отладки
        Debug.Log($"[TrackedModelHost] Иерархия после прикрепления модели {artifactId}:");
        Debug.Log($"  - Хост: {name}, parent: {(transform.parent != null ? transform.parent.name : "NULL")}");
        Debug.Log($"  - Модель: {loadedModel.name}, parent: {(loadedModel.transform.parent != null ? loadedModel.transform.parent.name : "NULL")}");
        if (modelInsideWrapper != null)
        {
            Debug.Log($"  - ModelInsideWrapper: {modelInsideWrapper.name}, parent: {(modelInsideWrapper.parent != null ? modelInsideWrapper.parent.name : "NULL")}");
        }
        
        // Активируем модель постепенно - сначала включаем, потом применяем альфу
        loadedModel.SetActive(true);
        yield return null;
        
        // Постепенно активируем рендереры для плавной загрузки
        yield return StartCoroutine(ActivateRenderersGradually(loadedModel.transform));
        
        // Применяем альфу (может вызвать кеширование материалов, но это будет в следующем кадре)
        ApplyAlphaToModel(1f);
        
        attachModelCoroutine = null;
    }
    
    /// <summary>
    /// Постепенно активирует рендереры модели для плавной загрузки
    /// </summary>
    private IEnumerator ActivateRenderersGradually(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
        
        if (renderers.Length == 0)
        {
            yield break;
        }
        
        // Активируем по 5-10 рендереров за кадр
        const int renderersPerFrame = 8;
        
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].enabled = true;
            }
            
            // Ждем кадр каждые N рендереров
            if (i > 0 && i % renderersPerFrame == 0)
            {
                yield return null;
            }
        }
    }
    
    /// <summary>
    /// Асинхронно вычисляет bounds модели, разбивая операцию на части
    /// </summary>
    private IEnumerator CalculateBoundsAsync(Transform root, System.Action<Bounds> onComplete)
    {
        // Получаем рендереры
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        
        if (renderers.Length == 0)
        {
            onComplete?.Invoke(new Bounds(Vector3.zero, Vector3.one));
            yield break;
        }
        
        // Вычисляем bounds постепенно
        Bounds bounds = new Bounds(
            root.InverseTransformPoint(renderers[0].bounds.center),
            root.InverseTransformVector(renderers[0].bounds.size)
        );
        
        // Обрабатываем по 10 рендереров за кадр для оптимизации
        const int renderersPerFrame = 10;
        for (int i = 1; i < renderers.Length; i++)
        {
            Bounds rendererBounds = new Bounds(
                root.InverseTransformPoint(renderers[i].bounds.center),
                root.InverseTransformVector(renderers[i].bounds.size)
            );
            bounds.Encapsulate(rendererBounds);
            
            // Ждем кадр каждые N рендереров
            if (i % renderersPerFrame == 0)
            {
                yield return null;
            }
        }
        
        onComplete?.Invoke(bounds);
    }

    [Serializable]
    private class ModelMetadata
    {
        public bool center_model;
    }

    private void CleanupSiblings(Transform keepObject)
    {
        if (keepObject == null)
        {
            Debug.LogError("[TrackedModelHost] CleanupSiblings: keepObject == null!");
            return;
        }
        
        Transform parent = keepObject.parent;
        
        if (parent == null)
        {
            Debug.LogError($"[TrackedModelHost] CleanupSiblings: keepObject.parent == NULL! keepObject={keepObject.name}, это означает, что модель не прикреплена к хосту!");
            // КРИТИЧНО: Если модель не прикреплена к хосту, прикрепляем её сейчас
            Debug.LogWarning($"[TrackedModelHost] ПРИНУДИТЕЛЬНО прикрепляем модель к хосту!");
            keepObject.SetParent(GetAttachmentRoot(), false);
            parent = keepObject.parent;
        }
        
        if (parent == null)
        {
            Debug.LogError($"[TrackedModelHost] CleanupSiblings: Не удалось прикрепить модель к хосту!");
            return;
        }
        
        int childCount = parent.childCount;
        // Итерируемся с конца, так как будем удалять
        for (int i = childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            if (child != keepObject)
            {
                DestroyObject(child.gameObject);
            }
        }
    }

    public void ResetToPlaceholder()
    {
        CancelFadeRoutine();
        ClearLoadedModel();
        CurrentArtifactId = null;
        isPinned = false;
        isTrackingActive = true;
        rendererCacheDirty = true;

        // Примечание: так как мы уничтожаем плейсхолдер при загрузке модели, 
        // здесь мы не можем его восстановить без повторного создания.
        // Если логика требует возврата плейсхолдера, нужно изменить стратегию очистки (скрывать, а не удалять).
        // Но согласно текущим требованиям - удаляем все лишнее.
        
        // Если плейсхолдер жив (например, модель не загружалась, а вызвался сброс), активируем его
        if (placeholderModel != null)
        {
            placeholderModel.SetActive(true);
            AlignModel(placeholderModel.transform, true);
        }
    }

    public void SetTrackingActive(bool isActive)
    {
        bool oldState = isTrackingActive;
        isTrackingActive = isActive;
        
        // Логируем только при изменении состояния трекинга
        if (oldState != isActive)
        {
            if (isActive)
            {
                Debug.Log($"[TrackedModelHost] Трекинг захвачен: host={name}, artifactId={CurrentArtifactId ?? "null"}, isPinned={isPinned}");
            }
            else
            {
                Debug.Log($"[TrackedModelHost] Трекинг потерян: host={name}, artifactId={CurrentArtifactId ?? "null"}, isPinned={isPinned}, начинаем FadeOut={!isPinned}");
            }
        }
        
        if (isPinned)
        {
            EnsureModelVisible();
            return;
        }
        
        if (isTrackingActive)
        {
            EnsureModelVisible();
            CancelFadeRoutine();
        }
        else
        {
            StartFadeOutRoutine();
        }
    }
    
    public bool TogglePinned()
    {
        return SetPinned(!isPinned);
    }
    
    public bool SetPinned(bool shouldPin)
    {
        if (isPinned == shouldPin)
        {
            return isPinned;
        }
        
        bool oldState = isPinned;
        isPinned = shouldPin;
        
        if (isPinned)
        {
            EnsureModelVisible();
            CancelFadeRoutine();
        }
        else
        {
            if (!isTrackingActive)
            {
                StartFadeOutRoutine();
            }
        }
        
        return isPinned;
    }

    private void ClearLoadedModel()
    {
        // Очищаем кеш материалов перед уничтожением модели
        ClearMaterialCache();
        
        if (loadedModel != null)
        {
            DestroyObject(loadedModel);
            loadedModel = null;
        }
        
        // Очищаем wrapper отдельно, если он существует
        if (modelWrapper != null)
        {
            DestroyObject(modelWrapper);
            modelWrapper = null;
        }
        
        // Очищаем ссылку на модель внутри wrapper'а
        modelInsideWrapper = null;
        
        // Инвалидируем кеш масштаба при очистке модели
        isScaleCacheValid = false;
        rendererCacheDirty = true;
        boundsCacheValid = false;
        cachedModelBounds = null;
    }

    private new void DestroyObject(UnityEngine.Object obj)
    {
        if (Application.isPlaying)
        {
            Destroy(obj);
        }
        else
        {
            DestroyImmediate(obj);
        }
    }

    /// <summary>
    /// Устанавливает базовое выравнивание модели (позиция, масштаб, базовая ротация).
    /// </summary>
    private void AlignModel(Transform target, bool isPlaceholder)
    {
        if (target == null) return;

        // 1. Расчет масштаба
        Vector3 finalScale;
        if (isPlaceholder)
        {
            finalScale = placeholderInitialScale;
        }
        else
        {
            // Для загруженных моделей вычисляем и кешируем масштаб
            if (!isScaleCacheValid)
            {
                Vector3 baseScaleVec = new Vector3(modelScale, modelScale, modelScale);
                cachedModelScale = CalculateConstrainedScale(target, baseScaleVec);
                isScaleCacheValid = true;
            }
            finalScale = cachedModelScale;
        }
        
        // Применяем масштаб временно для расчета bounds (чтобы получить корректный размер)
        Vector3 oldScale = target.localScale;
        target.localScale = Vector3.one;
        
        // 2. Расчет смещения (Position) по Bounds
        // Находим нижнюю точку модели, чтобы поставить её на distance от таргета
        // Для wrapper'а bounds будет включать всю модель, которая уже смещена внутри wrapper'а
        Bounds bounds = GetModelBounds(target);
        
        // Восстанавливаем/применяем итоговый масштаб
        target.localScale = finalScale;

        // Вычисляем смещение. bounds.min.y - это низ в локальных единицах (при скейле 1).
        // Умножаем на scale.y, чтобы получить реальное смещение низа.
        // Нам нужно, чтобы позиция Y была такой, чтобы низ модели был на высоте distance.
        // PosY + (BoundsMinY * ScaleY) = Distance
        // PosY = Distance - (BoundsMinY * ScaleY)
        float bottomOffset = -bounds.min.y * finalScale.y;
        
        if (isPlaceholder)
        {
            currentPlaceholderBottomOffset = bottomOffset;
        }
        else
        {
            currentModelBottomOffset = bottomOffset;
        }

        // Центрирование через мировые координаты:
        // 1. Получаем мировую позицию таргета (родителя)
        Vector3 targetWorldPosition = transform.position;
        
        // 2. Вычисляем желаемую мировую позицию центра модели
        // Центр таргета по X и Z, высота = distance + bottomOffset от таргета
        Vector3 desiredWorldPosition = new Vector3(
            targetWorldPosition.x,  // Центр таргета по X
            targetWorldPosition.y + distance + bottomOffset,  // Высота от таргета
            targetWorldPosition.z   // Центр таргета по Z
        );
        
        // 3. Преобразуем мировую позицию в локальную относительно родителя
        Vector3 localPosition = transform.InverseTransformPoint(desiredWorldPosition);
        
        // 4. Устанавливаем локальную позицию
        target.localPosition = localPosition;

        // 3. Ротация (Align to world up)
        // Выравнивание относительно мира (чтобы модель всегда стояла вертикально)
        Quaternion desiredWorldRotation = Quaternion.identity; // Вертикально вверх
        Quaternion parentWorldRotation = transform.rotation;
        Quaternion baseRotation = Quaternion.Inverse(parentWorldRotation) * desiredWorldRotation;
        
        // Сохраняем базовую ротацию
        if (isPlaceholder)
        {
            basePlaceholderRotation = baseRotation;
            placeholderRotationAngle = 0f; // Сбрасываем накопленный угол
        }
        else
        {
            baseLoadedModelRotation = baseRotation;
            loadedModelRotationAngle = 0f; // Сбрасываем накопленный угол
        }
        
        target.localRotation = baseRotation;
    }
    
    private void EnsureModelVisible()
    {
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
        
        if (HasLoadedModel)
        {
            ApplyAlphaToModel(1f);
        }
    }
    
    private void StartFadeOutRoutine()
    {
        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
        }
        
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
        
        fadeCoroutine = StartCoroutine(FadeOutCoroutine());
    }
    
    private IEnumerator FadeOutCoroutine()
    {
        Debug.Log($"[TrackedModelHost] FadeOut начат для {name}, delay={fadeOutDelaySeconds}s, duration={fadeOutDurationSeconds}s");
        
        if (HasLoadedModel)
        {
            ApplyAlphaToModel(1f);
        }
        
        if (fadeOutDelaySeconds > 0f)
        {
            yield return new WaitForSeconds(fadeOutDelaySeconds);
        }
        
        // Проверяем, не был ли трекинг восстановлен за время ожидания
        if (isTrackingActive || isPinned)
        {
            Debug.Log($"[TrackedModelHost] FadeOut отменен для {name}: isTracking={isTrackingActive}, isPinned={isPinned}");
            fadeCoroutine = null;
            yield break;
        }
        
        if (HasLoadedModel && fadeOutDurationSeconds > 0f)
        {
            float elapsed = 0f;
            while (elapsed < fadeOutDurationSeconds)
            {
                // Проверяем, не был ли трекинг восстановлен
                if (isTrackingActive || isPinned)
                {
                    Debug.Log($"[TrackedModelHost] FadeOut прерван во время анимации для {name}");
                    ApplyAlphaToModel(1f);
                    fadeCoroutine = null;
                    yield break;
                }
                
                float alpha = Mathf.Lerp(1f, 0f, elapsed / fadeOutDurationSeconds);
                ApplyAlphaToModel(alpha);
                elapsed += Time.deltaTime;
                yield return null;
            }
            ApplyAlphaToModel(0f);
        }
        else if (!HasLoadedModel && fadeOutDurationSeconds > 0f)
        {
            yield return new WaitForSeconds(fadeOutDurationSeconds);
        }
        
        Debug.Log($"[TrackedModelHost] FadeOut завершен для {name}, скрываем объект");
        gameObject.SetActive(false);
        fadeCoroutine = null;
    }
    
    private void CancelFadeRoutine()
    {
        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }
        
        if (HasLoadedModel)
        {
            ApplyAlphaToModel(1f);
        }
    }
    
    private void ApplyAlphaToModel(float alpha)
    {
        if (!HasLoadedModel)
        {
            return;
        }
        
        // Откладываем кеширование материалов до следующего кадра, если это первое обращение
        if (rendererCacheDirty)
        {
            // Кешируем материалы асинхронно, чтобы не блокировать кадр
            if (rendererMaterialStates.Count == 0)
            {
                StartCoroutine(CacheRendererMaterialsAsync());
            }
            else
            {
                CacheRendererMaterials();
            }
        }
        
        float clampedAlpha = Mathf.Clamp01(alpha);
        
        foreach (var state in rendererMaterialStates)
        {
            if (state?.Renderer == null || state.Materials == null)
            {
                continue;
            }
            
            for (int i = 0; i < state.Materials.Length; i++)
            {
                if (state.PropertyTypes[i] == PROP_NONE)
                {
                    continue;
                }
                
                Material mat = state.Materials[i];
                if (mat == null)
                {
                    continue;
                }
                
                Color baseColor = state.BaseColors[i];
                baseColor.a = baseColor.a * clampedAlpha;
                
                switch (state.PropertyTypes[i])
                {
                    case PROP_COLOR:
                        mat.SetColor(ColorPropertyId, baseColor);
                        break;
                    case PROP_BASE_COLOR:
                        mat.SetColor(BaseColorPropertyId, baseColor);
                        break;
                }
            }
        }
    }
    
    /// <summary>
    /// Асинхронно кеширует материалы рендереров, разбивая операцию на части
    /// </summary>
    private IEnumerator CacheRendererMaterialsAsync()
    {
        // Очищаем предыдущий кеш
        ClearMaterialCache();
        rendererCacheDirty = false;
        
        if (!HasLoadedModel)
        {
            yield break;
        }
        
        var renderers = loadedModel.GetComponentsInChildren<Renderer>(includeInactive: true);
        
        // Обрабатываем по 5 рендереров за кадр для оптимизации
        const int renderersPerFrame = 5;
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            var renderer = renderers[rendererIndex];
            if (renderer == null)
            {
                continue;
            }
            
            Material[] materials = renderer.materials;
            if (materials == null || materials.Length == 0)
            {
                continue;
            }
            
            var state = new RendererMaterialState
            {
                Renderer = renderer,
                Materials = materials,
                BaseColors = new Color[materials.Length],
                PropertyTypes = new int[materials.Length]
            };
            
            bool hasSupportedProperty = false;
            for (int i = 0; i < materials.Length; i++)
            {
                Material mat = materials[i];
                if (mat == null)
                {
                    state.PropertyTypes[i] = PROP_NONE;
                    continue;
                }
                
                if (mat.HasProperty(ColorPropertyId))
                {
                    state.BaseColors[i] = mat.GetColor(ColorPropertyId);
                    state.PropertyTypes[i] = PROP_COLOR;
                    hasSupportedProperty = true;
                }
                else if (mat.HasProperty(BaseColorPropertyId))
                {
                    state.BaseColors[i] = mat.GetColor(BaseColorPropertyId);
                    state.PropertyTypes[i] = PROP_BASE_COLOR;
                    hasSupportedProperty = true;
                }
                else
                {
                    state.PropertyTypes[i] = PROP_NONE;
                }
            }
            
            if (hasSupportedProperty)
            {
                rendererMaterialStates.Add(state);
            }
            
            // Ждем кадр каждые N рендереров
            if (rendererIndex > 0 && rendererIndex % renderersPerFrame == 0)
            {
                yield return null;
            }
        }
    }
    
    private void CacheRendererMaterials()
    {
        // Очищаем предыдущий кеш и освобождаем материалы
        ClearMaterialCache();
        rendererCacheDirty = false;
        
        if (!HasLoadedModel)
        {
            return;
        }
        
        var renderers = loadedModel.GetComponentsInChildren<Renderer>(includeInactive: true);
        foreach (var renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }
            
            // Используем sharedMaterials где возможно, чтобы избежать создания копий
            // Но для изменения альфа-канала нужны копии, поэтому используем materials
            Material[] materials = renderer.materials;
            if (materials == null || materials.Length == 0)
            {
                continue;
            }
            
            var state = new RendererMaterialState
            {
                Renderer = renderer,
                Materials = materials,
                BaseColors = new Color[materials.Length],
                PropertyTypes = new int[materials.Length]
            };
            
            bool hasSupportedProperty = false;
            for (int i = 0; i < materials.Length; i++)
            {
                Material mat = materials[i];
                if (mat == null)
                {
                    state.PropertyTypes[i] = PROP_NONE;
                    continue;
                }
                
                if (mat.HasProperty(ColorPropertyId))
                {
                    state.BaseColors[i] = mat.GetColor(ColorPropertyId);
                    state.PropertyTypes[i] = PROP_COLOR;
                    hasSupportedProperty = true;
                }
                else if (mat.HasProperty(BaseColorPropertyId))
                {
                    state.BaseColors[i] = mat.GetColor(BaseColorPropertyId);
                    state.PropertyTypes[i] = PROP_BASE_COLOR;
                    hasSupportedProperty = true;
                }
                else
                {
                    state.PropertyTypes[i] = PROP_NONE;
                }
            }
            
            if (hasSupportedProperty)
            {
                rendererMaterialStates.Add(state);
            }
        }
    }
    
    /// <summary>
    /// Очищает кеш материалов и освобождает копии материалов
    /// </summary>
    private void ClearMaterialCache()
    {
        foreach (var state in rendererMaterialStates)
        {
            if (state?.Materials != null)
            {
                // Освобождаем копии материалов, созданные через renderer.materials
                // Эти материалы были созданы Unity при обращении к renderer.materials
                // и должны быть явно уничтожены для предотвращения утечек памяти
                foreach (var mat in state.Materials)
                {
                    if (mat != null)
                    {
                        // Проверяем, что это не shared material (который не нужно уничтожать)
                        // Копии материалов, созданные через renderer.materials, нужно уничтожать
                        Destroy(mat);
                    }
                }
            }
        }
        rendererMaterialStates.Clear();
    }

    /// <summary>
    /// Обновляет трансформ модели в каждом кадре (позиция, масштаб, ротация с накоплением).
    /// </summary>
    private void UpdateModelTransform(Transform target, ref Quaternion baseRotation, ref float rotationAngle, float spinSpeed, bool shouldSpin, bool isPlaceholder)
    {
        if (target == null) return;

        // 1. Масштаб
        if (isPlaceholder)
        {
             // Для плейсхолдера сохраняем его исходный масштаб (не применяем modelScale)
             if (target.localScale != placeholderInitialScale)
             {
                 target.localScale = placeholderInitialScale;
             }
        }
        else
        {
             // Для загруженной модели используем кешированный рассчитанный масштаб
             if (!isScaleCacheValid)
             {
                 // Кеш невалиден - пересчитываем масштаб
                 Vector3 baseScaleVec = new Vector3(modelScale, modelScale, modelScale);
                 cachedModelScale = CalculateConstrainedScale(target, baseScaleVec);
                 isScaleCacheValid = true;
             }
             target.localScale = cachedModelScale;
        }

        // 2. Позиция (с учетом distance и смещения низа) - через мировые координаты
        float bottomOffset = isPlaceholder ? currentPlaceholderBottomOffset : currentModelBottomOffset;
        
        // Проверяем, является ли target wrapper'ом (для моделей с center_model=true)
        bool isWrapper = target.name.Contains("ModelWrapper");
        
        if (isWrapper && modelInsideWrapper != null)
        {
            // Для wrapper'а: wrapper остается в (0,0,0) относительно таргета
            // Позицию модели внутри wrapper'а обновляем в каждом кадре для компенсации движения таргета
            target.localPosition = Vector3.zero;
            
            // 1. Получаем мировую позицию таргета (хоста - родителя wrapper'а)
            Vector3 hostWorldPosition = transform.position;
            
            // 2. Вычисляем желаемую мировую позицию центра модели
            // Центр хоста по X и Z, высота = distance + bottomOffset от хоста
            Vector3 desiredWorldCenter = new Vector3(
                hostWorldPosition.x,
                hostWorldPosition.y + distance + bottomOffset,
                hostWorldPosition.z
            );
            
            // 3. КРИТИЧНО: Преобразуем мировую позицию в локальные координаты WRAPPER'а (target)
            // Wrapper может иметь вращение, поэтому нужно использовать его InverseTransformPoint
            // а не хоста (transform)
            Vector3 desiredLocalCenter = target.InverseTransformPoint(desiredWorldCenter);
            
            // 4. Модель уже смещена на -geometricCenter при создании wrapper'а,
            // поэтому её геометрический центр находится в (0,0,0) wrapper'а
            // Чтобы геометрический центр был в desiredLocalCenter, устанавливаем позицию
            modelInsideWrapper.localPosition = desiredLocalCenter;
        }
        else
        {
            // Для обычной модели: используем полное позиционирование через мировые координаты
            // 1. Получаем мировую позицию хоста
            Vector3 hostWorldPosition = transform.position;
            
            // 2. Вычисляем желаемую мировую позицию центра модели
            // Центр хоста по X и Z, высота = distance + bottomOffset от хоста
            Vector3 desiredWorldPosition = new Vector3(
                hostWorldPosition.x,
                hostWorldPosition.y + distance + bottomOffset,
                hostWorldPosition.z
            );
            
            // 3. КРИТИЧНО: Преобразуем мировую позицию в локальные координаты родителя модели
            // Используем parent модели, чтобы правильно учесть иерархию
            Transform modelParentTransform = target.parent;
            if (modelParentTransform != null)
            {
                Vector3 localPosition = modelParentTransform.InverseTransformPoint(desiredWorldPosition);
                target.localPosition = localPosition;
            }
            else
            {
                // Модель не имеет родителя - это ошибка, но устанавливаем мировую позицию
                target.position = desiredWorldPosition;
                Debug.LogWarning($"[TrackedModelHost] Модель {target.name} не имеет родителя! Устанавливаем мировую позицию.");
            }
        }

        // 3. Ротация
        // Обновляем базовую ротацию, если родитель повернулся
        Quaternion desiredWorldRotation = Quaternion.identity;
        Quaternion parentWorldRotation = transform.rotation;
        Quaternion newBaseRotation = Quaternion.Inverse(parentWorldRotation) * desiredWorldRotation;
        
        if (Quaternion.Angle(baseRotation, newBaseRotation) > 0.01f)
        {
            baseRotation = newBaseRotation;
        }

        // Накопление угла поворота
        if (shouldSpin && !Mathf.Approximately(spinSpeed, 0f))
        {
            rotationAngle += spinSpeed * Time.deltaTime;
            rotationAngle %= 360f;
        }

        target.localRotation = baseRotation * Quaternion.Euler(0, rotationAngle, 0);
    }

    /// <summary>
    /// Вычисляет масштаб модели с учетом ограничения максимального размера относительно таргета.
    /// </summary>
    private Vector3 CalculateConstrainedScale(Transform modelTransform, Vector3 baseScale)
    {
        if (modelTransform == null)
        {
            return baseScale;
        }

        // Сохраняем текущий масштаб
        Vector3 originalScale = modelTransform.localScale;
        
        // Временно устанавливаем масштаб 1 для корректного вычисления bounds
        modelTransform.localScale = Vector3.one;

        // Получаем габариты модели в локальных координатах (с масштабом 1)
        Bounds modelBounds = GetModelBounds(modelTransform);
        
        // Восстанавливаем масштаб
        modelTransform.localScale = originalScale;

        if (modelBounds.size.magnitude < 0.001f)
        {
            return baseScale;
        }

        // Вычисляем максимальный размер модели после базового масштабирования
        float maxModelDimension = Mathf.Max(modelBounds.size.x, modelBounds.size.y, modelBounds.size.z) * baseScale.x;
        
        // Максимально допустимый размер модели относительно таргета
        float maxAllowedSize = targetSize * maxSizeMultiplier;

        // Если модель слишком большая, уменьшаем масштаб
        if (maxModelDimension > maxAllowedSize)
        {
            float scaleFactor = maxAllowedSize / maxModelDimension;
            return baseScale * scaleFactor;
        }

        return baseScale;
    }

    /// <summary>
    /// Получает габариты модели в локальных координатах root, обходя все дочерние объекты с рендерерами.
    /// Предполагается, что root.localScale = Vector3.one при вызове.
    /// Использует кеш для оптимизации производительности.
    /// </summary>
    private Bounds GetModelBounds(Transform root)
    {
        // Проверяем кеш
        if (boundsCacheValid && cachedModelBounds.HasValue && root == loadedModel?.transform)
        {
            return cachedModelBounds.Value;
        }
        
        // Используем более эффективный метод получения рендереров
        // GetComponentsInChildren может быть медленным для больших моделей
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false); // false = только активные, быстрее
        
        if (renderers.Length == 0)
        {
            // Если активных нет, пробуем включить неактивные (но это медленнее)
            renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                var emptyBounds = new Bounds(Vector3.zero, Vector3.one);
                if (root == loadedModel?.transform)
                {
                    cachedModelBounds = emptyBounds;
                    boundsCacheValid = true;
                }
                return emptyBounds;
            }
        }

        // Получаем первый bounds в локальных координатах
        Bounds bounds = new Bounds(
            root.InverseTransformPoint(renderers[0].bounds.center),
            root.InverseTransformVector(renderers[0].bounds.size)
        );

        // Объединяем все bounds
        // Оптимизация: используем for вместо foreach для немного лучшей производительности
        for (int i = 1; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;
            
            Bounds rendererBounds = new Bounds(
                root.InverseTransformPoint(renderer.bounds.center),
                root.InverseTransformVector(renderer.bounds.size)
            );
            bounds.Encapsulate(rendererBounds);
        }
        
        // Кешируем результат
        if (root == loadedModel?.transform)
        {
            cachedModelBounds = bounds;
            boundsCacheValid = true;
        }
        
        return bounds;
    }

    private void OnDestroy()
    {
        // Гарантированная очистка материалов при уничтожении объекта
        ClearMaterialCache();
        
        // Отменяем корутину, если она активна
        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;
        Gizmos.color = gizmoColor;
        
        // Рисуем точку distance (где должен быть низ модели)
        Vector3 targetPos = transform.position + transform.up * distance;
        Gizmos.DrawWireSphere(targetPos, gizmoSize);
        
        // Если есть Bounds, можно нарисовать их (опционально)
        if (loadedModel != null)
        {
            // Это сложнее нарисовать точно без пересчета мировых координат bounds
        }
    }
}

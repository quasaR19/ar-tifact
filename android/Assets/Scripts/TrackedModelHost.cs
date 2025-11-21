using UnityEngine;

public class TrackedModelHost : MonoBehaviour
{
    [Header("Placement")]
    [SerializeField] private Transform modelParent;
    [SerializeField] private float distance = 0.3f;
    [SerializeField] private float modelScale = 0.2f;

    [Header("Size Constraints")]
    [Tooltip("Максимальный размер модели относительно размера таргета (например, 2.0 = модель может быть в 2 раза больше таргета)")]
    [SerializeField] private float maxSizeMultiplier = 2.0f;

    [Header("Rotation")]
    [SerializeField] private bool spinModel = true;
    [SerializeField] private float placeholderSpinSpeed = 50f;
    [SerializeField] private float modelSpinSpeed = 10f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private Color gizmoColor = Color.red;
    [SerializeField] private float gizmoSize = 0.1f;

    private GameObject placeholderModel;
    private GameObject loadedModel;
    private float targetSize = 0.5f; // Размер таргета по умолчанию (в метрах)
    
    // Базовые ротации для моделей (выравнивание относительно мира)
    private Quaternion basePlaceholderRotation = Quaternion.identity;
    private Quaternion baseLoadedModelRotation = Quaternion.identity;
    
    // Текущие углы поворота для накопления
    private float placeholderRotationAngle = 0f;
    private float loadedModelRotationAngle = 0f;

    public string CurrentArtifactId { get; private set; }
    public bool HasLoadedModel => loadedModel != null;

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
        }

        if (placeholderModel != null)
        {
            AlignModel(placeholderModel.transform);
        }
    }

    private void Update()
    {
        if (loadedModel != null)
        {
            UpdateModelTransform(loadedModel.transform, ref baseLoadedModelRotation, ref loadedModelRotationAngle, modelSpinSpeed, spinModel);
        }
        else if (placeholderModel != null && placeholderModel.activeSelf)
        {
            UpdateModelTransform(placeholderModel.transform, ref basePlaceholderRotation, ref placeholderRotationAngle, placeholderSpinSpeed, spinModel);
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
    }

    public void AttachLoadedModel(GameObject modelInstance, string artifactId)
    {
        if (modelInstance == null)
        {
            return;
        }

        // Если модель уже загружена с таким же artifactId, не делаем ничего
        if (HasLoadedArtifact(artifactId))
        {
            Debug.LogWarning($"[TrackedModelHost] Модель с artifactId={artifactId} уже загружена, пропускаем повторное прикрепление");
            // Уничтожаем переданный экземпляр, так как он дубликат
            if (Application.isPlaying)
            {
                Destroy(modelInstance);
            }
            else
            {
                DestroyImmediate(modelInstance);
            }
            return;
        }

        ClearLoadedModel();

        loadedModel = modelInstance;
        loadedModel.transform.SetParent(GetAttachmentRoot(), false);
        AlignModel(loadedModel.transform);

        if (placeholderModel != null)
        {
            placeholderModel.SetActive(false);
        }

        CurrentArtifactId = artifactId;
        loadedModel.SetActive(true);
    }

    public void ResetToPlaceholder()
    {
        ClearLoadedModel();
        CurrentArtifactId = null;

        if (placeholderModel != null)
        {
            placeholderModel.SetActive(true);
            AlignModel(placeholderModel.transform);
        }
    }

    public void SetTrackingActive(bool isActive)
    {
        gameObject.SetActive(isActive);
    }

    private void ClearLoadedModel()
    {
        if (loadedModel == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(loadedModel);
        }
        else
        {
            DestroyImmediate(loadedModel);
        }

        loadedModel = null;
    }

    /// <summary>
    /// Устанавливает базовое выравнивание модели (позиция, масштаб, базовая ротация).
    /// Вызывается при инициализации или изменении параметров.
    /// </summary>
    private void AlignModel(Transform target)
    {
        if (target == null)
        {
            return;
        }

        // Позиция относительно родителя (таргета)
        target.localPosition = Vector3.up * distance;

        // Выравнивание относительно мира (чтобы модель всегда стояла вертикально)
        // Компенсируем наклон родителя (таргета), чтобы модель была вертикальной в мировых координатах
        Quaternion desiredWorldRotation = Quaternion.identity; // Вертикально вверх
        Quaternion parentWorldRotation = transform.rotation;
        Quaternion baseRotation = Quaternion.Inverse(parentWorldRotation) * desiredWorldRotation;
        
        // Сохраняем базовую ротацию
        if (target == loadedModel?.transform)
        {
            baseLoadedModelRotation = baseRotation;
            loadedModelRotationAngle = 0f; // Сбрасываем накопленный угол
        }
        else if (target == placeholderModel?.transform)
        {
            basePlaceholderRotation = baseRotation;
            placeholderRotationAngle = 0f; // Сбрасываем накопленный угол
        }
        
        // Применяем базовую ротацию
        target.localRotation = baseRotation;

        // Масштабирование
        Vector3 baseScale = new Vector3(modelScale, modelScale, modelScale);
        
        // Для загруженных моделей ограничиваем размер относительно таргета
        if (target == loadedModel?.transform && loadedModel != null)
        {
            baseScale = CalculateConstrainedScale(target, baseScale);
        }
        
        target.localScale = baseScale;
    }

    /// <summary>
    /// Обновляет трансформ модели в каждом кадре (позиция, масштаб, ротация с накоплением).
    /// </summary>
    private void UpdateModelTransform(Transform target, ref Quaternion baseRotation, ref float rotationAngle, float spinSpeed, bool shouldSpin)
    {
        if (target == null)
        {
            return;
        }

        // Обновляем позицию (на случай, если родитель переместился)
        target.localPosition = Vector3.up * distance;

        // Обновляем базовую ротацию, если родитель повернулся
        Quaternion desiredWorldRotation = Quaternion.identity;
        Quaternion parentWorldRotation = transform.rotation;
        Quaternion newBaseRotation = Quaternion.Inverse(parentWorldRotation) * desiredWorldRotation;
        
        // Если базовая ротация изменилась (родитель повернулся), обновляем её
        // Угол поворота продолжает накапливаться независимо
        if (Quaternion.Angle(baseRotation, newBaseRotation) > 0.01f)
        {
            baseRotation = newBaseRotation;
        }

        // Накопление угла поворота
        if (shouldSpin && !Mathf.Approximately(spinSpeed, 0f))
        {
            rotationAngle += spinSpeed * Time.deltaTime;
            // Нормализуем угол в диапазон 0-360 для избежания переполнения
            rotationAngle = rotationAngle % 360f;
        }

        // Применяем базовую ротацию + накопленный поворот вокруг оси Y
        target.localRotation = baseRotation * Quaternion.Euler(0, rotationAngle, 0);

        // Обновляем масштаб (на случай изменения параметров)
        Vector3 baseScale = new Vector3(modelScale, modelScale, modelScale);
        if (target == loadedModel?.transform && loadedModel != null)
        {
            baseScale = CalculateConstrainedScale(target, baseScale);
        }
        target.localScale = baseScale;
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
    /// </summary>
    private Bounds GetModelBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return new Bounds(Vector3.zero, Vector3.one);
        }

        // Получаем первый bounds в локальных координатах
        Bounds bounds = new Bounds(
            root.InverseTransformPoint(renderers[0].bounds.center),
            root.InverseTransformVector(renderers[0].bounds.size)
        );

        // Объединяем все bounds
        foreach (Renderer renderer in renderers)
        {
            Bounds rendererBounds = new Bounds(
                root.InverseTransformPoint(renderer.bounds.center),
                root.InverseTransformVector(renderer.bounds.size)
            );
            bounds.Encapsulate(rendererBounds);
        }
        
        return bounds;
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;
        Gizmos.color = gizmoColor;
        Gizmos.DrawWireSphere(transform.position + transform.up * distance, gizmoSize);
    }
}

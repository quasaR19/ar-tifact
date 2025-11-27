using UnityEngine;
using UnityEngine.UIElements;

namespace ARArtifact.UI
{
    /// <summary>
    /// Контроллер орбитальной камеры для просмотра 3D моделей.
    /// Поддерживает вращение, масштабирование и автоматическое центрирование.
    /// ВАЖНО: Pivot должен быть независимым объектом (НЕ дочерним камеры),
    /// иначе орбитальное вращение работать не будет.
    /// </summary>
    public class OrbitCameraController : MonoBehaviour
    {
        [Header("Camera Settings")]
        [SerializeField] private Camera targetCamera;
        [SerializeField] private Transform pivotPoint;
        
        [Header("Orbit Settings")]
        [SerializeField] private float rotationSpeed = 0.5f; // Уменьшена чувствительность
        [SerializeField] private float distance = 3f;
        [SerializeField] private float minDistance = 0.5f;
        [SerializeField] private float maxDistance = 10f;
        [SerializeField] private float zoomSpeed = 0.1f; // Уменьшена чувствительность
        
        [Header("Constraints")]
        [SerializeField] private float minVerticalAngle = -80f;
        [SerializeField] private float maxVerticalAngle = 80f;
        
        private Vector2 currentRotation;
        private float currentDistance;
        private bool isDragging;
        private Vector2 lastPointerPosition;
        private bool hasExternalPivot; // Флаг для отслеживания внешнего pivot
        private GameObject ownedPivotGO; // Pivot, созданный этим контроллером (для очистки)
        
        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
                if (targetCamera == null)
                {
                    targetCamera = gameObject.AddComponent<Camera>();
                }
            }
            
            // Не создаем pivot в Awake - ждем, пока будет установлен внешний pivot
            // или создадим fallback в Start если внешний не был установлен
            
            currentDistance = distance;
            currentRotation = new Vector2(0, 30); // Начальный угол камеры
        }
        
        private void Start()
        {
            // Создаем fallback pivot только если внешний не был установлен
            if (pivotPoint == null && !hasExternalPivot)
            {
                CreateFallbackPivot();
            }
            
            UpdateCameraPosition();
        }
        
        /// <summary>
        /// Создает fallback pivot (НЕ как дочерний объект камеры).
        /// </summary>
        private void CreateFallbackPivot()
        {
            // КРИТИЧНО: Pivot НЕ должен быть дочерним объектом камеры!
            // Иначе при вращении камеры pivot тоже вращается, и орбитальное вращение не работает.
            ownedPivotGO = new GameObject("OrbitPivot_Fallback");
            
            // Размещаем pivot рядом с камерой, но как независимый объект
            if (transform.parent != null)
            {
                ownedPivotGO.transform.SetParent(transform.parent);
            }
            ownedPivotGO.transform.position = transform.position + transform.forward * 3f;
            pivotPoint = ownedPivotGO.transform;
            
            Debug.LogWarning("[OrbitCameraController] Создан fallback pivot. Рекомендуется использовать SetExternalPivot()");
        }
        
        /// <summary>
        /// Устанавливает внешний pivot для орбитальной камеры.
        /// Должен быть вызван ДО Start() для правильной работы.
        /// </summary>
        public void SetExternalPivot(Transform externalPivot)
        {
            if (externalPivot == null)
            {
                Debug.LogError("[OrbitCameraController] SetExternalPivot: externalPivot == null");
                return;
            }
            
            // Очищаем собственный pivot, если он был создан
            if (ownedPivotGO != null)
            {
                Destroy(ownedPivotGO);
                ownedPivotGO = null;
            }
            
            pivotPoint = externalPivot;
            hasExternalPivot = true;
            
            Debug.Log($"[OrbitCameraController] Установлен внешний pivot: {externalPivot.name}");
        }
        
        /// <summary>
        /// Устанавливает точку вращения камеры (центр модели).
        /// </summary>
        public void SetPivotPoint(Vector3 position)
        {
            if (pivotPoint != null)
            {
                pivotPoint.position = position;
                UpdateCameraPosition();
            }
        }
        
        /// <summary>
        /// Устанавливает оптимальное расстояние камеры на основе размера модели.
        /// </summary>
        public void SetOptimalDistance(float modelSize)
        {
            // Расстояние = размер модели * 2.5 для хорошего обзора
            currentDistance = Mathf.Clamp(modelSize * 2.5f, minDistance, maxDistance);
            distance = currentDistance;
            UpdateCameraPosition();
        }
        
        /// <summary>
        /// Сбрасывает камеру в начальное положение.
        /// </summary>
        public void ResetCamera()
        {
            currentRotation = new Vector2(0, 30);
            currentDistance = distance;
            UpdateCameraPosition();
        }
        
        /// <summary>
        /// Обрабатывает начало перетаскивания (drag).
        /// </summary>
        public void OnDragStart(Vector2 pointerPosition)
        {
            isDragging = true;
            lastPointerPosition = pointerPosition;
        }
        
        /// <summary>
        /// Обрабатывает перетаскивание для вращения камеры.
        /// </summary>
        public void OnDrag(Vector2 pointerPosition)
        {
            if (!isDragging) return;
            
            Vector2 delta = pointerPosition - lastPointerPosition;
            lastPointerPosition = pointerPosition;
            
            // Инвертируем X для более естественного вращения
            currentRotation.x -= delta.x * rotationSpeed;
            currentRotation.y -= delta.y * rotationSpeed;
            
            // Ограничиваем вертикальный угол
            currentRotation.y = Mathf.Clamp(currentRotation.y, minVerticalAngle, maxVerticalAngle);
            
            UpdateCameraPosition();
        }
        
        /// <summary>
        /// Обрабатывает конец перетаскивания.
        /// </summary>
        public void OnDragEnd()
        {
            isDragging = false;
        }
        
        /// <summary>
        /// Обрабатывает масштабирование (zoom).
        /// </summary>
        public void OnZoom(float delta)
        {
            currentDistance -= delta * zoomSpeed;
            currentDistance = Mathf.Clamp(currentDistance, minDistance, maxDistance);
            UpdateCameraPosition();
        }
        
        /// <summary>
        /// Обновляет позицию и вращение камеры на основе текущих параметров.
        /// </summary>
        private void UpdateCameraPosition()
        {
            if (pivotPoint == null || targetCamera == null) return;
            
            // Вычисляем rotation на основе углов
            Quaternion rotation = Quaternion.Euler(currentRotation.y, currentRotation.x, 0);
            
            // Вычисляем позицию камеры относительно pivot point
            Vector3 offset = rotation * (Vector3.back * currentDistance);
            targetCamera.transform.position = pivotPoint.position + offset;
            
            // Камера всегда смотрит на pivot point
            targetCamera.transform.LookAt(pivotPoint.position);
        }
        
        /// <summary>
        /// Подключает обработчики событий UI Toolkit к элементу.
        /// </summary>
        public void AttachToUIElement(VisualElement element)
        {
            if (element == null) return;
            
            element.RegisterCallback<PointerDownEvent>(evt =>
            {
                OnDragStart(evt.position);
                evt.StopPropagation();
                // Предотвращаем пролистывание через StopPropagation
            }, TrickleDown.NoTrickleDown);
            
            element.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (isDragging)
                {
                    OnDrag(evt.position);
                    evt.StopPropagation();
                    // Предотвращаем пролистывание через StopPropagation
                }
            }, TrickleDown.NoTrickleDown);
            
            element.RegisterCallback<PointerUpEvent>(evt =>
            {
                OnDragEnd();
                evt.StopPropagation();
                // Предотвращаем пролистывание через StopPropagation
            }, TrickleDown.NoTrickleDown);
            
            element.RegisterCallback<WheelEvent>(evt =>
            {
                OnZoom(evt.delta.y);
                evt.StopPropagation();
                // Предотвращаем пролистывание через StopPropagation
            }, TrickleDown.NoTrickleDown);
            
            // Предотвращаем прокрутку при touch на элементе через PointerMoveEvent
            // TouchMoveEvent не существует, используем PointerMoveEvent который обрабатывает и touch
        }
        
        /// <summary>
        /// Отключает обработчики событий от элемента.
        /// </summary>
        public void DetachFromUIElement(VisualElement element)
        {
            if (element == null) return;
            
            element.UnregisterCallback<PointerDownEvent>(evt => {});
            element.UnregisterCallback<PointerMoveEvent>(evt => {});
            element.UnregisterCallback<PointerUpEvent>(evt => {});
            element.UnregisterCallback<WheelEvent>(evt => {});
        }
        
        private void OnDestroy()
        {
            // Очищаем собственный pivot, если он был создан
            if (ownedPivotGO != null)
            {
                Destroy(ownedPivotGO);
                ownedPivotGO = null;
            }
        }
    }
}


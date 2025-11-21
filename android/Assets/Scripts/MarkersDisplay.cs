using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using ARArtifact.Storage;
using ARArtifact.Services;
#if UNITY_EDITOR
using UnityEngine.XR.Simulation;
using ARArtifact.Simulation;
#endif

public class MarkersDisplay : MonoBehaviour
{
   public GameObject markerPrefab;

   public bool showGizmos = true;
   public Color gizmoColor = Color.yellow;
   public float gizmoSize = 0.5f; // физический размер маркера
   public float gap = 0.2f; // расстояние между маркерами
   public int gizmosCount = 3; // количество маркеров
   public bool showNormal = true; // показывать вектор нормали
   public Color normalColor = Color.green; // цвет вектора нормали

   private MarkerStorage storage;
   private List<GameObject> createdMarkers = new List<GameObject>();

   private void Awake()
   {
#if !UNITY_EDITOR
       // Отключаем GameObject на реальном устройстве
       gameObject.SetActive(false);
       return;
#endif
   }
   
   private void Start()
   {
#if UNITY_EDITOR
       // Инициализируем хранилище, если еще не инициализировано
       if (storage == null)
       {
           storage = new MarkerStorage();
       }
       
       // Откладываем загрузку маркеров на следующий кадр, чтобы убедиться, что все системы инициализированы
       StartCoroutine(LoadMarkersDelayedCoroutine());
#endif
   }
   
   /// <summary>
   /// Корутина для отложенной загрузки маркеров (только для Unity Editor)
   /// </summary>
   private System.Collections.IEnumerator LoadMarkersDelayedCoroutine()
   {
       yield return null; // Ждем один кадр
       LoadAndCreateMarkers();
   }

   /// <summary>
   /// Загружает все маркеры из хранилища и создает префабы
   /// </summary>
   private void LoadAndCreateMarkers()
   {
#if UNITY_EDITOR
       // Debug.Log("[MarkersDisplay] LoadAndCreateMarkers START");
       
       if (markerPrefab == null)
       {
           Debug.LogWarning("[MarkersDisplay] markerPrefab не установлен!");
           return;
       }

       // Debug.Log($"[MarkersDisplay] markerPrefab: {markerPrefab.name}");

        // Очищаем ранее созданные маркеры
        ClearCreatedMarkers();
        SimulationMarkerRegistry.Clear();

       // Загружаем маркеры из хранилища
       List<MarkerStorage.MarkerData> markers = storage.GetMarkers();
       
       if (markers == null || markers.Count == 0)
       {
           Debug.Log("[MarkersDisplay] Нет доступных маркеров для отображения");
           return;
       }

       Debug.Log($"[MarkersDisplay] Загружено маркеров из хранилища: {markers.Count}");
       // Debug.Log("[MarkersDisplay] Список маркеров:");
       // for (int idx = 0; idx < markers.Count; idx++)
       // {
       //     var m = markers[idx];
       //     Debug.Log($"[MarkersDisplay]   [{idx}] id='{m.id}', url='{m.url}', localImagePath='{m.localImagePath}', exists={(!string.IsNullOrEmpty(m.localImagePath) && File.Exists(m.localImagePath))}");
       // }

       // Получаем локальную ось X для направления ряда
       Vector3 right = transform.right;

       // Создаем префабы для каждого маркера
       for (int i = 0; i < markers.Count; i++)
       {
           var marker = markers[i];
           Debug.Log($"[MarkersDisplay] Обработка маркера [{i}]: id='{marker.id}'");
           // Debug.Log($"[MarkersDisplay] Marker.url: '{marker.url}'");
           // Debug.Log($"[MarkersDisplay] Marker.localImagePath: '{marker.localImagePath}'");
           
           // Пропускаем маркеры без локального изображения
           if (string.IsNullOrEmpty(marker.localImagePath) || !File.Exists(marker.localImagePath))
           {
               Debug.LogWarning($"[MarkersDisplay] Маркер {marker.id} не имеет локального изображения, пропускаем");
               continue;
           }

           // Позиция маркера с учетом поворота объекта
           Vector3 position = transform.position + right * i * (gizmoSize + gap);
           // Debug.Log($"[MarkersDisplay] Позиция маркера: {position}");
           
           // Создаем экземпляр префаба
           GameObject markerInstance = Instantiate(markerPrefab, position, transform.rotation, transform);
           markerInstance.name = $"Marker_{marker.id}";
           // Debug.Log($"[MarkersDisplay] Создан GameObject: {markerInstance.name}");

           // Загружаем изображение
           Texture2D texture = MarkerImageService.Instance?.LoadLocalImage(marker.localImagePath);
           if (texture == null)
           {
               Debug.LogWarning($"[MarkersDisplay] Не удалось загрузить изображение для маркера {marker.id} по пути {marker.localImagePath}");
               Destroy(markerInstance);
               continue;
           }

           // Debug.Log($"[MarkersDisplay] Изображение загружено: {texture.width}x{texture.height}, format={texture.format}");

           // Устанавливаем изображение в компонент SimulatedTrackedImage
#if UNITY_EDITOR
           SimulatedTrackedImage trackedImage = markerInstance.GetComponent<SimulatedTrackedImage>();
            if (trackedImage != null)
           {
               // Debug.Log($"[MarkersDisplay] SimulatedTrackedImage найден, устанавливаем текстуру...");
               Debug.Log($"[MarkersDisplay] Marker.id: '{marker.id}', GameObject.name: {markerInstance.name}");
               // Debug.Log($"[MarkersDisplay] Текстура перед установкой: {texture.name}, {texture.width}x{texture.height}, format={texture.format}");
               
               SetTrackedImageTexture(trackedImage, texture, marker.id);

                var binder = markerInstance.GetComponent<SimulatedMarkerBinder>();
                if (binder == null)
                {
                    binder = markerInstance.AddComponent<SimulatedMarkerBinder>();
                }
                binder.Initialize(marker.id);
               
               // Проверяем, что текстура установлена
               // var imageField = typeof(SimulatedTrackedImage).GetField("m_Image", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
               // if (imageField != null)
               // {
               //     var setTexture = imageField.GetValue(trackedImage) as Texture2D;
               //     if (setTexture != null)
               //     {
               //         Debug.Log($"[MarkersDisplay] ✓ Текстура в m_Image после установки: {setTexture.name}, {setTexture.width}x{setTexture.height}");
               //         Debug.Log($"[MarkersDisplay] ✓ Текстура совпадает: {ReferenceEquals(texture, setTexture)}");
               //     }
               //     else
               //     {
               //         Debug.LogError($"[MarkersDisplay] ✗ Текстура в m_Image после установки: null!");
               //     }
               // }
               // else
               // {
               //     Debug.LogError($"[MarkersDisplay] ✗ Не удалось найти поле m_Image для проверки");
               // }
               
               // Дополнительная проверка: получаем имя reference image, если доступно
               // var nameField = typeof(SimulatedTrackedImage).GetField("m_ImageName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
               // if (nameField != null)
               // {
               //     var imageName = nameField.GetValue(trackedImage) as string;
               //     Debug.Log($"[MarkersDisplay] m_ImageName: '{imageName}'");
               // }
               
               Debug.Log($"[MarkersDisplay] ✓ Изображение установлено для маркера {marker.id}");
           }
           else
           {
               Debug.LogError($"[MarkersDisplay] ✗ Компонент SimulatedTrackedImage не найден на префабе {markerPrefab.name}");
           }
#else
           Debug.LogWarning("[MarkersDisplay] SimulatedTrackedImage доступен только в Unity Editor");
#endif

           createdMarkers.Add(markerInstance);
       }

       Debug.Log($"[MarkersDisplay] Итого создано маркеров: {createdMarkers.Count}");
#endif
   }

   /// <summary>
   /// Устанавливает текстуру в компонент SimulatedTrackedImage
   /// </summary>
#if UNITY_EDITOR
   private void SetTrackedImageTexture(SimulatedTrackedImage trackedImage, Texture2D texture, string markerId)
   {
       // Debug.Log($"[MarkersDisplay] SetTrackedImageTexture: markerId='{markerId}', texture={texture.name}");
       
       // Используем рефлексию для установки приватного поля m_Image
       FieldInfo imageField = typeof(SimulatedTrackedImage).GetField("m_Image", BindingFlags.NonPublic | BindingFlags.Instance);
       if (imageField != null)
       {
           // var oldTexture = imageField.GetValue(trackedImage) as Texture2D;
           // Debug.Log($"[MarkersDisplay] Старая текстура в m_Image: {(oldTexture != null ? $"{oldTexture.name}, {oldTexture.width}x{oldTexture.height}" : "null")}");
           
           imageField.SetValue(trackedImage, texture);
           // Debug.Log($"[MarkersDisplay] ✓ Текстура установлена в m_Image");
           
           // var verifyTexture = imageField.GetValue(trackedImage) as Texture2D;
           // if (verifyTexture != null)
           // {
           //     Debug.Log($"[MarkersDisplay] ✓ Проверка: текстура в m_Image после установки: {verifyTexture.name}, {verifyTexture.width}x{verifyTexture.height}");
           // }
       }
       else
       {
           Debug.LogError("[MarkersDisplay] ✗ Не удалось найти поле m_Image в SimulatedTrackedImage");
           return;
       }

       // Пытаемся установить имя изображения (если есть такое поле)
       // FieldInfo nameField = typeof(SimulatedTrackedImage).GetField("m_ImageName", BindingFlags.NonPublic | BindingFlags.Instance);
       // if (nameField != null)
       // {
       //     var oldName = nameField.GetValue(trackedImage) as string;
       //     Debug.Log($"[MarkersDisplay] Старое имя в m_ImageName: '{oldName}'");
       //     // Не устанавливаем имя здесь, так как оно должно быть установлено при создании reference image
       // }

       // Обновляем материал, чтобы он использовал новую текстуру
       FieldInfo materialField = typeof(SimulatedTrackedImage).GetField("m_QuadMaterial", BindingFlags.NonPublic | BindingFlags.Instance);
       if (materialField != null)
       {
           Material quadMaterial = materialField.GetValue(trackedImage) as Material;
           if (quadMaterial != null)
           {
               // Debug.Log($"[MarkersDisplay] Обновляем материал: {quadMaterial.name}");
               quadMaterial.mainTexture = texture;
               // Debug.Log($"[MarkersDisplay] ✓ Текстура установлена в материал");
           }
           // else
           // {
           //     Debug.LogWarning("[MarkersDisplay] m_QuadMaterial == null");
           // }
       }
       // else
       // {
       //     Debug.LogWarning("[MarkersDisplay] Поле m_QuadMaterial не найдено");
       // }

       // Обновляем физический размер, чтобы триггернуть обновление mesh и визуализации
       FieldInfo sizeField = typeof(SimulatedTrackedImage).GetField("m_ImagePhysicalSizeMeters", BindingFlags.NonPublic | BindingFlags.Instance);
       if (sizeField != null)
       {
           Vector2 currentSize = (Vector2)sizeField.GetValue(trackedImage);
           // Debug.Log($"[MarkersDisplay] Текущий размер: {currentSize}");
           // Небольшое изменение размера, чтобы триггернуть OnValidate
           sizeField.SetValue(trackedImage, currentSize + new Vector2(0.0001f, 0.0001f));
           // Возвращаем обратно
           sizeField.SetValue(trackedImage, currentSize);
           // Debug.Log($"[MarkersDisplay] ✓ Размер обновлен для триггера OnValidate");
       }
       // else
       // {
       //     Debug.LogWarning("[MarkersDisplay] Поле m_ImagePhysicalSizeMeters не найдено");
       // }

       // Вызываем UpdateQuadMesh через рефлексию, если метод доступен
       MethodInfo updateQuadMeshMethod = typeof(SimulatedTrackedImage).GetMethod("UpdateQuadMesh", BindingFlags.NonPublic | BindingFlags.Instance);
       if (updateQuadMeshMethod != null)
       {
           // Debug.Log($"[MarkersDisplay] Вызываем UpdateQuadMesh...");
           updateQuadMeshMethod.Invoke(trackedImage, null);
           // Debug.Log($"[MarkersDisplay] ✓ UpdateQuadMesh вызван");
       }
       // else
       // {
       //     Debug.LogWarning("[MarkersDisplay] Метод UpdateQuadMesh не найден");
       // }
   }
#endif

   /// <summary>
   /// Очищает все созданные маркеры
   /// </summary>
   private void ClearCreatedMarkers()
   {
       foreach (var marker in createdMarkers)
       {
           if (marker != null)
           {
               Destroy(marker);
           }
       }
       createdMarkers.Clear();
   }

   private void OnDrawGizmos() {
    if (!showGizmos) return;

    // Получаем локальную ось X для направления ряда
    Vector3 right = transform.right;
    // Получаем локальную ось Y для нормали (направление вперед от изображения)
    Vector3 up = transform.up;

    for (int i = 0; i < gizmosCount; i++) {
            // Позиция маркера с учетом поворота объекта
            Vector3 position = transform.position + right * i * (gizmoSize + gap);
            Gizmos.color = gizmoColor;
            
            // Сохраняем текущую матрицу и устанавливаем матрицу для учета поворота
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(position, transform.rotation, Vector3.one);
            
            // Рисование 2d прямоугольника + крест в центре (в локальных координатах)
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(gizmoSize, 0, gizmoSize));
            Gizmos.DrawLine(new Vector3(-gizmoSize/4, 0, 0), new Vector3(gizmoSize/4, 0, 0));
            Gizmos.DrawLine(new Vector3(0, 0, -gizmoSize/4), new Vector3(0, 0, gizmoSize / 4));
            
            // Восстанавливаем матрицу
            Gizmos.matrix = oldMatrix;
            
            // Рисование вектора нормали
            if (showNormal)
            {
                Gizmos.color = normalColor;
                float normalLength = gizmoSize / 8f;
                Vector3 normalEnd = position + up * normalLength;
                Gizmos.DrawLine(position, normalEnd);
            }
        }
    }

   private void OnDestroy()
   {
       ClearCreatedMarkers();
   }
}

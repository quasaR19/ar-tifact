using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.ARFoundation;
using Unity.Jobs;
using ARArtifact.Storage;

namespace ARArtifact.Services
{
    /// <summary>
    /// Сервис для создания динамической библиотеки референсов для AR Foundation
    /// </summary>
    public class DynamicReferenceLibrary : MonoBehaviour
    {
        private static DynamicReferenceLibrary _instance;
        public static DynamicReferenceLibrary Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("DynamicReferenceLibrary");
                    _instance = go.AddComponent<DynamicReferenceLibrary>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }
        
        private MarkerStorage storage;
        private MutableRuntimeReferenceImageLibrary mutableLibrary;
        private ARTrackedImageManager trackedImageManager;
        
        public event System.Action OnLibraryCreated;
        public event System.Action<string> OnLibraryCreationFailed;
        
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            storage = new MarkerStorage();
        }
        
        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
        private readonly Dictionary<Guid, string> referenceGuidToTargetId = new();
        private readonly Dictionary<Guid, string> textureGuidToTargetId = new();
        private readonly Dictionary<string, string> referenceNameToTargetId = new();

        /// <summary>
        /// Находит объект XROrigin по тегу и получает компонент ARTrackedImageManager
        /// </summary>
        private ARTrackedImageManager FindXROriginImageManager()
        {
            GameObject xrOrigin = GameObject.FindGameObjectWithTag("XROrigin");
            if (xrOrigin == null)
            {
                Debug.LogError("[DynamicReferenceLibrary] Объект с тегом 'XROrigin' не найден!");
                return null;
            }
            
            ARTrackedImageManager imageManager = xrOrigin.GetComponent<ARTrackedImageManager>();
            if (imageManager == null)
            {
                Debug.LogError("[DynamicReferenceLibrary] Компонент ARTrackedImageManager не найден на объекте XROrigin!");
                return null;
            }
            
            Debug.Log($"[DynamicReferenceLibrary] Найден XROrigin: {xrOrigin.name}, ARTrackedImageManager найден");
            return imageManager;
        }
        
        /// <summary>
        /// Создает динамическую библиотеку референсов из загруженных маркеров
        /// Автоматически находит объект XROrigin по тегу, если imageManager не указан
        /// </summary>
        public void CreateReferenceLibrary(ARTrackedImageManager imageManager = null)
        {
            // Если imageManager не указан, пытаемся найти XROrigin по тегу
            if (imageManager == null)
            {
                imageManager = FindXROriginImageManager();
                if (imageManager == null)
                {
                    OnLibraryCreationFailed?.Invoke("ARTrackedImageManager не найден");
                    return;
                }
            }
            
            trackedImageManager = imageManager;
            
            // Получаем маркеры
            List<Storage.MarkerStorage.MarkerData> markers = null;
            if (MarkerService.Instance != null)
            {
                markers = MarkerService.Instance.GetMarkers();
            }
            else
            {
                markers = storage.GetMarkers();
            }
            
            if (markers == null || markers.Count == 0)
            {
                Debug.LogWarning("[DynamicReferenceLibrary] Нет маркеров для создания библиотеки");
                OnLibraryCreationFailed?.Invoke("Нет маркеров для создания библиотеки");
                return;
            }
            
            StartCoroutine(CreateLibraryCoroutine(markers));
        }
        
        /// <summary>
        /// Корутина для создания библиотеки референсов
        /// </summary>
        private IEnumerator CreateLibraryCoroutine(List<Storage.MarkerStorage.MarkerData> markers)
        {
#if !UNITY_EDITOR
            // На устройстве проверяем поддержку mutable библиотек через дескриптор подсистемы
            var subsystem = trackedImageManager.subsystem;
            if (subsystem == null || subsystem.subsystemDescriptor == null)
            {
                Debug.LogError("[DynamicReferenceLibrary] Подсистема отслеживания изображений не найдена!");
                OnLibraryCreationFailed?.Invoke("Подсистема отслеживания изображений не найдена");
                yield break;
            }
            
            var descriptor = subsystem.subsystemDescriptor as XRImageTrackingSubsystemDescriptor;
            if (descriptor == null || !descriptor.supportsMutableLibrary)
            {
                Debug.LogError("[DynamicReferenceLibrary] Платформа не поддерживает mutable библиотеки!");
                OnLibraryCreationFailed?.Invoke("Платформа не поддерживает mutable библиотеки");
                yield break;
            }
            
            // Проверяем состояние AR Session
            if (ARSession.state < ARSessionState.Ready)
            {
                Debug.LogWarning("[DynamicReferenceLibrary] AR Session еще не готов. Ждем...");
                
                float sessionTimeout = 10f;
                float sessionElapsed = 0f;
                
                while (ARSession.state < ARSessionState.Ready)
                {
                    var state = ARSession.state;
                    
                    // Проверяем на ошибки
                    if (state == ARSessionState.Unsupported)
                    {
                        Debug.LogError("[DynamicReferenceLibrary] ARCore не поддерживается на этом устройстве!");
                        OnLibraryCreationFailed?.Invoke("ARCore не поддерживается");
                        yield break;
                    }
                    
                    // Проверяем таймаут
                    sessionElapsed += Time.deltaTime;
                    if (sessionElapsed >= sessionTimeout)
                    {
                        Debug.LogError($"[DynamicReferenceLibrary] Таймаут ожидания инициализации AR Session (состояние: {state})");
                        OnLibraryCreationFailed?.Invoke("Таймаут инициализации AR Session");
                        yield break;
                    }
                    
                    yield return null;
                }
            }
#else
            // В редакторе пропускаем проверку AR Session
            Debug.Log("[DynamicReferenceLibrary] UNITY_EDITOR: Пропускаем проверку AR Session");
#endif
            
            referenceGuidToTargetId.Clear();
            textureGuidToTargetId.Clear();
            referenceNameToTargetId.Clear();
            
#if !UNITY_EDITOR
            // На устройстве создаем mutable библиотеку через CreateRuntimeLibrary
            mutableLibrary = trackedImageManager.CreateRuntimeLibrary() as MutableRuntimeReferenceImageLibrary;
#else
            // В редакторе CreateRuntimeLibrary может не работать, используем обходной путь
            Debug.Log("[DynamicReferenceLibrary] UNITY_EDITOR: Попытка создания библиотеки для симуляции");
            try
            {
                mutableLibrary = trackedImageManager.CreateRuntimeLibrary() as MutableRuntimeReferenceImageLibrary;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[DynamicReferenceLibrary] UNITY_EDITOR: CreateRuntimeLibrary не поддерживается в редакторе: {e.Message}");
                // В редакторе просто помечаем, что библиотека "создана" (для совместимости)
                OnLibraryCreated?.Invoke();
                yield break;
            }
#endif
            
            if (mutableLibrary == null)
            {
                Debug.LogError("[DynamicReferenceLibrary] Не удалось создать mutable библиотеку!");
                OnLibraryCreationFailed?.Invoke("Не удалось создать mutable библиотеку");
                yield break;
            }
            
            Debug.Log($"[DynamicReferenceLibrary] СОЗДАНИЕ БИБЛИОТЕКИ МАРКЕРОВ: Всего маркеров для добавления: {markers.Count}");
            
            int successCount = 0;
            int failCount = 0;
            
            int markerIndex = 0;
            foreach (var marker in markers)
            {
                markerIndex++;
                // Debug.Log($"[DynamicReferenceLibrary] Обработка маркера {markerIndex}/{markers.Count}: ID={marker.id}");
                
                if (string.IsNullOrEmpty(marker.localImagePath))
                {
                    Debug.LogWarning($"[DynamicReferenceLibrary] Маркер {marker.id} не имеет локального изображения, пропускаем");
                    failCount++;
                    continue;
                }
                
                // Загружаем изображение
                Debug.Log($"[DynamicReferenceLibrary] [МАРКЕР {markerIndex}/{markers.Count}] Загрузка изображения для маркера {marker.id}: {marker.localImagePath}");
                Texture2D texture = MarkerImageService.Instance?.LoadLocalImage(marker.localImagePath);
                if (texture == null)
                {
                    Debug.LogError($"[DynamicReferenceLibrary] [МАРКЕР {markerIndex}/{markers.Count}] ✗ Не удалось загрузить изображение для маркера {marker.id} по пути {marker.localImagePath}");
                    failCount++;
                    continue;
                }
                
                Debug.Log($"[DynamicReferenceLibrary] [МАРКЕР {markerIndex}/{markers.Count}] Изображение загружено: размер={texture.width}x{texture.height}, формат={texture.format}, readable={texture.isReadable}");
                
                // Проверяем размер изображения (ARCore требует минимум 300x300)
                const int MIN_SIZE = 300;
                if (texture.width < MIN_SIZE || texture.height < MIN_SIZE)
                {
                    Debug.LogWarning($"[DynamicReferenceLibrary] [МАРКЕР {markerIndex}/{markers.Count}] ⚠️ Изображение слишком маленькое ({texture.width}x{texture.height}), требуется минимум {MIN_SIZE}x{MIN_SIZE}. Масштабируем...");
                    Texture2D scaledTexture = MarkerImageService.Instance?.ScaleTextureIfNeeded(texture, MIN_SIZE, MIN_SIZE);
                    if (scaledTexture != null)
                    {
                        // Уничтожаем старое изображение, если оно было создано временно
                        if (texture != null)
                        {
                            Destroy(texture);
                        }
                        texture = scaledTexture;
                        Debug.Log($"[DynamicReferenceLibrary] [МАРКЕР {markerIndex}/{markers.Count}] ✓ Изображение масштабировано: {texture.width}x{texture.height}");
                    }
                    else
                    {
                        Debug.LogError($"[DynamicReferenceLibrary] [МАРКЕР {markerIndex}/{markers.Count}] ✗ Не удалось масштабировать изображение для маркера {marker.id}");
                        failCount++;
                        continue;
                    }
                }
                
                // Проверяем, что текстура readable
                if (!texture.isReadable)
                {
                    Debug.LogWarning($"[DynamicReferenceLibrary] [МАРКЕР {markerIndex}/{markers.Count}] ⚠️ Текстура для маркера {marker.id} не является readable. Создаем копию...");
                    Texture2D readableTexture = CreateReadableTexture(texture);
                    if (readableTexture == null)
                    {
                        Debug.LogError($"[DynamicReferenceLibrary] [МАРКЕР {markerIndex}/{markers.Count}] ✗ Не удалось создать readable копию для маркера {marker.id}");
                        failCount++;
                        continue;
                    }
                    // Уничтожаем старое изображение
                    if (texture != null)
                    {
                        Destroy(texture);
                    }
                    texture = readableTexture;
                    Debug.Log($"[DynamicReferenceLibrary] [МАРКЕР {markerIndex}/{markers.Count}] ✓ Readable копия создана: размер={texture.width}x{texture.height}, формат={texture.format}");
                }
                
                // Финальная проверка параметров текстуры перед добавлением
                Debug.Log($"[DynamicReferenceLibrary] [МАРКЕР {markerIndex}/{markers.Count}] Параметры текстуры перед добавлением: размер={texture.width}x{texture.height}, формат={texture.format}, readable={texture.isReadable}, mipmap={texture.mipmapCount}");
                
                // Проверяем, что формат поддерживается ARCore
                // ARCore поддерживает: RGB24, RGBA32, ARGB32, Alpha8, R8, RFloat, BGRA32
                bool isFormatSupported = texture.format == TextureFormat.RGB24 ||
                                        texture.format == TextureFormat.RGBA32 ||
                                        texture.format == TextureFormat.ARGB32 ||
                                        texture.format == TextureFormat.BGRA32 ||
                                        texture.format == TextureFormat.Alpha8 ||
                                        texture.format == TextureFormat.R8 ||
                                        texture.format == TextureFormat.RFloat;
                
                if (!isFormatSupported)
                {
                    Debug.LogWarning($"[DynamicReferenceLibrary] [МАРКЕР {markerIndex}/{markers.Count}] ⚠️ Формат {texture.format} может не поддерживаться ARCore. Конвертируем в RGB24...");
                    Texture2D convertedTexture = ConvertToRGB24(texture);
                    if (convertedTexture != null)
                    {
                        Destroy(texture);
                        texture = convertedTexture;
                        Debug.Log($"[DynamicReferenceLibrary] [МАРКЕР {markerIndex}/{markers.Count}] ✓ Текстура конвертирована в RGB24: размер={texture.width}x{texture.height}, формат={texture.format}");
                    }
                }
                
                // Добавляем в библиотеку
                AddReferenceImageJobState? jobState = null;
                bool jobScheduled = false;
                
                try
                {
                    // Используем ScheduleAddImageWithValidationJob (рекомендуемый метод)
                    float markerSize = 0.2f; // 0.2 метра - размер маркера
                    Debug.Log($"[DynamicReferenceLibrary] [МАРКЕР {markerIndex}/{markers.Count}] Добавление маркера в библиотеку: ID={marker.id}, размер={markerSize}m, текстура={texture.width}x{texture.height}, формат={texture.format}");
                    
                    jobState = mutableLibrary.ScheduleAddImageWithValidationJob(
                        texture, 
                        marker.id, 
                        markerSize
                    );
                    jobScheduled = true;
                    Debug.Log($"[DynamicReferenceLibrary] [МАРКЕР {markerIndex}/{markers.Count}] ✓ Job запланирован для маркера {marker.id}");
                }
                catch (System.Exception e)
                {
                    failCount++;
                    Debug.LogError($"[DynamicReferenceLibrary] [МАРКЕР {markerIndex}/{markers.Count}] ✗ Ошибка при планировании добавления маркера {marker.id}: {e.Message}\nStackTrace: {e.StackTrace}");
                    continue;
                }
                
                // Ждем завершения добавления (yield вне try-catch)
                if (jobScheduled && jobState.HasValue)
                {
                    while (!jobState.Value.jobHandle.IsCompleted)
                    {
                        yield return null;
                    }
                    
                    try
                    {
                        jobState.Value.jobHandle.Complete();
                        
                        Debug.Log($"[DynamicReferenceLibrary] [МАРКЕР {markerIndex}/{markers.Count}] Статус валидации для маркера {marker.id}: {jobState.Value.status}");
                        
                        if (jobState.Value.status == AddReferenceImageJobStatus.Success)
                        {
                            successCount++;
                            Debug.Log($"[DynamicReferenceLibrary] [МАРКЕР {markerIndex}/{markers.Count}] ✓✓✓ Маркер {marker.id} успешно добавлен в библиотеку!");
                            
                            if (mutableLibrary != null && mutableLibrary.count > 0)
                            {
                                var addedImage = mutableLibrary[mutableLibrary.count - 1];
                                
                                Debug.Log($"[DynamicReferenceLibrary] Создание маппинга для маркера {marker.id}: guid={addedImage.guid}, textureGuid={addedImage.textureGuid}, name='{addedImage.name}' -> targetId='{marker.id}'");
                                
                                referenceGuidToTargetId[addedImage.guid] = marker.id;
                                textureGuidToTargetId[addedImage.textureGuid] = marker.id;
                                if (!string.IsNullOrEmpty(addedImage.name))
                                {
                                    referenceNameToTargetId[addedImage.name] = marker.id;
                                    // Debug.Log($"[DynamicReferenceLibrary] ✓ Маппинг создан: name='{addedImage.name}' -> targetId='{marker.id}'");
                                }
                                else
                                {
                                    Debug.LogWarning($"[DynamicReferenceLibrary] ⚠️ ReferenceImage.name пуст, маппинг по имени не создан");
                                }
                                
                                // Debug.Log($"[DynamicReferenceLibrary] ✓ Маппинг создан: guid={addedImage.guid} -> targetId='{marker.id}'");
                                // Debug.Log($"[DynamicReferenceLibrary] ✓ Маппинг создан: textureGuid={addedImage.textureGuid} -> targetId='{marker.id}'");
                            }
                            else
                            {
                                Debug.LogWarning($"[DynamicReferenceLibrary] ⚠️ Не удалось получить добавленное изображение из библиотеки");
                            }
                        }
                        else
                        {
                            failCount++;
                            string errorDetails = GetErrorStatusDescription(jobState.Value.status);
                            Debug.LogError($"[DynamicReferenceLibrary] [МАРКЕР {markerIndex}/{markers.Count}] ✗✗✗ Не удалось добавить маркер {marker.id}: {jobState.Value.status}\n{errorDetails}\nПараметры текстуры: размер={texture.width}x{texture.height}, формат={texture.format}, readable={texture.isReadable}\nПуть к файлу: {marker.localImagePath}");
                            
                            // Уничтожаем текстуру в случае ошибки (job не будет её использовать)
                            if (texture != null)
                            {
                                Destroy(texture);
                            }
                        }
                    }
                    catch (System.Exception e)
                    {
                        failCount++;
                        Debug.LogError($"[DynamicReferenceLibrary] Ошибка при завершении добавления маркера {marker.id}: {e.Message}");
                    }
                }
                
                // Небольшая задержка между добавлениями
                yield return null;
            }
            
            // Включаем ARTrackedImageManager перед установкой библиотеки
            if (!trackedImageManager.enabled)
            {
                Debug.Log("[DynamicReferenceLibrary] Включаем ARTrackedImageManager перед установкой библиотеки...");
                trackedImageManager.enabled = true;
            }
            
            // Устанавливаем библиотеку в менеджер
            trackedImageManager.referenceLibrary = mutableLibrary;
            
            // ВАЖНО: Включаем ARTrackedImageManager сразу после привязки библиотеки
            // Это гарантирует, что компонент будет включен даже если он был отключен ранее
            // Убеждаемся, что GameObject активен
            if (!trackedImageManager.gameObject.activeSelf)
            {
                Debug.Log("[DynamicReferenceLibrary] Активируем GameObject с ARTrackedImageManager...");
                trackedImageManager.gameObject.SetActive(true);
            }
            
            // Безусловно включаем компонент после установки библиотеки
            if (!trackedImageManager.enabled)
            {
                Debug.Log("[DynamicReferenceLibrary] Включаем ARTrackedImageManager после установки библиотеки...");
            }
            trackedImageManager.enabled = true;
            
            // Проверяем, что библиотека установлена
            if (trackedImageManager.referenceLibrary != null)
            {
                Debug.Log($"[DynamicReferenceLibrary] ✓ Библиотека успешно установлена в ARTrackedImageManager");
                Debug.Log($"[DynamicReferenceLibrary] ✓ ARTrackedImageManager.enabled = {trackedImageManager.enabled}");
                Debug.Log($"[DynamicReferenceLibrary] ✓ Библиотека содержит {trackedImageManager.referenceLibrary.count} изображений");
            }
            else
            {
                Debug.LogError("[DynamicReferenceLibrary] ✗ Не удалось установить библиотеку в ARTrackedImageManager!");
            }
            
            Debug.Log($"[DynamicReferenceLibrary] ИТОГО: Маркеров в библиотеке: {mutableLibrary?.count ?? 0}, Успешно добавлено: {successCount}, Ошибок: {failCount}");
            
            // Выводим все созданные маппинги
            if (successCount > 0)
            {
                LogAllMappings();
            }
            
            // Выводим информацию о загруженных маркерах
            // if (successCount > 0 && mutableLibrary != null)
            // {
            //     Debug.Log($"[DynamicReferenceLibrary] Всего маркеров в библиотеке: {mutableLibrary.count}");
            //     
            //     // Получаем информацию о каждом маркере из библиотеки
            //     for (int i = 0; i < mutableLibrary.count; i++)
            //     {
            //         try
            //         {
            //             var referenceImage = mutableLibrary[i];
            //             Debug.Log($"[DynamicReferenceLibrary] Маркер #{i + 1}:");
            //             Debug.Log($"  - Имя: {referenceImage.name}");
            //             Debug.Log($"  - GUID: {referenceImage.guid}");
            //             Debug.Log($"  - Размер: {referenceImage.size.x}m x {referenceImage.size.y}m");
            //             Debug.Log($"  - Размер указан: {referenceImage.specifySize}");
            //         }
            //         catch (System.Exception e)
            //         {
            //             Debug.LogWarning($"[DynamicReferenceLibrary] Не удалось получить информацию о маркере #{i + 1}: {e.Message}");
            //         }
            //     }
            // }
            
            if (successCount > 0)
            {
                // Выводим информацию о загруженных маркерах
                LogLoadedMarkers();
                
                OnLibraryCreated?.Invoke();
            }
            else
            {
                OnLibraryCreationFailed?.Invoke("Не удалось добавить ни одного маркера в библиотеку");
            }
        }
        
        /// <summary>
        /// Создает readable копию текстуры с правильным форматом для ARCore
        /// </summary>
        private Texture2D CreateReadableTexture(Texture2D source)
        {
            try
            {
                // Определяем правильный формат для ARCore (RGB24 или RGBA32)
                TextureFormat targetFormat = TextureFormat.RGB24;
                if (source.format == TextureFormat.RGBA32 || 
                    source.format == TextureFormat.ARGB32 ||
                    source.format == TextureFormat.RGBA4444)
                {
                    targetFormat = TextureFormat.RGBA32;
                }
                
                RenderTexture renderTexture = RenderTexture.GetTemporary(
                    source.width,
                    source.height,
                    0,
                    RenderTextureFormat.Default,
                    RenderTextureReadWrite.Linear);

                Graphics.Blit(source, renderTexture);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = renderTexture;

                // Создаем текстуру с правильным форматом для ARCore
                Texture2D readableTexture = new Texture2D(source.width, source.height, targetFormat, false);
                readableTexture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
                readableTexture.Apply();

                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(renderTexture);

                Debug.Log($"[DynamicReferenceLibrary] Readable копия создана: размер={readableTexture.width}x{readableTexture.height}, формат={readableTexture.format}, readable={readableTexture.isReadable}");

                return readableTexture;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[DynamicReferenceLibrary] Ошибка создания readable текстуры: {e.Message}\nStackTrace: {e.StackTrace}");
                return null;
            }
        }
        
        /// <summary>
        /// Конвертирует текстуру в RGB24 формат (гарантированно поддерживается ARCore)
        /// </summary>
        private Texture2D ConvertToRGB24(Texture2D source)
        {
            if (source == null)
            {
                return null;
            }
            
            try
            {
                RenderTexture renderTexture = RenderTexture.GetTemporary(
                    source.width,
                    source.height,
                    0,
                    RenderTextureFormat.Default,
                    RenderTextureReadWrite.Linear);
                
                Graphics.Blit(source, renderTexture);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = renderTexture;
                
                // Создаем новую текстуру в формате RGB24
                Texture2D rgbTexture = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
                rgbTexture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                rgbTexture.Apply();
                
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(renderTexture);
                
                return rgbTexture;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[DynamicReferenceLibrary] Ошибка конвертации в RGB24: {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Возвращает описание статуса ошибки валидации изображения
        /// </summary>
        private string GetErrorStatusDescription(AddReferenceImageJobStatus status)
        {
            switch (status)
            {
                case AddReferenceImageJobStatus.ErrorInvalidImage:
                    return "ОШИБКА: Неверное изображение. Возможные причины:\n" +
                           "- Изображение слишком маленькое (требуется минимум 300x300)\n" +
                           "- Недостаточный контраст или детали для трекинга\n" +
                           "- Неподдерживаемый формат пикселей\n" +
                           "- Изображение слишком простое (однотонное, без уникальных признаков)\n" +
                           "- Изображение слишком большое";
                case AddReferenceImageJobStatus.ErrorDuplicateImage:
                    return "ОШИБКА: Дублирующееся изображение (изображение с таким же содержимым уже существует в библиотеке)";
                case AddReferenceImageJobStatus.ErrorUnknown:
                    return "ОШИБКА: Неизвестная ошибка при добавлении изображения";
                case AddReferenceImageJobStatus.Pending:
                    return "СТАТУС: Задача еще выполняется";
                case AddReferenceImageJobStatus.Success:
                    return "УСПЕХ: Изображение успешно добавлено";
                case AddReferenceImageJobStatus.None:
                    return "СТАТУС: Статус не определен";
                default:
                    return $"Неизвестный статус: {status}";
            }
        }
        
        /// <summary>
        /// Обновляет библиотеку референсов (пересоздает с новыми маркерами)
        /// Автоматически находит объект XROrigin по тегу, если imageManager не указан
        /// </summary>
        public void UpdateReferenceLibrary(ARTrackedImageManager imageManager = null)
        {
            CreateReferenceLibrary(imageManager);
        }
        
        /// <summary>
        /// Получает текущую библиотеку референсов
        /// </summary>
        public MutableRuntimeReferenceImageLibrary GetLibrary()
        {
            return mutableLibrary;
        }
        
        /// <summary>
        /// Проверяет, создана ли библиотека
        /// </summary>
        public bool IsLibraryCreated()
        {
            return mutableLibrary != null;
        }
        
        /// <summary>
        /// Выводит информацию о загруженных маркерах в библиотеке
        /// </summary>
        public void LogLoadedMarkers()
        {
            if (mutableLibrary == null)
            {
                Debug.LogWarning("[DynamicReferenceLibrary] Библиотека еще не создана");
                return;
            }
            
            if (trackedImageManager == null)
            {
                Debug.LogWarning("[DynamicReferenceLibrary] ARTrackedImageManager не установлен");
                return;
            }
            
            // Проверяем, установлена ли библиотека в менеджер
            var currentLibrary = trackedImageManager.referenceLibrary;
            if (currentLibrary == null)
            {
                Debug.LogWarning("[DynamicReferenceLibrary] Библиотека не установлена в ARTrackedImageManager");
                return;
            }
            
            Debug.Log($"[DynamicReferenceLibrary] ИНФОРМАЦИЯ О БИБЛИОТЕКЕ МАРКЕРОВ: Всего маркеров={currentLibrary.count}, Тип={currentLibrary.GetType().Name}");
            
            if (currentLibrary.count > 0)
            {
                // Debug.Log($"[DynamicReferenceLibrary] Список маркеров:");
                
                // Получаем информацию о каждом маркере из библиотеки
                for (int i = 0; i < currentLibrary.count; i++)
                {
                    try
                    {
                        var referenceImage = currentLibrary[i];
                        Debug.Log($"[DynamicReferenceLibrary] Маркер #{i + 1}: name='{referenceImage.name}', guid={referenceImage.guid}, textureGuid={referenceImage.textureGuid}");
                        // Debug.Log($"  - Размер: {referenceImage.size.x}m x {referenceImage.size.y}m");
                        // Debug.Log($"  - Размер указан: {referenceImage.specifySize}");
                        // Debug.Log($"  - Текстура: {(referenceImage.texture != null ? referenceImage.texture.name : "null")}");
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"[DynamicReferenceLibrary] Не удалось получить информацию о маркере #{i + 1}: {e.Message}");
                    }
                }
            }
            else
            {
                Debug.LogWarning("[DynamicReferenceLibrary] Библиотека пуста!");
            }
        }

        /// <summary>
        /// Пытается получить targetId по имени/Guid-ам reference image/texture.
        /// </summary>
        public bool TryGetTargetId(Guid referenceImageGuid, Guid textureGuid, string referenceName, out string targetId)
        {
            // Debug.Log($"[DynamicReferenceLibrary] Входные параметры:");
            Debug.Log($"[DynamicReferenceLibrary] TryGetTargetId: refName='{referenceName}', refGuid={referenceImageGuid}, texGuid={textureGuid}");
            // Debug.Log($"[DynamicReferenceLibrary] Всего маппингов:");
            // Debug.Log($"[DynamicReferenceLibrary]   referenceNameToTargetId: {referenceNameToTargetId.Count}");
            // Debug.Log($"[DynamicReferenceLibrary]   referenceGuidToTargetId: {referenceGuidToTargetId.Count}");
            // Debug.Log($"[DynamicReferenceLibrary]   textureGuidToTargetId: {textureGuidToTargetId.Count}");
            
            // Попытка 1: по имени
            if (!string.IsNullOrEmpty(referenceName))
            {
                // Debug.Log($"[DynamicReferenceLibrary] Попытка 1: поиск по referenceName='{referenceName}'...");
                if (referenceNameToTargetId.TryGetValue(referenceName, out targetId))
                {
                    Debug.Log($"[DynamicReferenceLibrary] ✓ Найден по referenceName: '{targetId}'");
                    return true;
                }
                else
                {
                    Debug.Log($"[DynamicReferenceLibrary] ✗ Не найден по referenceName='{referenceName}'");
                    Debug.Log($"[DynamicReferenceLibrary] Доступные имена в referenceNameToTargetId:");
                    foreach (var kvp in referenceNameToTargetId)
                    {
                        Debug.Log($"[DynamicReferenceLibrary]   '{kvp.Key}' -> '{kvp.Value}'");
                    }
                }
            }
            // else
            // {
            //     Debug.Log($"[DynamicReferenceLibrary] referenceName пуст, пропускаем поиск по имени");
            // }

            // Попытка 2: по referenceImageGuid
            // Debug.Log($"[DynamicReferenceLibrary] Попытка 2: поиск по referenceImageGuid={referenceImageGuid}...");
            if (referenceGuidToTargetId.TryGetValue(referenceImageGuid, out targetId))
            {
                Debug.Log($"[DynamicReferenceLibrary] ✓ Найден по referenceImageGuid: '{targetId}'");
                return true;
            }
            else
            {
                Debug.Log($"[DynamicReferenceLibrary] ✗ Не найден по referenceImageGuid={referenceImageGuid}");
                Debug.Log($"[DynamicReferenceLibrary] Доступные GUID в referenceGuidToTargetId:");
                foreach (var kvp in referenceGuidToTargetId)
                {
                    Debug.Log($"[DynamicReferenceLibrary]   {kvp.Key} -> '{kvp.Value}'");
                }
            }

            // Попытка 3: по textureGuid
            // Debug.Log($"[DynamicReferenceLibrary] Попытка 3: поиск по textureGuid={textureGuid}...");
            if (textureGuidToTargetId.TryGetValue(textureGuid, out targetId))
            {
                Debug.Log($"[DynamicReferenceLibrary] ✓ Найден по textureGuid: '{targetId}'");
                return true;
            }
            else
            {
                Debug.Log($"[DynamicReferenceLibrary] ✗ Не найден по textureGuid={textureGuid}");
                Debug.Log($"[DynamicReferenceLibrary] Доступные texture GUID в textureGuidToTargetId:");
                foreach (var kvp in textureGuidToTargetId)
                {
                    Debug.Log($"[DynamicReferenceLibrary]   {kvp.Key} -> '{kvp.Value}'");
                }
            }

            Debug.LogError($"[DynamicReferenceLibrary] ✗ НЕ НАЙДЕН ни по одному ключу!");
            targetId = null;
            return false;
        }

        /// <summary>
        /// Выводит все доступные маппинги для отладки.
        /// </summary>
        public void LogAllMappings()
        {
            Debug.Log($"[DynamicReferenceLibrary] ВСЕ МАППИНГИ: referenceNameToTargetId={referenceNameToTargetId.Count}, referenceGuidToTargetId={referenceGuidToTargetId.Count}, textureGuidToTargetId={textureGuidToTargetId.Count}");
            foreach (var kvp in referenceNameToTargetId)
            {
                Debug.Log($"[DynamicReferenceLibrary]   name='{kvp.Key}' -> targetId='{kvp.Value}'");
            }
            foreach (var kvp in referenceGuidToTargetId)
            {
                Debug.Log($"[DynamicReferenceLibrary]   guid={kvp.Key} -> targetId='{kvp.Value}'");
            }
            foreach (var kvp in textureGuidToTargetId)
            {
                Debug.Log($"[DynamicReferenceLibrary]   textureGuid={kvp.Key} -> targetId='{kvp.Value}'");
            }
        }
    }
}


using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using ARArtifact.Services;

namespace ARArtifact.UI
{
    /// <summary>
    /// Контроллер для отображения прогресса загрузки моделей
    /// </summary>
    public class DownloadProgressController
    {
        private const string LogPrefix = "[DownloadProgressController]";

        private VisualElement container;
        private ModelLoaderService modelLoader;
        private ArtifactMediaService mediaService;
        private MonoBehaviour coroutineHost;
        private readonly Dictionary<string, DownloadProgressItem> activeDownloads = new();
        private Coroutine updateCoroutine;

        /// <summary>
        /// Элемент прогресса загрузки
        /// </summary>
        private class DownloadProgressItem
        {
            public string ArtifactId;
            public string DisplayName;
            public VisualElement ItemElement;
            public Label TitleLabel;
            public VisualElement ProgressContainer; // Внешний контейнер (фон)
            public VisualElement ProgressBar; // Внутренняя полоска прогресса
            public float LastProgress;
            public bool IsCompleted;
        }

        public DownloadProgressController(VisualElement container, MonoBehaviour coroutineHost = null)
        {
            this.container = container;
            this.coroutineHost = coroutineHost;
            modelLoader = ModelLoaderService.Instance;
            mediaService = ArtifactMediaService.Instance;
            
            if (container == null)
            {
                Debug.LogError($"{LogPrefix} Container is null при инициализации!");
            }
            
            // Если coroutineHost не передан, пытаемся найти через userData
            if (this.coroutineHost == null && container?.panel != null)
            {
                var root = container.panel.visualTree;
                while (root != null && this.coroutineHost == null)
                {
                    if (root.userData is MonoBehaviour mb)
                    {
                        this.coroutineHost = mb;
                        break;
                    }
                    root = root.parent;
                }
            }
            
            Debug.Log($"{LogPrefix} Инициализирован: container={container != null}, coroutineHost={coroutineHost != null}");
        }

        /// <summary>
        /// Начинает отслеживание загрузки модели
        /// </summary>
        public void StartTracking(string artifactId, string displayName = null)
        {
            if (string.IsNullOrEmpty(artifactId))
            {
                return;
            }

            // Если уже отслеживаем, обновляем имя
            if (activeDownloads.TryGetValue(artifactId, out var existing))
            {
                if (!string.IsNullOrEmpty(displayName) && existing.DisplayName != displayName)
                {
                    existing.DisplayName = displayName;
                    if (existing.TitleLabel != null)
                    {
                        existing.TitleLabel.text = GetDisplayText(displayName);
                    }
                }
                return;
            }

            // Создаем новый элемент прогресса
            var item = new DownloadProgressItem
            {
                ArtifactId = artifactId,
                DisplayName = displayName ?? artifactId,
                LastProgress = 0f,
                IsCompleted = false
            };

            // Создаем UI элементы
            item.ItemElement = new VisualElement();
            item.ItemElement.AddToClassList("download-item");

            item.TitleLabel = new Label(GetDisplayText(item.DisplayName));
            item.TitleLabel.AddToClassList("download-title");

            // Создаем контейнер прогресса (фон)
            item.ProgressContainer = new VisualElement();
            item.ProgressContainer.AddToClassList("download-progress-track");
            item.ProgressContainer.style.width = new StyleLength(new Length(100f, LengthUnit.Percent));
            item.ProgressContainer.style.height = new StyleLength(8f);
            
            // Создаем полоску прогресса (заполненная часть)
            item.ProgressBar = new VisualElement();
            item.ProgressBar.AddToClassList("download-progress-bar");
            item.ProgressBar.style.width = new StyleLength(new Length(0f, LengthUnit.Percent));
            item.ProgressBar.style.height = new StyleLength(new Length(100f, LengthUnit.Percent));
            
            item.ProgressContainer.Add(item.ProgressBar);

            item.ItemElement.Add(item.TitleLabel);
            item.ItemElement.Add(item.ProgressContainer);

            if (container != null)
            {
                container.Add(item.ItemElement);
                activeDownloads[artifactId] = item;
                Debug.Log($"{LogPrefix} Начато отслеживание загрузки: artifactId={artifactId}, displayName={displayName}, container={container != null}");
            }
            else
            {
                Debug.LogError($"{LogPrefix} Container is null! Не могу добавить элемент прогресса для {artifactId}");
            }

            // Запускаем корутину обновления, если еще не запущена
            if (updateCoroutine == null && coroutineHost != null)
            {
                updateCoroutine = coroutineHost.StartCoroutine(UpdateProgressCoroutine());
                Debug.Log($"{LogPrefix} Корутина обновления прогресса запущена для {artifactId}");
            }
            else if (updateCoroutine == null)
            {
                Debug.LogWarning($"{LogPrefix} Не удалось запустить корутину обновления прогресса: coroutineHost={(coroutineHost != null ? "OK" : "NULL")}");
            }
        }

        /// <summary>
        /// Останавливает отслеживание загрузки
        /// </summary>
        public void StopTracking(string artifactId, bool removeImmediately = true)
        {
            if (string.IsNullOrEmpty(artifactId))
            {
                return;
            }

            if (activeDownloads.TryGetValue(artifactId, out var item))
            {
                if (removeImmediately)
                {
                    RemoveItem(artifactId);
                }
                else
                {
                    // Помечаем как завершенную и устанавливаем прогресс на 100%
                    item.IsCompleted = true;
                    if (item.ProgressBar != null)
                    {
                        item.ProgressBar.style.width = new StyleLength(new Length(100f, LengthUnit.Percent));
                        item.ProgressBar.MarkDirtyRepaint();
                    }
                    // Корутина сама удалит элемент при следующей итерации
                }
            }
        }

        /// <summary>
        /// Удаляет элемент из списка
        /// </summary>
        private void RemoveItem(string artifactId)
        {
            if (activeDownloads.TryGetValue(artifactId, out var item))
            {
                if (item.ItemElement != null && item.ItemElement.parent != null)
                {
                    item.ItemElement.parent.Remove(item.ItemElement);
                }
                activeDownloads.Remove(artifactId);
                Debug.Log($"{LogPrefix} Удалено отслеживание: artifactId={artifactId}");
            }
        }

        /// <summary>
        /// Корутина для обновления прогресса загрузок
        /// </summary>
        private IEnumerator UpdateProgressCoroutine()
        {
            while (activeDownloads.Count > 0)
            {
                var toRemove = new List<string>();

                foreach (var kvp in activeDownloads)
                {
                    var artifactId = kvp.Key;
                    var item = kvp.Value;

                    if (modelLoader == null)
                    {
                        modelLoader = ModelLoaderService.Instance;
                    }
                    
                    if (mediaService == null)
                    {
                        mediaService = ArtifactMediaService.Instance;
                    }
                    
                    if (modelLoader == null && mediaService == null)
                    {
                        yield return new WaitForSeconds(0.5f);
                        continue;
                    }

                    float progress = 0f;
                    
                    // Сначала проверяем, идет ли скачивание из облака
                    if (mediaService != null && mediaService.IsDownloading(artifactId))
                    {
                        // Прогресс скачивания из облака: 0-50%
                        float cloudProgress = mediaService.GetDownloadProgress(artifactId);
                        progress = cloudProgress * 50f; // 0-50% для скачивания из облака
                    }
                    // Затем проверяем, идет ли загрузка модели на сцену
                    else if (modelLoader != null && modelLoader.IsLoading(artifactId))
                    {
                        // Прогресс загрузки на сцену: 50-100%
                        float modelProgress = modelLoader.GetModelProgress(artifactId);
                        progress = 50f + (modelProgress * 50f); // 50-100% для загрузки на сцену
                    }
                    else
                    {
                        // Ни скачивание, ни загрузка не идут
                        // Проверяем, завершена ли загрузка
                        if (modelLoader != null && modelLoader.TryGetLoadedModel(artifactId, out _))
                        {
                            // Модель успешно загружена - удаляем сразу
                            Debug.Log($"{LogPrefix} Загрузка завершена, удаляем прогресс-бар: artifactId={artifactId}");
                            toRemove.Add(artifactId);
                        }
                        else
                        {
                            // Модель не загружена (возможно, ошибка или еще не началась) - удаляем сразу
                            Debug.LogWarning($"{LogPrefix} Загрузка не найдена или завершена с ошибкой: artifactId={artifactId}");
                            toRemove.Add(artifactId);
                        }
                        continue;
                    }
                    
                    // Обновляем прогресс-бар при любом изменении
                    if (Mathf.Abs(progress - item.LastProgress) > 0.01f) // Обновляем при изменении больше 0.01%
                    {
                        item.LastProgress = progress;
                        if (item.ProgressBar != null)
                        {
                            item.ProgressBar.style.width = new StyleLength(new Length(progress, LengthUnit.Percent));
                            // Принудительно обновляем отображение
                            item.ProgressBar.MarkDirtyRepaint();
                        }
                    }
                }

                // Удаляем завершенные загрузки
                foreach (var artifactId in toRemove)
                {
                    RemoveItem(artifactId);
                }

                yield return new WaitForSeconds(0.05f); // Обновляем каждые 0.05 секунды для более плавного прогресса
            }

            updateCoroutine = null;
        }

        /// <summary>
        /// Получает текст для отображения
        /// </summary>
        private string GetDisplayText(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
            {
                return "Загрузка модели...";
            }

            return $"Загрузка: {displayName}";
        }

        /// <summary>
        /// Очищает все элементы загрузки
        /// </summary>
        public void ClearAll()
        {
            var artifactIds = new List<string>(activeDownloads.Keys);
            foreach (var artifactId in artifactIds)
            {
                RemoveItem(artifactId);
            }

            if (updateCoroutine != null && coroutineHost != null)
            {
                coroutineHost.StopCoroutine(updateCoroutine);
                updateCoroutine = null;
            }
        }
    }
}


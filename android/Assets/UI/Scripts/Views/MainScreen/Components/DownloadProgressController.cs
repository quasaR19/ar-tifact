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
            public ProgressBar ProgressBar;
            public float LastProgress;
            public bool IsCompleted;
        }

        public DownloadProgressController(VisualElement container, MonoBehaviour coroutineHost = null)
        {
            this.container = container;
            this.coroutineHost = coroutineHost;
            modelLoader = ModelLoaderService.Instance;
            
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

            item.ProgressBar = new ProgressBar();
            item.ProgressBar.AddToClassList("download-progress");
            item.ProgressBar.value = 0f;
            item.ProgressBar.lowValue = 0f;
            item.ProgressBar.highValue = 100f;

            item.ItemElement.Add(item.TitleLabel);
            item.ItemElement.Add(item.ProgressBar);

            container.Add(item.ItemElement);
            activeDownloads[artifactId] = item;

            Debug.Log($"{LogPrefix} Начато отслеживание загрузки: artifactId={artifactId}, displayName={displayName}");

            // Запускаем корутину обновления, если еще не запущена
            if (updateCoroutine == null && coroutineHost != null)
            {
                updateCoroutine = coroutineHost.StartCoroutine(UpdateProgressCoroutine());
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
                    // Помечаем как завершенную, но оставляем на экране на короткое время
                    item.IsCompleted = true;
                    item.ProgressBar.value = 100f;
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
                        if (modelLoader == null)
                        {
                            yield return new WaitForSeconds(0.5f);
                            continue;
                        }
                    }

                    // Проверяем, загружается ли еще модель
                    if (modelLoader.IsLoading(artifactId))
                    {
                        // Обновляем прогресс
                        float progress = modelLoader.GetModelProgress(artifactId) * 100f;
                        if (Mathf.Abs(progress - item.LastProgress) > 0.1f) // Обновляем только при заметном изменении
                        {
                            item.LastProgress = progress;
                            if (item.ProgressBar != null)
                            {
                                item.ProgressBar.value = progress;
                            }
                        }
                    }
                    else
                    {
                        // Модель больше не загружается
                        if (modelLoader.TryGetLoadedModel(artifactId, out _))
                        {
                            // Модель уже загружена (возможно, из кэша)
                            // Если она была загружена из кэша и отслеживание началось по ошибке, удаляем сразу
                            // Если она только что завершила загрузку, показываем 100% и удаляем через 1 секунду
                            if (!item.IsCompleted)
                            {
                                item.IsCompleted = true;
                                item.ProgressBar.value = 100f;
                                
                                // Проверяем, была ли это загрузка из кэша (прогресс был 0 и модель уже загружена)
                                // В этом случае удаляем сразу, без задержки
                                if (item.LastProgress == 0f)
                                {
                                    Debug.Log($"{LogPrefix} Модель уже загружена из кэша, удаляем прогресс-бар: artifactId={artifactId}");
                                    toRemove.Add(artifactId);
                                }
                                else
                                {
                                    Debug.Log($"{LogPrefix} Загрузка завершена: artifactId={artifactId}");
                                    // Удаляем через 1 секунду после завершения
                                    yield return new WaitForSeconds(1f);
                                    toRemove.Add(artifactId);
                                }
                            }
                        }
                        else
                        {
                            // Модель не загружена (возможно, ошибка) - удаляем сразу
                            Debug.LogWarning($"{LogPrefix} Загрузка не найдена или завершена с ошибкой: artifactId={artifactId}");
                            toRemove.Add(artifactId);
                        }
                    }
                }

                // Удаляем завершенные загрузки
                foreach (var artifactId in toRemove)
                {
                    RemoveItem(artifactId);
                }

                yield return new WaitForSeconds(0.1f); // Обновляем каждые 0.1 секунды
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


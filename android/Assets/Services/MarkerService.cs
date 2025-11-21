using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ARArtifact.Services
{
    /// <summary>
    /// Сервис для управления маркерами (загрузка, хранение, обновление)
    /// </summary>
    public class MarkerService : MonoBehaviour
    {
        private static MarkerService _instance;
        public static MarkerService Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("MarkerService");
                    _instance = go.AddComponent<MarkerService>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }
        
        private Storage.MarkerStorage storage;
        private Config.SupabaseConfig config;
        private bool isUpdating = false;
        
        public event Action<List<Storage.MarkerStorage.MarkerData>> OnMarkersUpdated;
        public event Action OnUpdateStarted;
        public event Action OnUpdateCompleted;
        
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            storage = new Storage.MarkerStorage();
            config = Resources.Load<Config.SupabaseConfig>("SupabaseConfig");
        }
        
        private void OnDestroy()
        {
            // Очищаем ссылку на instance при уничтожении
            // НЕ создаем новые объекты здесь, чтобы избежать предупреждений Unity
            if (_instance == this)
            {
                _instance = null;
            }
            
            // Останавливаем все корутины
            StopAllCoroutines();
        }
        
        private void OnApplicationQuit()
        {
            // Очищаем при выходе из приложения
            if (_instance == this)
            {
                _instance = null;
            }
        }
        
        private void Start()
        {
            InitializeMarkers();
        }
        
        /// <summary>
        /// Инициализация маркеров при запуске приложения
        /// </summary>
        private void InitializeMarkers()
        {
            if (!storage.HasMarkers())
            {
                // Если маркеров нет, загружаем их (блокирующая операция)
                Debug.Log("[MarkerService] Маркеры не найдены. Загружаем из Supabase...");
                LoadMarkersFromSupabase(false);
            }
            else
            {
                // Если маркеры есть, запускаем фоновое обновление
                Debug.Log("[MarkerService] Маркеры найдены. Запускаем фоновое обновление...");
                StartCoroutine(AutoUpdateCoroutine());
            }
        }
        
        /// <summary>
        /// Загружает маркеры из Supabase
        /// </summary>
        /// <param name="background">Если true, обновление происходит в фоне</param>
        public void LoadMarkersFromSupabase(bool background = true)
        {
            if (isUpdating)
            {
                Debug.LogWarning("[MarkerService] Обновление уже выполняется");
                return;
            }
            
            isUpdating = true;
            OnUpdateStarted?.Invoke();
            
            if (SupabaseService.Instance == null)
            {
                Debug.LogError("[MarkerService] SupabaseService не инициализирован");
                isUpdating = false;
                OnUpdateCompleted?.Invoke();
                return;
            }
            
            SupabaseService.Instance.LoadTargets(
                onSuccess: (targets) =>
                {
                    if (targets != null && targets.Count > 0)
                    {
                        List<Storage.MarkerStorage.MarkerData> markers = new List<Storage.MarkerStorage.MarkerData>();
                        
                        // Загружаем существующие маркеры для проверки локальных путей
                        var existingMarkers = storage.GetMarkers();
                        var existingMarkersDict = new System.Collections.Generic.Dictionary<string, Storage.MarkerStorage.MarkerData>();
                        foreach (var existing in existingMarkers)
                        {
                            existingMarkersDict[existing.id] = existing;
                        }
                        
                        foreach (var target in targets)
                        {
                            var marker = new Storage.MarkerStorage.MarkerData
                            {
                                id = target.id,
                                url = target.url,
                                createdAt = target.created_at
                            };
                            
                            // Проверяем, есть ли уже локальное изображение для этого маркера
                            if (existingMarkersDict.ContainsKey(target.id))
                            {
                                var existing = existingMarkersDict[target.id];
                                // Если URL не изменился и локальный файл существует, используем старый путь
                                if (existing.url == target.url && storage.HasLocalImage(existing.localImagePath))
                                {
                                    marker.localImagePath = existing.localImagePath;
                                }
                                // Если URL изменился, нужно загрузить новое изображение
                                else if (existing.url != target.url)
                                {
                                    // Удаляем старое изображение
                                    if (!string.IsNullOrEmpty(existing.localImagePath))
                                    {
                                        storage.DeleteLocalImage(existing.localImagePath);
                                    }
                                    marker.localImagePath = null; // Будет загружено ниже
                                }
                            }
                            
                            markers.Add(marker);
                        }
                        
                        // Сохраняем маркеры (пока без путей к изображениям)
                        storage.SaveMarkers(markers);
                        
                        // Загружаем изображения
                        StartCoroutine(DownloadMarkerImagesCoroutine(markers, background));
                    }
                    else
                    {
                        Debug.LogWarning("[MarkerService] Маркеры не найдены в базе данных");
                        isUpdating = false;
                        OnUpdateCompleted?.Invoke();
                    }
                },
                onError: (error) =>
                {
                    Debug.LogError($"[MarkerService] Ошибка загрузки маркеров: {error}");
                    isUpdating = false;
                    OnUpdateCompleted?.Invoke();
                }
            );
        }
        
        /// <summary>
        /// Корутина для загрузки изображений маркеров
        /// </summary>
        private IEnumerator DownloadMarkerImagesCoroutine(List<Storage.MarkerStorage.MarkerData> markers, bool background)
        {
            if (MarkerImageService.Instance == null)
            {
                Debug.LogWarning("[MarkerService] MarkerImageService не инициализирован, изображения не будут загружены");
                isUpdating = false;
                OnUpdateCompleted?.Invoke();
                if (!background)
                {
                    StartCoroutine(AutoUpdateCoroutine());
                }
                yield break;
            }
            
            int totalToDownload = 0;
            foreach (var marker in markers)
            {
                if (string.IsNullOrEmpty(marker.localImagePath) && !string.IsNullOrEmpty(marker.url))
                {
                    totalToDownload++;
                }
            }
            
            if (totalToDownload == 0)
            {
                // Все изображения уже загружены
                OnMarkersUpdated?.Invoke(markers);
                Debug.Log($"[MarkerService] Загружено маркеров: {markers.Count} (изображения уже загружены)");
                isUpdating = false;
                OnUpdateCompleted?.Invoke();
                if (!background)
                {
                    StartCoroutine(AutoUpdateCoroutine());
                }
                yield break;
            }
            
            Debug.Log($"[MarkerService] Начинаем загрузку {totalToDownload} изображений...");
            
            bool downloadComplete = false;
            
            MarkerImageService.Instance.DownloadMarkerImages(
                markers,
                onProgress: (current, total) =>
                {
                    // Можно добавить прогресс-бар, если нужно
                },
                onComplete: () =>
                {
                    downloadComplete = true;
                }
            );
            
            // Ждем завершения загрузки
            while (!downloadComplete)
            {
                yield return null;
            }
            
            // Сохраняем маркеры с обновленными путями к изображениям
            storage.SaveMarkers(markers);
            OnMarkersUpdated?.Invoke(markers);
            Debug.Log($"[MarkerService] Загружено маркеров: {markers.Count} (изображения: {totalToDownload} запрошено)");
            
            isUpdating = false;
            OnUpdateCompleted?.Invoke();
            
            // Если это не фоновое обновление, запускаем автообновление
            if (!background)
            {
                StartCoroutine(AutoUpdateCoroutine());
            }
        }
        
        /// <summary>
        /// Корутина для автоматического обновления маркеров
        /// </summary>
        private IEnumerator AutoUpdateCoroutine()
        {
            if (config == null)
            {
                Debug.LogWarning("[MarkerService] Конфиг не загружен, автообновление отключено");
                yield break;
            }
            
            int interval = config.autoUpdateIntervalSeconds;
            
            while (true)
            {
                yield return new WaitForSeconds(interval);
                
                Debug.Log("[MarkerService] Автоматическое обновление маркеров...");
                LoadMarkersFromSupabase(true);
            }
        }
        
        /// <summary>
        /// Получает список всех маркеров из локального хранилища
        /// </summary>
        public List<Storage.MarkerStorage.MarkerData> GetMarkers()
        {
            return storage.GetMarkers();
        }
        
        /// <summary>
        /// Получает дату последнего обновления
        /// </summary>
        public DateTime GetLastUpdateTime()
        {
            return storage.GetLastUpdateTime();
        }
        
        /// <summary>
        /// Проверяет, выполняется ли обновление
        /// </summary>
        public bool IsUpdating()
        {
            return isUpdating;
        }
    }
}


using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ARArtifact.Services
{
    /// <summary>
    /// Сервис для работы с Supabase API
    /// </summary>
    public class SupabaseService : MonoBehaviour
    {
        private static SupabaseService _instance;
        public static SupabaseService Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("SupabaseService");
                    _instance = go.AddComponent<SupabaseService>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }
        
        private Config.SupabaseConfig config;
        
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            LoadConfig();
        }
        
        private void OnDestroy()
        {
            // Очищаем ссылку на instance при уничтожении
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
        
        private void LoadConfig()
        {
            // Загружаем конфиг из Resources
            config = Resources.Load<Config.SupabaseConfig>("SupabaseConfig");
            if (config == null)
            {
                Debug.LogError("[SupabaseService] Конфиг не найден! Создайте SupabaseConfig в папке Resources.");
            }
            else if (!config.IsValid())
            {
                Debug.LogWarning("[SupabaseService] Конфиг не заполнен! Заполните URL и Anon Key.");
            }
        }
        
        /// <summary>
        /// Загружает все маркеры (targets) из Supabase
        /// </summary>
        public void LoadTargets(Action<List<TargetData>> onSuccess, Action<string> onError)
        {
            if (config == null || !config.IsValid())
            {
                onError?.Invoke("Конфигурация Supabase не настроена");
                return;
            }
            
            StartCoroutine(LoadTargetsCoroutine(onSuccess, onError));
        }
        
        private IEnumerator LoadTargetsCoroutine(Action<List<TargetData>> onSuccess, Action<string> onError)
        {
            string url = $"{config.supabaseUrl}/rest/v1/targets?select=*";
            
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("apikey", config.supabaseAnonKey);
                request.SetRequestHeader("Authorization", $"Bearer {config.supabaseAnonKey}");
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Prefer", "return=representation");
                
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        string jsonResponse = request.downloadHandler.text;
                        // Обертка для десериализации массива
                        string wrappedJson = "{\"items\":" + jsonResponse + "}";
                        TargetListWrapper wrapper = JsonUtility.FromJson<TargetListWrapper>(wrappedJson);
                        onSuccess?.Invoke(wrapper?.items ?? new List<TargetData>());
                    }
                    catch (Exception e)
                    {
                        onError?.Invoke($"Ошибка парсинга ответа: {e.Message}");
                    }
                }
                else
                {
                    onError?.Invoke($"Ошибка загрузки: {request.error} (HTTP {request.responseCode})");
                }
            }
        }
        
        /// <summary>
        /// Данные маркера (target)
        /// </summary>
        [Serializable]
        public class TargetData
        {
            public string id;
            public string url;
            public string created_at;
        }
        
        /// <summary>
        /// Обертка для десериализации списка через JsonUtility
        /// </summary>
        [Serializable]
        private class TargetListWrapper
        {
            public List<TargetData> items;
        }
    }
}


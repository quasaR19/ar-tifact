using UnityEngine;

namespace ARArtifact.Config
{
    /// <summary>
    /// Конфигурация для подключения к Supabase
    /// </summary>
    [CreateAssetMenu(fileName = "SupabaseConfig", menuName = "AR Artifact/Config/Supabase Config")]
    public class SupabaseConfig : ScriptableObject
    {
        [Header("Supabase Connection")]
        [Tooltip("URL вашего Supabase проекта (например: https://xxxxx.supabase.co)")]
        public string supabaseUrl = "";
        
        [Tooltip("Anon ключ для публичного доступа")]
        public string supabaseAnonKey = "";
        
        [Header("Auto Update Settings")]
        [Tooltip("Интервал автоматического обновления маркеров в секундах (по умолчанию 1 час)")]
        public int autoUpdateIntervalSeconds = 3600; // 1 час
        
        /// <summary>
        /// Проверяет, что конфигурация заполнена
        /// </summary>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(supabaseUrl) && !string.IsNullOrEmpty(supabaseAnonKey);
        }
    }
}


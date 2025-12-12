using System;

/// <summary>
/// Метаданные видео, извлеченные из БД.
/// Соответствует структуре metadata JSONB в таблице media.
/// </summary>
[Serializable]
public class VideoMetadata
{
    public int width;
    public int height;
    public float duration;
    public string filename;
    public long size;
    
    /// <summary>
    /// Проверяет, что метаданные содержат валидные размеры.
    /// </summary>
    public bool IsValid()
    {
        return width > 0 && height > 0 && duration > 0;
    }
    
    /// <summary>
    /// Вычисляет соотношение сторон видео.
    /// </summary>
    public float GetAspectRatio()
    {
        if (height <= 0) return 16f / 9f; // Значение по умолчанию
        return (float)width / (float)height;
    }
}

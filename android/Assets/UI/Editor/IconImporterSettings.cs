using UnityEngine;
using UnityEditor;
using UnityEditor.AssetImporters;

namespace ARArtifact.UI.Editor
{
    /// <summary>
    /// Настройки импорта для PNG иконок в Resources/UI/Icons
    /// Также обеспечивает правильную настройку SVG файлов
    /// </summary>
    public class IconImporterSettings : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            // Проверяем, что файл находится в Resources/UI/Icons
            if (!assetPath.Contains("Resources/UI/Icons")) return;
            if (!assetPath.EndsWith(".png")) return;
            
            TextureImporter textureImporter = (TextureImporter)assetImporter;
            
            // Настройки для UI иконок
            textureImporter.textureType = TextureImporterType.Sprite;
            textureImporter.spriteImportMode = SpriteImportMode.Single;
            textureImporter.alphaIsTransparency = true;
            textureImporter.mipmapEnabled = false; // Отключаем mipmaps для UI иконок
            textureImporter.filterMode = FilterMode.Bilinear;
            textureImporter.maxTextureSize = 256; // Для иконок достаточно 256px
            
            // Настройки для Android
            TextureImporterPlatformSettings androidSettings = textureImporter.GetPlatformTextureSettings("Android");
            androidSettings.maxTextureSize = 256;
            androidSettings.format = TextureImporterFormat.RGBA32; // Без сжатия для четкости
            androidSettings.compressionQuality = 100;
            textureImporter.SetPlatformTextureSettings(androidSettings);
            
            // Настройки для Default
            TextureImporterPlatformSettings defaultSettings = textureImporter.GetPlatformTextureSettings("DefaultTexturePlatform");
            defaultSettings.maxTextureSize = 256;
            defaultSettings.format = TextureImporterFormat.RGBA32;
            defaultSettings.compressionQuality = 100;
            textureImporter.SetPlatformTextureSettings(defaultSettings);
            
            Debug.Log($"[IconImporter] Настроены параметры импорта для: {assetPath}");
        }
    }
}


# Android приложение - детальное описание

## Обзор

Android приложение разработано на Unity с использованием ARFoundation для распознавания таргетов и отображения 3D моделей или видео в AR.

## Архитектура Unity проекта

### Структура папок

```
android/Assets/
├── Scripts/              # Основные скрипты
│   ├── TrackedArtifactManager.cs
│   ├── TrackedModelHost.cs
│   ├── TrackedVideoHost.cs
│   ├── ARVideoPlayer.cs
│   └── Simulation/       # Симуляция для редактора
├── Services/            # Сервисы приложения
│   ├── ARManager.cs
│   ├── ArtifactService.cs
│   ├── SupabaseService.cs
│   ├── ArtifactMediaService.cs
│   ├── ModelLoaderService.cs
│   ├── ModelSceneManager.cs
│   ├── VideoSceneManager.cs
│   ├── MarkerService.cs
│   └── DynamicReferenceLibrary.cs
├── Storage/             # Локальное хранилище
│   └── ArtifactStorage.cs
├── Config/              # Конфигурация
│   └── SupabaseConfig.cs
├── UI/                  # Пользовательский интерфейс
└── Scenes/              # Сцены Unity
```

## Основные сервисы и их ответственность

### ARManager

**Расположение:** `Assets/Services/ARManager.cs`

**Ответственность:**
- Инициализация AR сессии через ARFoundation
- Проверка доступности AR на устройстве
- Управление состоянием AR (запуск/остановка)
- Инициализация библиотеки таргетов

**Ключевые методы:**
- `InitializeAR(Action<bool> onComplete)` - инициализирует AR сессию
- `StopAR()` - останавливает AR сессию
- `EnableCamera()` / `DisableCamera()` - управление камерой

**Особенности:**
- Singleton паттерн
- Поддержка симуляции в Unity Editor
- Автоматическая проверка и установка AR сервисов

### ArtifactService

**Расположение:** `Assets/Services/ArtifactService.cs`

**Ответственность:**
- Центральный сервис для работы с артефактами
- Запрос артефактов из Supabase
- Кеширование артефактов локально
- Управление историей сканирования
- Координация загрузки медиа

**Ключевые методы:**
- `RequestArtifactForTarget(string targetId, Action<ArtifactAvailabilityResult> onSuccess, Action<string> onError)` - запрашивает артефакт для таргета
- `GetHistoryItems()` - возвращает историю сканирования
- `UpsertArtifactRecord(ArtifactRecord record)` - сохраняет/обновляет артефакт в кеше
- `AppendHistoryEntry(string artifactId, string targetId, ArtifactHistoryStatus status, string statusDetails)` - добавляет запись в историю

**Особенности:**
- Singleton паттерн
- Кеширование артефактов для быстрого доступа
- Отложенное сохранение истории (батчинг)
- Поддержка множественных колбэков для одного запроса

### TrackedArtifactManager

**Расположение:** `Assets/Scripts/TrackedArtifactManager.cs`

**Ответственность:**
- Отслеживание распознанных таргетов через ARTrackedImageManager
- Запрос артефактов для распознанных таргетов
- Координация размещения медиа на AR сцене
- Управление состоянием трекинга

**Ключевые методы:**
- Обработка событий `trackablesChanged` от ARTrackedImageManager
- `TogglePinForTarget(string targetId)` - закрепляет/открепляет таргет
- `IsTargetPinned(string targetId)` - проверяет состояние закрепления

**Особенности:**
- Кеширование хостов для оптимизации
- Предотвращение дублирования запросов
- Поддержка симуляции в Unity Editor

### ModelSceneManager

**Расположение:** `Assets/Services/ModelSceneManager.cs`

**Ответственность:**
- Управление размещением 3D моделей на AR сцене
- Координация работы с ModelLoaderService
- Клонирование моделей для размещения в хостах
- Управление жизненным циклом моделей на сцене

**Ключевые методы:**
- `RequestModelForHost(string artifactId, TrackedModelHost host, string localPath, string metadataJson, Action onSuccess, Action<string> onError, string remoteUrl)` - запрашивает модель для размещения
- `RemoveModelFromHost(string artifactId, TrackedModelHost host)` - удаляет модель из хоста
- `IsModelInScene(string artifactId)` - проверяет наличие модели на сцене

**Особенности:**
- Асинхронное клонирование моделей для распределения нагрузки
- Валидация операций размещения для предотвращения race conditions
- Управление ссылками на модели в ModelLoaderService

### VideoSceneManager

**Расположение:** `Assets/Services/VideoSceneManager.cs`

**Ответственность:**
- Управление размещением видео на AR сцене
- Поддержка локальных видео и YouTube
- Автовосстановление битых видео файлов
- Управление жизненным циклом видео на сцене

**Ключевые методы:**
- `RequestVideoForHost(string artifactId, TrackedVideoHost host, string videoPath, string videoUrl, bool isYouTube, Action onSuccess, Action<string> onError, string remoteUrl, string mediaId, string metadataJson)` - запрашивает видео для размещения
- `RemoveVideoFromHost(string artifactId, TrackedVideoHost host)` - удаляет видео из хоста
- `AutoRecoverVideo(PlacementOperation operation, string originalError)` - автоматически восстанавливает битое видео

**Особенности:**
- Автовосстановление битых видео файлов (до 3 попыток)
- Проверка целостности файлов перед использованием
- Поддержка метаданных видео для правильного отображения

### ArtifactStorage

**Расположение:** `Assets/Storage/ArtifactStorage.cs`

**Ответственность:**
- Локальное хранилище для кеширования артефактов
- Сохранение истории сканирования
- Управление путями для медиа файлов
- Удаление файлов из кеша

**Ключевые методы:**
- `LoadData()` - загружает данные из JSON файла
- `SaveData(ArtifactStorageData data)` - сохраняет данные в JSON файл
- `GetMediaFilePath(string artifactId, string mediaId, string remoteUrl)` - возвращает путь для сохранения медиа
- `GetPreviewFilePath(string artifactId, string remoteUrl)` - возвращает путь для сохранения превью
- `DeleteFileIfExists(string localPath)` - удаляет файл если существует

**Структура данных:**
- `ArtifactStorageData` - контейнер для всех данных
  - `artifacts` - список артефактов с метаданными
  - `history` - история сканирования

**Расположение файлов:**
- JSON файл: `Application.persistentDataPath/artifact_history.json`
- Медиа файлы: `Application.persistentDataPath/artifact_media/`
- Превью: `Application.persistentDataPath/artifact_previews/`

### ArtifactMediaService

**Расположение:** `Assets/Services/ArtifactMediaService.cs`

**Ответственность:**
- Загрузка медиа файлов из Vercel Blob
- Сохранение файлов в локальное хранилище
- Управление процессами загрузки

**Ключевые методы:**
- `DownloadModel(string artifactId, string mediaId, string remoteUrl, Action<string> onSuccess, Action<string> onError)` - загружает 3D модель
- `DownloadVideo(string artifactId, string mediaId, string remoteUrl, Action<string> onSuccess, Action<string> onError)` - загружает видео
- `DownloadPreview(string artifactId, string previewUrl, Action<string> onSuccess, Action<string> onError)` - загружает превью
- `IsDownloading(string artifactId)` - проверяет, загружается ли файл

### ModelLoaderService

**Расположение:** `Assets/Services/ModelLoaderService.cs`

**Ответственность:**
- Загрузка GLB/GLTF файлов
- Парсинг и создание GameObject из моделей
- Кеширование загруженных моделей
- Управление ссылками на модели

**Ключевые методы:**
- `RequestModelLoad(string artifactId, string localPath, string metadataJson, Action<GameObject> onSuccess, Action<string> onError, string remoteUrl)` - загружает модель
- `TryGetLoadedModel(string artifactId, out GameObject model)` - получает загруженную модель из кеша
- `IsLoading(string artifactId)` - проверяет, загружается ли модель
- `ReleaseModelReference(string artifactId)` - освобождает ссылку на модель

## AR система (ARFoundation)

### Компоненты ARFoundation

- **ARSession** - управляет AR сессией
- **ARTrackedImageManager** - отслеживает изображения (таргеты)
- **ARCameraManager** - управляет камерой для AR

### Библиотека таргетов

**DynamicReferenceLibrary** - динамически создает библиотеку таргетов из данных Supabase:
- Загружает таргеты через SupabaseService
- Создает XRReferenceImageLibrary для ARTrackedImageManager
- Сопоставляет GUID таргетов с targetId из базы данных

### Симуляция в Unity Editor

**SimulationMarkerRegistry** - регистр таргетов для симуляции в редакторе:
- Позволяет тестировать распознавание таргетов без реального устройства
- Сопоставляет TrackableId с targetId

## Загрузка и кеширование медиа

### Процесс загрузки 3D модели

1. ArtifactService запрашивает артефакт из Supabase
2. Проверяется наличие модели в локальном кеше
3. Если модель отсутствует, ArtifactMediaService загружает из Vercel Blob
4. Файл сохраняется в `Application.persistentDataPath/artifact_media/`
5. ModelLoaderService загружает GLB файл и создает GameObject
6. Модель кешируется в ModelLoaderService
7. ModelSceneManager клонирует модель и размещает в TrackedModelHost

### Процесс загрузки видео

1. ArtifactService запрашивает артефакт из Supabase
2. Определяется тип видео (локальное или YouTube)
3. Для локального видео проверяется кеш, при отсутствии загружается из Vercel Blob
4. Файл сохраняется в `Application.persistentDataPath/artifact_media/`
5. VideoSceneManager создает ARVideoPlayer и размещает в TrackedVideoHost
6. Видео воспроизводится на AR сцене

### Кеширование

- **Артефакты** - кешируются в ArtifactStorage (JSON файл)
- **Медиа файлы** - кешируются в файловой системе
- **3D модели** - кешируются в ModelLoaderService (GameObject в памяти)
- **История сканирования** - кешируется в ArtifactStorage

## Локальное хранилище

### Структура данных

```csharp
public class ArtifactStorageData
{
    public List<ArtifactRecord> artifacts;
    public List<ArtifactHistoryEntry> history;
}

public class ArtifactRecord
{
    public string artifactId;
    public string targetId;
    public string name;
    public string description;
    public string previewImageUrl;
    public string previewLocalPath;
    public bool isActive;
    public long lastUpdatedTicks;
    public List<MediaCacheRecord> media;
}
```

### Оптимизация производительности

- **Отложенное сохранение истории** - батчинг изменений (задержка 2 секунды)
- **Принудительное сохранение** - при паузе приложения, потере фокуса, уничтожении
- **Ограничение истории** - максимум 1000 записей

## Обработка ошибок

### Автовосстановление битых видео

VideoSceneManager автоматически восстанавливает битые видео файлы:
1. Обнаружение битого файла (0 bytes, ошибка загрузки)
2. Освобождение файла из памяти
3. Удаление битого файла
4. Повторная загрузка из Vercel Blob
5. Проверка целостности нового файла
6. До 3 попыток восстановления

### Обработка ошибок загрузки

- Все сервисы используют колбэки для обработки ошибок
- Ошибки логируются в консоль и MainScreen
- Пользователю показываются понятные сообщения об ошибках

## Производительность

### Оптимизации

- **Кеширование хостов** - TrackedArtifactManager кеширует TrackedModelHost
- **Кеширование размеров таргетов** - избежание повторных вычислений
- **Асинхронное клонирование** - распределение нагрузки на несколько кадров
- **Предотвращение дублирования запросов** - активные запросы отслеживаются
- **Отложенное сохранение** - батчинг операций записи

### Управление памятью

- Модели клонируются для размещения в хостах (оригинал остается в ModelLoaderService)
- Освобождение ссылок на модели при удалении из сцены
- Автоматическая очистка неактивных моделей


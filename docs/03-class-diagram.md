# Диаграмма классов

## Описание

Диаграмма классов показывает структуру основных классов системы, их атрибуты, методы и отношения между ними.

## Диаграмма классов Android приложения

```mermaid
classDiagram
    class ARManager {
        -ARSession arSession
        -ARTrackedImageManager trackedImageManager
        +bool IsARAvailable
        +bool IsARInitializing
        +InitializeAR(Action~bool~)
        +StopAR()
        +EnableCamera()
        +DisableCamera()
    }
    
    class TrackedArtifactManager {
        -ARTrackedImageManager trackedImageManager
        -Dictionary~TrackableId, TrackedArtifactInstance~ trackedInstances
        -ArtifactService artifactService
        -ModelSceneManager modelSceneManager
        -VideoSceneManager videoSceneManager
        +OnTargetRecognized(string)
        +OnTargetLost(string)
        +TogglePinForTarget(string) bool
    }
    
    class ArtifactService {
        -SupabaseConfig config
        -ArtifactStorage storage
        -ArtifactMediaService mediaService
        -List~ArtifactHistoryItem~ historyCache
        +RequestArtifactForTarget(string, Action, Action)
        +GetHistoryItems() IReadOnlyList
        +UpsertArtifactRecord(ArtifactRecord)
        +AppendHistoryEntry(string, string, ArtifactHistoryStatus, string)
        +ClearHistoryAndCache()
    }
    
    class ModelSceneManager {
        -ModelLoaderService modelLoader
        -Dictionary~string, SceneModelInstance~ sceneModels
        +RequestModelForHost(string, TrackedModelHost, string, string, Action, Action, string)
        +RemoveModelFromHost(string, TrackedModelHost)
        +IsModelInScene(string) bool
    }
    
    class VideoSceneManager {
        -Dictionary~string, SceneVideoInstance~ sceneVideos
        +RequestVideoForHost(string, TrackedVideoHost, string, string, bool, Action, Action, string, string, string)
        +RemoveVideoFromHost(string, TrackedVideoHost)
    }
    
    class TrackedModelHost {
        -GameObject loadedModel
        -GameObject placeholderModel
        -float targetSize
        -bool isPinned
        +string CurrentArtifactId
        +bool HasLoadedModel
        +AttachLoadedModel(GameObject, string, string)
        +ResetToPlaceholder()
        +SetTrackingActive(bool)
        +TogglePinned() bool
    }
    
    class TrackedVideoHost {
        -GameObject loadedVideo
        -ARVideoPlayer videoPlayer
        -float targetSize
        -bool isPinned
        +string CurrentArtifactId
        +bool HasLoadedVideo
        +AttachVideo(GameObject, string, string, bool, VideoMetadata, Action~string~)
        +ClearLoadedVideo()
        +SetTrackingActive(bool)
    }
    
    class ArtifactStorage {
        -string StoragePath
        -string MediaFolderPath
        -string PreviewFolderPath
        +LoadData() ArtifactStorageData
        +SaveData(ArtifactStorageData)
        +GetMediaFilePath(string, string, string) string
        +GetPreviewFilePath(string, string) string
        +DeleteFileIfExists(string)
    }
    
    class SupabaseService {
        -SupabaseConfig config
        +LoadTargets(Action~List~TargetData~~, Action~string~)
    }
    
    class ArtifactMediaService {
        +DownloadModel(string, string, string, Action~string~, Action~string~)
        +DownloadVideo(string, string, string, Action~string~, Action~string~)
        +DownloadPreview(string, string, Action~string~, Action~string~)
        +IsDownloading(string) bool
    }
    
    class ModelLoaderService {
        -Dictionary~string, GameObject~ loadedModels
        +TryGetLoadedModel(string, out GameObject) bool
        +RequestModelLoad(string, string, string, Action~GameObject~, Action~string~, string)
        +IsLoading(string) bool
        +ReleaseModelReference(string)
    }
    
    ARManager --> TrackedArtifactManager : использует
    TrackedArtifactManager --> ArtifactService : использует
    TrackedArtifactManager --> ModelSceneManager : использует
    TrackedArtifactManager --> VideoSceneManager : использует
    TrackedArtifactManager --> TrackedModelHost : управляет
    TrackedArtifactManager --> TrackedVideoHost : управляет
    ArtifactService --> SupabaseService : использует
    ArtifactService --> ArtifactStorage : использует
    ArtifactService --> ArtifactMediaService : использует
    ModelSceneManager --> ModelLoaderService : использует
    ModelSceneManager --> TrackedModelHost : размещает модели
    VideoSceneManager --> TrackedVideoHost : размещает видео
    ArtifactMediaService --> ArtifactStorage : сохраняет файлы
```

## Диаграмма классов веб-сервиса

```mermaid
classDiagram
    class ArtifactEditPage {
        -string artifactId
        -string name
        -string description
        -LocalMediaItem[] localMedia
        -LocalTargetItem[] localTargets
        +handleSave()
        +handleDelete()
        +handleMediaAdd(LocalMediaItem)
        +handleTargetAdd(LocalTargetItem)
    }
    
    class MediaUploader {
        -File file
        -string type
        -string url
        +onFileSelect(File)
        +onUrlChange(string)
    }
    
    class TargetUploader {
        -File file
        -number qualityScore
        +onFileSelect(File)
        +checkImageQuality(File)
    }
    
    class MediaList {
        -LocalMediaItem[] media
        +onMediaAdd(LocalMediaItem)
        +onMediaRemove(string)
        +onMediaUpdate(string, Partial~LocalMediaItem~)
    }
    
    class TargetList {
        -LocalTargetItem[] targets
        +onTargetAdd(LocalTargetItem)
        +onTargetRemove(string)
        +onTargetUpdate(string, Partial~LocalTargetItem~)
    }
    
    class UploadRoute {
        +POST(Request) NextResponse
        -handleUpload(HandleUploadBody)
    }
    
    class DeleteBlobRoute {
        +POST(Request) NextResponse
        -deleteBlob(string)
    }
    
    class Queries {
        +createArtifact(SupabaseClient, string, string) Artifact
        +updateArtifact(SupabaseClient, string, Partial~Artifact~)
        +deleteArtifact(SupabaseClient, string)
        +addArtifactMedia(SupabaseClient, string, string, string, Record)
        +createTarget(SupabaseClient, string, string, number)
        +getArtifactById(SupabaseClient, string, boolean) ArtifactWithDetails
    }
    
    class ImageQualityChecker {
        +checkImageQualityFromUrl(string) QualityResult
        +checkImageQuality(File) QualityResult
        -analyzeImage(ImageData) number
    }
    
    class VideoMetadata {
        +extractVideoMetadata(File) VideoMetadata
        +extractVideoMetadataFromUrl(string) VideoMetadata
        -hasCompleteVideoMetadata(Record) boolean
    }
    
    ArtifactEditPage --> MediaList : использует
    ArtifactEditPage --> TargetList : использует
    ArtifactEditPage --> MediaUploader : использует
    ArtifactEditPage --> TargetUploader : использует
    ArtifactEditPage --> Queries : использует
    ArtifactEditPage --> UploadRoute : загружает файлы
    MediaUploader --> UploadRoute : загружает файлы
    TargetUploader --> ImageQualityChecker : проверяет качество
    Queries --> SupabaseClient : работает с БД
    UploadRoute --> VercelBlob : сохраняет файлы
    DeleteBlobRoute --> VercelBlob : удаляет файлы
```

## Основные классы и их ответственность

### Android приложение

#### ARManager
**Ответственность:** Управление AR сессией и проверка доступности AR на устройстве.

**Ключевые методы:**
- `InitializeAR()` - инициализирует AR сессию
- `StopAR()` - останавливает AR сессию
- `EnableCamera()` / `DisableCamera()` - управление камерой

#### TrackedArtifactManager
**Ответственность:** Отслеживание распознанных таргетов и координация загрузки артефактов.

**Ключевые методы:**
- Обработка событий `trackablesChanged` от ARTrackedImageManager
- Запрос артефактов через ArtifactService
- Размещение медиа через ModelSceneManager/VideoSceneManager

#### ArtifactService
**Ответственность:** Центральный сервис для работы с артефактами, кеширование и история.

**Ключевые методы:**
- `RequestArtifactForTarget()` - запрашивает артефакт для таргета
- `UpsertArtifactRecord()` - сохраняет/обновляет артефакт в кеше
- `AppendHistoryEntry()` - добавляет запись в историю сканирования
- `GetHistoryItems()` - возвращает историю сканирования

#### ModelSceneManager
**Ответственность:** Управление размещением 3D моделей на AR сцене.

**Ключевые методы:**
- `RequestModelForHost()` - запрашивает модель для размещения в хосте
- `RemoveModelFromHost()` - удаляет модель из хоста
- `IsModelInScene()` - проверяет наличие модели на сцене

#### VideoSceneManager
**Ответственность:** Управление размещением видео на AR сцене.

**Ключевые методы:**
- `RequestVideoForHost()` - запрашивает видео для размещения в хосте
- `RemoveVideoFromHost()` - удаляет видео из хоста
- Поддержка автовосстановления битых видео файлов

#### TrackedModelHost
**Ответственность:** Хост для отображения 3D модели на AR сцене.

**Ключевые методы:**
- `AttachLoadedModel()` - прикрепляет загруженную модель
- `ResetToPlaceholder()` - сбрасывает к плейсхолдеру
- `SetTrackingActive()` - управляет видимостью при потере трекинга
- `TogglePinned()` - закрепляет модель на месте

#### TrackedVideoHost
**Ответственность:** Хост для отображения видео на AR сцене.

**Ключевые методы:**
- `AttachVideo()` - прикрепляет видео
- `ClearLoadedVideo()` - очищает загруженное видео
- `SetTrackingActive()` - управляет воспроизведением при потере трекинга

#### ArtifactStorage
**Ответственность:** Локальное хранилище для кеширования артефактов и медиа.

**Ключевые методы:**
- `LoadData()` - загружает данные из JSON файла
- `SaveData()` - сохраняет данные в JSON файл
- `GetMediaFilePath()` - возвращает путь для сохранения медиа
- `GetPreviewFilePath()` - возвращает путь для сохранения превью

### Веб-сервис

#### ArtifactEditPage
**Ответственность:** Главная страница для создания/редактирования артефактов.

**Ключевые методы:**
- `handleSave()` - сохраняет артефакт в БД
- `handleDelete()` - удаляет артефакт
- `handleMediaAdd()` / `handleMediaRemove()` - управление медиа
- `handleTargetAdd()` / `handleTargetRemove()` - управление таргетами

#### Queries
**Ответственность:** Функции для работы с базой данных Supabase.

**Ключевые методы:**
- `createArtifact()` - создает артефакт
- `updateArtifact()` - обновляет артефакт
- `addArtifactMedia()` - добавляет медиа к артефакту
- `createTarget()` - создает таргет

#### ImageQualityChecker
**Ответственность:** Анализ качества изображений таргетов.

**Ключевые методы:**
- `checkImageQualityFromUrl()` - проверяет качество по URL
- `checkImageQuality()` - проверяет качество файла
- Возвращает балл качества (0-100)

#### VideoMetadata
**Ответственность:** Извлечение метаданных из видео файлов.

**Ключевые методы:**
- `extractVideoMetadata()` - извлекает метаданные из файла
- `extractVideoMetadataFromUrl()` - извлекает метаданные по URL
- Возвращает разрешение, длительность, размер файла

## Отношения между классами

### Композиция
- `TrackedArtifactManager` содержит `TrackedModelHost` и `TrackedVideoHost`
- `ArtifactService` использует `ArtifactStorage` для хранения данных

### Зависимости
- `TrackedArtifactManager` зависит от `ArtifactService`, `ModelSceneManager`, `VideoSceneManager`
- `ModelSceneManager` зависит от `ModelLoaderService`
- `ArtifactEditPage` зависит от `Queries` для работы с БД

### Ассоциации
- `ArtifactService` ассоциирован с `SupabaseService` для API запросов
- `ArtifactMediaService` ассоциирован с `ArtifactStorage` для сохранения файлов


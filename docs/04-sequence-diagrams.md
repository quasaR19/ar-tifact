# Sequence диаграммы

## Описание

Sequence диаграммы показывают последовательность взаимодействий между компонентами системы во времени для ключевых процессов.

## 1. Распознавание таргета и загрузка артефакта (Android)

```mermaid
sequenceDiagram
    participant User as Пользователь
    participant AR as ARManager
    participant TIM as ARTrackedImageManager
    participant TAM as TrackedArtifactManager
    participant AS as ArtifactService
    participant SS as SupabaseService
    participant DB as Supabase DB
    participant MSM as ModelSceneManager
    participant MLS as ModelLoaderService
    participant TMH as TrackedModelHost

    User->>AR: Открыть приложение
    AR->>AR: InitializeAR()
    AR->>TIM: Инициализация AR сессии
    TIM->>TIM: Создание библиотеки таргетов
    
    User->>TIM: Навести камеру на таргет
    TIM->>TAM: trackedImagesChanged (added)
    TAM->>TAM: ResolveTargetIdFromTrackedImage()
    TAM->>AS: RequestArtifactForTarget(targetId)
    
    AS->>AS: TryGetCachedArtifact(targetId)
    alt Модель в кеше
        AS->>AS: BuildAvailabilityResult (из кеша)
        AS-->>TAM: ArtifactAvailabilityResult
    else Модель не в кеше
        AS->>SS: FetchArtifactBundlesForTarget(targetId)
        SS->>DB: GET /rest/v1/targets?select=...
        DB-->>SS: Target с Artifact и Media
        SS-->>AS: List~ArtifactRemoteEntry~
        
        AS->>AS: SelectPreferredEntry()
        AS->>AS: ConvertToRecord()
        AS->>AS: EnsurePreviewDownloaded()
        AS->>AS: EnsureModelDownloaded()
        AS->>AS: UpsertArtifactRecord()
        AS-->>TAM: ArtifactAvailabilityResult
    end
    
    TAM->>MSM: RequestModelForHost(artifactId, host, localPath)
    MSM->>MLS: TryGetLoadedModel(artifactId)
    alt Модель загружена
        MLS-->>MSM: GameObject model
    else Модель не загружена
        MSM->>MLS: RequestModelLoad(artifactId, localPath)
        MLS->>MLS: Загрузка GLB файла
        MLS-->>MSM: GameObject model
    end
    
    MSM->>TMH: AttachLoadedModel(model, artifactId, metadata)
    TMH->>TMH: Размещение модели на сцене
    TMH-->>MSM: Успех
    MSM-->>TAM: Успех
    TAM-->>User: Модель отображается на AR сцене
```

## 2. Загрузка и отображение видео

```mermaid
sequenceDiagram
    participant TAM as TrackedArtifactManager
    participant AS as ArtifactService
    participant VSM as VideoSceneManager
    participant AMS as ArtifactMediaService
    participant TVH as TrackedVideoHost
    participant AVP as ARVideoPlayer
    participant Blob as Vercel Blob

    TAM->>AS: RequestArtifactForTarget(targetId)
    AS->>AS: Проверка кеша
    alt Видео в кеше
        AS-->>TAM: ArtifactAvailabilityResult (IsVideo=true)
    else Видео не в кеше
        AS->>AS: FetchArtifactBundlesForTarget()
        AS->>AS: EnsureVideoDownloaded()
        AS->>AMS: DownloadVideo(artifactId, mediaId, remoteUrl)
        AMS->>Blob: Загрузка видео файла
        Blob-->>AMS: Видео файл
        AMS->>AMS: Сохранение в локальное хранилище
        AMS-->>AS: localPath
        AS-->>TAM: ArtifactAvailabilityResult (IsVideo=true)
    end
    
    TAM->>VSM: RequestVideoForHost(artifactId, host, localPath, videoUrl, isYouTube)
    VSM->>VSM: CreateAndPlaceVideoAsync()
    VSM->>VSM: Создание GameObject с ARVideoPlayer
    VSM->>TVH: AttachVideo(videoObject, artifactId, videoPath, isYouTube, metadata)
    
    alt YouTube видео
        TVH->>AVP: Воспроизведение по URL
        AVP->>AVP: Загрузка YouTube видео
    else Локальное видео
        TVH->>AVP: Воспроизведение из файла
        AVP->>AVP: Загрузка локального файла
    end
    
    AVP-->>TVH: Видео готово к воспроизведению
    TVH-->>VSM: Успех
    VSM-->>TAM: Видео размещено
    TAM-->>TAM: Видео отображается на AR сцене
```

## 3. Создание артефакта через веб-интерфейс

```mermaid
sequenceDiagram
    participant Admin as Администратор
    participant AEP as ArtifactEditPage
    participant Upload as UploadRoute
    participant Blob as Vercel Blob
    participant Queries as Queries
    participant Supabase as Supabase Client
    participant DB as Supabase DB
    participant IQC as ImageQualityChecker

    Admin->>AEP: Заполнение формы артефакта
    Admin->>AEP: Загрузка превью изображения
    AEP->>Upload: POST /api/upload (preview file)
    Upload->>Blob: Загрузка файла
    Blob-->>Upload: blob.url
    Upload-->>AEP: previewImageUrl
    
    Admin->>AEP: Добавление медиа файла
    AEP->>Upload: POST /api/upload (media file)
    Upload->>Blob: Загрузка файла
    Blob-->>Upload: blob.url
    Upload-->>AEP: mediaUrl
    
    alt Видео файл
        AEP->>AEP: extractVideoMetadata(file)
        AEP->>AEP: Получение метаданных (width, height, duration)
    end
    
    Admin->>AEP: Загрузка таргета
    AEP->>IQC: checkImageQuality(file)
    IQC->>IQC: Анализ изображения
    IQC-->>AEP: qualityScore
    
    alt qualityScore < 75
        AEP-->>Admin: Ошибка: качество таргета недостаточно
    else qualityScore >= 75
        AEP->>Upload: POST /api/upload (target file)
        Upload->>Blob: Загрузка файла
        Blob-->>Upload: blob.url
        Upload-->>AEP: targetUrl
        
        Admin->>AEP: Нажатие "Сохранить"
        AEP->>Queries: createArtifact(name, description)
        Queries->>Supabase: INSERT INTO artifacts
        Supabase->>DB: Сохранение артефакта
        DB-->>Supabase: artifact.id
        Supabase-->>Queries: Artifact
        Queries-->>AEP: artifactId
        
        AEP->>Queries: updateArtifact(artifactId, preview_image_url)
        AEP->>Queries: addArtifactMedia(artifactId, type, url, metadata)
        AEP->>Queries: createTarget(artifactId, url, size_cm)
        
        Queries->>Supabase: INSERT/UPDATE операции
        Supabase->>DB: Сохранение данных
        DB-->>Supabase: Успех
        Supabase-->>Queries: Успех
        Queries-->>AEP: Успех
        AEP-->>Admin: Артефакт создан
    end
```

## 4. Загрузка медиа файла

```mermaid
sequenceDiagram
    participant Admin as Администратор
    participant MU as MediaUploader
    participant AEP as ArtifactEditPage
    participant Upload as UploadRoute
    participant Blob as Vercel Blob
    participant VM as VideoMetadata

    Admin->>MU: Выбор файла
    MU->>MU: Валидация типа файла
    
    alt 3D модель (GLB)
        MU->>AEP: onFileSelect(file)
        AEP->>AEP: Добавление в localMedia
        Admin->>AEP: Нажатие "Сохранить"
        AEP->>Upload: POST /api/upload (GLB file)
        Upload->>Blob: Загрузка GLB файла
        Blob-->>Upload: blob.url
        Upload-->>AEP: mediaUrl
        AEP->>AEP: addArtifactMedia(artifactId, "3d_model", url)
    else Видео файл
        MU->>AEP: onFileSelect(file)
        AEP->>VM: extractVideoMetadata(file)
        VM->>VM: Извлечение метаданных
        VM-->>AEP: VideoMetadata (width, height, duration)
        AEP->>AEP: Добавление в localMedia с метаданными
        Admin->>AEP: Нажатие "Сохранить"
        AEP->>Upload: POST /api/upload (video file)
        Upload->>Blob: Загрузка видео файла
        Blob-->>Upload: blob.url
        Upload-->>AEP: mediaUrl
        AEP->>AEP: addArtifactMedia(artifactId, "video", url, metadata)
    else YouTube ссылка
        Admin->>MU: Ввод YouTube URL
        MU->>AEP: onUrlChange(url)
        AEP->>AEP: Добавление в localMedia (type="youtube")
        Admin->>AEP: Нажатие "Сохранить"
        AEP->>AEP: addArtifactMedia(artifactId, "youtube", url)
    end
```

## 5. Автовосстановление битого видео файла

```mermaid
sequenceDiagram
    participant VSM as VideoSceneManager
    participant TVH as TrackedVideoHost
    participant AVP as ARVideoPlayer
    participant AMS as ArtifactMediaService
    participant Blob as Vercel Blob
    participant Storage as ArtifactStorage

    VSM->>TVH: AttachVideo(videoObject, artifactId, videoPath)
    TVH->>AVP: Загрузка видео из файла
    AVP->>AVP: Проверка файла
    
    alt Файл битый (0 bytes или ошибка)
        AVP-->>TVH: Ошибка загрузки
        TVH-->>VSM: Ошибка с признаком битого файла
        VSM->>VSM: AutoRecoverVideo(operation)
        
        VSM->>AVP: ForceReleaseFileCoroutine()
        AVP->>AVP: Освобождение файла
        AVP-->>VSM: Файл освобожден
        
        VSM->>Storage: Удаление битого файла
        Storage->>Storage: File.Delete(localPath)
        
        VSM->>AMS: DownloadVideo(artifactId, mediaId, remoteUrl)
        AMS->>Blob: Загрузка видео файла
        Blob-->>AMS: Видео файл
        AMS->>AMS: Сохранение в локальное хранилище
        AMS-->>VSM: newLocalPath
        
        VSM->>VSM: Проверка целостности файла
        alt Файл валиден
            VSM->>VSM: RequestVideoForHost(newLocalPath)
            VSM->>TVH: AttachVideo(videoObject, artifactId, newLocalPath)
            TVH->>AVP: Загрузка видео из нового файла
            AVP-->>TVH: Видео готово
            TVH-->>VSM: Успех
        else Файл все еще битый (повторная попытка)
            VSM->>VSM: AutoRecoverVideo (следующая попытка)
        end
    else Файл валиден
        AVP-->>TVH: Видео готово к воспроизведению
        TVH-->>VSM: Успех
    end
```

## 6. Просмотр истории сканирования

```mermaid
sequenceDiagram
    participant User as Пользователь
    participant UI as History UI
    participant AS as ArtifactService
    participant Storage as ArtifactStorage

    User->>UI: Открыть экран истории
    UI->>AS: GetHistoryItems()
    AS->>Storage: LoadData()
    Storage->>Storage: Чтение JSON файла
    Storage-->>AS: ArtifactStorageData
    
    AS->>AS: RebuildHistoryCache()
    AS->>AS: Группировка по targetId
    AS->>AS: Создание ArtifactHistoryItem[]
    AS-->>UI: IReadOnlyList~ArtifactHistoryItem~
    
    UI->>UI: Отображение списка артефактов
    UI-->>User: История сканирования
    
    User->>UI: Выбор артефакта
    UI->>UI: Отображение деталей артефакта
    UI-->>User: Детали артефакта
```


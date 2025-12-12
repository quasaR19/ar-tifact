# Процессы и потоки данных

## Описание

Документ описывает потоки данных в системе AR-tifact, включая процессы распознавания таргетов, загрузки медиа, кеширования и синхронизации.

## 1. Поток данных при распознавании таргета

```mermaid
flowchart TD
    Start([Пользователь наводит камеру на таргет])
    ARInit[ARManager: Инициализация AR сессии]
    ARTrack[ARTrackedImageManager: Распознавание таргета]
    ResolveId[TrackedArtifactManager: Определение targetId]
    CheckCache{Артефакт в кеше?}
    
    CacheHit[ArtifactService: Получение из кеша]
    CacheMiss[ArtifactService: Запрос из Supabase]
    SupabaseQuery[SupabaseService: REST API запрос]
    DBQuery[(Supabase DB: Запрос артефакта)]
    
    DownloadMedia{Медиа в кеше?}
    DownloadFile[ArtifactMediaService: Загрузка из Vercel Blob]
    BlobStorage[(Vercel Blob: Получение файла)]
    SaveLocal[ArtifactStorage: Сохранение в локальное хранилище]
    
    LoadModel[ModelLoaderService: Загрузка GLB]
    PlaceModel[ModelSceneManager: Размещение на сцене]
    Display[TrackedModelHost: Отображение модели]
    
    End([Модель отображается на AR сцене])
    
    Start --> ARInit
    ARInit --> ARTrack
    ARTrack --> ResolveId
    ResolveId --> CheckCache
    
    CheckCache -->|Да| CacheHit
    CheckCache -->|Нет| CacheMiss
    
    CacheMiss --> SupabaseQuery
    SupabaseQuery --> DBQuery
    DBQuery --> DownloadMedia
    
    DownloadMedia -->|Нет| DownloadFile
    DownloadFile --> BlobStorage
    BlobStorage --> SaveLocal
    SaveLocal --> LoadModel
    
    DownloadMedia -->|Да| LoadModel
    CacheHit --> LoadModel
    
    LoadModel --> PlaceModel
    PlaceModel --> Display
    Display --> End
    
    style Start fill:#e1f5ff
    style End fill:#c8e6c9
    style DBQuery fill:#fff4e1
    style BlobStorage fill:#fff4e1
    style SaveLocal fill:#f3e5f5
```

## 2. Поток данных при загрузке медиа

```mermaid
flowchart TD
    Start([Администратор загружает медиа файл])
    SelectFile[MediaUploader: Выбор файла]
    ValidateType{Тип файла валиден?}
    
    VideoFile{Видео файл?}
    ExtractMeta[VideoMetadata: Извлечение метаданных]
    
    UploadAPI[POST /api/upload]
    AuthCheck{Пользователь авторизован?}
    BlobUpload[Vercel Blob: Загрузка файла]
    GetURL[Получение URL файла]
    
    SaveMedia[Queries: createArtifactMedia]
    SupabaseInsert[Supabase: INSERT INTO media]
    LinkMedia[Queries: addArtifactMedia]
    SupabaseLink[Supabase: INSERT INTO artifact_media]
    
    End([Медиа сохранено и связано с артефактом])
    
    Start --> SelectFile
    SelectFile --> ValidateType
    
    ValidateType -->|Нет| Error1[Ошибка: неверный тип файла]
    ValidateType -->|Да| VideoFile
    
    VideoFile -->|Да| ExtractMeta
    VideoFile -->|Нет| UploadAPI
    ExtractMeta --> UploadAPI
    
    UploadAPI --> AuthCheck
    AuthCheck -->|Нет| Error2[Ошибка: не авторизован]
    AuthCheck -->|Да| BlobUpload
    
    BlobUpload --> GetURL
    GetURL --> SaveMedia
    SaveMedia --> SupabaseInsert
    SupabaseInsert --> LinkMedia
    LinkMedia --> SupabaseLink
    SupabaseLink --> End
    
    style Start fill:#e1f5ff
    style End fill:#c8e6c9
    style BlobUpload fill:#fff4e1
    style SupabaseInsert fill:#fff4e1
    style SupabaseLink fill:#fff4e1
    style Error1 fill:#ffcdd2
    style Error2 fill:#ffcdd2
```

## 3. Кеширование на Android устройстве

```mermaid
flowchart TD
    Start([Запрос артефакта])
    CheckMemory{Модель в памяти?}
    CheckDisk{Файл на диске?}
    CheckDB{Запись в БД кеша?}
    
    MemoryHit[ModelLoaderService: Использование из памяти]
    DiskHit[ModelLoaderService: Загрузка с диска]
    DBCache[ArtifactStorage: Получение метаданных]
    
    Download[ArtifactMediaService: Загрузка из сети]
    SaveDisk[ArtifactStorage: Сохранение на диск]
    LoadMemory[ModelLoaderService: Загрузка в память]
    
    UpdateCache[ArtifactStorage: Обновление кеша]
    SaveHistory[ArtifactStorage: Сохранение истории]
    
    End([Артефакт готов к использованию])
    
    Start --> CheckMemory
    CheckMemory -->|Да| MemoryHit
    CheckMemory -->|Нет| CheckDisk
    
    CheckDisk -->|Да| DiskHit
    DiskHit --> LoadMemory
    LoadMemory --> End
    
    CheckDisk -->|Нет| CheckDB
    CheckDB -->|Да| DBCache
    CheckDB -->|Нет| Download
    
    DBCache --> Download
    Download --> SaveDisk
    SaveDisk --> LoadMemory
    LoadMemory --> UpdateCache
    UpdateCache --> SaveHistory
    SaveHistory --> End
    
    MemoryHit --> End
    
    style Start fill:#e1f5ff
    style End fill:#c8e6c9
    style MemoryHit fill:#c8e6c9
    style DiskHit fill:#fff9c4
    style Download fill:#ffccbc
    style SaveDisk fill:#f3e5f5
    style LoadMemory fill:#e1bee7
```

## 4. Синхронизация данных

```mermaid
flowchart TD
    Start([Изменение данных в веб-интерфейсе])
    WebUpdate[Веб-сервис: Обновление в Supabase]
    SupabaseUpdate[(Supabase DB: Обновление данных)]
    
    AndroidCheck{Android приложение активно?}
    Polling[Android: Периодический опрос]
    CacheCheck{Данные в кеше?}
    
    InvalidateCache[ArtifactService: Инвалидация кеша]
    ReDownload[ArtifactMediaService: Повторная загрузка]
    UpdateLocal[ArtifactStorage: Обновление локальных данных]
    
    End([Данные синхронизированы])
    
    Start --> WebUpdate
    WebUpdate --> SupabaseUpdate
    
    SupabaseUpdate --> AndroidCheck
    AndroidCheck -->|Да| Polling
    AndroidCheck -->|Нет| Wait[Ожидание следующего запуска]
    
    Polling --> CacheCheck
    CacheCheck -->|Да| InvalidateCache
    CacheCheck -->|Нет| End
    
    InvalidateCache --> ReDownload
    ReDownload --> UpdateLocal
    UpdateLocal --> End
    
    Wait --> Polling
    
    style Start fill:#e1f5ff
    style End fill:#c8e6c9
    style SupabaseUpdate fill:#fff4e1
    style InvalidateCache fill:#ffccbc
    style ReDownload fill:#ffccbc
    style UpdateLocal fill:#f3e5f5
```

## 5. Процесс создания артефакта

```mermaid
flowchart TD
    Start([Администратор создает артефакт])
    FillForm[Заполнение формы]
    UploadPreview[Загрузка превью изображения]
    UploadMedia[Загрузка медиа файлов]
    UploadTargets[Загрузка таргетов]
    
    ValidateTargets{Таргеты валидны?}
    QualityCheck[ImageQualityChecker: Проверка качества]
    
    CreateArtifact[Queries: createArtifact]
    SaveArtifact[(Supabase: Сохранение артефакта)]
    
    UploadBlobs[Vercel Blob: Загрузка файлов]
    SaveMedia[(Supabase: Сохранение медиа)]
    SaveTargets[(Supabase: Сохранение таргетов)]
    
    LinkMedia[(Supabase: Связывание медиа с артефактом)]
    LinkTargets[(Supabase: Связывание таргетов с артефактом)]
    
    End([Артефакт создан и доступен])
    
    Start --> FillForm
    FillForm --> UploadPreview
    UploadPreview --> UploadMedia
    UploadMedia --> UploadTargets
    
    UploadTargets --> QualityCheck
    QualityCheck --> ValidateTargets
    
    ValidateTargets -->|Нет| Error[Ошибка: качество таргетов недостаточно]
    ValidateTargets -->|Да| CreateArtifact
    
    CreateArtifact --> SaveArtifact
    SaveArtifact --> UploadBlobs
    UploadBlobs --> SaveMedia
    SaveMedia --> SaveTargets
    SaveTargets --> LinkMedia
    LinkMedia --> LinkTargets
    LinkTargets --> End
    
    style Start fill:#e1f5ff
    style End fill:#c8e6c9
    style SaveArtifact fill:#fff4e1
    style UploadBlobs fill:#fff4e1
    style SaveMedia fill:#fff4e1
    style SaveTargets fill:#fff4e1
    style LinkMedia fill:#fff4e1
    style LinkTargets fill:#fff4e1
    style Error fill:#ffcdd2
```

## 6. Автовосстановление битого видео

```mermaid
flowchart TD
    Start([Попытка воспроизведения видео])
    LoadVideo[ARVideoPlayer: Загрузка видео]
    CheckFile{Файл валиден?}
    
    FileError[Обнаружена ошибка файла]
    CheckAttempts{Попыток < 3?}
    
    ReleaseFile[Освобождение файла из памяти]
    DeleteFile[Удаление битого файла]
    WaitDelay[Ожидание задержки]
    
    ReDownload[ArtifactMediaService: Повторная загрузка]
    BlobDownload[(Vercel Blob: Загрузка файла)]
    SaveNew[ArtifactStorage: Сохранение нового файла]
    
    VerifyFile{Файл валиден?}
    PlaceVideo[VideoSceneManager: Размещение видео]
    
    Success([Видео успешно восстановлено])
    Failure([Не удалось восстановить])
    
    Start --> LoadVideo
    LoadVideo --> CheckFile
    
    CheckFile -->|Да| Success
    CheckFile -->|Нет| FileError
    
    FileError --> CheckAttempts
    CheckAttempts -->|Нет| Failure
    CheckAttempts -->|Да| ReleaseFile
    
    ReleaseFile --> DeleteFile
    DeleteFile --> WaitDelay
    WaitDelay --> ReDownload
    
    ReDownload --> BlobDownload
    BlobDownload --> SaveNew
    SaveNew --> VerifyFile
    
    VerifyFile -->|Да| PlaceVideo
    VerifyFile -->|Нет| CheckAttempts
    
    PlaceVideo --> Success
    
    style Start fill:#e1f5ff
    style Success fill:#c8e6c9
    style Failure fill:#ffcdd2
    style FileError fill:#ffccbc
    style ReDownload fill:#fff4e1
    style BlobDownload fill:#fff4e1
    style SaveNew fill:#f3e5f5
```

## 7. Управление историей сканирования

```mermaid
flowchart TD
    Start([Распознавание таргета])
    AppendHistory[ArtifactService: AppendHistoryEntry]
    CheckDirty{История изменена?}
    
    ScheduleSave[Планирование отложенного сохранения]
    WaitDelay[Ожидание 2 секунды]
    CheckDirtyAgain{История все еще изменена?}
    
    SaveToDisk[ArtifactStorage: SaveData]
    WriteJSON[Запись в JSON файл]
    
    ForceSave{Принудительное сохранение?}
    ImmediateSave[Немедленное сохранение]
    
    End([История сохранена])
    
    Start --> AppendHistory
    AppendHistory --> CheckDirty
    
    CheckDirty -->|Да| ScheduleSave
    CheckDirty -->|Нет| End
    
    ScheduleSave --> WaitDelay
    WaitDelay --> CheckDirtyAgain
    
    CheckDirtyAgain -->|Да| SaveToDisk
    CheckDirtyAgain -->|Нет| End
    
    SaveToDisk --> WriteJSON
    WriteJSON --> End
    
    ForceSave -->|Да| ImmediateSave
    ImmediateSave --> SaveToDisk
    
    style Start fill:#e1f5ff
    style End fill:#c8e6c9
    style ScheduleSave fill:#fff9c4
    style SaveToDisk fill:#f3e5f5
    style WriteJSON fill:#e1bee7
    style ImmediateSave fill:#ffccbc
```

## Особенности потоков данных

### Оптимизация производительности

1. **Кеширование на трех уровнях:**
   - Память (GameObject в ModelLoaderService)
   - Диск (локальные файлы в ArtifactStorage)
   - База данных (метаданные в JSON кеше)

2. **Отложенное сохранение:**
   - История сканирования сохраняется с задержкой 2 секунды
   - Батчинг изменений для уменьшения операций записи

3. **Предотвращение дублирования:**
   - Отслеживание активных запросов
   - Кеширование результатов запросов

### Обработка ошибок

1. **Автовосстановление:**
   - Автоматическое восстановление битых видео файлов
   - До 3 попыток восстановления
   - Экспоненциальная задержка между попытками

2. **Graceful degradation:**
   - Использование кеша при отсутствии сети
   - Показ понятных сообщений об ошибках
   - Сохранение состояния для последующего восстановления

### Синхронизация

1. **Односторонняя синхронизация:**
   - Изменения в веб-интерфейсе сразу доступны в Android
   - Android приложение периодически проверяет обновления
   - Инвалидация кеша при обнаружении изменений

2. **Конфликты:**
   - Последнее изменение имеет приоритет
   - Метаданные обновляются автоматически
   - Медиа файлы перезагружаются при необходимости


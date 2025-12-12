# Веб-сервис - детальное описание

## Обзор

Веб-сервис разработан на Next.js и предоставляет систему управления контентом (CMS) для создания, редактирования и управления артефактами, медиа файлами и таргетами.

## Архитектура Next.js приложения

### Структура папок

```
site/
├── app/                    # Next.js App Router
│   ├── api/               # API маршруты
│   │   ├── upload/
│   │   └── delete-blob/
│   ├── artifacts/         # Страницы артефактов
│   │   └── edit/
│   └── auth/             # Страницы аутентификации
├── components/            # React компоненты
│   ├── artifact-*.tsx
│   ├── media-*.tsx
│   ├── target-*.tsx
│   └── ui/               # UI компоненты (Radix UI)
├── lib/                   # Утилиты и библиотеки
│   ├── queries.ts        # Функции для работы с БД
│   ├── supabase/         # Supabase клиенты
│   ├── image-analysis/   # Анализ изображений
│   ├── video-metadata.ts # Метаданные видео
│   └── image-converter.ts
└── components.json        # Конфигурация shadcn/ui
```

## API маршруты

### POST /api/upload

**Расположение:** `site/app/api/upload/route.ts`

**Назначение:** Загрузка файлов в Vercel Blob Storage.

**Аутентификация:** Требуется авторизованный пользователь.

**Параметры запроса:**
```typescript
{
  pathname: string;      // Имя файла
  contentType: string;    // MIME тип файла
  contentLength: number; // Размер файла
  clientPayload?: any;   // Дополнительные данные
}
```

**Поддерживаемые типы файлов:**
- **GLB файлы** (3D модели): `model/gltf-binary`, `application/octet-stream`
- **Видео**: `video/mp4`, `video/webm`, `video/quicktime`, `video/x-msvideo`
- **Изображения**: `image/jpeg`, `image/png`, `image/gif`, `image/svg+xml`

**Процесс:**
1. Проверка аутентификации пользователя
2. Определение разрешенных типов контента по расширению файла
3. Генерация токена для загрузки через Vercel Blob
4. Возврат URL загруженного файла

**Ответ:**
```typescript
{
  url: string;           // URL загруженного файла
  downloadUrl?: string;  // URL для скачивания
}
```

### POST /api/delete-blob

**Расположение:** `site/app/api/delete-blob/route.ts`

**Назначение:** Удаление файлов из Vercel Blob Storage.

**Аутентификация:** Требуется авторизованный пользователь.

**Параметры запроса:**
```typescript
{
  urls: string[];  // Массив URL файлов для удаления
}
```

**Процесс:**
1. Проверка аутентификации пользователя
2. Удаление каждого файла из Vercel Blob
3. Возврат результата операции

## Компоненты React

### ArtifactEditPage

**Расположение:** `site/app/artifacts/edit/[id]/page.tsx`

**Назначение:** Главная страница для создания и редактирования артефактов.

**Основной функционал:**
- Создание нового артефакта
- Редактирование существующего артефакта
- Управление медиа файлами (3D модели, видео, YouTube)
- Управление таргетами (маркерами)
- Загрузка превью изображения
- Валидация таргетов (минимальный балл качества 75)

**Состояние:**
- `name` - название артефакта
- `description` - описание артефакта
- `localMedia` - список медиа файлов
- `localTargets` - список таргетов
- `previewImageFile` - файл превью изображения
- `saveSteps` - шаги процесса сохранения

**Ключевые методы:**
- `handleSave()` - сохраняет артефакт в БД
- `handleDelete()` - удаляет артефакт
- `handleMediaAdd()` / `handleMediaRemove()` - управление медиа
- `handleTargetAdd()` / `handleTargetRemove()` - управление таргетами

**Процесс сохранения:**
1. Валидация данных (название, качество таргетов)
2. Загрузка превью изображения (если изменено)
3. Создание/обновление артефакта в Supabase
4. Удаление старых медиа/таргетов
5. Обновление существующих медиа
6. Загрузка новых медиа файлов в Vercel Blob
7. Создание новых таргетов
8. Обновление порядка отображения медиа (`display_order`)

### MediaUploader

**Расположение:** `site/components/media-uploader.tsx`

**Назначение:** Компонент для загрузки медиа файлов.

**Поддерживаемые типы:**
- **3D модели**: GLB файлы
- **Видео**: MP4, WebM, MOV, AVI файлы
- **YouTube**: URL ссылки на YouTube видео

**Функционал:**
- Выбор файла для загрузки
- Ввод YouTube URL
- Предпросмотр загруженного медиа
- Удаление медиа

### TargetUploader

**Расположение:** `site/components/target-uploader.tsx`

**Назначение:** Компонент для загрузки таргетов (маркеров).

**Функционал:**
- Выбор изображения таргета
- Анализ качества изображения
- Отображение балла качества
- Валидация (минимальный балл 75)
- Настройка размера таргета (size_cm)

### MediaList

**Расположение:** `site/components/media-list.tsx`

**Назначение:** Список медиа файлов артефакта с возможностью управления порядком.

**Функционал:**
- Отображение списка медиа
- Изменение порядка (перемещение вверх/вниз)
- Удаление медиа
- Добавление нового медиа
- Предпросмотр медиа

**Особенности:**
- Сортировка по `display_order`
- Нормализация `display_order` при удалении элементов

### TargetList

**Расположение:** `site/components/target-list.tsx`

**Назначение:** Список таргетов артефакта.

**Функционал:**
- Отображение списка таргетов
- Отображение балла качества каждого таргета
- Удаление таргетов
- Добавление нового таргета
- Редактирование размера таргета

### SaveProgressDialog

**Расположение:** `site/components/save-progress-dialog.tsx`

**Назначение:** Модальное окно с отображением прогресса сохранения артефакта.

**Функционал:**
- Отображение списка шагов сохранения
- Статусы шагов: `pending`, `processing`, `success`, `error`
- Детали каждого шага
- Общий статус операции

## Работа с Supabase

### Queries

**Расположение:** `site/lib/queries.ts`

**Назначение:** Функции для работы с базой данных Supabase.

**Основные функции:**

#### `getArtifactsPaginated()`
Получение артефактов постранично.

```typescript
getArtifactsPaginated(
  supabaseClient: SupabaseClient,
  page: number = 1,
  pageSize: number = 20
): Promise<PaginatedArtifacts>
```

#### `getArtifactById()`
Получение полной информации об артефакте с медиа и таргетами.

```typescript
getArtifactById(
  supabaseClient: SupabaseClient,
  artifactId: string,
  includeInactive: boolean = false
): Promise<ArtifactWithDetails | null>
```

#### `createArtifact()`
Создание нового артефакта.

```typescript
createArtifact(
  supabaseClient: SupabaseClient,
  name: string,
  description: string | null
): Promise<Artifact>
```

#### `updateArtifact()`
Обновление артефакта.

```typescript
updateArtifact(
  supabaseClient: SupabaseClient,
  artifactId: string,
  updates: Partial<Artifact>
): Promise<Artifact>
```

#### `deleteArtifact()`
Удаление артефакта и всех связанных данных.

```typescript
deleteArtifact(
  supabaseClient: SupabaseClient,
  artifactId: string
): Promise<{ previewImageUrl, mediaUrls, targetUrls }>
```

#### `addArtifactMedia()`
Добавление медиа к артефакту.

```typescript
addArtifactMedia(
  supabaseClient: SupabaseClient,
  artifactId: string,
  mediaType: "3d_model" | "video" | "youtube",
  url: string,
  metadata: Record<string, unknown> | null
): Promise<ArtifactMedia>
```

#### `createTarget()`
Создание таргета для артефакта.

```typescript
createTarget(
  supabaseClient: SupabaseClient,
  artifactId: string,
  url: string,
  sizeCm: number
): Promise<Target>
```

### Supabase клиенты

**Расположение:** `site/lib/supabase/`

#### `server.ts`
Клиент для серверных компонентов Next.js.

```typescript
export async function createClient(): Promise<SupabaseClient>
```

Использует `@supabase/ssr` для работы с cookies в серверных компонентах.

#### `client.ts`
Клиент для клиентских компонентов.

```typescript
export function createClient(): SupabaseClient
```

Использует браузерный клиент Supabase.

## Загрузка файлов в Vercel Blob

### Процесс загрузки

1. **Клиент** выбирает файл через `MediaUploader` или `TargetUploader`
2. **Компонент** вызывает `upload()` из `@vercel/blob/client`
3. **Vercel Blob** генерирует токен для загрузки через `/api/upload`
4. **API маршрут** проверяет аутентификацию и тип файла
5. **Файл** загружается в Vercel Blob Storage
6. **Возвращается** URL загруженного файла
7. **URL** сохраняется в Supabase вместе с метаданными

### Обработка ошибок

- Валидация типа файла на клиенте и сервере
- Проверка размера файла
- Обработка ошибок сети
- Отображение понятных сообщений об ошибках

## Анализ качества изображений

### ImageQualityChecker

**Расположение:** `site/lib/image-analysis/imageQualityChecker.ts`

**Назначение:** Анализ качества изображений таргетов для обеспечения надежного распознавания.

**Методы:**
- `checkImageQualityFromUrl(url: string)` - анализ по URL
- `checkImageQuality(file: File)` - анализ файла

**Критерии оценки:**
- Контраст изображения
- Резкость и детализация
- Наличие четких границ
- Общее качество изображения

**Результат:**
- Балл качества от 0 до 100
- Минимальный проходной балл: **75**

**Использование:**
- Автоматическая проверка при загрузке таргета
- Отображение балла качества в интерфейсе
- Блокировка сохранения при низком качестве

## Извлечение метаданных видео

### VideoMetadata

**Расположение:** `site/lib/video-metadata.ts`

**Назначение:** Извлечение метаданных из видео файлов.

**Методы:**
- `extractVideoMetadata(file: File)` - извлечение из файла
- `extractVideoMetadataFromUrl(url: string)` - извлечение по URL
- `hasCompleteVideoMetadata(metadata: Record<string, unknown>)` - проверка полноты метаданных

**Извлекаемые метаданные:**
- `width` - ширина видео (пиксели)
- `height` - высота видео (пиксели)
- `duration` - длительность видео (секунды)
- `size` - размер файла (байты)
- `filename` - имя файла

**Использование:**
- Автоматическое извлечение при загрузке видео
- Сохранение метаданных в БД (JSONB поле `metadata`)
- Использование метаданных в Android приложении для правильного отображения

## Конвертация изображений

### ImageConverter

**Расположение:** `site/lib/image-converter.ts`

**Назначение:** Конвертация изображений между форматами.

**Функционал:**
- Конвертация WebP в JPG/PNG
- Определение формата изображения
- Оптимизация изображений

**Использование:**
- Конвертация WebP превью изображений в JPG для совместимости
- Обработка изображений перед загрузкой в Vercel Blob

## Аутентификация

### Страницы аутентификации

**Расположение:** `site/app/auth/`

- `login/page.tsx` - страница входа
- `sign-up/page.tsx` - страница регистрации
- `forgot-password/page.tsx` - восстановление пароля
- `update-password/page.tsx` - обновление пароля

### Компоненты

- `login-form.tsx` - форма входа
- `sign-up-form.tsx` - форма регистрации
- `forgot-password-form.tsx` - форма восстановления пароля
- `auth-button.tsx` - кнопка аутентификации
- `logout-button.tsx` - кнопка выхода

### Безопасность

- Использование Supabase Auth для аутентификации
- Row Level Security (RLS) в базе данных
- Проверка аутентификации в API маршрутах
- Защита от CSRF через SameSite cookies

## UI компоненты

### Radix UI компоненты

**Расположение:** `site/components/ui/`

Используются компоненты из Radix UI через shadcn/ui:

- `button.tsx` - кнопки
- `card.tsx` - карточки
- `dialog.tsx` - модальные окна
- `input.tsx` - поля ввода
- `label.tsx` - метки
- `checkbox.tsx` - чекбоксы
- `dropdown-menu.tsx` - выпадающие меню
- `badge.tsx` - бейджи

### Стилизация

- **Tailwind CSS** - утилитарный CSS фреймворк
- **CSS Variables** - для темной/светлой темы
- **Responsive Design** - адаптивный дизайн для мобильных устройств

## Оптимизация производительности

### Кеширование

- Кеширование запросов к Supabase
- Оптимизация загрузки изображений
- Ленивая загрузка компонентов

### Оптимизация загрузки

- Code splitting для уменьшения размера бандла
- Оптимизация изображений через Next.js Image
- Минификация и сжатие ресурсов

### Обработка больших файлов

- Прогрессивная загрузка файлов
- Показ прогресса загрузки
- Обработка ошибок сети с повторными попытками


# API документация

## Обзор

Система использует REST API Supabase для работы с базой данных и собственные API маршруты Next.js для загрузки файлов.

## Supabase REST API

### Базовый URL

```
https://<project-id>.supabase.co/rest/v1
```

### Аутентификация

Все запросы требуют заголовки:
```
apikey: <supabase-anon-key>
Authorization: Bearer <supabase-anon-key>
Content-Type: application/json
Prefer: return=representation
```

### Endpoints для работы с артефактами

#### GET /artifacts

Получение списка артефактов.

**Параметры запроса:**
- `select` - поля для выборки (по умолчанию `*`)
- `is_active` - фильтр по активности (eq=true)
- `order` - сортировка (например, `created_at.desc`)
- `limit` - ограничение количества результатов
- `offset` - смещение для пагинации

**Пример:**
```http
GET /rest/v1/artifacts?select=*&is_active=eq.true&order=created_at.desc&limit=20
```

**Ответ:**
```json
[
  {
    "id": "uuid",
    "name": "Название артефакта",
    "description": "Описание",
    "preview_image_url": "https://...",
    "created_at": "2024-01-01T00:00:00Z",
    "updated_at": "2024-01-01T00:00:00Z",
    "is_active": true
  }
]
```

#### GET /artifacts?id=eq.{artifactId}

Получение артефакта по ID.

**Пример:**
```http
GET /rest/v1/artifacts?id=eq.123e4567-e89b-12d3-a456-426614174000
```

#### POST /artifacts

Создание нового артефакта.

**Тело запроса:**
```json
{
  "name": "Название артефакта",
  "description": "Описание артефакта",
  "preview_image_url": "https://...",
  "is_active": true
}
```

**Ответ:**
```json
{
  "id": "uuid",
  "name": "Название артефакта",
  "description": "Описание артефакта",
  "preview_image_url": "https://...",
  "created_at": "2024-01-01T00:00:00Z",
  "updated_at": "2024-01-01T00:00:00Z",
  "is_active": true
}
```

#### PATCH /artifacts?id=eq.{artifactId}

Обновление артефакта.

**Тело запроса:**
```json
{
  "name": "Новое название",
  "description": "Новое описание",
  "preview_image_url": "https://..."
}
```

#### DELETE /artifacts?id=eq.{artifactId}

Удаление артефакта.

**Примечание:** При удалении артефакта автоматически удаляются связанные таргеты (CASCADE).

### Endpoints для работы с медиа

#### GET /media

Получение списка медиа ресурсов.

**Параметры запроса:**
- `select` - поля для выборки
- `media_type` - фильтр по типу (eq=3d_model, video, youtube)

**Пример:**
```http
GET /rest/v1/media?select=*&media_type=eq.3d_model
```

#### POST /media

Создание нового медиа ресурса.

**Тело запроса:**
```json
{
  "media_type": "3d_model",
  "url": "https://...",
  "metadata": {
    "center_model": true,
    "size": 1234567,
    "filename": "model.glb"
  }
}
```

**Для видео:**
```json
{
  "media_type": "video",
  "url": "https://...",
  "metadata": {
    "width": 1920,
    "height": 1080,
    "duration": 120.5,
    "filename": "video.mp4",
    "size": 56789012
  }
}
```

#### PATCH /media?id=eq.{mediaId}

Обновление медиа ресурса.

**Тело запроса:**
```json
{
  "metadata": {
    "width": 1920,
    "height": 1080,
    "duration": 120.5
  }
}
```

#### DELETE /media?id=eq.{mediaId}

Удаление медиа ресурса.

### Endpoints для работы с таргетами

#### GET /targets

Получение списка таргетов.

**Параметры запроса:**
- `select` - поля для выборки
- `artifact_id` - фильтр по артефакту (eq={artifactId})
- `id` - фильтр по ID таргета (eq={targetId})

**Пример получения таргета с артефактом:**
```http
GET /rest/v1/targets?select=id,artifact_id,artifacts(*,artifact_media(media(*)))&id=eq.{targetId}
```

**Ответ:**
```json
[
  {
    "id": "uuid",
    "url": "https://...",
    "size_cm": 10,
    "artifact_id": "uuid",
    "created_at": "2024-01-01T00:00:00Z",
    "artifacts": {
      "id": "uuid",
      "name": "Название артефакта",
      "description": "Описание",
      "preview_image_url": "https://...",
      "artifact_media": [
        {
          "id": "uuid",
          "display_order": 0,
          "media": {
            "id": "uuid",
            "media_type": "3d_model",
            "url": "https://...",
            "metadata": {...}
          }
        }
      ]
    }
  }
]
```

#### POST /targets

Создание нового таргета.

**Тело запроса:**
```json
{
  "url": "https://...",
  "size_cm": 10,
  "artifact_id": "uuid"
}
```

#### PATCH /targets?id=eq.{targetId}

Обновление таргета.

**Тело запроса:**
```json
{
  "size_cm": 15
}
```

#### DELETE /targets?id=eq.{targetId}

Удаление таргета.

### Endpoints для работы с artifact_media

#### GET /artifact_media

Получение связей артефактов с медиа.

**Параметры запроса:**
- `artifact_id` - фильтр по артефакту (eq={artifactId})
- `select` - поля для выборки (можно включить связанные таблицы)

**Пример:**
```http
GET /rest/v1/artifact_media?select=*,media(*)&artifact_id=eq.{artifactId}&order=display_order.asc
```

#### POST /artifact_media

Создание связи артефакта с медиа.

**Тело запроса:**
```json
{
  "artifact_id": "uuid",
  "media_id": "uuid",
  "display_order": 0
}
```

#### PATCH /artifact_media?id=eq.{id}

Обновление связи (например, изменение порядка).

**Тело запроса:**
```json
{
  "display_order": 1
}
```

#### DELETE /artifact_media?id=eq.{id}

Удаление связи артефакта с медиа.

## Next.js API маршруты

### POST /api/upload

Загрузка файла в Vercel Blob Storage.

**Аутентификация:** Требуется авторизованный пользователь (Supabase Auth).

**Заголовки:**
```
Content-Type: application/json
Cookie: sb-<project-id>-auth-token=...
```

**Тело запроса:**
```json
{
  "pathname": "model.glb",
  "contentType": "model/gltf-binary",
  "contentLength": 1234567,
  "clientPayload": {}
}
```

**Ответ:**
```json
{
  "url": "https://...",
  "downloadUrl": "https://...",
  "pathname": "model.glb",
  "contentType": "model/gltf-binary",
  "contentLength": 1234567,
  "uploadedAt": "2024-01-01T00:00:00Z"
}
```

**Ошибки:**
- `401 Unauthorized` - пользователь не авторизован
- `400 Bad Request` - неверный тип файла или формат запроса

### POST /api/delete-blob

Удаление файла из Vercel Blob Storage.

**Аутентификация:** Требуется авторизованный пользователь.

**Тело запроса:**
```json
{
  "urls": [
    "https://...",
    "https://..."
  ]
}
```

**Ответ:**
```json
{
  "success": true,
  "deleted": 2
}
```

**Ошибки:**
- `401 Unauthorized` - пользователь не авторизован
- `400 Bad Request` - неверный формат запроса

## Примеры использования

### Получение артефакта с медиа и таргетами (Android)

```http
GET /rest/v1/targets?select=id,artifact_id,artifacts(*,artifact_media(media(*)))&id=eq.{targetId}
```

Этот запрос используется Android приложением для получения полной информации об артефакте при распознавании таргета.

### Создание артефакта с медиа (Web)

```http
# 1. Создание артефакта
POST /rest/v1/artifacts
{
  "name": "Название",
  "description": "Описание"
}

# 2. Загрузка медиа файла
POST /api/upload
{
  "pathname": "model.glb",
  "contentType": "model/gltf-binary",
  "contentLength": 1234567
}

# 3. Создание записи медиа
POST /rest/v1/media
{
  "media_type": "3d_model",
  "url": "https://...",
  "metadata": {...}
}

# 4. Связывание медиа с артефактом
POST /rest/v1/artifact_media
{
  "artifact_id": "uuid",
  "media_id": "uuid",
  "display_order": 0
}
```

### Обновление порядка медиа

```http
PATCH /rest/v1/artifact_media?id=eq.{id}
{
  "display_order": 1
}
```

## Row Level Security (RLS)

Все таблицы имеют RLS политики:

### Политики чтения (публичный доступ)
- Все могут читать активные артефакты (`is_active = true`)
- Все могут читать медиа, таргеты и связи

### Политики записи (только авторизованные)
- Только авторизованные пользователи могут создавать/обновлять/удалять данные
- Проверка через `auth.role() = 'authenticated'`

## Ограничения и лимиты

### Supabase
- Максимальный размер запроса: зависит от плана
- Rate limiting: зависит от плана
- Timeout запросов: 30 секунд

### Vercel Blob
- Максимальный размер файла: зависит от плана
- Поддерживаемые типы: все типы файлов
- Rate limiting: зависит от плана

## Обработка ошибок

### Коды ошибок Supabase

- `PGRST205` - таблица не найдена
- `23505` - нарушение уникального ограничения
- `23503` - нарушение внешнего ключа
- `42501` - недостаточно прав (RLS)

### Обработка в коде

```typescript
try {
  const { data, error } = await supabase
    .from("artifacts")
    .select("*")
    .eq("id", artifactId)
    .single();
    
  if (error) {
    if (error.code === "PGRST205") {
      // Таблица не существует
      return null;
    }
    throw error;
  }
  
  return data;
} catch (error) {
  console.error("Ошибка запроса:", error);
  throw error;
}
```


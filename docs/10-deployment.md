# Развертывание и конфигурация

## Обзор

Документ описывает процесс развертывания и настройки всех компонентов системы AR-tifact.

## Настройка Supabase

### 1. Создание проекта

1. Перейдите на [supabase.com](https://supabase.com)
2. Создайте новый проект
3. Запишите следующие данные:
   - Project URL: `https://<project-id>.supabase.co`
   - Anon Key: публичный ключ для клиентских приложений
   - Service Role Key: секретный ключ для серверных операций (храните в безопасности!)

### 2. Настройка базы данных

1. Откройте SQL Editor в Supabase Dashboard
2. Выполните миграции из папки `supabase/migrations/` в порядке:
   - `000_base_schema.sql`
   - `001_update_targets_schema.sql`
   - `002_add_display_order_to_artifact_media.sql`
   - `final_schema.sql`

3. Проверьте создание таблиц:
   ```sql
   SELECT table_name 
   FROM information_schema.tables 
   WHERE table_schema = 'public';
   ```

4. Проверьте RLS политики:
   ```sql
   SELECT tablename, policyname 
   FROM pg_policies 
   WHERE schemaname = 'public';
   ```

### 3. Настройка аутентификации

1. В Supabase Dashboard перейдите в Authentication > Settings
2. Настройте Email Auth (включено по умолчанию)
3. При необходимости настройте OAuth провайдеры
4. Настройте URL для редиректов после аутентификации

### 4. Переменные окружения

Для веб-сервиса добавьте в `.env.local`:
```env
NEXT_PUBLIC_SUPABASE_URL=https://<project-id>.supabase.co
NEXT_PUBLIC_SUPABASE_PUBLISHABLE_DEFAULT_KEY=<anon-key>
```

## Настройка Vercel Blob

### 1. Создание хранилища

1. Перейдите на [vercel.com](https://vercel.com)
2. Создайте новый проект или используйте существующий
3. Перейдите в Storage > Create Database
4. Выберите "Blob" и создайте хранилище
5. Запишите токен доступа

### 2. Настройка переменных окружения

Для веб-сервиса добавьте в `.env.local`:
```env
BLOB_READ_WRITE_TOKEN=<blob-token>
```

### 3. Настройка CORS (если необходимо)

Если требуется доступ к файлам из Android приложения напрямую, настройте CORS в Vercel Blob.

## Конфигурация Android приложения

### 1. Настройка Supabase конфигурации

1. В Unity откройте проект `android/`
2. Создайте ScriptableObject `SupabaseConfig`:
   - В меню: Assets > Create > ARArtifact > SupabaseConfig
   - Заполните поля:
     - `Supabase URL`: URL вашего Supabase проекта
     - `Supabase Anon Key`: Anon Key из Supabase

3. Сохраните конфигурацию в папку `Assets/Resources/` с именем `SupabaseConfig`

### 2. Настройка AR

1. Убедитесь, что установлены пакеты:
   - AR Foundation
   - ARCore XR Plugin (для Android)

2. В сцене добавьте:
   - AR Session
   - AR Session Origin
   - AR Tracked Image Manager

3. Настройте AR Tracked Image Manager:
   - Включите "Runtime Library"
   - Библиотека таргетов будет создана динамически

### 3. Настройка сборки

1. File > Build Settings
2. Выберите платформу Android
3. Настройте:
   - Minimum API Level: Android 7.0 (API level 24) или выше
   - Target API Level: Latest
   - Scripting Backend: IL2CPP
   - Target Architectures: ARM64

4. Player Settings:
   - Package Name: `com.yourcompany.artifact`
   - Minimum API Level: 24
   - Target API Level: Auto (highest installed)

### 4. Разрешения Android

В `AndroidManifest.xml` добавьте разрешения:
```xml
<uses-permission android:name="android.permission.CAMERA" />
<uses-permission android:name="android.permission.INTERNET" />
<uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />
<uses-feature android:name="android.hardware.camera.ar" android:required="true" />
```

## Развертывание веб-сервиса

### 1. Локальная разработка

1. Установите зависимости:
   ```bash
   cd site
   npm install
   ```

2. Создайте файл `.env.local`:
   ```env
   NEXT_PUBLIC_SUPABASE_URL=https://<project-id>.supabase.co
   NEXT_PUBLIC_SUPABASE_PUBLISHABLE_DEFAULT_KEY=<anon-key>
   BLOB_READ_WRITE_TOKEN=<blob-token>
   ```

3. Запустите dev сервер:
   ```bash
   npm run dev
   ```

4. Откройте [http://localhost:3000](http://localhost:3000)

### 2. Развертывание на Vercel

1. Подключите репозиторий к Vercel:
   - Перейдите на [vercel.com](https://vercel.com)
   - Import Project > выберите репозиторий
   - Root Directory: `site`

2. Настройте переменные окружения в Vercel:
   - `NEXT_PUBLIC_SUPABASE_URL`
   - `NEXT_PUBLIC_SUPABASE_PUBLISHABLE_DEFAULT_KEY`
   - `BLOB_READ_WRITE_TOKEN`

3. Настройте Build Command:
   ```
   npm run build
   ```

4. Настройте Output Directory:
   ```
   .next
   ```

5. Деплой:
   - Vercel автоматически задеплоит при push в основную ветку
   - Или нажмите "Deploy" вручную

### 3. Настройка домена (опционально)

1. В Vercel Dashboard перейдите в Settings > Domains
2. Добавьте ваш домен
3. Настройте DNS записи согласно инструкциям Vercel

## Переменные окружения

### Веб-сервис (.env.local)

```env
# Supabase
NEXT_PUBLIC_SUPABASE_URL=https://<project-id>.supabase.co
NEXT_PUBLIC_SUPABASE_PUBLISHABLE_DEFAULT_KEY=<anon-key>

# Vercel Blob
BLOB_READ_WRITE_TOKEN=<blob-token>

# Next.js (опционально)
NEXT_PUBLIC_APP_URL=http://localhost:3000
```

### Android приложение (SupabaseConfig)

```csharp
[CreateAssetMenu(fileName = "SupabaseConfig", menuName = "ARArtifact/SupabaseConfig")]
public class SupabaseConfig : ScriptableObject
{
    public string supabaseUrl = "https://<project-id>.supabase.co";
    public string supabaseAnonKey = "<anon-key>";
}
```

## Проверка развертывания

### Проверка Supabase

1. Проверьте доступность API:
   ```bash
   curl https://<project-id>.supabase.co/rest/v1/artifacts \
     -H "apikey: <anon-key>" \
     -H "Authorization: Bearer <anon-key>"
   ```

2. Проверьте аутентификацию:
   - Создайте тестового пользователя через веб-интерфейс
   - Попробуйте войти

### Проверка Vercel Blob

1. Загрузите тестовый файл через веб-интерфейс
2. Проверьте доступность файла по URL
3. Проверьте удаление файла

### Проверка Android приложения

1. Соберите APK:
   - File > Build Settings > Build
   - Установите на тестовое устройство

2. Проверьте:
   - Инициализация AR
   - Распознавание таргетов
   - Загрузка артефактов
   - Отображение 3D моделей/видео

### Проверка веб-сервиса

1. Откройте развернутый сайт
2. Проверьте:
   - Страница входа/регистрации
   - Создание артефакта
   - Загрузка медиа файлов
   - Загрузка таргетов
   - Редактирование артефактов

## Обновление системы

### Обновление базы данных

1. Создайте новую миграцию в `supabase/migrations/`
2. Выполните миграцию через SQL Editor в Supabase Dashboard
3. Проверьте изменения

### Обновление веб-сервиса

1. Внесите изменения в код
2. Запушьте изменения в репозиторий
3. Vercel автоматически задеплоит обновления

### Обновление Android приложения

1. Внесите изменения в Unity проект
2. Соберите новую версию APK
3. Обновите версию в Player Settings
4. Распространите обновление через Google Play или другой канал

## Резервное копирование

### База данных Supabase

1. В Supabase Dashboard перейдите в Database > Backups
2. Настройте автоматические бэкапы (доступно на платных планах)
3. Или экспортируйте данные вручную через SQL Editor

### Vercel Blob

1. Vercel Blob автоматически реплицирует данные
2. Для дополнительной защиты можно настроить регулярный экспорт файлов

### Android кеш

Кеш на устройствах пользователей не требует резервного копирования, так как данные синхронизируются с сервером.

## Мониторинг и логирование

### Supabase

1. В Dashboard перейдите в Logs для просмотра запросов
2. Настройте алерты для ошибок
3. Мониторьте использование ресурсов

### Vercel

1. В Dashboard перейдите в Analytics для просмотра метрик
2. Проверяйте логи функций в Functions
3. Мониторьте использование хранилища

### Android приложение

1. Используйте Unity Console для просмотра логов
2. Настройте удаленное логирование при необходимости
3. Собирайте crash reports через Firebase Crashlytics или аналогичный сервис

## Безопасность

### Рекомендации

1. **Никогда не коммитьте секретные ключи** в репозиторий
2. Используйте разные ключи для development и production
3. Регулярно обновляйте зависимости
4. Настройте CORS правильно для API
5. Используйте HTTPS везде
6. Регулярно проверяйте логи на подозрительную активность

### Проверка безопасности

1. Проверьте RLS политики в Supabase
2. Убедитесь, что Service Role Key не используется в клиентском коде
3. Проверьте права доступа к Vercel Blob
4. Настройте rate limiting при необходимости


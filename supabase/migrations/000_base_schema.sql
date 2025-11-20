-- Таблица для хранения артефактов
CREATE TABLE artifacts (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  name TEXT NOT NULL,
  description TEXT, -- markdown описание
  preview_image_url TEXT, -- превью изображение артефакта
  created_at TIMESTAMPTZ DEFAULT NOW(),
  updated_at TIMESTAMPTZ DEFAULT NOW(),
  is_active BOOLEAN DEFAULT true
);

-- Таблица для хранения медиа-ресурсов (3D модели, видео)
CREATE TABLE media (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  media_type TEXT NOT NULL CHECK (media_type IN ('3d_model', 'video', 'youtube')),
  url TEXT NOT NULL,
  metadata JSONB, -- дополнительные данные (размер файла, длительность, размеры модели и т.д.)
  created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Таблица для хранения таргетов (маркеров)
CREATE TABLE targets (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  url TEXT NOT NULL, -- ссылка на изображение-таргет
  created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Таблица для связи артефактов с медиа-ресурсами
CREATE TABLE artifact_media (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  artifact_id UUID NOT NULL REFERENCES artifacts(id) ON DELETE CASCADE,
  media_id UUID NOT NULL REFERENCES media(id) ON DELETE CASCADE,
  created_at TIMESTAMPTZ DEFAULT NOW(),
  UNIQUE(artifact_id, media_id) -- один артефакт может быть связан с медиа только один раз
);

-- Таблица для связи артефактов с таргетами (маркерами)
CREATE TABLE artifact_targets (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  artifact_id UUID NOT NULL REFERENCES artifacts(id) ON DELETE CASCADE,
  target_id UUID NOT NULL REFERENCES targets(id) ON DELETE CASCADE,
  display_priority INTEGER DEFAULT 0, -- приоритет отображения, 0 - наивысший
  created_at TIMESTAMPTZ DEFAULT NOW(),
  UNIQUE(artifact_id, target_id) -- один артефакт может быть привязан к таргету только один раз
);

-- Индексы для быстрого поиска
-- Пока без индексов

-- Функция для автоматического обновления updated_at
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
  NEW.updated_at = NOW();
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Триггер для автоматического обновления updated_at
CREATE TRIGGER update_artifacts_updated_at
  BEFORE UPDATE ON artifacts
  FOR EACH ROW
  EXECUTE FUNCTION update_updated_at_column();

-- RLS политики (Row Level Security)
-- TL;DR: anon - readonly, auth - CRUD
ALTER TABLE artifacts ENABLE ROW LEVEL SECURITY;
ALTER TABLE media ENABLE ROW LEVEL SECURITY;
ALTER TABLE targets ENABLE ROW LEVEL SECURITY;
ALTER TABLE artifact_media ENABLE ROW LEVEL SECURITY;
ALTER TABLE artifact_targets ENABLE ROW LEVEL SECURITY;

-- Политики для чтения (публичный доступ)
CREATE POLICY "Artifacts are viewable by everyone"
  ON artifacts FOR SELECT
  USING (is_active = true);

-- Авторизованные пользователи могут видеть все артефакты (включая неактивные)
CREATE POLICY "Authenticated users can view all artifacts"
  ON artifacts FOR SELECT
  USING (auth.role() = 'authenticated');

CREATE POLICY "Media are viewable by everyone"
  ON media FOR SELECT
  USING (true);

CREATE POLICY "Targets are viewable by everyone"
  ON targets FOR SELECT
  USING (true);

CREATE POLICY "Artifact media are viewable by everyone"
  ON artifact_media FOR SELECT
  USING (true);

CREATE POLICY "Artifact targets are viewable by everyone"
  ON artifact_targets FOR SELECT
  USING (true);

-- Политики для записи (только для аутентифицированных пользователей)
-- В будущем можно добавить проверку ролей
CREATE POLICY "Authenticated users can insert artifacts"
  ON artifacts FOR INSERT
  WITH CHECK (auth.role() = 'authenticated');

CREATE POLICY "Authenticated users can update artifacts"
  ON artifacts FOR UPDATE
  USING (auth.role() = 'authenticated');

CREATE POLICY "Authenticated users can delete artifacts"
  ON artifacts FOR DELETE
  USING (auth.role() = 'authenticated');

CREATE POLICY "Authenticated users can insert media"
  ON media FOR INSERT
  WITH CHECK (auth.role() = 'authenticated');

CREATE POLICY "Authenticated users can update media"
  ON media FOR UPDATE
  USING (auth.role() = 'authenticated');

CREATE POLICY "Authenticated users can delete media"
  ON media FOR DELETE
  USING (auth.role() = 'authenticated');

CREATE POLICY "Authenticated users can insert targets"
  ON targets FOR INSERT
  WITH CHECK (auth.role() = 'authenticated');

CREATE POLICY "Authenticated users can update targets"
  ON targets FOR UPDATE
  USING (auth.role() = 'authenticated');

CREATE POLICY "Authenticated users can delete targets"
  ON targets FOR DELETE
  USING (auth.role() = 'authenticated');

CREATE POLICY "Authenticated users can insert artifact media"
  ON artifact_media FOR INSERT
  WITH CHECK (auth.role() = 'authenticated');

CREATE POLICY "Authenticated users can update artifact media"
  ON artifact_media FOR UPDATE
  USING (auth.role() = 'authenticated');

CREATE POLICY "Authenticated users can delete artifact media"
  ON artifact_media FOR DELETE
  USING (auth.role() = 'authenticated');

CREATE POLICY "Authenticated users can insert artifact targets"
  ON artifact_targets FOR INSERT
  WITH CHECK (auth.role() = 'authenticated');

CREATE POLICY "Authenticated users can update artifact targets"
  ON artifact_targets FOR UPDATE
  USING (auth.role() = 'authenticated');

CREATE POLICY "Authenticated users can delete artifact targets"
  ON artifact_targets FOR DELETE
  USING (auth.role() = 'authenticated');


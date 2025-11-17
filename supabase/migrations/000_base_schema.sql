-- Таблица для хранения артефактов
CREATE TABLE artifacts (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  name TEXT NOT NULL,
  description TEXT, -- markdown описание
  created_at TIMESTAMPTZ DEFAULT NOW(),
  updated_at TIMESTAMPTZ DEFAULT NOW(),
  is_active BOOLEAN DEFAULT true
);

-- Таблица для хранения медиа-ресурсов (3D модели, видео)
CREATE TABLE artifact_media (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  artifact_id UUID NOT NULL REFERENCES artifacts(id) ON DELETE CASCADE,
  media_type TEXT NOT NULL CHECK (media_type IN ('3d_model', 'video', 'youtube')),
  url TEXT NOT NULL,
  thumbnail_url TEXT, -- превью для видео/моделей
  metadata JSONB, -- дополнительные данные (размер файла, длительность, размеры модели и т.д.)
  created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Таблица для связи артефактов с таргетами (маркерами)
CREATE TABLE artifact_targets (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  artifact_id UUID NOT NULL REFERENCES artifacts(id) ON DELETE CASCADE,
  target_name TEXT NOT NULL, -- имя/идентификатор таргета (например, "marker_1", "image_abc123")
  display_priority INTEGER DEFAULT 0, -- приоритет отображения, 0 - наивысший
  created_at TIMESTAMPTZ DEFAULT NOW(),
  UNIQUE(artifact_id, target_name) -- один артефакт может быть привязан к таргету только один раз
);

-- Индексы для быстрого поиска
CREATE INDEX idx_artifact_media_artifact_id ON artifact_media(artifact_id);
CREATE INDEX idx_artifact_targets_artifact_id ON artifact_targets(artifact_id);
CREATE INDEX idx_artifact_targets_target_name ON artifact_targets(target_name);
CREATE INDEX idx_artifacts_active ON artifacts(is_active) WHERE is_active = true;

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
ALTER TABLE artifacts ENABLE ROW LEVEL SECURITY;
ALTER TABLE artifact_media ENABLE ROW LEVEL SECURITY;
ALTER TABLE artifact_targets ENABLE ROW LEVEL SECURITY;

-- Политики для чтения (публичный доступ)
CREATE POLICY "Artifacts are viewable by everyone"
  ON artifacts FOR SELECT
  USING (is_active = true);

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

CREATE POLICY "Authenticated users can insert media"
  ON artifact_media FOR INSERT
  WITH CHECK (auth.role() = 'authenticated');

CREATE POLICY "Authenticated users can update media"
  ON artifact_media FOR UPDATE
  USING (auth.role() = 'authenticated');

CREATE POLICY "Authenticated users can insert targets"
  ON artifact_targets FOR INSERT
  WITH CHECK (auth.role() = 'authenticated');

CREATE POLICY "Authenticated users can update targets"
  ON artifact_targets FOR UPDATE
  USING (auth.role() = 'authenticated');


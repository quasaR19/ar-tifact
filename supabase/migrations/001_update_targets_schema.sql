-- Добавляем поле размера в см (по умолчанию 10)
ALTER TABLE targets ADD COLUMN size_cm INTEGER DEFAULT 10;

-- Добавляем связь с артефактом (1:N)
ALTER TABLE targets ADD COLUMN artifact_id UUID REFERENCES artifacts(id) ON DELETE CASCADE;

-- Миграция данных из таблицы связи artifact_targets в targets
UPDATE targets
SET artifact_id = artifact_targets.artifact_id
FROM artifact_targets
WHERE targets.id = artifact_targets.target_id;

-- (Опционально) Удаляем дубликаты таргетов, если они были привязаны к нескольким артефактам
-- В текущей логике мы просто взяли первый попавшийся artifact_id из UPDATE
-- Если есть таргеты, привязанные к нескольким артефактам, они теперь привязаны только к одному.

-- Удаляем старую таблицу связи
DROP TABLE artifact_targets;

-- Обновляем RLS политики для targets

-- Удаляем старые политики
DROP POLICY IF EXISTS "Targets are viewable by everyone" ON targets;
DROP POLICY IF EXISTS "Authenticated users can insert targets" ON targets;
DROP POLICY IF EXISTS "Authenticated users can update targets" ON targets;
DROP POLICY IF EXISTS "Authenticated users can delete targets" ON targets;

-- Создаем новые политики
CREATE POLICY "Targets are viewable by everyone"
  ON targets FOR SELECT
  USING (true);

CREATE POLICY "Authenticated users can insert targets"
  ON targets FOR INSERT
  WITH CHECK (auth.role() = 'authenticated');

CREATE POLICY "Authenticated users can update targets"
  ON targets FOR UPDATE
  USING (auth.role() = 'authenticated');

CREATE POLICY "Authenticated users can delete targets"
  ON targets FOR DELETE
  USING (auth.role() = 'authenticated');




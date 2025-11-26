-- Добавляем поле display_order для управления порядком отображения медиа
-- 0 - наивысший приоритет
ALTER TABLE artifact_media ADD COLUMN display_order INTEGER DEFAULT 0;

-- Комментарий к полю
COMMENT ON COLUMN artifact_media.display_order IS '0 - наивысший приоритет';

-- Устанавливаем начальные значения для существующих записей на основе created_at
-- Более старые записи получают больший display_order
UPDATE artifact_media
SET display_order = subquery.row_number - 1
FROM (
  SELECT 
    id,
    ROW_NUMBER() OVER (PARTITION BY artifact_id ORDER BY created_at ASC) as row_number
  FROM artifact_media
) AS subquery
WHERE artifact_media.id = subquery.id;


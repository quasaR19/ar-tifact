export interface Artifact {
  id: string;
  name: string;
  description: string | null;
  preview_image_url: string | null;
  created_at: string;
  updated_at: string;
  is_active: boolean;
}

// Медиа-ресурс
export interface Media {
  id: string;
  media_type: "3d_model" | "video" | "youtube";
  url: string;
  metadata: Record<string, unknown> | null;
  created_at: string;
}

// Таргет (маркер)
export interface Target {
  id: string;
  url: string;
  created_at: string;
}

// Связь артефакта с медиа (с полной информацией о медиа)
export interface ArtifactMedia {
  id: string; // ID связи из artifact_media
  artifact_id: string;
  media_id: string;
  media: Media; // полная информация о медиа
  created_at: string;
}

// Связь артефакта с таргетом (с полной информацией о таргете)
export interface ArtifactTarget {
  id: string; // ID связи из artifact_targets
  artifact_id: string;
  target_id: string;
  target: Target; // полная информация о таргете
  display_priority: number;
  created_at: string;
}

// Типы для данных из Supabase (сырые данные из БД)
export interface ArtifactDb {
  id: string;
  name: string;
  description: string | null;
  preview_image_url: string | null;
  created_at: string;
  updated_at: string;
  is_active: boolean;
}

// Типы для данных из БД
export interface MediaDb {
  id: string;
  media_type: "3d_model" | "video" | "youtube";
  url: string;
  metadata: Record<string, unknown> | null;
  created_at: string;
}

export interface TargetDb {
  id: string;
  url: string;
  created_at: string;
}

export interface ArtifactMediaLinkDb {
  id: string;
  artifact_id: string;
  media_id: string;
  created_at: string;
}

export interface ArtifactTargetLinkDb {
  id: string;
  artifact_id: string;
  target_id: string;
  display_priority: number;
  created_at: string;
}

// Типы для JOIN запросов (с вложенными объектами)
export interface ArtifactMediaDb {
  id: string;
  artifact_id: string;
  media_id: string;
  created_at: string;
  media: MediaDb;
}

export interface ArtifactTargetDb {
  id: string;
  artifact_id: string;
  target_id: string;
  display_priority: number;
  created_at: string;
  targets: TargetDb; // Supabase возвращает данные под именем таблицы
}

/**
 * Конвертация артефакта из формата БД в TypeScript тип
 */
export function convertArtifactFromDb(db: ArtifactDb): Artifact {
  return {
    id: db.id,
    name: db.name,
    description: db.description,
    preview_image_url: db.preview_image_url,
    created_at: db.created_at,
    updated_at: db.updated_at,
    is_active: db.is_active,
  };
}

/**
 * Конвертация артефакта из TypeScript типа в формат БД
 */
export function convertArtifactToDb(artifact: Artifact): ArtifactDb {
  return {
    id: artifact.id,
    name: artifact.name,
    description: artifact.description,
    preview_image_url: artifact.preview_image_url,
    created_at: artifact.created_at,
    updated_at: artifact.updated_at,
    is_active: artifact.is_active,
  };
}

/**
 * Конвертация медиа из формата БД в TypeScript тип
 */
export function convertMediaFromDb(db: MediaDb): Media {
  return {
    id: db.id,
    media_type: db.media_type,
    url: db.url,
    metadata: db.metadata,
    created_at: db.created_at,
  };
}

/**
 * Конвертация таргета из формата БД в TypeScript тип
 */
export function convertTargetFromDb(db: TargetDb): Target {
  return {
    id: db.id,
    url: db.url,
    created_at: db.created_at,
  };
}

/**
 * Конвертация связи артефакта с медиа из формата БД в TypeScript тип
 */
export function convertArtifactMediaFromDb(db: ArtifactMediaDb): ArtifactMedia {
  return {
    id: db.id,
    artifact_id: db.artifact_id,
    media_id: db.media_id,
    media: convertMediaFromDb(db.media),
    created_at: db.created_at,
  };
}

/**
 * Конвертация связи артефакта с таргетом из формата БД в TypeScript тип
 */
export function convertArtifactTargetFromDb(
  db: ArtifactTargetDb
): ArtifactTarget {
  return {
    id: db.id,
    artifact_id: db.artifact_id,
    target_id: db.target_id,
    target: convertTargetFromDb(db.targets),
    display_priority: db.display_priority,
    created_at: db.created_at,
  };
}

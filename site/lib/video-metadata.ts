/**
 * Утилиты для извлечения и работы с метаданными видео
 */

export interface VideoMetadata {
  width: number;
  height: number;
  duration: number;
  filename?: string;
  size?: number;
}

/**
 * Преобразует VideoMetadata в Record<string, unknown> для сохранения в БД
 */
export function videoMetadataToRecord(
  metadata: VideoMetadata
): Record<string, unknown> {
  const record: Record<string, unknown> = {
    width: metadata.width,
    height: metadata.height,
    duration: metadata.duration,
  };
  
  if (metadata.filename !== undefined) {
    record.filename = metadata.filename;
  }
  
  if (metadata.size !== undefined) {
    record.size = metadata.size;
  }
  
  return record;
}

/**
 * Извлекает метаданные видео из файла (размеры и длительность)
 * @param file - Файл видео
 * @returns Promise с метаданными видео
 */
export async function extractVideoMetadata(
  file: File
): Promise<VideoMetadata> {
  return new Promise((resolve, reject) => {
    const video = document.createElement("video");
    const url = URL.createObjectURL(file);

    video.addEventListener("loadedmetadata", () => {
      URL.revokeObjectURL(url);

      const metadata: VideoMetadata = {
        width: video.videoWidth,
        height: video.videoHeight,
        duration: video.duration,
        filename: file.name,
        size: file.size,
      };

      resolve(metadata);
    });

    video.addEventListener("error", (e) => {
      URL.revokeObjectURL(url);
      reject(
        new Error(
          `Не удалось загрузить видео для извлечения метаданных: ${e.message || "Неизвестная ошибка"}`
        )
      );
    });

    video.preload = "metadata";
    video.src = url;
  });
}

/**
 * Извлекает метаданные видео из URL
 * @param url - URL видео
 * @returns Promise с метаданными видео
 */
export async function extractVideoMetadataFromUrl(
  url: string
): Promise<VideoMetadata> {
  return new Promise((resolve, reject) => {
    const video = document.createElement("video");

    video.addEventListener("loadedmetadata", () => {
      const metadata: VideoMetadata = {
        width: video.videoWidth,
        height: video.videoHeight,
        duration: video.duration,
      };

      resolve(metadata);
    });

    video.addEventListener("error", (e) => {
      reject(
        new Error(
          `Не удалось загрузить видео для извлечения метаданных: ${e.message || "Неизвестная ошибка"}`
        )
      );
    });

    video.preload = "metadata";
    video.crossOrigin = "anonymous";
    video.src = url;
  });
}

/**
 * Проверяет, есть ли у медиа полные метаданные видео (width, height, duration)
 * @param metadata - Метаданные медиа
 * @returns true, если все необходимые метаданные присутствуют
 */
export function hasCompleteVideoMetadata(
  metadata: Record<string, unknown> | null | undefined
): boolean {
  if (!metadata) return false;

  return (
    typeof metadata.width === "number" &&
    typeof metadata.height === "number" &&
    typeof metadata.duration === "number" &&
    metadata.width > 0 &&
    metadata.height > 0 &&
    metadata.duration > 0
  );
}

/**
 * Проверяет и обновляет метаданные видео для медиа, если они отсутствуют
 * @param media - Медиа элемент
 * @param supabaseClient - Клиент Supabase
 * @returns Promise с обновленными метаданными или null, если обновление не требуется
 */
export async function checkAndUpdateVideoMetadata(
  media: { type: string; url?: string; id?: string },
  supabaseClient: any
): Promise<VideoMetadata | null> {
  // Проверяем только видео медиа
  if (media.type !== "video") {
    return null;
  }

  // Если нет URL, не можем извлечь метаданные
  if (!media.url) {
    return null;
  }

  // Если нет ID, не можем обновить в БД
  if (!media.id) {
    return null;
  }

  try {
    // Пытаемся извлечь метаданные из URL
    const metadata = await extractVideoMetadataFromUrl(media.url);
    
    // Обновляем в БД
    const { updateArtifactMediaMetadata } = await import("@/lib/queries");
    await updateArtifactMediaMetadata(
      supabaseClient,
      media.id,
      metadata
    );

    return metadata;
  } catch (error) {
    console.error(
      "[checkAndUpdateVideoMetadata] Ошибка при извлечении метаданных:",
      error
    );
    return null;
  }
}

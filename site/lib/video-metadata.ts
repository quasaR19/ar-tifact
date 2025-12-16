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

    // Проверяем формат файла перед попыткой загрузки
    const extension = file.name.split(".").pop()?.toLowerCase();
    const unsupportedFormats = ["avi", "mkv", "flv", "wmv"];
    
    if (extension && unsupportedFormats.includes(extension)) {
      URL.revokeObjectURL(url);
      reject(
        new Error(
          `Формат ${extension.toUpperCase()} не поддерживается браузером. HTML5 video элемент поддерживает только MP4, WebM и некоторые другие форматы. Пожалуйста, конвертируйте видео в поддерживаемый формат (например, MP4).`
        )
      );
      return;
    }

    // Устанавливаем таймаут на случай, если событие не сработает
    const timeout = setTimeout(() => {
      URL.revokeObjectURL(url);
      reject(
        new Error(
          `Таймаут при загрузке метаданных видео. Возможно, формат файла не поддерживается или файл поврежден.`
        )
      );
    }, 10000); // 10 секунд

    video.addEventListener("loadedmetadata", () => {
      clearTimeout(timeout);
      URL.revokeObjectURL(url);

      // Проверяем, что метаданные действительно загрузились
      if (video.videoWidth === 0 || video.videoHeight === 0 || isNaN(video.duration)) {
        reject(
          new Error(
            `Не удалось извлечь метаданные видео: некорректные размеры или длительность. Возможно, формат файла не полностью поддерживается браузером.`
          )
        );
        return;
      }

      const metadata: VideoMetadata = {
        width: video.videoWidth,
        height: video.videoHeight,
        duration: video.duration,
        filename: file.name,
        size: file.size,
      };

      resolve(metadata);
    }, { once: true });

    video.addEventListener("error", () => {
      clearTimeout(timeout);
      URL.revokeObjectURL(url);
      
      // Получаем детальную информацию об ошибке
      const error = video.error;
      let errorMessage = "Неизвестная ошибка при загрузке видео";
      
      if (error) {
        // Коды ошибок MediaError согласно HTML стандарту
        // MEDIA_ERR_ABORTED = 1
        // MEDIA_ERR_NETWORK = 2
        // MEDIA_ERR_DECODE = 3
        // MEDIA_ERR_SRC_NOT_SUPPORTED = 4
        switch (error.code) {
          case 1: // MEDIA_ERR_ABORTED
            errorMessage = "Загрузка видео была прервана";
            break;
          case 2: // MEDIA_ERR_NETWORK
            errorMessage = "Ошибка сети при загрузке видео";
            break;
          case 3: // MEDIA_ERR_DECODE
            errorMessage = "Ошибка декодирования видео. Возможно, файл поврежден или использует неподдерживаемый кодек";
            break;
          case 4: // MEDIA_ERR_SRC_NOT_SUPPORTED
            errorMessage = `Формат видео не поддерживается браузером. Файл "${file.name}" использует формат, который не может быть воспроизведен HTML5 video элементом. Рекомендуется использовать MP4 (H.264) или WebM формат.`;
            break;
          default:
            errorMessage = error.message || `Ошибка загрузки видео (код: ${error.code})`;
        }
      }
      
      reject(new Error(errorMessage));
    }, { once: true });

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

    // Устанавливаем таймаут на случай, если событие не сработает
    const timeout = setTimeout(() => {
      reject(
        new Error(
          `Таймаут при загрузке метаданных видео из URL. Возможно, формат видео не поддерживается или ресурс недоступен.`
        )
      );
    }, 10000); // 10 секунд

    video.addEventListener("loadedmetadata", () => {
      clearTimeout(timeout);

      // Проверяем, что метаданные действительно загрузились
      if (video.videoWidth === 0 || video.videoHeight === 0 || isNaN(video.duration)) {
        reject(
          new Error(
            `Не удалось извлечь метаданные видео: некорректные размеры или длительность. Возможно, формат файла не полностью поддерживается браузером.`
          )
        );
        return;
      }

      const metadata: VideoMetadata = {
        width: video.videoWidth,
        height: video.videoHeight,
        duration: video.duration,
      };

      resolve(metadata);
    }, { once: true });

    video.addEventListener("error", () => {
      clearTimeout(timeout);
      
      // Получаем детальную информацию об ошибке
      const error = video.error;
      let errorMessage = "Неизвестная ошибка при загрузке видео";
      
      if (error) {
        // Коды ошибок MediaError согласно HTML стандарту
        // MEDIA_ERR_ABORTED = 1
        // MEDIA_ERR_NETWORK = 2
        // MEDIA_ERR_DECODE = 3
        // MEDIA_ERR_SRC_NOT_SUPPORTED = 4
        switch (error.code) {
          case 1: // MEDIA_ERR_ABORTED
            errorMessage = "Загрузка видео была прервана";
            break;
          case 2: // MEDIA_ERR_NETWORK
            errorMessage = "Ошибка сети при загрузке видео";
            break;
          case 3: // MEDIA_ERR_DECODE
            errorMessage = "Ошибка декодирования видео. Возможно, файл поврежден или использует неподдерживаемый кодек";
            break;
          case 4: // MEDIA_ERR_SRC_NOT_SUPPORTED
            errorMessage = `Формат видео не поддерживается браузером. URL использует формат, который не может быть воспроизведен HTML5 video элементом. Рекомендуется использовать MP4 (H.264) или WebM формат.`;
            break;
          default:
            errorMessage = error.message || `Ошибка загрузки видео (код: ${error.code})`;
        }
      }
      
      reject(new Error(errorMessage));
    }, { once: true });

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
      videoMetadataToRecord(metadata)
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

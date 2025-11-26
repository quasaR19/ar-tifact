import type {
  Artifact,
  ArtifactDb,
  ArtifactMedia,
  ArtifactMediaDb,
  Target,
  TargetDb,
} from "@/lib/types/artifact";
import {
  convertArtifactFromDb,
  convertArtifactMediaFromDb,
  convertTargetFromDb,
} from "@/lib/types/artifact";
import type { SupabaseClient } from "@supabase/supabase-js";

export interface PaginatedArtifacts {
  artifacts: Artifact[];
  total: number;
  page: number;
  pageSize: number;
  hasMore: boolean;
}

export interface ArtifactWithDetails extends Artifact {
  media: ArtifactMedia[];
  targets: Target[];
}

/**
 * Получение артефактов постранично
 * @param supabaseClient - клиент Supabase (обязательный)
 * @param page - номер страницы (начиная с 1)
 * @param pageSize - размер страницы
 * @returns объект с артефактами и метаданными пагинации
 */
export async function getArtifactsPaginated(
  supabaseClient: SupabaseClient,
  page: number = 1,
  pageSize: number = 20
): Promise<PaginatedArtifacts> {
  const supabase = supabaseClient;
  const from = (page - 1) * pageSize;
  const to = from + pageSize - 1;

  // Получаем общее количество артефактов
  const { count, error: countError } = await supabase
    .from("artifacts")
    .select("*", { count: "exact", head: true })
    .eq("is_active", true);

  // Получаем артефакты для текущей страницы
  const { data, error } = await supabase
    .from("artifacts")
    .select("*")
    .eq("is_active", true)
    .order("created_at", { ascending: false })
    .range(from, to);

  // Если таблица не существует или другая ошибка - возвращаем пустой результат
  if (error || countError) {
    // Проверяем, является ли это ошибкой отсутствия таблицы
    const isTableNotFound =
      error?.code === "PGRST205" || countError?.code === "PGRST205";

    if (isTableNotFound) {
      // Возвращаем пустой результат вместо ошибки
      return {
        artifacts: [],
        total: 0,
        page,
        pageSize,
        hasMore: false,
      };
    }

    // Для других ошибок все еще выбрасываем исключение
    throw error || countError;
  }

  const total = count ?? 0;
  const artifacts = (data ?? []).map((item) =>
    convertArtifactFromDb(item as ArtifactDb)
  );

  return {
    artifacts,
    total,
    page,
    pageSize,
    hasMore: to < total - 1,
  };
}

/**
 * Получение полной информации об артефакте (с медиа и таргетами)
 * @param supabaseClient - клиент Supabase (обязательный)
 * @param artifactId - ID артефакта
 * @param includeInactive - включить неактивные артефакты (по умолчанию false)
 * @returns артефакт с медиа и таргетами или null, если не найден
 */
export async function getArtifactById(
  supabaseClient: SupabaseClient,
  artifactId: string,
  includeInactive: boolean = false
): Promise<ArtifactWithDetails | null> {
  const supabase = supabaseClient;

  // Получаем артефакт
  let query = supabase.from("artifacts").select("*").eq("id", artifactId);

  if (!includeInactive) {
    query = query.eq("is_active", true);
  }

  const { data: artifactData, error: artifactError } = await query.single();

  // Если таблица не существует - возвращаем null
  if (artifactError?.code === "PGRST205") {
    return null;
  }

  if (artifactError || !artifactData) {
    return null;
  }

  // Получаем медиа-ресурсы через JOIN с таблицей media
  const { data: mediaData, error: mediaError } = await supabase
    .from("artifact_media")
    .select("*, media(*)")
    .eq("artifact_id", artifactId)
    .order("created_at", { ascending: true });

  // Если таблица не существует - используем пустой массив
  if (mediaError?.code === "PGRST205") {
    // Продолжаем с пустым массивом медиа
  } else if (mediaError) {
    throw mediaError;
  }

  // Получаем таргеты из таблицы targets
  const { data: targetsData, error: targetsError } = await supabase
    .from("targets")
    .select("*")
    .eq("artifact_id", artifactId)
    .order("created_at", { ascending: true });

  // Если таблица не существует - используем пустой массив
  if (targetsError?.code === "PGRST205") {
    // Продолжаем с пустым массивом таргетов
  } else if (targetsError) {
    throw targetsError;
  }

  return {
    ...convertArtifactFromDb(artifactData as ArtifactDb),
    media: (mediaData ?? []).map((item: ArtifactMediaDb) =>
      convertArtifactMediaFromDb(item)
    ),
    targets: (targetsData ?? []).map((item: TargetDb) =>
      convertTargetFromDb(item)
    ),
  };
}

/**
 * Создание нового артефакта
 * @param supabaseClient - клиент Supabase (обязательный)
 * @param name - название артефакта
 * @param description - описание артефакта (опционально)
 * @returns созданный артефакт
 */
export async function createArtifact(
  supabaseClient: SupabaseClient,
  name: string,
  description: string | null = null
): Promise<Artifact> {
  const supabase = supabaseClient;

  const { data, error } = await supabase
    .from("artifacts")
    .insert({
      name,
      description,
      is_active: true,
    })
    .select()
    .single();

  if (error) {
    throw error;
  }

  return convertArtifactFromDb(data as ArtifactDb);
}

/**
 * Обновление артефакта
 * @param supabaseClient - клиент Supabase (обязательный)
 * @param artifactId - ID артефакта
 * @param updates - объект с обновляемыми полями
 * @returns обновленный артефакт
 */
export async function updateArtifact(
  supabaseClient: SupabaseClient,
  artifactId: string,
  updates: {
    name?: string;
    description?: string | null;
    preview_image_url?: string | null;
  }
): Promise<Artifact> {
  const supabase = supabaseClient;

  const { data, error } = await supabase
    .from("artifacts")
    .update(updates)
    .eq("id", artifactId)
    .select()
    .single();

  if (error) {
    throw error;
  }

  return convertArtifactFromDb(data as ArtifactDb);
}

/**
 * Удаление артефакта (полное удаление из БД)
 * @param supabaseClient - клиент Supabase (обязательный)
 * @param artifactId - ID артефакта
 * @returns объект с URL файлов для удаления из Blob Storage
 */
export async function deleteArtifact(
  supabaseClient: SupabaseClient,
  artifactId: string
): Promise<{
  previewImageUrl: string | null;
  mediaUrls: string[];
  targetUrls: string[];
}> {
  const supabase = supabaseClient;

  // Получаем данные артефакта перед удалением
  const { data: artifactData, error: artifactError } = await supabase
    .from("artifacts")
    .select("preview_image_url")
    .eq("id", artifactId)
    .single();

  if (artifactError) {
    throw artifactError;
  }

  // Получаем все медиа-файлы артефакта
  const { data: mediaData, error: mediaError } = await supabase
    .from("artifact_media")
    .select("media_id, media(url)")
    .eq("artifact_id", artifactId);

  if (mediaError) {
    console.error("[deleteArtifact] Ошибка при получении медиа:", mediaError);
    // Продолжаем удаление даже при ошибке получения медиа
  }

  // Получаем все таргеты артефакта
  const { data: targetsData, error: targetsError } = await supabase
    .from("targets")
    .select("url")
    .eq("artifact_id", artifactId);

  if (targetsError) {
    console.error("[deleteArtifact] Ошибка при получении таргетов:", targetsError);
    // Продолжаем удаление даже при ошибке получения таргетов
  }

  // Собираем URL для удаления
  const mediaUrls: string[] = [];
  if (mediaData) {
    for (const item of mediaData) {
      // @ts-ignore
      const url = item.media?.url;
      if (url) {
        mediaUrls.push(url);
      }
    }
  }

  const targetUrls: string[] = (targetsData || []).map((t) => t.url).filter(Boolean);

  // Удаляем связи медиа с артефактом
  const { error: mediaLinksError } = await supabase
    .from("artifact_media")
    .delete()
    .eq("artifact_id", artifactId);

  if (mediaLinksError) {
    console.error(
      "[deleteArtifact] Ошибка при удалении связей медиа:",
      mediaLinksError
    );
    // Продолжаем удаление
  }

  // Удаляем медиа-ресурсы, которые больше не используются
  if (mediaData) {
    for (const item of mediaData) {
      const mediaId = item.media_id;
      // Проверяем, используется ли медиа другими артефактами
      const { data: otherLinks } = await supabase
        .from("artifact_media")
        .select("id")
        .eq("media_id", mediaId)
        .limit(1);

      if (!otherLinks || otherLinks.length === 0) {
        // Медиа больше не используется, удаляем его
        const { error: mediaDeleteError } = await supabase
          .from("media")
          .delete()
          .eq("id", mediaId);

        if (mediaDeleteError) {
          console.error(
            "[deleteArtifact] Ошибка при удалении медиа:",
            mediaDeleteError
          );
        }
      }
    }
  }

  // Удаляем таргеты
  const { error: targetsDeleteError } = await supabase
    .from("targets")
    .delete()
    .eq("artifact_id", artifactId);

  if (targetsDeleteError) {
    console.error(
      "[deleteArtifact] Ошибка при удалении таргетов:",
      targetsDeleteError
    );
    // Продолжаем удаление
  }

  // Удаляем сам артефакт
  const { error } = await supabase
    .from("artifacts")
    .delete()
    .eq("id", artifactId);

  if (error) {
    throw error;
  }

  return {
    previewImageUrl: artifactData?.preview_image_url || null,
    mediaUrls,
    targetUrls,
  };
}

/**
 * Добавление медиа к артефакту
 * @param supabaseClient - клиент Supabase (обязательный)
 * @param artifactId - ID артефакта
 * @param mediaType - тип медиа
 * @param url - URL медиа
 * @param metadata - дополнительные метаданные (опционально)
 * @returns созданная связь артефакта с медиа
 */
export async function addArtifactMedia(
  supabaseClient: SupabaseClient,
  artifactId: string,
  mediaType: "3d_model" | "video" | "youtube",
  url: string,
  metadata: Record<string, unknown> | null = null
): Promise<ArtifactMedia> {
  const supabase = supabaseClient;

  console.log("[addArtifactMedia] Параметры:", {
    artifactId,
    mediaType,
    url,
    metadata,
  });

  // Сначала создаем медиа-ресурс
  const { data: mediaData, error: mediaError } = await supabase
    .from("media")
    .insert({
      media_type: mediaType,
      url,
      metadata,
    })
    .select()
    .single();

  if (mediaError) {
    console.error("[addArtifactMedia] Ошибка при создании медиа:", mediaError);
    throw mediaError;
  }

  console.log("[addArtifactMedia] Медиа создано:", mediaData);

  // Затем создаем связь между артефактом и медиа
  const { data: linkData, error: linkError } = await supabase
    .from("artifact_media")
    .insert({
      artifact_id: artifactId,
      media_id: mediaData.id,
    })
    .select("*, media(*)")
    .single();

  if (linkError) {
    console.error("[addArtifactMedia] Ошибка при создании связи:", linkError);
    throw linkError;
  }

  console.log("[addArtifactMedia] Связь создана:", linkData);

  return convertArtifactMediaFromDb(linkData as ArtifactMediaDb);
}

/**
 * Обновление метаданных медиа-ресурса через связь с артефактом
 * @param supabaseClient - клиент Supabase
 * @param linkId - ID связи из таблицы artifact_media
 * @param metadata - новые метаданные
 */
export async function updateArtifactMediaMetadata(
  supabaseClient: SupabaseClient,
  linkId: string,
  metadata: Record<string, unknown>
): Promise<void> {
  const supabase = supabaseClient;

  // Получаем media_id из связи
  const { data: linkData, error: linkError } = await supabase
    .from("artifact_media")
    .select("media_id")
    .eq("id", linkId)
    .single();

  if (linkError || !linkData) {
    console.error(
      "[updateArtifactMediaMetadata] Ошибка при получении связи:",
      linkError
    );
    throw linkError || new Error("Связь не найдена");
  }

  // Обновляем метаданные в таблице media
  const { error } = await supabase
    .from("media")
    .update({ metadata })
    .eq("id", linkData.media_id);

  if (error) {
    console.error(
      "[updateArtifactMediaMetadata] Ошибка при обновлении метаданных:",
      error
    );
    throw error;
  }
}

/**
 * Удаление связи медиа с артефактом
 * @param supabaseClient - клиент Supabase (обязательный)
 * @param linkId - ID связи из таблицы artifact_media
 * @param deleteMedia - удалить сам медиа-ресурс, если он больше не используется (по умолчанию false)
 * @returns URL удаленного медиа-ресурса, если он был удален, иначе null
 */
export async function deleteArtifactMedia(
  supabaseClient: SupabaseClient,
  linkId: string,
  deleteMedia: boolean = false
): Promise<string | null> {
  const supabase = supabaseClient;

  console.log("[deleteArtifactMedia] Удаление связи с ID:", linkId);

  // Получаем информацию о связи перед удалением (включая URL медиа)
  const { data: linkData, error: linkFetchError } = await supabase
    .from("artifact_media")
    .select("media_id, media(url)")
    .eq("id", linkId)
    .single();

  if (linkFetchError) {
    console.error(
      "[deleteArtifactMedia] Ошибка при получении связи:",
      linkFetchError
    );
    throw linkFetchError;
  }

  if (!linkData) {
    throw new Error(`Связь с ID ${linkId} не найдена.`);
  }

  const mediaId = linkData.media_id;
  // @ts-ignore
  const mediaUrl = linkData.media?.url;

  // Удаляем связь
  const { error, data } = await supabase
    .from("artifact_media")
    .delete()
    .eq("id", linkId)
    .select();

  if (error) {
    console.error("[deleteArtifactMedia] Ошибка при удалении связи:", error);
    throw error;
  }

  if (!data || data.length === 0) {
    console.warn(
      "[deleteArtifactMedia] Связь не была удалена (возможно, нет прав):",
      linkId
    );
    throw new Error(
      `Связь с ID ${linkId} не была удалена. Возможно, нет прав доступа.`
    );
  }

  console.log("[deleteArtifactMedia] Связь успешно удалена:", data);

  // Если нужно удалить сам медиа-ресурс, проверяем, используется ли он еще
  if (deleteMedia) {
    const { data: otherLinks, error: checkError } = await supabase
      .from("artifact_media")
      .select("id")
      .eq("media_id", mediaId)
      .limit(1);

    if (checkError) {
      console.error(
        "[deleteArtifactMedia] Ошибка при проверке использования медиа:",
        checkError
      );
      // Не прерываем выполнение, так как связь уже удалена
    } else if (!otherLinks || otherLinks.length === 0) {
      // Медиа больше не используется, удаляем его
      const { error: mediaDeleteError } = await supabase
        .from("media")
        .delete()
        .eq("id", mediaId);

      if (mediaDeleteError) {
        console.error(
          "[deleteArtifactMedia] Ошибка при удалении медиа:",
          mediaDeleteError
        );
        // Не прерываем выполнение, так как связь уже удалена
      } else {
        console.log(
          "[deleteArtifactMedia] Медиа-ресурс также удален:",
          mediaId
        );
        return mediaUrl || null;
      }
    }
  }

  return null;
}

/**
 * Создание нового таргета
 * @param supabaseClient - клиент Supabase (обязательный)
 * @param artifactId - ID артефакта
 * @param url - URL изображения-таргета
 * @param sizeCm - размер стороны квадрата в см
 * @param displayPriority - приоритет отображения
 * @returns созданный таргет
 */
export async function createTarget(
  supabaseClient: SupabaseClient,
  artifactId: string,
  url: string,
  sizeCm: number = 10
): Promise<Target> {
  const supabase = supabaseClient;

  const { data, error } = await supabase
    .from("targets")
    .insert({
      artifact_id: artifactId,
      url,
      size_cm: sizeCm,
    })
    .select()
    .single();

  if (error) {
    throw error;
  }

  return convertTargetFromDb(data as TargetDb);
}

/**
 * Удаление таргета
 * @param supabaseClient - клиент Supabase (обязательный)
 * @param targetId - ID таргета
 * @returns URL удаленного таргета или null
 */
export async function deleteArtifactTarget(
  supabaseClient: SupabaseClient,
  targetId: string
): Promise<string | null> {
  const supabase = supabaseClient;

  // Получаем информацию о таргете перед удалением
  const { data: targetData, error: targetFetchError } = await supabase
    .from("targets")
    .select("url")
    .eq("id", targetId)
    .single();

  if (targetFetchError) {
    console.error(
      "[deleteArtifactTarget] Ошибка при получении таргета:",
      targetFetchError
    );
    throw targetFetchError;
  }

  const { error } = await supabase
    .from("targets")
    .delete()
    .eq("id", targetId);

  if (error) {
    console.error("[deleteArtifactTarget] Ошибка при удалении таргета:", error);
    throw error;
  }

  return targetData?.url || null;
}


import type {
  Artifact,
  ArtifactDb,
  ArtifactMedia,
  ArtifactMediaDb,
  ArtifactTarget,
  ArtifactTargetDb,
  Target,
  TargetDb,
} from "@/lib/types/artifact";
import {
  convertArtifactFromDb,
  convertArtifactMediaFromDb,
  convertArtifactTargetFromDb,
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
  targets: ArtifactTarget[];
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

  // Получаем таргеты через JOIN с таблицей targets
  const { data: targetsData, error: targetsError } = await supabase
    .from("artifact_targets")
    .select(
      `
      *,
      targets(*)
    `
    )
    .eq("artifact_id", artifactId)
    .order("display_priority", { ascending: true })
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
    targets: (targetsData ?? []).map((item: ArtifactTargetDb) =>
      convertArtifactTargetFromDb(item)
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
 * Удаление артефакта (мягкое удаление через is_active = false)
 * @param supabaseClient - клиент Supabase (обязательный)
 * @param artifactId - ID артефакта
 */
export async function deleteArtifact(
  supabaseClient: SupabaseClient,
  artifactId: string
): Promise<void> {
  const supabase = supabaseClient;

  const { error } = await supabase
    .from("artifacts")
    .update({ is_active: false })
    .eq("id", artifactId);

  if (error) {
    throw error;
  }
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
 * Удаление связи медиа с артефактом
 * @param supabaseClient - клиент Supabase (обязательный)
 * @param linkId - ID связи из таблицы artifact_media
 * @param deleteMedia - удалить сам медиа-ресурс, если он больше не используется (по умолчанию false)
 */
export async function deleteArtifactMedia(
  supabaseClient: SupabaseClient,
  linkId: string,
  deleteMedia: boolean = false
): Promise<void> {
  const supabase = supabaseClient;

  console.log("[deleteArtifactMedia] Удаление связи с ID:", linkId);

  // Получаем информацию о связи перед удалением
  const { data: linkData, error: linkFetchError } = await supabase
    .from("artifact_media")
    .select("media_id")
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
      }
    }
  }
}

/**
 * Создание нового таргета
 * @param supabaseClient - клиент Supabase (обязательный)
 * @param url - URL изображения-таргета
 * @returns созданный таргет
 */
export async function createTarget(
  supabaseClient: SupabaseClient,
  url: string
): Promise<Target> {
  const supabase = supabaseClient;

  const { data, error } = await supabase
    .from("targets")
    .insert({
      url,
    })
    .select()
    .single();

  if (error) {
    throw error;
  }

  return convertTargetFromDb(data as TargetDb);
}

/**
 * Добавление таргета к артефакту
 * @param supabaseClient - клиент Supabase (обязательный)
 * @param artifactId - ID артефакта
 * @param targetId - ID таргета
 * @param displayPriority - приоритет отображения (по умолчанию 0)
 * @returns созданная связь артефакта с таргетом
 */
export async function addArtifactTarget(
  supabaseClient: SupabaseClient,
  artifactId: string,
  targetId: string,
  displayPriority: number = 0
): Promise<ArtifactTarget> {
  const supabase = supabaseClient;

  console.log("[addArtifactTarget] Параметры:", {
    artifactId,
    targetId,
    displayPriority,
  });

  const { data, error } = await supabase
    .from("artifact_targets")
    .insert({
      artifact_id: artifactId,
      target_id: targetId,
      display_priority: displayPriority,
    })
    .select("*, targets(*)")
    .single();

  if (error) {
    console.error("[addArtifactTarget] Ошибка при создании связи:", error);
    throw error;
  }

  console.log("[addArtifactTarget] Связь создана:", data);

  return convertArtifactTargetFromDb(data as ArtifactTargetDb);
}

/**
 * Удаление связи таргета с артефактом
 * @param supabaseClient - клиент Supabase (обязательный)
 * @param linkId - ID связи из таблицы artifact_targets
 * @param deleteTarget - удалить сам таргет, если он больше не используется (по умолчанию false)
 */
export async function deleteArtifactTarget(
  supabaseClient: SupabaseClient,
  linkId: string,
  deleteTarget: boolean = false
): Promise<void> {
  const supabase = supabaseClient;

  console.log("[deleteArtifactTarget] Удаление связи с ID:", linkId);

  // Получаем информацию о связи перед удалением
  const { data: linkData, error: linkFetchError } = await supabase
    .from("artifact_targets")
    .select("target_id")
    .eq("id", linkId)
    .single();

  if (linkFetchError) {
    console.error(
      "[deleteArtifactTarget] Ошибка при получении связи:",
      linkFetchError
    );
    throw linkFetchError;
  }

  if (!linkData) {
    throw new Error(`Связь с ID ${linkId} не найдена.`);
  }

  const targetId = linkData.target_id;

  // Удаляем связь
  const { error, data } = await supabase
    .from("artifact_targets")
    .delete()
    .eq("id", linkId)
    .select();

  if (error) {
    console.error("[deleteArtifactTarget] Ошибка при удалении связи:", error);
    throw error;
  }

  if (!data || data.length === 0) {
    console.warn(
      "[deleteArtifactTarget] Связь не была удалена (возможно, нет прав):",
      linkId
    );
    throw new Error(
      `Связь с ID ${linkId} не была удалена. Возможно, нет прав доступа.`
    );
  }

  console.log("[deleteArtifactTarget] Связь успешно удалена:", data);

  // Если нужно удалить сам таргет, проверяем, используется ли он еще
  if (deleteTarget) {
    const { data: otherLinks, error: checkError } = await supabase
      .from("artifact_targets")
      .select("id")
      .eq("target_id", targetId)
      .limit(1);

    if (checkError) {
      console.error(
        "[deleteArtifactTarget] Ошибка при проверке использования таргета:",
        checkError
      );
      // Не прерываем выполнение, так как связь уже удалена
    } else if (!otherLinks || otherLinks.length === 0) {
      // Таргет больше не используется, удаляем его
      const { error: targetDeleteError } = await supabase
        .from("targets")
        .delete()
        .eq("id", targetId);

      if (targetDeleteError) {
        console.error(
          "[deleteArtifactTarget] Ошибка при удалении таргета:",
          targetDeleteError
        );
        // Не прерываем выполнение, так как связь уже удалена
      } else {
        console.log("[deleteArtifactTarget] Таргет также удален:", targetId);
      }
    }
  }
}

/**
 * Обновление приоритета отображения таргета для артефакта
 * @param supabaseClient - клиент Supabase (обязательный)
 * @param linkId - ID связи из таблицы artifact_targets
 * @param displayPriority - новый приоритет отображения
 */
export async function updateArtifactTargetPriority(
  supabaseClient: SupabaseClient,
  linkId: string,
  displayPriority: number
): Promise<ArtifactTarget> {
  const supabase = supabaseClient;

  const { data, error } = await supabase
    .from("artifact_targets")
    .update({ display_priority: displayPriority })
    .eq("id", linkId)
    .select("*, targets(*)")
    .single();

  if (error) {
    throw error;
  }

  return convertArtifactTargetFromDb(data as ArtifactTargetDb);
}

"use client";

import { MediaList } from "@/components/media-list";
import type { LocalMediaItem } from "@/components/media-uploader";
import { PreviewImageUploader } from "@/components/preview-image-uploader";
import {
  SaveProgressDialog,
  type SaveStep,
} from "@/components/save-progress-dialog";
import { TargetList } from "@/components/target-list";
import type { LocalTargetItem } from "@/components/target-uploader";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { checkImageQualityFromUrl } from "@/lib/image-analysis/imageQualityChecker";
import type { ArtifactWithDetails } from "@/lib/queries";
import {
  addArtifactMedia,
  createArtifact,
  createTarget,
  deleteArtifact,
  deleteArtifactMedia,
  deleteArtifactTarget,
  getArtifactById,
  updateArtifact,
  updateArtifactMediaMetadata,
} from "@/lib/queries";
import { createClient } from "@/lib/supabase/client";
import { upload } from "@vercel/blob/client";
import { Loader2, Save, Trash2 } from "lucide-react";
import { useParams, useRouter } from "next/navigation";
import { useCallback, useEffect, useMemo, useState } from "react";

export default function ArtifactEditPage() {
  const params = useParams();
  const router = useRouter();
  const artifactId = params.id as string | undefined;
  const isNew = artifactId === "new" || !artifactId;

  const [artifact, setArtifact] = useState<ArtifactWithDetails | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [localMedia, setLocalMedia] = useState<LocalMediaItem[]>([]);
  const [localTargets, setLocalTargets] = useState<LocalTargetItem[]>([]);
  const [previewImageFile, setPreviewImageFile] = useState<File | null>(null);
  const [previewImageUrl, setPreviewImageUrl] = useState<string | null>(null);

  // Состояние для модального окна прогресса
  const [saveSteps, setSaveSteps] = useState<SaveStep[]>([]);
  const [saveProgressOpen, setSaveProgressOpen] = useState(false);
  const [saveOverallStatus, setSaveOverallStatus] = useState<
    "saving" | "success" | "error"
  >("saving");

  // Загружаем существующий артефакт
  useEffect(() => {
    if (isNew) {
      setIsLoading(false);
      return;
    }

    const loadArtifact = async () => {
      try {
        const supabase = createClient();
        // Включаем неактивные артефакты для редактирования
        const data = await getArtifactById(supabase, artifactId, true);
        if (!data) {
          setError("Артефакт не найден");
          return;
        }
        setArtifact(data);
        setName(data.name);
        setDescription(data.description || "");
        setPreviewImageUrl(data.preview_image_url);

        // Преобразуем существующие медиа в LocalMediaItem
        const existingMedia: LocalMediaItem[] = data.media.map((m) => ({
          id: m.id,
          type: m.media.media_type,
          url: m.media.url,
          metadata: m.media.metadata || undefined,
        }));
        setLocalMedia(existingMedia);

        // Преобразуем существующие таргеты в LocalTargetItem и анализируем качество
        const existingTargets: LocalTargetItem[] = await Promise.all(
          data.targets.map(async (t) => {
            let qualityScore: number | undefined;
            try {
              const qualityResult = await checkImageQualityFromUrl(t.url);
              qualityScore = qualityResult.score;
            } catch (err) {
              console.warn(
                `Не удалось проанализировать качество таргета ${t.id}:`,
                err
              );
              // Продолжаем без балла качества, если анализ не удался
            }
            return {
              id: t.id,
              url: t.url,
              size_cm: t.size_cm,
              quality_score: qualityScore,
            };
          })
        );
        setLocalTargets(existingTargets);
      } catch (err) {
        console.error("Ошибка загрузки артефакта:", {
          error: err,
          artifactId,
          isNew,
          errorType: err?.constructor?.name,
          errorMessage: err instanceof Error ? err.message : String(err),
          errorStack: err instanceof Error ? err.stack : undefined,
          timestamp: new Date().toISOString(),
        });
        setError(
          err instanceof Error ? err.message : "Ошибка загрузки артефакта"
        );
      } finally {
        setIsLoading(false);
      }
    };

    loadArtifact();
  }, [artifactId, isNew]);

  const handleMediaAdd = useCallback((media: LocalMediaItem) => {
    setLocalMedia((prev) => [...prev, media]);
  }, []);

  const handleMediaRemove = useCallback((id: string) => {
    setLocalMedia((prev) => prev.filter((m) => m.id !== id));
  }, []);

  const handleMediaUpdate = useCallback(
    (id: string, updates: Partial<LocalMediaItem>) => {
      setLocalMedia((prev) =>
        prev.map((m) => (m.id === id ? { ...m, ...updates } : m))
      );
    },
    []
  );

  const handlePreviewImageSelect = useCallback((file: File) => {
    setPreviewImageFile(file);
  }, []);

  const handlePreviewImageRemove = useCallback(() => {
    setPreviewImageFile(null);
    setPreviewImageUrl(null);
  }, []);

  const handleTargetAdd = useCallback((target: LocalTargetItem) => {
    setLocalTargets((prev) => [...prev, target]);
  }, []);

  const handleTargetRemove = useCallback((id: string) => {
    setLocalTargets((prev) => prev.filter((t) => t.id !== id));
  }, []);

  const handleTargetUpdate = useCallback(
    (id: string, updates: Partial<LocalTargetItem>) => {
      setLocalTargets((prev) =>
        prev.map((t) => (t.id === id ? { ...t, ...updates } : t))
      );
    },
    []
  );

  const uploadMediaToBlob = async (
    file: File,
    _artifactId: string,
    prefix: "preview" | "model" | "video" | "target" = "preview"
  ): Promise<string> => {
    // Добавляем префикс к имени файла
    const fileName = `${prefix}-${file.name}`;
    const blob = await upload(fileName, file, {
      access: "public",
      handleUploadUrl: "/api/upload",
      contentType: file.type,
    });
    return blob.url;
  };

  const updateStepStatus = (
    stepId: string,
    status: SaveStep["status"],
    error?: string,
    details?: string
  ) => {
    setSaveSteps((prev) =>
      prev.map((step) =>
        step.id === stepId ? { ...step, status, error, details } : step
      )
    );
  };

  const deleteBlobs = async (urls: string[]) => {
    if (urls.length === 0) return;
    try {
      await fetch("/api/delete-blob", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ urls }),
      });
    } catch (e) {
      console.error("Failed to delete blobs", e);
    }
  };

  // Валидация таргетов: минимальный проходной балл = 75
  const targetValidation = useMemo(() => {
    const MIN_QUALITY_SCORE = 75;
    const invalidTargets = localTargets.filter(
      (target) =>
        target.quality_score !== undefined &&
        target.quality_score < MIN_QUALITY_SCORE
    );
    const hasInvalidTargets = invalidTargets.length > 0;
    const invalidCount = invalidTargets.length;

    return {
      isValid: !hasInvalidTargets,
      invalidTargets,
      invalidCount,
      errorMessage: hasInvalidTargets
        ? `Нельзя сохранить артефакт: ${invalidCount} таргет${invalidCount === 1 ? "" : invalidCount < 5 ? "а" : "ов"} не соответствует минимальному баллу качества (75). Удалите или замените таргеты с низким качеством.`
        : null,
    };
  }, [localTargets]);

  const handleSave = async () => {
    if (!name.trim()) {
      setError("Название артефакта обязательно");
      return;
    }

    // Проверяем валидность таргетов
    if (!targetValidation.isValid) {
      setError(targetValidation.errorMessage || "Ошибка валидации таргетов");
      return;
    }

    setIsSaving(true);
    setError(null);
    setSaveProgressOpen(true);
    setSaveOverallStatus("saving");

    // Инициализируем шаги
    const existingMediaIds = artifact?.media.map((m) => m.id) || [];
    const newMedia = localMedia.filter((m) => {
      if (!m.id) return true;
      return !existingMediaIds.includes(m.id);
    });

    const existingTargetIds = artifact?.targets.map((t) => t.id) || [];
    const newTargets = localTargets.filter((t) => {
      if (!t.id) return true;
      if (isNew) return true;
      return !existingTargetIds.includes(t.id);
    });

    const mediaToDelete = !isNew
      ? existingMediaIds.filter(
          (id) =>
            !localMedia
              .filter((m) => m.id && existingMediaIds.includes(m.id))
              .map((m) => m.id!)
              .includes(id)
        )
      : [];

    // Медиа для обновления (существующие, которые остались)
    const mediaToUpdate = !isNew
      ? localMedia.filter((m) => m.id && existingMediaIds.includes(m.id))
      : [];

    const targetsToDelete = !isNew
      ? existingTargetIds.filter(
          (id) =>
            !localTargets
              .filter((t) => t.id && existingTargetIds.includes(t.id))
              .map((t) => t.id!)
              .includes(id)
        )
      : [];

    const steps: SaveStep[] = [
      {
        id: "auth",
        label: "Проверка авторизации",
        status: "pending",
      },
      ...(previewImageFile
        ? [
            {
              id: "preview-upload",
              label: "Загрузка превью изображения",
              status: "pending" as const,
            },
          ]
        : []),
      {
        id: isNew ? "create-artifact" : "update-artifact",
        label: isNew ? "Создание артефакта" : "Обновление артефакта",
        status: "pending" as const,
      },
      ...mediaToDelete.map((id, index) => ({
        id: `delete-media-${id}`,
        label: `Удаление медиа ${index + 1}`,
        status: "pending" as const,
      })),
      ...mediaToUpdate.map((media, index) => ({
        id: `update-media-${media.id || index}`,
        label: `Обновление медиа: ${media.type}`,
        status: "pending" as const,
        details: "Обновление метаданных",
      })),
      ...newMedia.map((media, index) => ({
        id: `media-${media.id || index}`,
        label: media.file
          ? `Загрузка медиа: ${media.file.name}`
          : `Сохранение медиа: ${media.type}`,
        status: "pending" as const,
        details: media.file
          ? `Загрузка в Blob Storage и сохранение в БД`
          : `Сохранение ссылки в БД`,
      })),
      ...targetsToDelete.map((id, index) => ({
        id: `delete-target-${id}`,
        label: `Удаление таргета ${index + 1}`,
        status: "pending" as const,
      })),
      ...newTargets.map((target, index) => ({
        id: `target-${target.id || index}`,
        label: target.file
          ? `Загрузка таргета: ${target.file.name}`
          : "Сохранение таргета",
        status: "pending" as const,
        details: target.file
          ? `Загрузка в Blob Storage и создание таргета`
          : `Обновление таргета`,
      })),
    ];

    setSaveSteps(steps);

    try {
      const supabase = createClient();

      // Проверяем аутентификацию
      updateStepStatus("auth", "processing");
      const {
        data: { user },
      } = await supabase.auth.getUser();
      if (!user) {
        updateStepStatus("auth", "error", "Необходима авторизация");
        throw new Error("Необходима авторизация");
      }
      updateStepStatus("auth", "success");

      // Загружаем превью-изображение в Blob, если оно было выбрано
      let previewImageUrlToSave: string | null = previewImageUrl;
      if (previewImageFile) {
        updateStepStatus(
          "preview-upload",
          "processing",
          undefined,
          "Загрузка файла..."
        );
        previewImageUrlToSave = await uploadMediaToBlob(
          previewImageFile,
          artifactId || "temp",
          "preview"
        );
        updateStepStatus("preview-upload", "success");
      }

      let currentArtifactId: string;

      if (isNew) {
        // Создаем новый артефакт
        updateStepStatus("create-artifact", "processing");
        const newArtifact = await createArtifact(
          supabase,
          name.trim(),
          description.trim() || null
        );
        currentArtifactId = newArtifact.id;

        // Обновляем артефакт с превью-изображением
        if (previewImageUrlToSave) {
          await updateArtifact(supabase, currentArtifactId, {
            preview_image_url: previewImageUrlToSave,
          });
        }
        updateStepStatus("create-artifact", "success");
      } else {
        // Обновляем существующий артефакт
        updateStepStatus("update-artifact", "processing");

        // Если изображение изменилось, удаляем старое
        if (
          artifact?.preview_image_url &&
          previewImageUrlToSave !== artifact.preview_image_url
        ) {
          try {
            await deleteBlobs([artifact.preview_image_url]);
          } catch (e) {
            console.error("Ошибка при удалении старого превью:", e);
          }
        }

        await updateArtifact(supabase, artifactId, {
          name: name.trim(),
          description: description.trim() || null,
          preview_image_url: previewImageUrlToSave,
        });
        currentArtifactId = artifactId;
        updateStepStatus("update-artifact", "success");

        // Удаляем медиа, которые были удалены из локального списка
        // currentMediaIds - это ID медиа, которые есть и в локальном списке, и в БД
        const currentMediaIds = localMedia
          .filter((m) => m.id && existingMediaIds.includes(m.id))
          .map((m) => m.id!);

        // mediaToDelete - это ID медиа, которые есть в БД, но отсутствуют в локальном списке
        const mediaToDelete = existingMediaIds.filter(
          (id) => !currentMediaIds.includes(id)
        );

        console.log(
          "[handleSave] ID медиа в локальном списке:",
          localMedia.map((m) => m.id)
        );
        console.log(
          "[handleSave] ID существующих медиа из БД:",
          existingMediaIds
        );
        console.log(
          "[handleSave] ID медиа, которые остаются (есть и в списке, и в БД):",
          currentMediaIds
        );
        console.log("[handleSave] ID медиа для удаления:", mediaToDelete);

        if (mediaToDelete.length > 0) {
          for (const mediaId of mediaToDelete) {
            const stepId = `delete-media-${mediaId}`;
            updateStepStatus(stepId, "processing");
            try {
              const deletedMediaUrl = await deleteArtifactMedia(
                supabase,
                mediaId,
                true
              );
              if (deletedMediaUrl) {
                await deleteBlobs([deletedMediaUrl]);
              }
              updateStepStatus(stepId, "success");
            } catch (error) {
              updateStepStatus(
                stepId,
                "error",
                error instanceof Error ? error.message : "Неизвестная ошибка"
              );
              throw error;
            }
          }
        }

        // Обновляем существующие медиа
        if (mediaToUpdate.length > 0) {
          for (const media of mediaToUpdate) {
            if (!media.id) continue;
            const stepId = `update-media-${media.id}`;
            updateStepStatus(stepId, "processing");
            try {
              await updateArtifactMediaMetadata(
                supabase,
                media.id,
                media.metadata || {}
              );
              updateStepStatus(stepId, "success");
            } catch (error) {
              updateStepStatus(
                stepId,
                "error",
                error instanceof Error ? error.message : "Неизвестная ошибка"
              );
              // Не прерываем процесс сохранения из-за ошибки обновления метаданных
              console.error("Ошибка обновления медиа:", error);
            }
          }
        }
      }

      // Загружаем новые медиа файлы в Blob и добавляем в БД
      for (const media of newMedia) {
        const stepId = `media-${media.id || newMedia.indexOf(media)}`;
        try {
          updateStepStatus(stepId, "processing");

          if (media.file) {
            // Загружаем файл в Blob
            updateStepStatus(
              stepId,
              "processing",
              undefined,
              "Загрузка в Blob Storage..."
            );
            const blobUrl = await uploadMediaToBlob(
              media.file,
              currentArtifactId,
              media.type === "3d_model" ? "model" : "video"
            );

            // Добавляем в БД
            updateStepStatus(
              stepId,
              "processing",
              undefined,
              "Сохранение в БД..."
            );
            await addArtifactMedia(
              supabase,
              currentArtifactId,
              media.type,
              blobUrl,
              media.metadata || null
            );
            updateStepStatus(stepId, "success");
          } else if (media.url) {
            // Для YouTube просто добавляем в БД
            updateStepStatus(
              stepId,
              "processing",
              undefined,
              "Сохранение ссылки в БД..."
            );
            await addArtifactMedia(
              supabase,
              currentArtifactId,
              media.type,
              media.url,
              media.metadata || null
            );
            updateStepStatus(stepId, "success");
          } else {
            updateStepStatus(stepId, "error", "Медиа без файла и без URL");
          }
        } catch (mediaError) {
          const errorMessage =
            mediaError instanceof Error
              ? mediaError.message
              : "Неизвестная ошибка";
          updateStepStatus(stepId, "error", errorMessage);
          throw new Error(
            `Ошибка сохранения медиа (${media.type}): ${errorMessage}`
          );
        }
      }

      // Обработка таргетов
      if (!isNew && targetsToDelete.length > 0) {
        for (const targetId of targetsToDelete) {
          const stepId = `delete-target-${targetId}`;
          updateStepStatus(stepId, "processing");
          try {
            const deletedTargetUrl = await deleteArtifactTarget(
              supabase,
              targetId
            );
            if (deletedTargetUrl) {
              await deleteBlobs([deletedTargetUrl]);
            }
            updateStepStatus(stepId, "success");
          } catch (error) {
            updateStepStatus(
              stepId,
              "error",
              error instanceof Error ? error.message : "Неизвестная ошибка"
            );
            throw error;
          }
        }
      }

      // Загружаем новые таргеты в Blob и добавляем в БД
      for (const target of newTargets) {
        const stepId = `target-${target.id || newTargets.indexOf(target)}`;
        try {
          updateStepStatus(stepId, "processing");

          if (target.file) {
            // Загружаем файл в Blob
            updateStepStatus(
              stepId,
              "processing",
              undefined,
              "Загрузка в Blob Storage..."
            );
            const blobUrl = await uploadMediaToBlob(
              target.file,
              currentArtifactId,
              "target"
            );

            // Создаем таргет в БД
            updateStepStatus(
              stepId,
              "processing",
              undefined,
              "Создание таргета в БД..."
            );
            await createTarget(
              supabase,
              currentArtifactId,
              blobUrl,
              target.size_cm || 10
            );

            updateStepStatus(stepId, "success");
          } else if (target.url) {
            // Для существующих таргетов просто обновляем статус
            updateStepStatus(stepId, "success", "Таргет уже существует");
          } else {
            updateStepStatus(stepId, "error", "Таргет без файла и без URL");
          }
        } catch (targetError) {
          const errorMessage =
            targetError instanceof Error
              ? targetError.message
              : "Неизвестная ошибка";
          updateStepStatus(stepId, "error", errorMessage);
          throw new Error(`Ошибка сохранения таргета: ${errorMessage}`);
        }
      }

      setSaveOverallStatus("success");
      setTimeout(() => {
        router.push("/");
        router.refresh();
      }, 1000);
    } catch (err) {
      const errorMessage =
        err instanceof Error ? err.message : "Ошибка сохранения артефакта";
      setError(errorMessage);
      setSaveOverallStatus("error");
    } finally {
      setIsSaving(false);
    }
  };

  const handleDelete = async () => {
    if (!artifactId || isNew) return;

    if (
      !confirm(
        "Вы уверены, что хотите удалить этот артефакт? Это действие нельзя отменить."
      )
    ) {
      return;
    }

    setIsSaving(true);
    setError(null);

    try {
      const supabase = createClient();
      const { previewImageUrl, mediaUrls, targetUrls } = await deleteArtifact(
        supabase,
        artifactId
      );

      // Удаляем все файлы из Blob Storage
      const urlsToDelete: string[] = [];
      if (previewImageUrl) {
        urlsToDelete.push(previewImageUrl);
      }
      urlsToDelete.push(...mediaUrls);
      urlsToDelete.push(...targetUrls);

      if (urlsToDelete.length > 0) {
        try {
          await deleteBlobs(urlsToDelete);
        } catch (blobError) {
          console.error(
            "Ошибка при удалении файлов из Blob Storage:",
            blobError
          );
          // Не прерываем процесс, так как артефакт уже удален из БД
        }
      }

      router.push("/");
      router.refresh();
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Ошибка удаления артефакта"
      );
      setIsSaving(false);
    }
  };

  if (isLoading) {
    return (
      <div className="container mx-auto py-8 px-4">
        <div className="flex items-center justify-center min-h-[400px]">
          <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
        </div>
      </div>
    );
  }

  return (
    <div className="container mx-auto py-8 px-4">
      <div className="max-w-6xl mx-auto">
        <div className="flex justify-between items-center mb-6">
          <h1 className="text-3xl font-bold">
            {isNew ? "Создать артефакт" : "Редактировать артефакт"}
          </h1>
          {!isNew && (
            <Button
              variant="destructive"
              onClick={handleDelete}
              disabled={isSaving}
              icon={Trash2}
            >
              Удалить
            </Button>
          )}
        </div>

        {(error || targetValidation.errorMessage) && (
          <div className="mb-4 p-4 bg-destructive/10 border border-destructive/20 rounded-lg text-destructive text-sm">
            {error || targetValidation.errorMessage}
          </div>
        )}

        {/* Mobile-first: одна колонка */}
        <div className="flex flex-col md:grid md:grid-cols-2 md:gap-8">
          {/* Левая колонка: название и описание (на мобильных - вторая) */}
          <div className="order-2 md:order-1 space-y-6">
            <PreviewImageUploader
              previewImageUrl={previewImageUrl}
              onImageSelect={handlePreviewImageSelect}
              onImageRemove={handlePreviewImageRemove}
            />
            <div className="space-y-2">
              <Label htmlFor="name">Название *</Label>
              <Input
                id="name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Введите название артефакта"
                required
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="description">Описание</Label>
              <textarea
                id="description"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                placeholder="Введите описание артефакта (Markdown поддерживается)"
                className="flex min-h-[120px] w-full rounded-md border border-input bg-transparent px-3 py-2 text-base shadow-sm transition-colors placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50 md:text-sm resize-y"
              />
            </div>
          </div>

          {/* Правая колонка: медиа (на мобильных - первая) */}
          <div className="order-1 md:order-2 space-y-6">
            <MediaList
              media={localMedia}
              onMediaAdd={handleMediaAdd}
              onMediaRemove={handleMediaRemove}
              onMediaUpdate={handleMediaUpdate}
            />
            <TargetList
              targets={localTargets}
              onTargetAdd={handleTargetAdd}
              onTargetRemove={handleTargetRemove}
              onTargetUpdate={handleTargetUpdate}
            />
          </div>
        </div>

        {/* Кнопки действий */}
        <div className="mt-8 flex gap-4 justify-end">
          <Button
            variant="outline"
            onClick={() => router.back()}
            disabled={isSaving}
          >
            Отмена
          </Button>
          <Button
            onClick={handleSave}
            disabled={isSaving || !targetValidation.isValid}
            icon={Save}
          >
            {isSaving ? (
              <>
                <Loader2 className="h-4 w-4 animate-spin" />
                Сохранение...
              </>
            ) : (
              "Сохранить"
            )}
          </Button>
        </div>
      </div>

      <SaveProgressDialog
        open={saveProgressOpen}
        steps={saveSteps}
        overallStatus={saveOverallStatus}
        onClose={() => {
          if (saveOverallStatus !== "saving") {
            setSaveProgressOpen(false);
          }
        }}
      />
    </div>
  );
}

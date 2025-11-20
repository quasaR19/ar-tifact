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
import type { ArtifactWithDetails } from "@/lib/queries";
import {
  addArtifactMedia,
  addArtifactTarget,
  createArtifact,
  createTarget,
  deleteArtifact,
  deleteArtifactMedia,
  deleteArtifactTarget,
  getArtifactById,
  updateArtifact,
} from "@/lib/queries";
import { createClient } from "@/lib/supabase/client";
import { upload } from "@vercel/blob/client";
import { Loader2, Save, Trash2 } from "lucide-react";
import { useParams, useRouter } from "next/navigation";
import { useCallback, useEffect, useState } from "react";

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

        // Преобразуем существующие таргеты в LocalTargetItem
        const existingTargets: LocalTargetItem[] = data.targets.map((t) => ({
          id: t.id,
          url: t.target.url,
        }));
        setLocalTargets(existingTargets);
      } catch (err) {
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

  const uploadMediaToBlob = async (
    file: File,
    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    _artifactId: string
  ): Promise<string> => {
    const blob = await upload(file.name, file, {
      access: "public",
      handleUploadUrl: "/api/upload",
      contentType: file.type,
    });
    return blob.url;
  };

  const updateStepStatus = (
    stepId: string,
    status: SaveStep["status"],
    error?: string
  ) => {
    setSaveSteps((prev) =>
      prev.map((step) =>
        step.id === stepId ? { ...step, status, error } : step
      )
    );
  };

  const handleSave = async () => {
    if (!name.trim()) {
      setError("Название артефакта обязательно");
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
          ? `Загрузка в Blob Storage, создание таргета и связи`
          : `Создание связи с существующим таргетом`,
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
        updateStepStatus("preview-upload", "processing", "Загрузка файла...");
        previewImageUrlToSave = await uploadMediaToBlob(
          previewImageFile,
          artifactId || "temp"
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
              await deleteArtifactMedia(supabase, mediaId);
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
              "Загрузка в Blob Storage..."
            );
            const blobUrl = await uploadMediaToBlob(
              media.file,
              currentArtifactId
            );

            // Добавляем в БД
            updateStepStatus(stepId, "processing", "Сохранение в БД...");
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
            updateStepStatus(stepId, "processing", "Сохранение ссылки в БД...");
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
            await deleteArtifactTarget(supabase, targetId);
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
              "Загрузка в Blob Storage..."
            );
            const blobUrl = await uploadMediaToBlob(
              target.file,
              currentArtifactId
            );

            // Создаем таргет в БД
            updateStepStatus(stepId, "processing", "Создание таргета в БД...");
            const createdTarget = await createTarget(supabase, blobUrl);

            // Создаем связь между артефактом и таргетом
            updateStepStatus(stepId, "processing", "Создание связи...");
            await addArtifactTarget(
              supabase,
              currentArtifactId,
              createdTarget.id,
              0
            );
            updateStepStatus(stepId, "success");
          } else if (target.url) {
            // Для существующих таргетов обычно связь уже есть
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
      await deleteArtifact(supabase, artifactId);
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

        {error && (
          <div className="mb-4 p-4 bg-destructive/10 border border-destructive/20 rounded-lg text-destructive text-sm">
            {error}
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
            />
            <TargetList
              targets={localTargets}
              onTargetAdd={handleTargetAdd}
              onTargetRemove={handleTargetRemove}
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
          <Button onClick={handleSave} disabled={isSaving} icon={Save}>
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

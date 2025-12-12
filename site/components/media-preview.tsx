"use client";

import { useEffect, useMemo, useRef } from "react";
import type { CSSProperties } from "react";
import { X, ChevronUp, ChevronDown } from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import type { LocalMediaItem } from "./media-uploader";
import { GLBViewer } from "./glb-viewer";

interface MediaPreviewProps {
  media: LocalMediaItem;
  onRemove: () => void;
  onUpdate?: (updates: Partial<LocalMediaItem>) => void;
  canMoveUp?: boolean;
  canMoveDown?: boolean;
  onMoveUp?: () => void;
  onMoveDown?: () => void;
  className?: string;
  style?: CSSProperties;
}

export function MediaPreview({
  media,
  onRemove,
  onUpdate,
  canMoveUp = false,
  canMoveDown = false,
  onMoveUp,
  onMoveDown,
  className,
  style,
}: MediaPreviewProps) {
  // Используем ref для хранения предыдущего blob URL, чтобы не отзывать его слишком рано
  const previousBlobUrlRef = useRef<string | null>(null);
  const currentFileRef = useRef<File | null>(null);

  const previewUrl = useMemo(() => {
    if (media.type === "youtube" && media.url) {
      return media.metadata?.embedUrl as string | undefined;
    }
    if (media.file) {
      // Если файл изменился, отзываем предыдущий blob URL
      if (previousBlobUrlRef.current && currentFileRef.current !== media.file) {
        URL.revokeObjectURL(previousBlobUrlRef.current);
      }
      const blobUrl = URL.createObjectURL(media.file);
      previousBlobUrlRef.current = blobUrl;
      currentFileRef.current = media.file;
      return blobUrl;
    }
    if (media.url) {
      // Если это не blob URL, отзываем предыдущий blob URL если он был
      if (previousBlobUrlRef.current) {
        URL.revokeObjectURL(previousBlobUrlRef.current);
        previousBlobUrlRef.current = null;
      }
      currentFileRef.current = null;
      return media.url;
    }
    // Если нет ни файла, ни URL, отзываем предыдущий blob URL
    if (previousBlobUrlRef.current) {
      URL.revokeObjectURL(previousBlobUrlRef.current);
      previousBlobUrlRef.current = null;
    }
    currentFileRef.current = null;
    return null;
  }, [media]);

  // Освобождаем URL при размонтировании компонента
  useEffect(() => {
    return () => {
      if (previousBlobUrlRef.current) {
        URL.revokeObjectURL(previousBlobUrlRef.current);
        previousBlobUrlRef.current = null;
      }
    };
  }, []);

  return (
    <div
      className={cn(
        "relative border rounded-lg overflow-hidden bg-muted/50",
        className
      )}
      style={style}
    >
      <Button
        type="button"
        variant="destructive"
        size="icon"
        className="absolute top-2 right-2 z-10 h-8 w-8"
        onClick={onRemove}
      >
        <X className="h-4 w-4" />
      </Button>

      <div className="aspect-video w-full flex items-center justify-center">
        {media.type === "3d_model" && previewUrl ? (
          <GLBViewer
            url={previewUrl}
            className="w-full h-full"
            centerModel={media.metadata?.center_model as boolean | undefined}
            onCenterModelChange={(center) => {
              onUpdate?.({
                metadata: { ...media.metadata, center_model: center },
              });
            }}
          />
        ) : media.type === "3d_model" ? (
          <div className="w-full h-full flex items-center justify-center bg-muted">
            <div className="text-center space-y-2 p-4">
              <div className="text-4xl">📦</div>
              <p className="text-sm font-medium">3D Модель</p>
              <p className="text-xs text-muted-foreground">
                {media.file?.name || "3D модель"}
              </p>
            </div>
          </div>
        ) : null}

        {media.type === "video" && previewUrl && (
          <video
            src={previewUrl}
            controls
            className="w-full h-full object-contain"
          >
            Ваш браузер не поддерживает видео.
          </video>
        )}

        {media.type === "youtube" && previewUrl && (
          <iframe
            src={previewUrl}
            title="YouTube video player"
            allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
            allowFullScreen
            className="w-full h-full"
          />
        )}
      </div>

      <div className="p-2 text-xs text-muted-foreground text-center">
        {media.type === "3d_model" && "3D Модель"}
        {media.type === "video" && "Видео"}
        {media.type === "youtube" && "YouTube"}
      </div>

      {(canMoveUp || canMoveDown) && (
        <div className="p-2 flex gap-2 justify-center items-center border-t">
          {canMoveUp && (
            <Button
              type="button"
              variant="secondary"
              size="icon"
              className="h-7 w-7"
              onClick={onMoveUp}
              title="Переместить вверх"
            >
              <ChevronUp className="h-4 w-4" />
            </Button>
          )}
          {canMoveDown && (
            <Button
              type="button"
              variant="secondary"
              size="icon"
              className="h-7 w-7"
              onClick={onMoveDown}
              title="Переместить вниз"
            >
              <ChevronDown className="h-4 w-4" />
            </Button>
          )}
        </div>
      )}
    </div>
  );
}

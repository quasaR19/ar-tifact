"use client";

import { useEffect, useMemo } from "react";
import { X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import type { LocalMediaItem } from "./media-uploader";
import { GLBViewer } from "./glb-viewer";

interface MediaPreviewProps {
  media: LocalMediaItem;
  onRemove: () => void;
  className?: string;
}

export function MediaPreview({
  media,
  onRemove,
  className,
}: MediaPreviewProps) {
  const previewUrl = useMemo(() => {
    if (media.type === "youtube" && media.url) {
      return media.metadata?.embedUrl as string | undefined;
    }
    if (media.file) {
      return URL.createObjectURL(media.file);
    }
    if (media.url) {
      return media.url;
    }
    return null;
  }, [media]);

  // Освобождаем URL при размонтировании или изменении файла
  useEffect(() => {
    if (previewUrl && media.file) {
      return () => {
        URL.revokeObjectURL(previewUrl);
      };
    }
  }, [previewUrl, media.file]);

  return (
    <div
      className={cn(
        "relative border rounded-lg overflow-hidden bg-muted/50",
        className
      )}
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
          <GLBViewer url={previewUrl} className="w-full h-full" />
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
    </div>
  );
}


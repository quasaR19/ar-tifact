"use client";

import { useState, useRef, useCallback } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Upload, X } from "lucide-react";
import { cn } from "@/lib/utils";
import { extractVideoMetadata } from "@/lib/video-metadata";

export interface LocalMediaItem {
  id: string;
  type: "3d_model" | "video" | "youtube";
  file?: File;
  url?: string; // для YouTube или существующих медиа
  metadata?: Record<string, unknown>;
  display_order?: number; // порядок отображения (0 - наивысший приоритет)
}

interface MediaUploaderProps {
  onMediaAdd: (media: LocalMediaItem) => void;
  className?: string;
}

export function MediaUploader({ onMediaAdd, className }: MediaUploaderProps) {
  const [isDragging, setIsDragging] = useState(false);
  const [youtubeUrl, setYoutubeUrl] = useState("");
  const [showYoutubeInput, setShowYoutubeInput] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const youtubeInputRef = useRef<HTMLInputElement>(null);

  const handleFileSelect = useCallback(
    async (file: File) => {
      const extension = file.name.split(".").pop()?.toLowerCase();
      let type: "3d_model" | "video" | "youtube";

      // Проверяем неподдерживаемые форматы видео перед обработкой
      const unsupportedFormats = ["avi", "mkv", "flv", "wmv"];
      if (extension && unsupportedFormats.includes(extension)) {
        alert(
          `⚠️ Формат ${extension.toUpperCase()} не поддерживается браузером.\n\nHTML5 video элемент поддерживает только MP4, WebM и MOV форматы. Пожалуйста, конвертируйте видео в поддерживаемый формат (например, MP4).`
        );
        return;
      }

      if (extension === "glb") {
        type = "3d_model";
        onMediaAdd({
          id: crypto.randomUUID(),
          type,
          file,
          metadata: {
            filename: file.name,
            size: file.size,
          },
        });
      } else if (["mp4", "webm", "mov"].includes(extension || "")) {
        type = "video";
        
        // Извлекаем метаданные видео
        try {
          const videoMetadata = await extractVideoMetadata(file);
          onMediaAdd({
            id: crypto.randomUUID(),
            type,
            file,
            metadata: {
              ...videoMetadata,
            },
          });
        } catch (error) {
          console.error("[MediaUploader] Ошибка извлечения метаданных видео:", error);
          
          // Показываем пользователю понятное сообщение об ошибке
          const errorMessage = error instanceof Error ? error.message : "Неизвестная ошибка";
          
          // Если формат не поддерживается, не добавляем файл
          if (errorMessage.includes("не поддерживается браузером") || errorMessage.includes("не поддерживается")) {
            alert(
              `⚠️ Формат видео не поддерживается\n\n${errorMessage}\n\nФайл не будет добавлен. Пожалуйста, конвертируйте видео в поддерживаемый формат (MP4, WebM или MOV).`
            );
            return;
          }
          
          // Для других ошибок (поврежденный файл, ошибка декодирования и т.д.) также не добавляем
          alert(
            `⚠️ Ошибка при обработке видео\n\n${errorMessage}\n\nФайл не будет добавлен.`
          );
        }
      } else {
        alert(
          "Неподдерживаемый формат файла. Используйте .glb или видео файлы (MP4, WebM, MOV)."
        );
        return;
      }
    },
    [onMediaAdd]
  );

  const handleFileInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) {
      handleFileSelect(file);
    }
    // Сбрасываем значение input, чтобы можно было выбрать тот же файл снова
    if (fileInputRef.current) {
      fileInputRef.current.value = "";
    }
  };

  const handleClick = () => {
    if (showYoutubeInput) {
      youtubeInputRef.current?.focus();
    } else {
      fileInputRef.current?.click();
    }
  };

  const handleYoutubeSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    const url = youtubeUrl.trim();
    if (!url) return;

    console.log("[MediaUploader] Обработка YouTube URL:", url);

    // Извлекаем ID видео из различных форматов YouTube URL
    const youtubeIdMatch =
      url.match(
        /(?:youtube\.com\/(?:[^\/]+\/.+\/|(?:v|e(?:mbed)?)\/|.*[?&]v=)|youtu\.be\/)([^"&?\/\s]{11})/
      ) || url.match(/^([^"&?\/\s]{11})$/);

    if (!youtubeIdMatch || !youtubeIdMatch[1]) {
      console.error("[MediaUploader] Неверный формат YouTube URL:", url);
      alert("Неверный формат YouTube ссылки");
      return;
    }

    const videoId = youtubeIdMatch[1];
    const mediaItem = {
      id: crypto.randomUUID(),
      type: "youtube" as const,
      url: `https://www.youtube.com/watch?v=${videoId}`,
      metadata: {
        videoId,
        embedUrl: `https://www.youtube.com/embed/${videoId}`,
      },
    };

    console.log("[MediaUploader] Создан медиа-элемент:", mediaItem);
    onMediaAdd(mediaItem);

    setYoutubeUrl("");
    setShowYoutubeInput(false);
  };

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(true);
  };

  const handleDragLeave = (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(false);
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(false);

    const file = e.dataTransfer.files[0];
    if (file) {
      handleFileSelect(file);
      return;
    }

    // Проверяем, не перетащили ли текст (YouTube ссылку)
    const text = e.dataTransfer.getData("text");
    if (text) {
      setYoutubeUrl(text);
      setShowYoutubeInput(true);
    }
  };

  return (
    <div className={cn("w-full", className)}>
      <div
        onDragOver={handleDragOver}
        onDragLeave={handleDragLeave}
        onDrop={handleDrop}
        className={cn(
          "border-2 border-dashed rounded-lg p-6 transition-colors",
          isDragging
            ? "border-primary bg-primary/5"
            : "border-muted-foreground/25 hover:border-muted-foreground/50"
        )}
      >
        {showYoutubeInput ? (
          <form onSubmit={handleYoutubeSubmit} className="space-y-4">
            <div className="flex gap-2">
              <Input
                ref={youtubeInputRef}
                type="text"
                placeholder="Вставьте ссылку на YouTube видео"
                value={youtubeUrl}
                onChange={(e) => setYoutubeUrl(e.target.value)}
                className="flex-1"
              />
              <Button
                type="button"
                variant="outline"
                onClick={() => {
                  setShowYoutubeInput(false);
                  setYoutubeUrl("");
                }}
              >
                <X className="h-4 w-4" />
              </Button>
            </div>
            <div className="flex gap-2">
              <Button type="submit" className="flex-1">
                Добавить
              </Button>
              <Button
                type="button"
                variant="outline"
                onClick={() => {
                  setShowYoutubeInput(false);
                  fileInputRef.current?.click();
                }}
              >
                Выбрать файл
              </Button>
            </div>
          </form>
        ) : (
          <div className="flex flex-col items-center justify-center gap-4">
            <Upload className="h-8 w-8 text-muted-foreground" />
            <div className="text-center space-y-2">
              <p className="text-sm font-medium">
                Перетащите файл или нажмите для выбора
              </p>
              <p className="text-xs text-muted-foreground">
                Поддерживаются: .glb, видео (MP4, WebM, MOV) или YouTube ссылки
              </p>
            </div>
            <div className="flex gap-2">
              <Button type="button" onClick={handleClick} variant="outline">
                Выбрать файл
              </Button>
              <Button
                type="button"
                onClick={() => setShowYoutubeInput(true)}
                variant="outline"
              >
                Вставить YouTube ссылку
              </Button>
            </div>
          </div>
        )}
      </div>
      <input
        ref={fileInputRef}
        type="file"
        accept=".glb,.mp4,.webm,.mov,video/mp4,video/webm,video/quicktime"
        onChange={handleFileInputChange}
        className="hidden"
      />
    </div>
  );
}

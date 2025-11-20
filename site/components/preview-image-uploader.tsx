"use client";

import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { cn } from "@/lib/utils";
import { Image as ImageIcon, Upload, X } from "lucide-react";
import Image from "next/image";
import { useCallback, useEffect, useRef, useState } from "react";

interface PreviewImageUploaderProps {
  previewImageUrl: string | null;
  onImageSelect: (file: File) => void;
  onImageRemove: () => void;
  className?: string;
}

export function PreviewImageUploader({
  previewImageUrl,
  onImageSelect,
  onImageRemove,
  className,
}: PreviewImageUploaderProps) {
  const [isDragging, setIsDragging] = useState(false);
  const [previewUrl, setPreviewUrl] = useState<string | null>(previewImageUrl);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const handleFileSelect = useCallback(
    (file: File) => {
      // Проверяем, что это изображение
      if (!file.type.startsWith("image/")) {
        alert("Пожалуйста, выберите файл изображения");
        return;
      }

      // Создаем превью
      const url = URL.createObjectURL(file);
      setPreviewUrl(url);

      onImageSelect(file);
    },
    [onImageSelect]
  );

  const handleFileInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) {
      handleFileSelect(file);
    }
    // Сбрасываем значение input
    if (fileInputRef.current) {
      fileInputRef.current.value = "";
    }
  };

  const handleRemove = () => {
    // Освобождаем blob URL перед удалением
    if (previewUrl && previewUrl.startsWith("blob:")) {
      URL.revokeObjectURL(previewUrl);
    }
    setPreviewUrl(null);
    onImageRemove();
  };

  // Очистка blob URL при размонтировании
  useEffect(() => {
    return () => {
      if (previewUrl && previewUrl.startsWith("blob:")) {
        URL.revokeObjectURL(previewUrl);
      }
    };
  }, [previewUrl]);

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
    }
  };

  // Обновляем previewUrl при изменении previewImageUrl извне
  useEffect(() => {
    // Освобождаем старый blob URL, если он был создан локально
    if (
      previewUrl &&
      previewUrl.startsWith("blob:") &&
      previewImageUrl !== previewUrl
    ) {
      URL.revokeObjectURL(previewUrl);
    }
    if (previewImageUrl !== previewUrl) {
      setPreviewUrl(previewImageUrl);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [previewImageUrl]);

  return (
    <div className={cn("w-full", className)}>
      <Label>Превью изображение</Label>
      <div
        onDragOver={handleDragOver}
        onDragLeave={handleDragLeave}
        onDrop={handleDrop}
        className={cn(
          "mt-2 border-2 border-dashed rounded-lg transition-colors overflow-hidden",
          isDragging
            ? "border-primary bg-primary/5"
            : "border-muted-foreground/25 hover:border-muted-foreground/50"
        )}
      >
        {previewUrl ? (
          <div className="relative w-full aspect-video bg-muted">
            <Image
              src={previewUrl}
              alt="Превью"
              fill
              sizes="(max-width: 768px) 100vw, 50vw"
              className="object-cover"
              loading="eager"
              priority
            />
            <Button
              type="button"
              variant="destructive"
              size="icon"
              className="absolute top-2 right-2 z-10 h-8 w-8"
              onClick={handleRemove}
            >
              <X className="h-4 w-4" />
            </Button>
          </div>
        ) : (
          <div className="flex flex-col items-center justify-center gap-4 p-6">
            <ImageIcon className="h-8 w-8 text-muted-foreground" />
            <div className="text-center space-y-2">
              <p className="text-sm font-medium">
                Перетащите изображение или нажмите для выбора
              </p>
              <p className="text-xs text-muted-foreground">
                Поддерживаются: JPG, PNG, WebP
              </p>
            </div>
            <Button
              type="button"
              onClick={() => fileInputRef.current?.click()}
              variant="outline"
            >
              <Upload className="h-4 w-4 mr-2" />
              Выбрать изображение
            </Button>
          </div>
        )}
      </div>
      <input
        ref={fileInputRef}
        type="file"
        accept="image/*"
        onChange={handleFileInputChange}
        className="hidden"
      />
    </div>
  );
}

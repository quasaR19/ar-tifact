"use client";

import { useState, useRef, useCallback } from "react";
import { Button } from "@/components/ui/button";
import { Upload, Image as ImageIcon, Loader2 } from "lucide-react";
import { cn } from "@/lib/utils";
import {
  cropImageToSquare,
  createFileFromCropped,
} from "@/lib/image-analysis/imageCropper";
import { checkImageQuality } from "@/lib/image-analysis/imageQualityChecker";

export interface LocalTargetItem {
  id: string;
  file?: File;
  url?: string; // для существующих таргетов
  size_cm: number;
  quality_score?: number; // балл качества (0-100)
}

interface TargetUploaderProps {
  onTargetAdd: (target: LocalTargetItem) => void;
  className?: string;
}

export function TargetUploader({
  onTargetAdd,
  className,
}: TargetUploaderProps) {
  const [isDragging, setIsDragging] = useState(false);
  const [isProcessing, setIsProcessing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const handleFileSelect = useCallback(
    async (file: File) => {
      // Проверяем формат файла (только jpg/jpeg/png)
      const allowedTypes = ["image/jpeg", "image/jpg", "image/png"];
      const fileExtension = file.name.toLowerCase().split(".").pop();
      const isValidType = allowedTypes.includes(file.type) || 
        (file.type.startsWith("image/") && ["jpg", "jpeg", "png"].includes(fileExtension || ""));
      
      if (!isValidType) {
        setError("Поддерживаются только форматы JPG, JPEG и PNG");
        return;
      }

      setIsProcessing(true);
      setError(null);

      try {
        // 1. Обрезаем до квадрата 1:1
        const croppedResult = await cropImageToSquare(file);
        const croppedFile = createFileFromCropped(croppedResult, file.name);

        // 2. Анализируем качество
        const analysisResult = await checkImageQuality(croppedFile);

        // Если качество слишком низкое (не соответствует требованиям ARCore)
        // Минимальный проходной балл = 75
        if (!analysisResult.meetsMinimumRequirements || analysisResult.score < 75) {
          throw new Error(
            "Изображение не подходит для ARCore. Минимальный балл качества: 75. " +
              (analysisResult.recommendations.length > 0
                ? analysisResult.recommendations[0]
                : `Текущий балл: ${analysisResult.score}.`)
          );
        }

        onTargetAdd({
          id: crypto.randomUUID(),
          file: croppedFile,
          size_cm: 10, // значение по умолчанию
          quality_score: analysisResult.score, // сохраняем балл качества
        });
      } catch (err) {
        setError(
          err instanceof Error ? err.message : "Ошибка обработки изображения"
        );
      } finally {
        setIsProcessing(false);
      }
    },
    [onTargetAdd]
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
    if (!isProcessing) {
      fileInputRef.current?.click();
    }
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
    }
  };

  return (
    <div className={cn("w-full", className)}>
      {error && (
        <div className="mb-4 p-3 bg-destructive/10 border border-destructive/20 rounded-md text-destructive text-sm">
          {error}
        </div>
      )}

      <div
        onDragOver={handleDragOver}
        onDragLeave={handleDragLeave}
        onDrop={handleDrop}
        onClick={handleClick}
        className={cn(
          "border-2 border-dashed rounded-lg transition-colors cursor-pointer relative",
          isDragging
            ? "border-primary bg-primary/5"
            : "border-muted-foreground/25 hover:border-muted-foreground/50",
          isProcessing && "opacity-50 pointer-events-none"
        )}
      >
        {isProcessing && (
          <div className="absolute inset-0 flex items-center justify-center bg-background/50 z-10">
            <Loader2 className="h-8 w-8 animate-spin text-primary" />
          </div>
        )}

        <div className="flex flex-col items-center justify-center gap-2 p-4">
          <ImageIcon className="h-6 w-6 text-muted-foreground" />
          <div className="text-center space-y-1">
            <p className="text-sm font-medium">
              {isProcessing
                ? "Обработка изображения..."
                : "Перетащите изображение-таргет или нажмите для выбора"}
            </p>
            <p className="text-xs text-muted-foreground">
              Автоматическая обрезка 1:1 и проверка качества ARCore
            </p>
          </div>
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={isProcessing}
            onClick={(e) => {
              e.stopPropagation();
              handleClick();
            }}
          >
            <Upload className="h-4 w-4 mr-2" />
            Выбрать изображение
          </Button>
        </div>
      </div>
      <input
        ref={fileInputRef}
        type="file"
        accept="image/jpeg,image/jpg,image/png"
        onChange={handleFileInputChange}
        className="hidden"
        disabled={isProcessing}
      />
    </div>
  );
}


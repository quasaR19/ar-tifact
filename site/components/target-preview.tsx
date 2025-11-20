"use client";

import { Button } from "@/components/ui/button";
import { X } from "lucide-react";
import Image from "next/image";
import { useEffect, useState } from "react";
import type { LocalTargetItem } from "./target-uploader";

interface TargetPreviewProps {
  target: LocalTargetItem;
  onRemove: () => void;
  className?: string;
}

export function TargetPreview({
  target,
  onRemove,
  className,
}: TargetPreviewProps) {
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);

  useEffect(() => {
    if (target.file) {
      const url = URL.createObjectURL(target.file);
      setPreviewUrl(url);
      return () => {
        URL.revokeObjectURL(url);
      };
    } else if (target.url) {
      setPreviewUrl(target.url);
    }
  }, [target.file, target.url]);

  if (!previewUrl) return null;

  return (
    <div className={className}>
      <div className="relative w-full aspect-square bg-muted rounded-lg overflow-hidden border">
        <Image
          src={previewUrl}
          alt="Таргет"
          fill
          sizes="(max-width: 768px) 50vw, 33vw"
          className="object-cover"
          loading="lazy"
        />
        <Button
          type="button"
          variant="destructive"
          size="icon"
          className="absolute top-2 right-2 z-10 h-8 w-8"
          onClick={onRemove}
        >
          <X className="h-4 w-4" />
        </Button>
      </div>
    </div>
  );
}


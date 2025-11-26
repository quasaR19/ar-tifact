"use client";

import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { TargetUploader } from "./target-uploader";
import type { LocalTargetItem } from "./target-uploader";
import { Trash2 } from "lucide-react";
import { useEffect, useState } from "react";

interface TargetListProps {
  targets: LocalTargetItem[];
  onTargetAdd: (target: LocalTargetItem) => void;
  onTargetRemove: (id: string) => void;
  onTargetUpdate: (id: string, updates: Partial<LocalTargetItem>) => void;
  className?: string;
}

export function TargetList({
  targets,
  onTargetAdd,
  onTargetRemove,
  onTargetUpdate,
  className,
}: TargetListProps) {
  return (
    <div className={className}>
      <h2 className="text-lg font-semibold mb-4">Таргеты (маркеры)</h2>
      <div className="space-y-4">
        {targets.length > 0 && (
          <div className="grid grid-cols-1 gap-4">
            {targets.map((target) => (
              <TargetCard
                key={target.id}
                target={target}
                onRemove={() => onTargetRemove(target.id)}
                onUpdate={(updates) => onTargetUpdate(target.id, updates)}
              />
            ))}
          </div>
        )}
        <TargetUploader onTargetAdd={onTargetAdd} />
      </div>
    </div>
  );
}

interface TargetCardProps {
  target: LocalTargetItem;
  onRemove: () => void;
  onUpdate: (updates: Partial<LocalTargetItem>) => void;
}

function TargetCard({ target, onRemove, onUpdate }: TargetCardProps) {
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);

  useEffect(() => {
    if (target.file) {
      const url = URL.createObjectURL(target.file);
      setPreviewUrl(url);
      return () => URL.revokeObjectURL(url);
    } else if (target.url) {
      setPreviewUrl(target.url);
    }
  }, [target.file, target.url]);

  return (
    <Card className="p-4 flex items-center gap-4">
      <div className="h-24 w-24 rounded-md overflow-hidden bg-muted flex-shrink-0 border">
        {previewUrl ? (
          <img
            src={previewUrl}
            alt="Target preview"
            className="h-full w-full object-cover"
          />
        ) : (
          <div className="h-full w-full flex items-center justify-center text-muted-foreground text-xs">
            Нет фото
          </div>
        )}
      </div>
      
      <div className="flex-1 space-y-2">
        <div className="flex items-center justify-between">
          <Label htmlFor={`size-${target.id}`}>Размер (см)</Label>
          {target.quality_score !== undefined && (
            <div className="flex flex-col items-end gap-1">
              <div className="flex items-center gap-1.5">
                <span className="text-xs text-muted-foreground">Качество:</span>
                <div
                  className={`px-2 py-0.5 rounded text-xs font-semibold ${
                    target.quality_score >= 75
                      ? "bg-green-500/20 text-green-700 dark:text-green-400"
                      : target.quality_score >= 50
                      ? "bg-yellow-500/20 text-yellow-700 dark:text-yellow-400"
                      : "bg-red-500/20 text-red-700 dark:text-red-400"
                  }`}
                  title={
                    target.quality_score >= 75
                      ? "Отличное качество для ARCore"
                      : target.quality_score >= 50
                      ? "Приемлемое качество"
                      : "Низкое качество, может не работать в ARCore"
                  }
                >
                  {target.quality_score}/100
                </div>
              </div>
              <div className="w-24 h-1.5 bg-muted rounded-full overflow-hidden">
                <div
                  className={`h-full transition-all ${
                    target.quality_score >= 75
                      ? "bg-green-500"
                      : target.quality_score >= 50
                      ? "bg-yellow-500"
                      : "bg-red-500"
                  }`}
                  style={{ width: `${target.quality_score}%` }}
                />
              </div>
            </div>
          )}
        </div>
        <div className="flex items-center gap-2">
          <Input
            id={`size-${target.id}`}
            type="number"
            min="1"
            value={target.size_cm}
            onChange={(e) =>
              onUpdate({ size_cm: parseInt(e.target.value) || 10 })
            }
            className="max-w-[150px]"
          />
          <span className="text-sm text-muted-foreground">см</span>
        </div>
      </div>

      <Button
        variant="secondary"
        size="icon"
        onClick={onRemove}
        className="text-destructive hover:text-destructive/90"
      >
        <Trash2 className="h-5 w-5" />
      </Button>
    </Card>
  );
}

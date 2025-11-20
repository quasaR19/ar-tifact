"use client";

import { TargetPreview } from "./target-preview";
import { TargetUploader } from "./target-uploader";
import type { LocalTargetItem } from "./target-uploader";

interface TargetListProps {
  targets: LocalTargetItem[];
  onTargetAdd: (target: LocalTargetItem) => void;
  onTargetRemove: (id: string) => void;
  className?: string;
}

export function TargetList({
  targets,
  onTargetAdd,
  onTargetRemove,
  className,
}: TargetListProps) {
  return (
    <div className={className}>
      <h2 className="text-lg font-semibold mb-4">Таргеты (маркеры)</h2>
      <div className="space-y-4">
        {targets.length > 0 && (
          <div className="grid grid-cols-2 md:grid-cols-3 gap-4">
            {targets.map((target) => (
              <TargetPreview
                key={target.id}
                target={target}
                onRemove={() => onTargetRemove(target.id)}
              />
            ))}
          </div>
        )}
        <TargetUploader onTargetAdd={onTargetAdd} />
      </div>
    </div>
  );
}


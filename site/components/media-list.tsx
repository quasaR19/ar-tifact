"use client";

import { MediaPreview } from "./media-preview";
import { MediaUploader } from "./media-uploader";
import type { LocalMediaItem } from "./media-uploader";

interface MediaListProps {
  media: LocalMediaItem[];
  onMediaAdd: (media: LocalMediaItem) => void;
  onMediaRemove: (id: string) => void;
  onMediaUpdate?: (id: string, updates: Partial<LocalMediaItem>) => void;
  onMediaMoveUp?: (id: string) => void;
  onMediaMoveDown?: (id: string) => void;
  className?: string;
}

export function MediaList({
  media,
  onMediaAdd,
  onMediaRemove,
  onMediaUpdate,
  onMediaMoveUp,
  onMediaMoveDown,
  className,
}: MediaListProps) {
  const minOrder =
    media.length > 0 ? Math.min(...media.map((m) => m.display_order ?? 0)) : 0;
  const maxOrder =
    media.length > 0 ? Math.max(...media.map((m) => m.display_order ?? 0)) : 0;

  return (
    <div className={className}>
      <h2 className="text-lg font-semibold mb-4">Медиа</h2>
      <div className="flex flex-col gap-4">
        {media.map((item) => {
          const order = item.display_order ?? 0;
          const canMoveUp = order > minOrder;
          const canMoveDown = order < maxOrder;

          return (
            <MediaPreview
              key={item.id}
              media={item}
              onRemove={() => onMediaRemove(item.id)}
              onUpdate={(updates) => onMediaUpdate?.(item.id, updates)}
              canMoveUp={canMoveUp}
              canMoveDown={canMoveDown}
              onMoveUp={() => onMediaMoveUp?.(item.id)}
              onMoveDown={() => onMediaMoveDown?.(item.id)}
              style={{ order }}
            />
          );
        })}
        <div style={{ order: 9999 }}>
          <MediaUploader onMediaAdd={onMediaAdd} />
        </div>
      </div>
    </div>
  );
}

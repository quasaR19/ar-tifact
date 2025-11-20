"use client";

import { MediaPreview } from "./media-preview";
import { MediaUploader } from "./media-uploader";
import type { LocalMediaItem } from "./media-uploader";

interface MediaListProps {
  media: LocalMediaItem[];
  onMediaAdd: (media: LocalMediaItem) => void;
  onMediaRemove: (id: string) => void;
  className?: string;
}

export function MediaList({
  media,
  onMediaAdd,
  onMediaRemove,
  className,
}: MediaListProps) {
  return (
    <div className={className}>
      <h2 className="text-lg font-semibold mb-4">Медиа</h2>
      <div className="space-y-4">
        {media.map((item) => (
          <MediaPreview
            key={item.id}
            media={item}
            onRemove={() => onMediaRemove(item.id)}
          />
        ))}
        <MediaUploader onMediaAdd={onMediaAdd} />
      </div>
    </div>
  );
}


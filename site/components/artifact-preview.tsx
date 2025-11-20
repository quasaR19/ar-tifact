import { Card, CardContent } from "@/components/ui/card";
import type { Artifact } from "@/lib/types/artifact";
import Image from "next/image";
import Link from "next/link";

interface ArtifactPreviewProps {
  artifact: Artifact;
}

export function ArtifactPreview({ artifact }: ArtifactPreviewProps) {
  return (
    <Link href={`/artifacts/edit/${artifact.id}`} className="block">
      <Card className="overflow-hidden transition-all hover:shadow-lg hover:scale-[1.02] cursor-pointer h-full">
        <CardContent className="p-0">
          <div className="flex flex-col h-full">
            {artifact.preview_image_url ? (
              <div className="relative w-full aspect-video bg-muted">
                <Image
                  src={artifact.preview_image_url}
                  alt={artifact.name}
                  fill
                  sizes="(max-width: 768px) 100vw, (max-width: 1200px) 50vw, 33vw"
                  className="object-cover"
                />
              </div>
            ) : (
              <div className="w-full aspect-video bg-muted flex items-center justify-center">
                <span className="text-muted-foreground text-sm">
                  Нет изображения
                </span>
              </div>
            )}
            <div className="p-4">
              <h3 className="font-semibold text-lg">{artifact.name}</h3>
            </div>
          </div>
        </CardContent>
      </Card>
    </Link>
  );
}


import { ArtifactPreview } from "@/components/artifact-preview";
import type { Artifact } from "@/lib/types/artifact";

interface ArtifactListProps {
  artifacts: Artifact[];
}

export function ArtifactList({ artifacts }: ArtifactListProps) {
  if (artifacts.length === 0) {
    return (
      <div className="text-center py-12 text-muted-foreground">
        Артефакты не найдены
      </div>
    );
  }

  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-4">
      {artifacts.map((artifact) => (
        <ArtifactPreview key={artifact.id} artifact={artifact} />
      ))}
    </div>
  );
}


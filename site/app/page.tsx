import { ArtifactContainer } from "@/components/artifact-container";
import { Button } from "@/components/ui/button";
import { Plus } from "lucide-react";
import Link from "next/link";

export const dynamic = 'force-dynamic';

export default function Home() {
  return (
    <main className="min-h-screen flex flex-col">
      <div className="container mx-auto py-8 px-4 flex-1">
        <div className="flex justify-between items-center mb-6">
          <h1 className="text-3xl font-bold">Артефакты</h1>
          <Button asChild variant="default">
            <Link href="/artifacts/edit/new">
              <Plus className="h-4 w-4" />
              Добавить
            </Link>
          </Button>
        </div>
        <ArtifactContainer />
      </div>
    </main>
  );
}

import { ArtifactList } from "@/components/artifact-list";
import { getArtifactsPaginated } from "@/lib/queries";
import { createClient } from "@/lib/supabase/server";

export async function ArtifactContainer() {
  try {
    const supabase = await createClient();
    const { artifacts } = await getArtifactsPaginated(supabase, 1, 20);
    return <ArtifactList artifacts={artifacts} />;
  } catch (error) {
    // Если произошла ошибка (кроме отсутствия таблицы, которое уже обработано в queries)
    console.error("Ошибка загрузки артефактов:", error);
    return (
      <div className="text-center py-12 text-muted-foreground">
        <p className="mb-2">Ошибка загрузки артефактов</p>
        <p className="text-sm">Убедитесь, что миграции базы данных применены</p>
      </div>
    );
  }
}

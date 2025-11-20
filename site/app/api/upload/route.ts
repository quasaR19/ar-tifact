import { handleUpload, type HandleUploadBody } from "@vercel/blob/client";
import { NextResponse } from "next/server";
import { createClient } from "@/lib/supabase/server";

export async function POST(request: Request): Promise<NextResponse> {
  const body = (await request.json()) as HandleUploadBody;

  try {
    // Проверяем аутентификацию пользователя
    const supabase = await createClient();
    const {
      data: { user },
    } = await supabase.auth.getUser();

    if (!user) {
      return NextResponse.json(
        { error: "Unauthorized" },
        { status: 401 }
      );
    }

    const jsonResponse = await handleUpload({
      body,
      request,
      onBeforeGenerateToken: async (pathname, clientPayload) => {
        // Определяем разрешенные типы контента на основе расширения файла
        const extension = pathname.split(".").pop()?.toLowerCase();
        let allowedContentTypes: string[] = [];

        if (extension === "glb") {
          allowedContentTypes = ["model/gltf-binary", "application/octet-stream"];
        } else if (["mp4", "webm", "mov", "avi"].includes(extension || "")) {
          allowedContentTypes = [
            "video/mp4",
            "video/webm",
            "video/quicktime",
            "video/x-msvideo",
          ];
        } else if (["jpg", "jpeg", "png", "webp", "gif", "svg"].includes(extension || "")) {
          // Поддержка изображений для превью
          allowedContentTypes = [
            "image/jpeg",
            "image/png",
            "image/webp",
            "image/gif",
            "image/svg+xml",
          ];
        } else {
          // Для других типов файлов
          allowedContentTypes = ["*/*"];
        }

        return {
          allowedContentTypes,
          addRandomSuffix: true,
          tokenPayload: JSON.stringify({
            userId: user.id,
            pathname,
            clientPayload,
          }),
        };
      },
    });

    return NextResponse.json(jsonResponse);
  } catch (error) {
    return NextResponse.json(
      { error: (error as Error).message },
      { status: 400 }
    );
  }
}


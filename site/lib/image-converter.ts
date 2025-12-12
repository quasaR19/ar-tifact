/**
 * Конвертирует WebP изображение в JPG или PNG
 * @param file - исходный файл WebP
 * @param format - целевой формат ('jpeg' или 'png')
 * @returns Promise с конвертированным файлом
 */
export async function convertWebPToImage(
  file: File,
  format: "jpeg" | "png" = "jpeg"
): Promise<File> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = (e) => {
      const img = new Image();
      img.onload = () => {
        const canvas = document.createElement("canvas");
        canvas.width = img.width;
        canvas.height = img.height;
        const ctx = canvas.getContext("2d");
        if (!ctx) {
          reject(new Error("Не удалось получить контекст canvas"));
          return;
        }
        ctx.drawImage(img, 0, 0);

        canvas.toBlob(
          (blob) => {
            if (!blob) {
              reject(new Error("Не удалось создать blob из canvas"));
              return;
            }

            const mimeType = format === "jpeg" ? "image/jpeg" : "image/png";
            const extension = format === "jpeg" ? "jpg" : "png";

            // Создаем новое имя файла с правильным расширением
            const originalName = file.name.replace(/\.webp$/i, "");
            const newFileName = `${originalName}.${extension}`;

            const convertedFile = new File([blob], newFileName, {
              type: mimeType,
              lastModified: file.lastModified,
            });

            resolve(convertedFile);
          },
          format === "jpeg" ? "image/jpeg" : "image/png",
          0.92 // качество для JPEG
        );
      };
      img.onerror = () => {
        reject(new Error("Ошибка загрузки изображения"));
      };
      if (e.target?.result) {
        img.src = e.target.result as string;
      }
    };
    reader.onerror = () => {
      reject(new Error("Ошибка чтения файла"));
    };
    reader.readAsDataURL(file);
  });
}

/**
 * Проверяет, является ли файл WebP
 */
export function isWebP(file: File): boolean {
  return (
    file.type === "image/webp" || file.name.toLowerCase().endsWith(".webp")
  );
}

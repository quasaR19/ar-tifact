/**
 * Интерфейс для результата обрезки изображения
 */
export interface CroppedImageResult {
  dataUrl: string; // Data URL обрезанного изображения
  blob: Blob; // Blob обрезанного изображения
  width: number; // Ширина обрезанного изображения
  height: number; // Высота обрезанного изображения
}

/**
 * Обрезает изображение до формата 1:1 (квадрат)
 * Обрезка происходит по центру изображения
 * @param file - Исходный файл изображения
 * @param quality - Качество для JPEG (0.0 - 1.0), по умолчанию 0.92
 * @returns Promise с результатом обрезки
 */
export async function cropImageToSquare(
  file: File,
  quality: number = 0.92
): Promise<CroppedImageResult> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();

    reader.onload = (e) => {
      const img = new Image();

      img.onload = async () => {
        try {
          const result = await performCrop(img, file.type, quality);
          resolve(result);
        } catch (error) {
          reject(error);
        }
      };

      img.onerror = () => {
        reject(new Error("Не удалось загрузить изображение для обрезки"));
      };

      if (e.target?.result) {
        img.src = e.target.result as string;
      }
    };

    reader.onerror = () => {
      reject(new Error("Не удалось прочитать файл"));
    };

    reader.readAsDataURL(file);
  });
}

/**
 * Выполняет обрезку изображения до квадрата
 */
function performCrop(
  img: HTMLImageElement,
  mimeType: string,
  quality: number
): Promise<CroppedImageResult> {
  return new Promise((resolve, reject) => {
    // Определяем размер квадрата (минимальная сторона)
    const size = Math.min(img.width, img.height);

    // Вычисляем координаты для обрезки по центру
    const startX = (img.width - size) / 2;
    const startY = (img.height - size) / 2;

    // Создаем canvas для обрезанного изображения
    const canvas = document.createElement("canvas");
    canvas.width = size;
    canvas.height = size;

    const ctx = canvas.getContext("2d");
    if (!ctx) {
      reject(new Error("Не удалось получить контекст canvas"));
      return;
    }

    // Рисуем обрезанное изображение на canvas
    ctx.drawImage(
      img,
      startX,
      startY,
      size,
      size,
      0,
      0,
      size,
      size
    );

    // Получаем data URL
    const dataUrl = canvas.toDataURL(mimeType, quality);

    // Конвертируем canvas в Blob
    canvas.toBlob(
      (blob) => {
        if (!blob) {
          reject(new Error("Не удалось создать blob из canvas"));
          return;
        }

        resolve({
          dataUrl,
          blob,
          width: size,
          height: size,
        });
      },
      mimeType,
      quality
    );
  });
}

/**
 * Создает File объект из обрезанного изображения
 * @param croppedResult - Результат обрезки
 * @param originalFileName - Исходное имя файла
 * @returns File объект
 */
export function createFileFromCropped(
  croppedResult: CroppedImageResult,
  originalFileName: string
): File {
  // Определяем расширение из исходного имени файла
  const extension = originalFileName.split(".").pop() || "jpg";
  const nameWithoutExt = originalFileName.replace(/\.[^/.]+$/, "");
  const newFileName = `${nameWithoutExt}_cropped_1x1.${extension}`;

  return new File([croppedResult.blob], newFileName, {
    type: croppedResult.blob.type,
  });
}

